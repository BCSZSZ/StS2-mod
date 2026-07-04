#!/usr/bin/env bash
# Runs ON the EC2 box, from the repo root. Collects ONE BASE dataset of
# teacher-labeled decisions. Group weighting is NOT decided here — it is a cheap
# train-time knob (tag-groups.py + reweight.py).
#
# collect-search-policy-data is single-threaded, so we parallelize with a
# DECK-LEVEL work queue: one task per deck (--skip-decks i --limit-decks 1),
# fed biggest-deck-first (LPT) into a WORKERS-wide xargs queue. Why:
#   - Big/engine decks cost several× the small ones. As isolated tasks in a
#     dynamic queue, an expensive deck occupies just one of WORKERS cores while
#     the rest churn through cheap decks — no shard clusters expensive decks, and
#     freed workers immediately pull the next deck (cores busy until the end).
#   - LPT (biggest first) starts the long poles when all cores are free, so they
#     finish while small decks fill the gaps → the tail is one deck, not a shard.
#   - Each deck's branch-2 baseline sim runs ONCE (seed-sharding re-ran all decks'
#     sims in every shard).
# Volume is set by RUNS (~24 groups per deck-run at turns=14): total ≈ decks×RUNS×24.
# Each deck task is checkpointed to S3 and marked .done (Spot-reclaim safe).
#
# Env knobs (defaults tuned for a base run on c7a.16xlarge / 64 vCPU):
#   WORKERS            parallel processes            (default: nproc-4)
#   RUNS TURNS         sim runs / turns per deck      (default: 13 / 14; RUNS sets volume)
#   BASE_SEED          seed base (per-deck seed = BASE_SEED+deckIndex) (default: 1000)
#   MAX_BRANCH         student beam width             (default: 2)
#   TEACHER_MAX_BRANCH teacher beam width             (default: 8 — full quality)
#   TEACHER_MAX_PLAYS  teacher within-turn depth      (default: 8)
#   TEACHER_FORWARD_TURNS forward-Q horizon (turns)   (default: 4)
#   TRAINING_DECKS     deck source                    (default: v0.107.x filtered set)
#   SHARD_TIMEOUT      per-deck wall-clock secs        (default: 14400 = 4h; termination guard)
#   S3_BUCKET RUN_ID   if set, each finished deck task is synced to S3 (checkpoint)
#   PYTHON             python for LPT ordering         (default: python3)
set -euo pipefail

DOTNET="${DOTNET:-/opt/dotnet/dotnet}"; command -v "$DOTNET" >/dev/null 2>&1 || DOTNET=dotnet
DLL="CardValueOverlay.Tools/bin/Release/net8.0/CardValueOverlay.Tools.dll"
[ -f "$DLL" ] || { echo "Missing $DLL — run bootstrap.sh first."; exit 1; }
[ -f "data/extracted/card_facts.generated.json" ] || { echo "Missing card_facts.generated.json — scp it up (README Step 2)."; exit 1; }

WORKERS="${WORKERS:-$(( $(nproc) - 4 > 1 ? $(nproc) - 4 : 1 ))}"
BASE_SEED="${BASE_SEED:-1000}"
RUNS="${RUNS:-13}"; TURNS="${TURNS:-14}"
MAX_BRANCH="${MAX_BRANCH:-2}"; TEACHER_MAX_BRANCH="${TEACHER_MAX_BRANCH:-8}"; TEACHER_MAX_PLAYS="${TEACHER_MAX_PLAYS:-8}"
TEACHER_FORWARD_TURNS="${TEACHER_FORWARD_TURNS:-4}"
# Rollouts averaged per candidate to denoise the teacher-Q label (>1 = cleaner labels, K× cost).
TEACHER_ROLLOUTS="${TEACHER_ROLLOUTS:-1}"
TRAINING_DECKS="${TRAINING_DECKS:-history-analysis/data/regent_v107_wins_filtered_decks.json}"
SHARD_TIMEOUT="${SHARD_TIMEOUT:-14400}"
S3_BUCKET="${S3_BUCKET:-}"; RUN_ID="${RUN_ID:-run-manual}"
PY="${PYTHON:-python3}"

[ -f "$TRAINING_DECKS" ] || { echo "Missing deck file $TRAINING_DECKS"; exit 1; }
OUT_DIR="data/generated/search_policy"
SHARD_DIR="$OUT_DIR/shards"; LOG_DIR="$OUT_DIR/logs"
mkdir -p "$SHARD_DIR" "$LOG_DIR"

# Deck indices ordered biggest-first (LPT); card count is the cost proxy.
mapfile -t DECK_ORDER < <("$PY" - "$TRAINING_DECKS" <<'PYEOF' | tr -d '\r'
import json,sys
d=json.load(open(sys.argv[1],encoding="utf-8"))["decks"]
cost=lambda dk: dk.get("cardCount") or sum(c["count"] for c in dk["cards"])
for i in sorted(range(len(d)), key=lambda i:-cost(d[i])): print(i)
PYEOF
)
DECK_COUNT=${#DECK_ORDER[@]}
[ "$DECK_COUNT" -gt 0 ] || { echo "no decks parsed from $TRAINING_DECKS"; exit 1; }

echo "deck-sharded (LPT biggest-first): decks=$DECK_COUNT workers=$WORKERS runs=$RUNS turns=$TURNS teacher-branch=$TEACHER_MAX_BRANCH forward-turns=$TEACHER_FORWARD_TURNS"
echo "decks=$TRAINING_DECKS  expected ~$(( DECK_COUNT * RUNS * 24 )) groups (volume set by RUNS)"
[ -n "$S3_BUCKET" ] && echo "checkpoint -> s3://$S3_BUCKET/$RUN_ID/shards/"

run_deck() {
  local idx="$1"
  local seed=$(( BASE_SEED + idx ))
  local out="$SHARD_DIR/deck-$idx.jsonl"
  local log="$LOG_DIR/deck-$idx.log"
  if [ -f "$out.done" ]; then echo "deck $idx: already done, skipping"; return 0; fi
  if timeout "$SHARD_TIMEOUT" "$DOTNET" "$DLL" collect-search-policy-data \
      --training-decks "$TRAINING_DECKS" --skip-decks "$idx" --limit-decks 1 \
      --max-branch "$MAX_BRANCH" --teacher-max-branch "$TEACHER_MAX_BRANCH" --teacher-max-plays "$TEACHER_MAX_PLAYS" \
      --teacher-forward-turns "$TEACHER_FORWARD_TURNS" --teacher-rollouts "$TEACHER_ROLLOUTS" \
      --runs "$RUNS" --turns "$TURNS" --candidate-decks 0 \
      --seed "$seed" --max-groups 100000000 \
      --output-jsonl "$out" > "$log" 2>&1; then
    touch "$out.done"
    echo "deck $idx: complete ($(wc -l < "$out") groups)"
  else
    echo "deck $idx: TIMEOUT/ERROR after ${SHARD_TIMEOUT}s — partial kept ($(wc -l < "$out" 2>/dev/null || echo 0) groups); re-run on resume"
  fi
  if [ -n "$S3_BUCKET" ] && [ -f "$out" ]; then
    aws s3 cp "$out" "s3://$S3_BUCKET/$RUN_ID/shards/deck-$idx.jsonl" --only-show-errors || true
  fi
}
export -f run_deck
export DOTNET DLL SHARD_DIR LOG_DIR SHARD_TIMEOUT BASE_SEED RUNS TURNS \
       MAX_BRANCH TEACHER_MAX_BRANCH TEACHER_MAX_PLAYS TEACHER_FORWARD_TURNS TEACHER_ROLLOUTS TRAINING_DECKS S3_BUCKET RUN_ID

printf '%s\n' "${DECK_ORDER[@]}" | xargs -P "$WORKERS" -I{} bash -c 'run_deck "$@"' _ {}

FINAL="$OUT_DIR/search_policy_teacher_base.generated.jsonl"
cat "$SHARD_DIR"/deck-*.jsonl > "$FINAL"
echo "concatenated $(wc -l < "$FINAL") base decision groups -> $FINAL"
if [ -n "$S3_BUCKET" ]; then
  aws s3 cp "$FINAL" "s3://$S3_BUCKET/$RUN_ID/search_policy_teacher_base.generated.jsonl" --only-show-errors
  echo "final synced -> s3://$S3_BUCKET/$RUN_ID/"
fi
echo "Next (local): fetch-results.sh, then tag-groups.py + reweight.py before train-ranker."
