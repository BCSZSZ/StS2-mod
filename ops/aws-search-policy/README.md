# Remote search-policy teacher-data collection on AWS

Runbook + scripts to collect the search-policy distillation **teacher dataset**
(the "one-time large investment" from
[`docs/modeling/search-policy-distillation-plan.md`](../../docs/modeling/search-policy-distillation-plan.md))
on a rented multi-core box, then bring it back to train the ranker locally.

**Core strategy: collect a large, balanced BASE once; make weighting a cheap
local knob.** Renting a server is the expensive part, so we do it once and
generously - plenty of decisions from every deck group, branch-8 teacher, no
skimping. How much each act-stage should influence the ranker is decided *later*,
at training time, by resampling the tagged base (`reweight.py`). No re-renting to
try a different mix.

## Why EC2 Spot, not Fargate

CPU-only, embarrassingly-parallel, ~2 MB input, few-MB output. One large
compute-optimized Spot box maps 1:1 onto the work at ~70% off; Fargate caps at
16 vCPU/task and costs severalx more per vCPU-hour. Recommended:
**`c7a.16xlarge`** (64 vCPU AMD, 128 GB; 1 vCPU ~ 1 physical core).

## Two facts that shape the scripts

1. **`collect-search-policy-data` is single-threaded** (sequential deck-variant
   loop, one autoflushed writer - [`Program.SearchPolicy.cs`](../../CardValueOverlay.Tools/Program.SearchPolicy.cs)).
   One invocation = one core. We parallelize with a **deck-level work queue**:
   one task per deck (`--skip-decks i --limit-decks 1`), fed **biggest-deck-first
   (LPT)** into a `WORKERS`-wide `xargs` queue, then concatenated. Big/engine
   decks cost severalx the small ones, so isolating them as individual queued
   tasks (rather than lumping every deck into every shard) keeps all cores busy:
   a worker on a big deck is one of `WORKERS`; the rest clear cheap decks and
   pull the next. Volume is set primarily by `RUNS`; the exact decision-group
   count remains deck-dependent under the 12-turn standard.

2. **The branch-8 teacher is expensive and its cost is engine-driven, not card
   count.** Measured on end-of-act-2 decks (runs 40, turns 8): a 15-card *engine*
   deck took **27 min**; a 29-card deck took **71 s**. Bigger/later decks
   (act2Start -> preAct2Boss -> **final**, final being the largest) are the costly
   ones; floor8 is cheap. So the per-shard `timeout` is a **termination guard**
   (a pathological node can run to ~8^8), **not a cost lever** - set it generous
   and keep the teacher at branch 8.

## What ships to the cloud

- Source + committed data via `git clone https://github.com/BCSZSZ/StS2-mod.git`
  (deck files under `history-analysis/data/`, calibration, pools - all committed,
  including the 308-deck set below).
- The one git-ignored input: `data/extracted/card_facts.generated.json` (~1.5 MB),
  produced locally by `parse-card-facts`. `scp` it up. Nothing else - no game,
  no GPU.

## The deck set: `regent_v107_wins_filtered_decks.json`

Regent A10 wins at **v0.107.x** (the version `card_facts` is extracted from):
dashen's runs + the local player's own wins, x **4 snapshots** (`floor8` layer 8,
`act2Start` 17, `preAct2Boss` ~32, `final` 47). **316 decks / 85 runs** after
removing decks that contain any **truly-unsimulatable** card
(`data/manual-tags/unsimulatable_cards.json` - Alchemize, Splash, Mayhem,
Monarch's Gaze, Nostalgia, Panic Button, Royalties, The Gambit, Anointed, Rend;
cards the simulator cannot value under either strategy). Regenerate that list
from the eligibility engine after any simulator/version change. (`preAct2Boss`
was added after measuring wide search matters there -
[gap check below](#why-preact2boss-was-added).)

---

## Cost / time envelope

The teacher is a **forward-Q over `TEACHER_FORWARD_TURNS=4`** turns (realized
value), which is ~100x the old within-turn teacher and explodes on big/engine
decks - so keep the per-deck `SHARD_TIMEOUT` as a termination guard. Volume is
set by **`RUNS`** (~decks x RUNS x 24 groups); `RUNS=13` on the 316-deck set ~
**100k groups ~ 1-3 hrs on 64 vCPU, ~$3-5** Spot. More `RUNS` re-samples the same
decks; for more diversity add decks, not runs.

---

## Step 0 - Local prep (one time)

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-card-facts
```

Push source to `main` (repo rule: `main` only, personal `BCSZSZ` - see root `CLAUDE.md`).

## Step 1 - Provision (Spot)

```bash
export AWS_REGION=ap-northeast-1        # Tokyo; us-east-1 is cheapest Spot
export KEY_NAME=my-ec2-keypair
export S3_BUCKET=sts2-search-policy-$(whoami)
export INSTANCE_TYPE=c7a.16xlarge
# export TARGET_OS=al2023                       # optional: Amazon Linux 2023 instead of Ubuntu 24.04 (default)
bash ops/aws-search-policy/provision.sh   # creates S3+IAM+SG, launches, bootstraps, uploads card_facts
```

`TARGET_OS` picks the AMI, login user, and root device automatically (`ubuntu` ->
Ubuntu 24.04 / `ubuntu` / `/dev/sda1`; `al2023` -> Amazon Linux 2023 / `ec2-user`
/ `/dev/xvda`). `bootstrap.sh` detects `apt` vs `dnf`, so either OS works.

## Step 2 - Collect the base (on the box, in tmux)

```bash
ssh -i ~/.ssh/$KEY_NAME.pem ubuntu@<PUBLIC_IP>
tmux new -s collect && cd ~/StS2-mod

# Canary first: RUNS=1 is a quick pass over all decks (shakes out inputs, S3, rate).
WORKERS=60 RUNS=1 S3_BUCKET=$S3_BUCKET RUN_ID=canary \
  bash ops/aws-search-policy/run-collection.sh

# Full base: forward-Q teacher, deck-sharded LPT queue, syncing each deck to S3.
WORKERS=60 RUNS=13 S3_BUCKET=$S3_BUCKET RUN_ID=run-$(date +%Y%m%d) \
  bash ops/aws-search-policy/run-collection.sh
```

Detach with `Ctrl-b d`. If a group's shards keep hitting `SHARD_TIMEOUT` on engine
decks, drop *only that run's* `TEACHER_MAX_BRANCH=6` and continue - but prefer
raising `SHARD_TIMEOUT` first, since branch 8 is the full-quality teacher.

## Step 3 - Fetch + tag (local)

```bash
export AWS_REGION=ap-northeast-1 S3_BUCKET=sts2-search-policy-$(whoami) RUN_ID=run-YYYYMMDD
bash ops/aws-search-policy/fetch-results.sh
```

Pulls the base JSONL and tags every record with its group
(`data/generated/search_policy/search_policy_teacher_tagged.generated.jsonl`).

## Step 4 - Choose weights + train (local, cheap, repeatable)

Weighting is a free knob now - the collection is already paid for. Each try is a
few-second resample + a few-minute retrain; **no re-renting**.

```bash
# example weighting; try any mix you like
python ops/aws-search-policy/reweight.py \
  --jsonl data/generated/search_policy/search_policy_teacher_tagged.generated.jsonl \
  --weights "floor8:0.10,act2Start:0.35,preAct2Boss:0.20,final:0.35" \
  --out data/generated/search_policy/train.jsonl
```

```powershell
cd search-policy-training
uv sync
uv run prepare-dataset --input ..\data\generated\search_policy\train.jsonl
uv run train-ranker
uv run export-model      # place JSON at data/manual-tags/search_policy_ranker.json
uv run eval-ranker
```

`reweight.py` can down-sample any group and (default) over-sample a short one to
hit the target weights - the only constraint is the base must contain enough of
each group, which is why Step 2 collects generously.

## Step 5 - Tear down

```bash
aws ec2 terminate-instances --region $AWS_REGION --instance-ids <INSTANCE_ID>
```

---

## How weighting affects the result (and why it's low-risk)

The mix sets how many decisions from each act-stage the ranker trains on; it gets
sharper where it saw more. Crucially the ranker **only reorders which lines the
beam explores - it never enters the reported EV** (see the distillation doc's
"realized EV vs search heuristic"). So a sub-optimal mix at worst makes the ranker
slightly less sharp in an under-represented stage; it **cannot corrupt value
numbers**. That is why baking weights into training (cheap, reversible) rather
than into collection (expensive, one-shot) is the right split.

## Why `preAct2Boss` was added

Measured branch-2 vs branch-8 EV gap on a size-stratified 10-deck sample of the
new preAct2Boss decks (runs 40, turns 8, same seed):

| stat | value |
| --- | --- |
| aggregate gap (sumb8 vs sumb2) | **+16.3%** |
| per-deck gap median / mean / max | 7.1% / 14.4% / **76.8%** |
| decks with >=10% gap | 5 / 10 |

Wide search adds a lot of EV here, and this layer band (~32) sits in the
previously-uncovered gap between `act2Start` (17) and `final` (47) - exactly where
extra teacher coverage is most useful. High-gap states are also the most valuable
training signal (teacher and student disagree most). Hence the group was added to
the base deck set. (This measures the *room* to improve, not that the ranker fully
closes it - that is confirmed later via `eval-ranker` top-2 recall + a branch-8
benchmark on held-out decks.)

## Private-repo fallback / advanced sharding

See the git-archive tarball fallback and disjoint-sharding notes - same as before:
if `git clone` needs auth, push `git archive HEAD` + `card_facts.generated.json`
as a tarball and run `bootstrap.sh SKIP_CLONE=1`. For provably disjoint work per
shard (instead of seed diversity), partition by `--candidate` or `--skip-decks`
in `run-collection.sh`.
