#!/usr/bin/env bash
# Runs ON the EC2 box, from the repo root. Single-process branch-4 direct-play-value
# estimation — the definitive "est" dataset (value per direct play), matching the in-game
# runtime (branch-4, max-plays 8). Spot-reclaim safe:
#   - estimate-direct-play-values checkpoints its output JSON every ~10 cards and --resume
#     skips completed cards (only if run-shape matches: runs/decks/branch/turns/strategy/horizons);
#   - on start we PULL any partial output from S3 so a fresh box resumes where the last left off;
#   - a background loop SYNCS the output dir to S3 every 5 min so a reclaim loses <5 min.
# If the Spot box is reclaimed, just re-run provision + this script on a new box; it resumes.
#
# Env knobs (defaults = the approved est config):
#   DECK_SOURCE   deck source            (default regent_v107 filtered set, committed)
#   DECK_MIX      per-group COUNTS as ratios (default: floor8/act2 full + 15 preAct2Boss + 10 final)
#   DECK_COUNT    total decks            (default 190)
#   DECK_SEED     big-deck sampling seed (default 20260705)
#   RUNS          MC runs per sim        (default 200)
#   MAX_BRANCH    beam width             (default 4 — matches runtime)
#   MAX_PLAYS     plays/turn cap         (default 8 — matches runtime)
#   TURNS         horizon                (default 14)
#   DOP           parallel form groups   (default nproc)
#   S3_BUCKET RUN_ID   S3 checkpoint target
set -euo pipefail

DOTNET="${DOTNET:-/opt/dotnet/dotnet}"; command -v "$DOTNET" >/dev/null 2>&1 || DOTNET=dotnet
DLL="CardValueOverlay.Tools/bin/Release/net8.0/CardValueOverlay.Tools.dll"
[ -f "$DLL" ] || { echo "Missing $DLL — run bootstrap.sh first (it builds Release)."; exit 1; }
[ -f "data/extracted/card_facts.generated.json" ] || { echo "Missing card_facts.generated.json — scp it up."; exit 1; }
[ -f "data/extracted/card_pool_memberships.generated.json" ] || { echo "Missing card_pool_memberships.generated.json — scp it up."; exit 1; }

DECK_SOURCE="${DECK_SOURCE:-history-analysis/data/regent_v107_wins_filtered_decks.json}"
DECK_MIX="${DECK_MIX:-floor8:85,act2Start:80,preAct2Boss:15,final:10}"
DECK_COUNT="${DECK_COUNT:-190}"
DECK_SEED="${DECK_SEED:-20260705}"
RUNS="${RUNS:-200}"
MAX_BRANCH="${MAX_BRANCH:-4}"
MAX_PLAYS="${MAX_PLAYS:-8}"
TURNS="${TURNS:-14}"
DOP="${DOP:-$(nproc)}"
S3_BUCKET="${S3_BUCKET:-}"; RUN_ID="${RUN_ID:-est-manual}"

[ -f "$DECK_SOURCE" ] || { echo "Missing deck source $DECK_SOURCE"; exit 1; }
OUT_DIR="data/generated/direct_play_values"
mkdir -p "$OUT_DIR"
OUT_JSON="$OUT_DIR/est_branch${MAX_BRANCH}_r${RUNS}_d${DECK_COUNT}.generated.json"
S3_PREFIX=""
[ -n "$S3_BUCKET" ] && S3_PREFIX="s3://$S3_BUCKET/$RUN_ID/direct_play_values"

echo "est run: decks=$DECK_COUNT ($DECK_MIX) seed=$DECK_SEED runs=$RUNS branch=$MAX_BRANCH plays=$MAX_PLAYS turns=$TURNS dop=$DOP"
[ -n "$S3_PREFIX" ] && echo "checkpoint <-> $S3_PREFIX/"

# Restore any partial output from a previous (reclaimed) box so --resume continues.
if [ -n "$S3_PREFIX" ]; then
  aws s3 cp "$S3_PREFIX/$(basename "$OUT_JSON")" "$OUT_JSON" --only-show-errors 2>/dev/null \
    && echo "restored partial output from S3 for resume" || echo "no prior partial in S3 (fresh start)"
fi

# Background: sync output dir to S3 every 5 min (partial results survive a reclaim).
if [ -n "$S3_PREFIX" ]; then
  ( while true; do sleep 300; aws s3 sync "$OUT_DIR" "$S3_PREFIX/" --only-show-errors || true; done ) &
  SYNC_PID=$!
  trap '[ -n "${SYNC_PID:-}" ] && kill "$SYNC_PID" 2>/dev/null || true' EXIT
fi

"$DOTNET" "$DLL" estimate-direct-play-values \
  --deck-source "$DECK_SOURCE" \
  --deck-mix "$DECK_MIX" --deck-count "$DECK_COUNT" --deck-seed "$DECK_SEED" \
  --runs "$RUNS" --max-branch "$MAX_BRANCH" --max-plays "$MAX_PLAYS" --turns "$TURNS" \
  --horizons shortline:4,midline:8,longline:14 --value-strategy auto \
  --degree-of-parallelism "$DOP" --run-degree 1 --resume \
  --output-json "$OUT_JSON"

if [ -n "$S3_PREFIX" ]; then
  aws s3 sync "$OUT_DIR" "$S3_PREFIX/" --only-show-errors
  echo "final synced -> $S3_PREFIX/"
fi
echo "est run complete -> $OUT_JSON"
