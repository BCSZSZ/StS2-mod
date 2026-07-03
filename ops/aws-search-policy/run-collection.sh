#!/usr/bin/env bash
# Runs ON the EC2 box, from the repo root. Collects ONE large, balanced BASE
# dataset of teacher-labeled decisions across all deck groups. Group weighting is
# NOT decided here — it is a cheap train-time knob (tag-groups.py + reweight.py).
# So this run should be generous: collect plenty from every group, branch-8, once.
#
# collect-search-policy-data is single-threaded, so we shard: NUM_SHARDS small
# JSONL shards, each a distinct --seed, processed by a pool of WORKERS. Each shard
# is checkpointed to S3 and marked .done, so a Spot reclaim costs at most the
# in-flight shards. Shards are concatenated into one base JSONL at the end.
#
# Env knobs (defaults tuned for a one-time base run on c7a.16xlarge / 64 vCPU):
#   WORKERS            parallel processes             (default: nproc-4)
#   TARGET_GROUPS      total decision groups in base  (default: 400000)
#   NUM_SHARDS         shard count (>= WORKERS)        (default: WORKERS; raise for finer resume granularity)
#   BASE_SEED          seed of shard 0                 (default: 1000)
#   RUNS TURNS         sim runs / turns                (default: 50 / 14)
#   MAX_BRANCH         student beam width              (default: 2)
#   TEACHER_MAX_BRANCH teacher beam width              (default: 8 — full quality; see note)
#   TEACHER_MAX_PLAYS  teacher depth                   (default: 8)
#   CANDIDATE_DECKS    decks that get candidate variants (default: 20)
#   TRAINING_DECKS     deck source                     (default: 308-deck 4-group set)
#   SHARD_TIMEOUT      per-shard wall-clock secs       (default: 14400 = 4h; termination guard, NOT a cost lever)
#   S3_BUCKET RUN_ID   if set, each finished shard is synced to S3 (checkpoint)
#
# NOTE on TEACHER_MAX_BRANCH: keep 8 (full teacher). Only drop to 6 if a specific
# group's shards keep hitting SHARD_TIMEOUT on engine/generation decks (VoidForm-
# style states can be super-expensive). The timeout exists to GUARANTEE termination
# on a pathological node, not to save money — set it generous and leave branch at 8.
set -euo pipefail

DOTNET="${DOTNET:-/opt/dotnet/dotnet}"; command -v "$DOTNET" >/dev/null 2>&1 || DOTNET=dotnet
DLL="CardValueOverlay.Tools/bin/Release/net8.0/CardValueOverlay.Tools.dll"
[ -f "$DLL" ] || { echo "Missing $DLL — run bootstrap.sh first."; exit 1; }
[ -f "data/extracted/card_facts.generated.json" ] || { echo "Missing card_facts.generated.json — scp it up (README Step 2)."; exit 1; }

WORKERS="${WORKERS:-$(( $(nproc) - 4 > 1 ? $(nproc) - 4 : 1 ))}"
TARGET_GROUPS="${TARGET_GROUPS:-400000}"
# Fewer shards = each shard amortizes its per-variant branch-2 sims over more
# collected groups (candidate variants re-run the sim per shard), so default to
# one big shard per worker. Raise NUM_SHARDS only for finer resume/checkpointing.
NUM_SHARDS="${NUM_SHARDS:-$WORKERS}"
BASE_SEED="${BASE_SEED:-1000}"
RUNS="${RUNS:-50}"; TURNS="${TURNS:-14}"
MAX_BRANCH="${MAX_BRANCH:-2}"; TEACHER_MAX_BRANCH="${TEACHER_MAX_BRANCH:-8}"; TEACHER_MAX_PLAYS="${TEACHER_MAX_PLAYS:-8}"
CANDIDATE_DECKS="${CANDIDATE_DECKS:-20}"
TRAINING_DECKS="${TRAINING_DECKS:-history-analysis/data/dashen_77_all_308_decks.json}"
SHARD_TIMEOUT="${SHARD_TIMEOUT:-14400}"
S3_BUCKET="${S3_BUCKET:-}"; RUN_ID="${RUN_ID:-run-manual}"

[ -f "$TRAINING_DECKS" ] || { echo "Missing deck file $TRAINING_DECKS"; exit 1; }
PER_SHARD=$(( (TARGET_GROUPS + NUM_SHARDS - 1) / NUM_SHARDS ))
OUT_DIR="data/generated/search_policy"
SHARD_DIR="$OUT_DIR/shards"; LOG_DIR="$OUT_DIR/logs"
mkdir -p "$SHARD_DIR" "$LOG_DIR"

echo "workers=$WORKERS shards=$NUM_SHARDS per-shard=$PER_SHARD target=$TARGET_GROUPS runs=$RUNS turns=$TURNS teacher-branch=$TEACHER_MAX_BRANCH"
echo "decks=$TRAINING_DECKS  (base dataset — group weighting is a train-time knob, not set here)"
[ -n "$S3_BUCKET" ] && echo "checkpoint -> s3://$S3_BUCKET/$RUN_ID/shards/"

run_shard() {
  local i="$1"
  local seed=$(( BASE_SEED + i ))
  local out="$SHARD_DIR/shard-$i.jsonl"
  local log="$LOG_DIR/shard-$i.log"
  if [ -f "$out.done" ]; then echo "shard $i: already done, skipping"; return 0; fi
  echo "shard $i: seed=$seed budget=$PER_SHARD -> $out"
  if timeout "$SHARD_TIMEOUT" "$DOTNET" "$DLL" collect-search-policy-data \
      --training-decks "$TRAINING_DECKS" \
      --max-branch "$MAX_BRANCH" --teacher-max-branch "$TEACHER_MAX_BRANCH" --teacher-max-plays "$TEACHER_MAX_PLAYS" \
      --runs "$RUNS" --turns "$TURNS" --candidate-decks "$CANDIDATE_DECKS" \
      --seed "$seed" --max-groups "$PER_SHARD" \
      --output-jsonl "$out" > "$log" 2>&1; then
    touch "$out.done"
    echo "shard $i: complete ($(wc -l < "$out") groups)"
  else
    echo "shard $i: TIMEOUT/ERROR after ${SHARD_TIMEOUT}s — partial shard kept ($(wc -l < "$out" 2>/dev/null || echo 0) groups); re-run on resume"
  fi
  if [ -n "$S3_BUCKET" ] && [ -f "$out" ]; then
    aws s3 cp "$out" "s3://$S3_BUCKET/$RUN_ID/shards/shard-$i.jsonl" --only-show-errors || true
  fi
}
export -f run_shard
export DOTNET DLL SHARD_DIR LOG_DIR SHARD_TIMEOUT PER_SHARD BASE_SEED RUNS TURNS \
       MAX_BRANCH TEACHER_MAX_BRANCH TEACHER_MAX_PLAYS CANDIDATE_DECKS TRAINING_DECKS S3_BUCKET RUN_ID

seq 0 $(( NUM_SHARDS - 1 )) | xargs -P "$WORKERS" -I{} bash -c 'run_shard "$@"' _ {}

FINAL="$OUT_DIR/search_policy_teacher_base.generated.jsonl"
cat "$SHARD_DIR"/shard-*.jsonl > "$FINAL"
echo "concatenated $(wc -l < "$FINAL") base decision groups -> $FINAL"
if [ -n "$S3_BUCKET" ]; then
  aws s3 cp "$FINAL" "s3://$S3_BUCKET/$RUN_ID/search_policy_teacher_base.generated.jsonl" --only-show-errors
  echo "final synced -> s3://$S3_BUCKET/$RUN_ID/"
fi
echo "Next (local): fetch-results.sh, then tag-groups.py + reweight.py before train-ranker."
