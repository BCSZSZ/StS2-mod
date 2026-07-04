#!/usr/bin/env bash
# Runs LOCALLY (Git Bash on liao-home/liao-work) from the repo root. Pulls the
# collected BASE dataset back from S3, then tags each record with its deck group
# so weighting becomes a cheap local knob (reweight.py). Does NOT pick weights.
#
# Required env: S3_BUCKET, RUN_ID   Optional: AWS_REGION, DECKS
set -euo pipefail

: "${S3_BUCKET:?set S3_BUCKET}"; : "${RUN_ID:?set RUN_ID (e.g. run-20260703)}"
DECKS="${DECKS:-history-analysis/data/regent_v107_wins_filtered_decks.json}"
OUT_DIR="data/generated/search_policy"
SHARD_DIR="$OUT_DIR/shards"
BASE="$OUT_DIR/search_policy_teacher_base.generated.jsonl"
TAGGED="$OUT_DIR/search_policy_teacher_tagged.generated.jsonl"
mkdir -p "$SHARD_DIR"

echo "pulling s3://$S3_BUCKET/$RUN_ID/ ..."
if aws s3 ls "s3://$S3_BUCKET/$RUN_ID/search_policy_teacher_base.generated.jsonl" >/dev/null 2>&1; then
  aws s3 cp "s3://$S3_BUCKET/$RUN_ID/search_policy_teacher_base.generated.jsonl" "$BASE" --only-show-errors
else
  aws s3 sync "s3://$S3_BUCKET/$RUN_ID/shards/" "$SHARD_DIR/" --only-show-errors
  cat "$SHARD_DIR"/*.jsonl > "$BASE"
fi
echo "base: $(wc -l < "$BASE") decision groups -> $BASE"

echo "tagging groups (deckIndex -> group) ..."
python ops/aws-search-policy/tag-groups.py --jsonl "$BASE" --decks "$DECKS" --out "$TAGGED"

cat <<EOF

Base dataset is tagged and ready. Choose a weighting and train (all local, cheap):

  # example: light floor8, heavy late game
  python ops/aws-search-policy/reweight.py --jsonl $TAGGED \\
    --weights "floor8:0.10,act2Start:0.35,preAct2Boss:0.20,final:0.35" \\
    --out $OUT_DIR/train.jsonl

  cd search-policy-training && uv sync
  uv run prepare-dataset --input ../$OUT_DIR/train.jsonl
  uv run train-ranker && uv run export-model && uv run eval-ranker

Re-run reweight.py with any weights and retrain — no re-collection needed.
EOF
