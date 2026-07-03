# Search-Policy Distillation: making a narrow beam match a wide beam

## Why this document exists

Card play-values come from a Monte-Carlo deck simulator whose per-turn play
search is a **beam search**: at each decision it keeps the top `MaxBranchingCards`
(the "branch width") candidate plays and recurses. Measurements show the branch
width dominates cost roughly like `B^(cards played per turn)`:

| Config (4 cards, 30/50/20 mix) | branch 2 | branch 4 | branch 8/16 |
| --- | --- | --- | --- |
| 40 decks × 100 runs | ~146 s | > 1 h (killed) | infeasible |
| 8 decks × 50 runs | 9.9 s | 655 s (≈66×) | hours / explodes |

Wider search also produces **better** play lines, so it changes the EV estimate
(branch 4 vs branch 2 shifts value-per-play, sometimes flipping the sign on
marginal cards like Glimmer). We want the accuracy of a wide beam at the cost of
a narrow one.

**Goal:** a one-time, offline learning investment that lets `branch 2 + model`
reproduce most of the quality of `branch 8`, so every future estimation run is
cheap. This is *policy distillation* / imitation learning (the AlphaGo idea: a
cheap learned policy guides/replaces expensive search).

Realistic target: **approach**, not equal. A narrow beam can only keep a few
candidates per node, so some wide-beam lines are unreachable. Aim for "branch 2 +
model ≈ branch 5–8 EV" and measure the residual gap.

---

## The principle in one paragraph

The branch2→branch8 EV gap exists only because the narrow beam **prunes away the
card that leads to the better line**. If a learned scorer reliably ranks the
"teacher-best" card inside the top-2 that branch 2 keeps, then branch 2 explores
the same good line branch 8 would have — same EV, branch-2 cost. So we train a
scorer to imitate what the expensive search chooses.

---

## What already exists (the v1 scaffold — do not rebuild)

A full teacher→student pipeline is already in the repo:

1. **Data collection (C#)** — `collect-search-policy-data`
   ([Program.SearchPolicy.cs](../../CardValueOverlay.Tools/Program.SearchPolicy.cs)).
   Runs the *student* search (`--max-branch 2`). At every decision node with ≥2
   legal cards it runs, for each legal card, a *teacher* rollout
   (`--teacher-max-branch 8`,
   [`TeacherRouteDecisionValue`](../../CardValueOverlay.Modeling/Simulation/DeckMonteCarloSimulator.cs))
   = "play this card first, then search with the teacher's wide/deep settings",
   yielding that card's **teacher route value** (a Q-value: how good the line
   that starts with this card turns out). It writes one JSONL record per decision
   ([`SearchPolicyDecisionGroup`](../../CardValueOverlay.Modeling/Simulation/SearchPolicyData.cs)):
   context features + per-card features + heuristic score + teacher route value +
   teacher rank.

2. **Training (Python/torch/uv)** — [search-policy-training/](../../search-policy-training/).
   `SearchRanker` MLP (input → 128 → 64 → 1, ReLU). Loss = **pairwise ranking**
   (learn to order cards by teacher value) **+ 0.2 · MSE** (regress the teacher
   value). Metrics: top1 accuracy, top2 recall, ndcg@2, mean/p95 regret. Exports
   `search_policy_ranker.json`.

3. **Inference integration (C#)** —
   [`NeuralSearchCardScorer`](../../CardValueOverlay.Modeling/Simulation/NeuralSearchCardScorer.cs)
   implements `ISearchCardScorer`; loaded via `--search-policy neural`, it scores
   each candidate card and reorders the narrow beam in
   [`SelectTopPlayableCards`](../../CardValueOverlay.Modeling/Simulation/DeckMonteCarloSimulator.cs).

Feature set is already rich: context (energy/stars/hand composition/each power's
amount…) + per-card (damage/block/cost/scaling/forge/star/action-kind counts…) +
one-hot card id
([`BuildContextFeatures`](../../CardValueOverlay.Modeling/Simulation/DeckMonteCarloSimulator.cs) /
[`BuildActionFeatures`](../../CardValueOverlay.Modeling/Simulation/DeckMonteCarloSimulator.cs)).

**Important framing:** the scorer only reorders which lines the beam *explores*.
It never enters the reported value (`PlayCard`/`PlayValue`). So a policy scorer,
right or wrong, can only change *which lines are searched*, never corrupt the EV
number's meaning. (The value network in §Full design is different — see the
"口径 / measurement basis" note there.)

### Operations checklist (run the existing scaffold)

```powershell
# 1. Collect teacher-labeled decision data (offline, heavy — this is the big cost)
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  collect-search-policy-data `
  --training-decks history-analysis\data\dashen_77_all_231_decks.json `
  --max-branch 2 --teacher-max-branch 8 --teacher-max-plays 8 `
  --runs 50 --turns 14 --candidate-decks 20 --max-groups 200000 `
  --output-jsonl data\generated\search_policy\search_policy_teacher.generated.jsonl

# 2. Train + export the ranker (Python/uv)
cd search-policy-training
uv sync
uv run prepare-dataset --input ..\data\generated\search_policy\search_policy_teacher.generated.jsonl
uv run train-ranker
uv run export-model      # writes checkpoint; place JSON at data/manual-tags/search_policy_ranker.json
uv run eval-ranker       # top1/top2/ndcg/regret on held-out split
cd ..

# 3. Use the model in a narrow-beam estimation run
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  estimate-direct-play-values --deck-count 40 --runs 100 --max-branch 2 `
  --search-policy neural --search-policy-model data\manual-tags\search_policy_ranker.json
```

Generated JSONL / checkpoints stay under `data/generated/` (git-ignored); only
the exported `search_policy_ranker.json` and docs are committed.

---

## Phase 0 first: measure the gap before investing

The single most important missing piece is an **apples-to-apples EV comparison**,
not more training. Before any large investment, build one comparison and run it on
a small **held-out** deck set (decks/cards NOT used for training):

- A. `branch 2` (baseline)
- B. `branch 2 + model` (candidate)
- C. `branch 8` (gold standard — slow, but only needed once, on a small set)

Compare per-card value-per-direct-play and cost. Success = B moves clearly toward
C while costing ≈ A. This number decides whether Phases 1–2 are worth it.

(There is no dedicated `evaluate-search-policy` command yet; Phase 0 includes
adding a thin one, or scripting three `estimate-direct-play-values` runs on the
same held-out decks and diffing the JSON.)

---

## Phase 0 results (2026-07-03) — the approach works

First end-to-end Phase 0 run, entirely on the existing scaffold.

**Setup.** Split `dashen_77_floor8_decks.json` into 57 train + 20 held-out eval
decks (disjoint). Teacher = branch 8 / depth 8; student = branch 2. Collected
**50,000** teacher-labeled decision groups on the 57 train decks (baseline decks,
no probe; runs 15, turns 8) in **39.6 s**. Trained the existing 128→64→1 ranker
(20 epochs, pairwise + 0.2·MSE).

**Ranker quality (held-out test split of the training data):**

| metric | value | meaning |
| --- | --- | --- |
| top1Accuracy | 0.71 | picks the teacher-best card 71% of the time |
| **top2Recall** | **0.917** | teacher-best card is in the model's top-2 92% of the time — i.e. it survives a branch-2 beam |
| ndcgAt2 | 0.967 | ranking quality |
| meanRegret | 0.41 | small value lost vs teacher-best |

**Realized EV — 3-way benchmark on the 8 held-out decks (50 runs, turns 8):**

| deck | branch 2 | branch 2 + model | branch 8 | gap (b8−b2) | closed |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 280.4 | 316.5 | 337.4 | 57.0 | 63% |
| 2 | 268.7 | 302.3 | 304.2 | 35.5 | 95% |
| 6 | 181.1 | 216.2 | 192.4 | 11.3 | overshoot |
| 7 | 308.4 | 316.5 | 312.0 | 3.6 | overshoot |
| … small-gap decks (1/3/4/5) | | | | < 4 | noisy |
| **sum** | | | | **116.2** | **98.2%** |

**Verdict: GO.** On decks where the wide beam clearly beats the narrow one
(deck 0/2), `branch 2 + model` recovers 63–95% of the gap; the aggregate is
~98% (dominated by the large-gap decks). Combined with top2Recall 0.917, both the
mechanism and the direction are confirmed: a one-time branch-8 teacher can lift a
branch-2 student close to branch-8 quality.

**Honest caveats (why this is a signal, not a final number):**
- Only 50 runs → per-deck EV is noisy; small-gap decks give unstable ratios and a
  couple of decks show `b2+model > b8` (overshoot = noise and/or the model guiding
  branch 2 to lines the branch-8 *heuristic* did not prioritize). Treat "~98%" as
  "closes the large gaps," not "exactly equals branch 8."
- Held-out but **floor8-only** (same group as training); generalization to
  act2Start / final is untested.
- Measured **deck-level EV on baseline decks**, not the real
  `estimate-direct-play-values` probe use case.
- Cost note: on these *small* floor8 decks branch 8 is itself cheap (6.4 s), so
  the saving is not visible here. The cost win is inherent — `branch 2 + model`
  stays at branch-2 cost regardless of deck size, while branch 8 explodes on big
  act2Start / final / VoidForm decks. Phase 0 proves the *quality* transfer; the
  *cost* advantage lives on the big decks.

**Reproduction (scratch paths under `data/generated/`, git-ignored):**

```powershell
# split done once into history-analysis/data/_phase0_floor8_{train57,eval20}.json
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  collect-search-policy-data --training-decks history-analysis\data\_phase0_floor8_train57.json `
  --candidate-decks 0 --runs 15 --turns 8 --max-branch 2 --teacher-max-branch 8 --teacher-max-plays 8 `
  --max-groups 50000 --output-jsonl data\generated\search_policy\phase0_teacher.generated.jsonl
# search-policy-training: uv run prepare-dataset/train-ranker/export-model  (export to a generated/ path, NOT the tracked model)
# then benchmark-training-decks on _phase0_floor8_eval20.json at --max-branch 2 (heuristic),
#   --max-branch 2 --search-policy neural --search-policy-model <phase0_ranker.json>, and --max-branch 8 (heuristic)
```

**Phase 1 next steps (see roadmap):** evaluate the real probe use case on
act2Start/final, run DAgger rounds, enlarge + balance the dataset across acts,
and raise runs (≥200) for clean EV numbers.

## Full design (what to add beyond v1 to actually close the gap)

### D1. Data & teacher (the "one-time large investment")
- **Label** = teacher route Q per candidate card (exists).
- **Teacher strength vs feasibility:** branch 8 rollouts themselves explode on
  VoidForm / generation-heavy states. Mitigate with a per-rollout time/node
  budget (fall back to the deepest completed iterative-deepening result), or use
  a branch 5–6 teacher. The teacher need not be perfect — only clearly better
  than the branch-2 student.
- **Scale & coverage:** target hundreds of thousands of decision groups sampled
  representatively across floor8 / act2Start / final (the 30/50/20 standard mix
  or wider). Offline, resumable, multi-core, decoupled from EV estimation.
- **DAgger (dataset aggregation) — the key upgrade:** v1 collects states only
  along the *branch-2 student's* trajectory. Once the model changes the policy,
  the states actually visited change too (distribution shift), so a model trained
  only on old states underperforms live. DAgger fixes this by iterating: run the
  *current* student+model, collect the new states it visits, label those states
  with the teacher, add them to the dataset, retrain. 2–3 rounds usually suffice.
  This is the difference between "v1 can rank" and "branch 2 truly tracks
  branch 8."

### D2. Models (two, not one)
1. **Policy / ranker (exists, enhance):** reorders the beam. Increase capacity or
   move to a **listwise / set model** (e.g., attention over the candidate set) to
   capture **cross-card synergy** (play the enabler before the payoff) that the
   current pointwise MLP can only see indirectly through context features.
2. **Value network `V(s)` (new, optional Phase 2):** predicts the state's
   EV-to-go under the teacher policy, used as a **leaf evaluator** so a
   narrow/shallow beam plus value bootstrapping approximates a deep search
   (AlphaZero's policy+value split). Integration point: when
   [`Search`](../../CardValueOverlay.Modeling/Simulation/DeckMonteCarloSimulator.cs)
   hits its depth/width limit, use `V(s)` instead of the truncation value.
   - **口径 / measurement-basis caveat:** unlike the policy scorer, `V(s)` feeds
     the *reported* EV, so turning it on **changes the computed number**, not just
     the search path. It must be treated as a **new, separately calibrated
     valuation basis** (a distinct "口径"), reported alongside — never silently
     swapped for — the current full-rollout basis. See the glossary.

### D3. Training objective (align to what we actually care about)
- v1 optimizes ranking accuracy (a proxy). Keep pairwise ranking but:
  - weight **Q regression** more (predict the teacher route Q directly — doubles
    as the value signal);
  - early-stop on **top-k hit rate with k = beam width (2)**, since what actually
    matters is whether the teacher-best card survives the narrow beam;
  - select the final model by **realized-EV gap** (§Phase 0 metric), not ndcg/
    regret alone.

### D4. Integration
- Policy: already wired (`ISearchCardScorer`, `--search-policy neural`).
- Value: add an `ILeafValueEstimator` hook at the search leaf; default off,
  explicit opt-in, reported as a new basis.

### D5. Evaluation loop (build in Phase 0, reuse forever)
`collect (teacher labels) → train → evaluate vs branch 8 on held-out decks →
if gap large, DAgger re-sample → retrain`, until the EV gap converges or returns
diminish.

---

## Phased roadmap with decision gates

- **Phase 0 (cheap, do first):** run the existing v1 end-to-end at a modestly
  larger scale, build the branch2+model-vs-branch8 comparison, and **quantify the
  gap** on held-out decks. This gate decides whether to invest further.
- **Phase 1:** if the gap is still large → add **DAgger (2–3 rounds)** + larger,
  balanced data + more capacity / listwise model. Usually gets the policy scorer
  to "near branch 6."
- **Phase 2 (optional, heavy):** if the policy scorer alone is insufficient
  (depth loss) → add the **value network** leaf evaluator as a new, recalibrated
  valuation basis.
- **Phase 3:** lock the model → make `branch 2 + model` the default estimation
  setting → every future run benefits.

## One-time cost (order of magnitude)
- Teacher data: hundreds of thousands of groups × per-card teacher rollout →
  offline, days, multi-core, resumable. This is the bulk.
- Training: minutes–hours (small MLP, single machine). DAgger repeats the collect
  step each round.
- Payoff: every future estimation run saves the branch-4→branch-8 cost multiplier
  (tens to thousands×). The one-time cost amortizes across all future runs.

## Risks & realistic expectations
- **Not exactly equal:** branch 2 keeps only the top-2; if the optimal line needs
  the 3rd-ranked card at some node (non-decomposable value, or imperfect model),
  it is missed. Target is "approach," not "equal."
- **Teacher ceiling:** the branch-8 teacher itself explodes on VoidForm-style
  decks, so the practical teacher may be branch 5–6, lowering the student ceiling.
- **Value network changes results:** it enters realized EV → it is a new basis,
  must be recalibrated, not a silent replacement.
- **Distribution shift:** one-shot collection (no DAgger) usually underperforms
  live.
- **Generalization:** always evaluate on non-overlapping held-out decks; v1's
  16-deck sample is too small — enlarge first.

---

## Glossary (plain language)

- **Beam search / branch width (`MaxBranchingCards`):** at each decision the
  search only follows the few most promising plays (the "beam"). Width 2 = follow
  2, width 8 = follow 8. Wider = more thorough but exponentially slower.
- **Policy distillation / imitation learning:** train a cheap model to copy the
  decisions of an expensive procedure (here, the wide search). "蒸馏" = distill
  the wide search's judgment into a small fast model.
- **Teacher / student:** teacher = the expensive wide/deep search that produces
  the "right answers" (labels). student = the cheap narrow search + model that
  learns to imitate the teacher.
- **Q-value / route value:** "if I play this card first and then keep playing
  well, how much total value do I end up with?" The teacher answers this by
  actually searching; the model learns to predict it.
- **DAgger (Dataset Aggregation):** an iterative training recipe for imitation
  learning. Round 1: train on the teacher's answers for the states the *baseline*
  visits. Then let the *new* model drive, see which *new* states it wanders into,
  ask the teacher for the right move in *those* states too, add them, retrain.
  Repeat. It fixes "distribution shift": the model is now trained on the states it
  actually encounters when it is in control, not only the states the old policy
  saw. Analogy: a driving instructor who doesn't just grade your practice route,
  but rides along on the routes *you* actually take and corrects you there.
- **Distribution shift / covariate shift:** the states a model sees in training
  differ from the states it faces once deployed (because deploying it changes the
  trajectory). Untreated, this quietly degrades live performance.
- **Value network `V(s)`:** a model that looks at a game state and estimates the
  remaining value without searching — used to "score the leaves" so a shallow
  search can act deep.
- **Leaf evaluator:** at the bottom of the search (depth/width limit), instead of
  assuming 0 future value, ask the value network "how good is this position?"
- **口径 / measurement basis:** the *definition* behind a number, i.e. how it was
  computed. Two numbers are only comparable if they share a 口径. The current
  card value is "expected value from a full Monte-Carlo rollout." A value-network
  leaf estimate is a *different* definition of the same quantity, so its numbers
  live on a different 口径 and must be recalibrated and labeled separately, not
  mixed with the rollout numbers.
- **Realized EV vs search heuristic:** *realized EV* is the value the simulator
  actually computes from resolving real card effects — the reported number. The
  *search heuristic / policy scorer* only decides which lines to explore; it never
  becomes the reported number. This is why a policy scorer is safe (can't corrupt
  results) while a value-network leaf evaluator is a genuine change of basis.
