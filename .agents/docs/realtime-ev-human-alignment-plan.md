# Realtime dEV Refactor And Human-Alignment Plan

Status: the dEV-only realtime path, paired confidence statistics, stable
counterfactual shuffle, independent 4/8/12 solves, and adaptive 15/30/45/60
scheduling are implemented in the workspace. Card-object continuation and
generator-admission rules are implemented; broader human-objective work remains.

Date: 2026-07-14.

## Decision Summary

Realtime card value has one metric only: deck delta EV (`dEV`). The earlier
proposal to retain or redefine `calc` is superseded and must not be implemented.

Accepted decisions:

1. Reward, shop, and non-owned-card inspection compare the current deck with the
   current deck plus the candidate.
2. Deck inspection removes one matching owned card for the counterfactual and
   compares that `baseline-1` deck with the current `baseline` deck.
3. Upgrade preview compares the current deck with the deck produced by replacing
   the inspected unupgraded instance with its upgraded form.
4. Realtime blocked-play simulations, blocked caches, per-play counts, and all
   `calc` UI fields are removed.
5. Realtime output uses paired uncertainty and adaptive batches instead of one
   fixed run count.
6. Stale `est` values are not a realtime correctness target. They should not
   share the primary value table with `dEV`; the existing value JSON may remain
   untouched until a separate retraining or data-removal decision.

The modeling simulator's generic blocked-play capability is not automatically
deleted by this refactor. Offline `estimate-direct-play-values` still uses it for
cards whose draw, create, move, or transform effects cannot be source-attributed.
It is retained only as an offline modeling feature, not as a realtime overlay
path.

## Evidence Behind The Priorities

The plan follows the investigation of the reported Regent reward screens:

- `Glimmer+` was nearly exactly reproduced with the current simulator, including
  its negative long dEV. This is evidence that the displayed result came from the
  current policy rather than from a rendering or deck-reconstruction error.
- The old `Charge` target policy transformed `Quasar+` every time it appeared as
  a candidate in the traced run. Changing only the target policy moved long dEV
  from strongly negative to strongly positive in the reconstructed deck.
- A blanket reversal of `Glimmer` target order was worse than the current
  approximation. The correct unit is the combined action "play this card with
  this selected target", evaluated in the current state.
- The current realtime default of 36 runs can move complex-card dEV materially.
  Same-seed baseline and normal simulations reduce some noise, but ordinary
  shuffling does not preserve the relative order of shared cards after one deck
  gains or loses a card.

The largest observed error is policy bias, especially target selection. Sampling
noise is also material, but more samples alone would produce a more precise
answer for the wrong policy. The implementation order therefore fixes metric
semantics and statistical foundations before tuning broad human objectives.

## One Metric, Three Counterfactuals

Let `EV_h(D)` be cumulative expected simulator value for deck `D` through horizon
`h`, where `h` is 4, 8, or 12 turns.

### Add basis: reward, shop, and non-owned inspection

```text
before_h = EV_h(D)
after_h  = EV_h(D + c)
dEV_h    = after_h - before_h
```

This answers: "How much does taking or buying this card change this deck over
the selected horizon?"

One baseline is shared by every candidate shown for the same deck signature.

### Remove-one basis: owned card in deck inspection

```text
before_h = EV_h(D - one exact matching c)
after_h  = EV_h(D)
dEV_h    = after_h - before_h
```

This answers: "How much value is this owned copy currently contributing to the
deck compared with not having it?"

The removed copy must match model id, upgrade form, and enchantment. The
counterfactual key must include that full card token. The current key shape that
omits enchantment from the remove-one baseline is not sufficient.

When several copies are otherwise identical, removing one canonical occurrence
is acceptable because the represented game state does not distinguish those
copies. Once stable starting-card identities exist, all surviving instances must
retain their identities across the pair.

### Replace basis: upgrade preview

```text
before_h = EV_h(D)
after_h  = EV_h(D - c + c+)
dEV_h    = after_h - before_h
```

This answers: "How much does this upgrade improve the current deck?"

The upgraded replacement inherits the removed card's stable starting identity,
so paired shuffling isolates the form change instead of also changing its random
position.

The existing implementation derives this by valuing both forms against the same
remove-one deck and subtracting their deltas. The refactor may keep that algebra
or expose a direct replace request, but the public UI meaning must be the formula
above.

## Realtime Runtime Cleanup

The first implementation phase should replace the realtime service cleanly.
It must not keep a hidden full/blocked compatibility mode.

Delete from the realtime path:

- `CardEvCalculationMode.Full` and `DeckDeltaOnly` branching;
- `RequestCardDeltaEv` as a distinct calculation API;
- `blockedByProbe` and blocked simulation persistence;
- realtime `BlockedPlayModelIds` and `BlockedPlayInstanceIds` option wiring;
- `CalcShort`, `CalcMid`, and `CalcLong`;
- probe play counts used only by the removed formula;
- starting-instance play-count tracking used only to block one owned instance;
- three-stage progress semantics tied to normal, blocked, and final work;
- overlay headers, upgrade columns, logging, and help text that mention `calc`.

Keep or reshape:

- a shared current-deck baseline cache;
- remove-one counterfactual caches;
- normal/add and replacement scenario caches;
- three independent 4-, 8-, and 12-turn simulations, each searched with its own
  horizon-specific policy;
- combat-time baseline warming and lower worker priority;
- stale-signature cancellation;
- adoption statistics remain visible only in a separate second table; they are
  not mixed into the primary dEV and uncertainty columns.

With no realtime per-play metric, candidate simulations can use the cheap
expected-value API instead of `SimulateTrackedCard`. The current full-deck
starting-instance play report is also unnecessary for realtime use.

### Target result shape

Each horizon should expose a value object equivalent to:

```text
HorizonDeltaResult
  Mean
  LowerConfidence
  UpperConfidence
  CompletedRuns
  SamplingState  // preview, refining, stable, maxUncertain, failed
```

`CardEvResult` then contains short, mid, and long `HorizonDeltaResult` values,
plus result identity, monotonic progress, completion, failure, and a version
counter for UI refreshes.

Do not retain nullable calc fields as placeholders. If a serialized cache schema
contains old calc/blocked data, bump the semantic version and ignore that cache.

## Statistical Foundation

Adaptive sampling must operate on paired run differences, not on separately
estimated normal and baseline confidence intervals.

For run `i` and horizon `h`:

```text
d_i,h = normal_i,h - baseline_i,h
mean_h = sum(d_i,h) / n
```

Store or accumulate, per horizon:

```text
n
sum(d)
sum(d * d)
```

The sample variance, standard error, and configured descriptive interval come
from the `d_i,h` samples. This captures positive covariance between the two
counterfactuals and is substantially tighter than treating them as independent.

### Planned-look interval rule

The overlay shows an ordinary paired Student-t interval at the configured
confidence level (default 95%) and current sample size. Adaptive stopping must
not repeatedly use that same interval as if only one decision had been made.

Runs are checked in 15-run steps, up to 60. The actual planned stopping looks
are derived from the effective minimum and maximum: ordinary cards default to
15/30/45/60 (four looks), while complex cards default to 30/45/60 (three).
Use a Bonferroni-adjusted two-sided stopping interval with per-look alpha
`(1 - confidence) / plannedLooks`. This is conservative and transparent. When
early stopping is disabled there is one decision at the maximum, so no repeated-
look correction is needed.

Display interval and stopping interval are deliberately different:

- displayed configured interval explains current uncertainty to the player;
- adjusted stopping interval decides whether refinement may end early.

The in-game settings expose search branch, turn depth, ordinary minimum runs,
maximum runs, complex-card minimum runs, confidence level, and the early-stop
toggle. Bonferroni look count is deliberately derived rather than configurable.

As of 2026-07-15, the runtime search defaults to branch width 3, eight fully
branched ordinary play decisions, a 64 resolved-play safety cap, and loop
detection enabled. Forced-prelude plays count toward the safety cap but do not
consume the eight ordinary branch decisions. The settings `TurnDepth` therefore
controls full branch-decision depth, not the total number of cards resolved.

### No uncalibrated neutral band

The first implementation does not invent a practical-neutral EV range. A
horizon is stable only when the adjusted stopping interval is wholly above or
below zero. A genuinely near-zero result therefore continues to the configured
maximum and is honestly labeled uncertain. A non-zero neutral band may be added
only after it is calibrated against reviewed human decisions.

## Run-Indexed Randomness And Stable Pairing

### Indexable run seeds

An adaptive extension must compute only the new run range. Introduce a run-range
API with semantics equivalent to:

```text
SimulatePrefixSamples(deck, options, startRun, runCount, horizons)
```

`SeedForRun(baseSeed, runIndex)` must be directly indexable. Extending 20 runs to
40 runs evaluates indices 20 through 39 and never repeats indices 0 through 19.

Required invariant:

```text
aggregate(run 0..19) + aggregate(run 20..39)
  == aggregate(run 0..39)
```

The equality should hold exactly apart from deterministic floating-point
accumulation order chosen by the implementation.

### Counterfactual-stable shuffle

The same sequential RNG seed does not sufficiently pair decks of different
sizes. Replace starting-deck and reshuffle ordering with deterministic random
priorities:

```text
priority = Hash(runSeed, shuffleCycle, stableCardIdentity)
```

Sorting by that priority produces a pseudorandom permutation while preserving
the relative order of shared cards across baseline and normal until game effects
genuinely make the states diverge.

Stable identity requirements:

- assign canonical distinct identities to duplicate starting cards;
- derive the normal and counterfactual decks from one full-deck identity map;
- an added reward probe receives a new identity without renumbering old cards;
- a removed owned card disappears without renumbering survivors;
- an upgraded replacement retains the removed instance's identity;
- transforms retain identity;
- copied and generated cards receive deterministic child identities from source
  event identity and occurrence ordinal;
- each reshuffle has an explicit cycle number.

This shuffle should become one clean simulator behavior, not a realtime-only
fork with two competing shuffle implementations. Deterministic archives will
change, so simulator cache semantics and exact-seed tests must be updated.

Large-run tests must verify that the new shuffle does not materially bias each
deck's marginal EV.

## Adaptive Sampling And Work Scheduling

### Batch schedule

Use the fixed checkpoints:

```text
20 -> 40 -> 60 -> 80
```

Initial defaults:

- every visible candidate receives 20 paired runs;
- cards with select, move, transform, create-card, or other high-variance
  mechanics receive at least 40 runs;
- ordinary cards may stop at 20 if all horizons are stable;
- uncertain cards extend to the next checkpoint;
- 80 is the hard realtime cap;
- a result still crossing zero at 80 is labeled uncertain,
  not forced into a confident sign.

Each horizon owns its sample count and stopping state. Work advances the horizon
with the fewest completed runs, so 4, 8, and 12 all publish a 15-run preview
before an unresolved horizon receives its 30-run refinement. A stable short
horizon does not force unnecessary short samples while mid or long continues.

### Reward-screen fairness

The current single queue can finish one candidate before the next begins. The
adaptive scheduler must operate on a deck evaluation cohort:

1. ensure 4-turn baseline samples 0..14;
2. compute candidate A, B, and C 4-turn samples 0..14;
3. publish a preview for all visible candidates;
4. repeat the preview pass for independent 8- and 12-turn problems;
5. determine which candidate/horizon pairs need 30;
6. extend only the matching shared baseline, then round-robin required pairs;
7. repeat for 45 and 60 while work remains relevant and within budget.

Never refine candidate A beyond 15 while candidate B still has no 15-run preview
for the same horizon.

For reward decisions, 45/60-run refinement should favor candidates whose
intervals overlap the best candidate or cross zero. Clearly
dominated candidates can stop earlier. A deck-view request has only one focal
card and may use the full budget when necessary.

### Work budget

Removing blocked calculation changes a three-card reward from approximately:

```text
old: 1 baseline + 3 normal + 3 blocked = 7 simulation streams
new: 1 baseline + 3 normal             = 4 simulation streams
```

The independent initial preview costs `15 * (4 + 8 + 12) = 360` simulated
turn-runs per stream. Across one baseline and three candidates that is 1,440,
versus `20 * 14 * 4 = 1,120` for the former shared-prefix preview: about 29%
more horizon work in exchange for solving the correct three decision problems.
Search-node growth and generator branches dominate this linear estimate, so
maximum runs alone are not a sufficient performance guard.

Add a per-cohort work budget calibrated against the old reward-screen wall time.
Count actual search nodes or measured batch time in addition to run count,
because a `Charge` run with target branching is more expensive than a simple
attack run. When the budget is reached:

- preserve every already-published result;
- mark unfinished results `maxUncertain` or `budgetUncertain`;
- do not fall back to a static estimate;
- never block the game thread waiting for refinement.

Keep the current worker priorities and reserved-core rules. Do not introduce
nested candidate-level and run-level parallelism.

### Execution slices and cancellation

A work item is one candidate/horizon batch, not an entire 60-run calculation. After
every batch:

- publish an immutable result snapshot;
- check the current deck/signature and screen cohort;
- drop obsolete work;
- allow another visible candidate to run;
- persist only a coherent checkpoint.

Batch boundaries are the primary execution slices. Do not add blocking sleeps.
If a single 15-run batch is too slow on the benchmark machine, reduce internal
sub-batch size while publishing only at the planned checkpoints.

Combat baseline warming should initially compute 15 runs for each independent
horizon only. Additional baseline samples are demand-driven; this avoids spending the full 60-run budget
during combat for a reward screen that may never request refinement.

## Overlay Contract

The primary realtime table should contain only dEV, its uncertainty, and sample
state. Do not show stale `est` or removed `calc` beside it.

Recommended narrow form:

```text
dEV     mean          95% CI  runs
short   +8.4     [+2.1,+14.7] n40ok
mid     -1.2    [-13.8,+11.4] n80?
long   -20.1   [-28.4,-11.8] n40ok

choice  stats
deck    12.3%
pick +0 24.5%
copies   1.18
```

Use ASCII-safe labels in runtime text. `?` means the interval is still being
refined or remained uncertain at the cap. Exact spacing can be shortened for
the reward card width.

Context headers must reveal the counterfactual:

- reward/shop/non-owned card: `dEV`;
- deck inspection: `keep dEV` or `owned dEV`;
- upgrade delta: `upgrade dEV`.

The underlying metric remains dEV in every case, but the header prevents a
player from interpreting remove-one contribution as add-one reward value.

Baseline and after totals may remain in an expanded diagnostic table, because
they explain the subtraction rather than introduce a competing card metric. In
deck view, label them explicitly as `without one -> current`; in reward view,
label them `current -> with card`.

Historical card-choice statistics remain visible as a separate second table;
they are not mixed into the dEV columns.

### Color rule

Color dEV directly from the displayed mean:

- green when mean dEV is positive;
- red when mean dEV is negative;
- neutral when mean dEV is zero;
- keep the confidence interval and run count colors unchanged;
- muted/gray remains available for pending or failed value cells.

### Refresh and progress

Publish UI values only at batch checkpoints to avoid visible number jitter.

Replace the old normal/blocked/final progress stages with:

```text
queued -> preview(n20) -> refining(n40/n60/n80) -> stable
                                             \-> max/budget uncertain
                                             \-> failed
```

The progress bar must be monotonic. A result that stops early jumps to complete;
it must not appear stuck at 20/80. The text sample count is the authoritative
detail.

Poll or pump at roughly 250-500 ms, but skip label rebuilds when the result
version has not changed. Scene-stabilization refreshes for reward and upgrade
screens remain separate from simulation-result refreshes.

## Priority 0: Lock Evidence, Semantics, And Budgets

This is the prerequisite planning/benchmark phase.

Deliverables:

- committed reproducible `Glimmer+` and `Charge` deck/scenario fixtures;
- concise expected behavior assertions: target choices and relative card
  ordering, not fragile exact Monte Carlo numbers;
- a small expert-decision corpus from the 77-win source and other reviewed hard
  cases, with pick ranking plus reason tags;
- latency baselines for reward, deck view, and upgrade preview on the known
  machine profile;
- a documented old work budget and p50/p95 time;
- exact formulas and UI labels from this plan accepted as tests.

Evaluation corpus reason tags should include at least:

- destroys a unique engine component;
- good when drawn but bad to add;
- draw dilution;
- insufficient remaining horizon;
- defensive survival need;
- scaling or Power setup;
- target-selection mistake;
- put-back timing mistake.

Success criteria:

- both reported cases are reproducible;
- the metric basis for every UI context is unambiguous;
- performance regressions can be measured before solver behavior changes.

## Priority 1: dEV-Only Realtime Refactor

Implement the cleanup and counterfactual contract before changing the solver.

Deliverables:

- one realtime request path with explicit add/remove-one/replace basis;
- baseline plus normal only;
- no runtime blocked cache or computation;
- no calc fields, play counts, columns, logs, or progress stages;
- dEV-only overlay with contextual headers;
- semantic cache bump;
- remove-one keys include upgrade and enchantment;
- current-deck baseline remains shared and prewarmable.

Tests:

- reward dEV equals `EV(D+c)-EV(D)`;
- deck-view dEV equals `EV(D)-EV(D-c)`;
- upgrade dEV equals `EV(D-c+c+)-EV(D)`;
- same model with different enchantment produces distinct remove-one keys;
- a three-card reward invokes four streams, never seven;
- searches for realtime calc/blocked names find no retained dead path.

Success criteria:

- displayed values preserve current dEV semantics within old Monte Carlo noise;
- reward wall time is materially below the blocked-era path;
- runtime UI and cache contain no obsolete calc contract.

## Priority 2: Paired Samples, Stable Shuffle, And Intervals

This phase makes dEV statistically measurable and resumable.

Deliverables:

- run-indexed sample-range API;
- stable starting-card identities and counterfactual shuffle;
- per-run total samples for independently configured 4/8/12 horizons;
- paired dEV accumulators;
- descriptive 95% and adjusted stopping intervals;
- cache/checkpoint representation for sample count and coherent statistics.

Keep raw baseline total samples per horizon in memory up to the 60-run cap so
one matching baseline can pair with several candidate batches. Candidate entries need only
their paired accumulators and coherent checkpoint metadata once differences are
formed. Persistence may store completed result statistics and baseline samples;
partial entries should be persisted only if they can resume without replaying or
double-counting runs.

Tests:

- 15 plus the 15-run extension equals a direct 30-run execution;
- shared starting cards preserve relative shuffle order across add/remove pairs;
- duplicate, transformed, copied, and generated identities are deterministic;
- marginal deck EV remains statistically unchanged at large run counts;
- paired standard error is no worse than independent-error calculation on the
  representative cases;
- cache reload cannot combine incompatible shuffle or sampling semantics.

Success criteria:

- the low-run-versus-80 instability in the reported complex cases is reduced or is
  honestly exposed by a wide interval;
- every displayed interval is computed from paired run differences.

## Priority 3: Bounded State-Aware Card Choices

Status: implemented in the shared card-object framework. This addresses the
observed `Charge`, `Glimmer`, and `CosmicIndifference` failures directly.

### Framework

Keep card identity and declarative parameters in `CardBehaviorCatalog`. Keep
state access, candidate enumeration, application, and search integration in
`DeckMonteCarloSimulator`.

The conceptual API is:

```text
EnumerateChoices(sourceCard, state, action, maxChoices)
ScoreChoiceContinuation(sourceCard, state, choice, turnsRemaining)
ApplyChoice(sourceCard, state, choice)
```

The same concrete choice object must drive:

- legality and exact target count;
- play-route scoring;
- state mutation;
- diagnostics and regression tests.

The search action is the pair `(play source card, choose targets)`. Do not select
targets after the play route has already been chosen using an unrelated static
sort.

Implemented limits:

- ordinary cards create no extra choice branches;
- registered select/move/transform actions expose at most three representative
  target plans;
- each preview uses a two-card beam, branches through depth two, and searches at
  most six remaining plays;
- `Charge`, `Glimmer`, and `CosmicIndifference` extend through exactly one next
  turn; `Begone` and `Guards` stop at the current turn;
- nested previews disable object lookahead, preventing recursive expansion;
- deterministic state-aware ordering is used when lookahead is disabled.

### Charge

Obey the real required transform count. Never silently transform fewer legal
targets because the heuristic dislikes the remaining choice.

For target `x` and replacement `r`:

```text
targetDelta(x) = reachableFutureValue(r)
               - reachableFutureKeepValue(x)
```

Choice score includes the sum of target deltas, source-card immediate value,
energy opportunity cost, and the value of the remaining current-turn play line.

The first implementation uses:

- the real remainder-of-turn search and one real next-turn rollout;
- target energy/star cost, current resources, upgrade state, and instance Replay;
- the actual generated-choice pool expectation for registered key cards such as
  `Quasar`;
- replacement reachability through the mutated combat piles.

`DisposableFodder` retains its explicit policy: Ethereal cards are excluded
because they disappear naturally at turn end, non-Ethereal Status is the first
transform tier, an unenchanted `StrikeRegent` is the second tier, and
`DefendRegent` follows it. A `StrikeRegent` carrying `TEZCATARAS_EMBER` belongs
to the remaining-card tier because the enchantment makes it a zero-cost
9-damage card. The state-aware search branches only within the last required
tier, so it cannot skip a higher-priority category to consume a lower one.
`Eternal` remains unrelated to combat-pile selection.

Representative target sets are the three lowest-continuation legal sets inside
the applicable hard fodder tier. Their final ordering comes from the bounded
continuation search, not the static card-object score.

`Charge` uses a fixed play-setup value of 31 and first-availability extra search
admission. To prevent that prior from forcing an empty or destructive play, the
route is illegal unless the draw pile contains two suitable non-Ethereal
targets. Status, unenchanted `StrikeRegent`, and `DefendRegent` are always
suitable. Every remaining-tier target, including an Ember-enchanted Strike,
must independently have lower continuation value than `MinionDiveBomb`; one
good target cannot make a destructive second target legal.

The shared transform-target constraint layer adds Charge-only protections for
deck resource balance and timing. A star-gain target is protected when removing
it would leave static star cost greater than static star gain plus 2.
`FallingStar` is protected unless reusable non-Ethereal Weak and Vulnerable
coverage remains. Power targets are protected outside the penultimate simulated
turn, `Stratagem` is always protected, and Charge itself is illegal on the final
simulated turn. These declarations do not change `Begone` or `Guards`.

### Glimmer and top-deck actions

After the draw resolves, evaluate put-back choices by:

```text
putBackDelta = nextTurnGuaranteedDrawBenefit
             - currentTurnOpportunityLoss
             + deckCycleContinuationEffect
```

Candidate generation should include at most:

- a valuable card that cannot realistically be played with current resources;
- the lowest current-turn opportunity-cost card;
- a high next-turn synergy card, replacing one of the above only when distinct
  and materially relevant.

For every candidate, rerun the remainder of the current-turn play search after
the put-back. This is why neither "always highest" nor "always lowest" is a
correct policy.

Use the same framework for `PhotonCut` and later hand-to-draw-top effects.

### Diagnostics

Diagnostics remain opt-in and do not allocate on normal realtime branches.
The current report records:

- source card and selected concrete targets;
- every concrete transform candidate and whether it was selected;
- state-aware candidate and replacement continuation scores.

Full rejected-plan continuation deltas and reason tags remain a later debugging
improvement, not part of the realtime path.

Success criteria:

- reported `Charge` scenarios do not burn `Quasar+` or another unique engine
  component when a materially cheaper legal target set exists;
- `Glimmer` choices change with energy, remaining playable cards, and next-turn
  state rather than following one global ordering;
- reward-screen time remains within the calibrated cohort budget.

## Priority 4: Adaptive Scheduler And Progressive Overlay

After paired sampling works and choice-branch cost is measurable, enable the
15/30/45/60 scheduler and interval UI described above.

Deliverables:

- cohort-based round-robin batches;
- each independently solved horizon stops only when its adjusted interval has a
  stable sign;
- minimum 30 runs for registered complex actions;
- decision-aware 45/60 refinement;
- monotonic progress and versioned UI updates;
- stale cohort cancellation at every batch boundary;
- maximum work/time/node budget with honest uncertain status.

Tests:

- all three reward cards publish 15-run previews for a horizon before any card
  reaches 30 on that horizon;
- stable ordinary cards stop early;
- complex/uncertain cards extend automatically;
- early-stopped horizon rows show their own complete run state while other rows
  continue refining;
- obsolete deck work cannot overwrite the current overlay;
- reward, deck, shop, and upgrade views all show their correct basis label;
- no UI update occurs when the result version is unchanged.

Success criteria:

- ordinary rewards are faster than the old fixed-36 blocked path;
- the player sees a useful preview promptly;
- expensive cases consume saved budget selectively instead of making every card
  pay the 60-run cost.

## Priority 5: Cheap Continuation Value For The Play Model

Choice-aware cards solve the known target bugs, but the general play policy can
still be myopic. Add a transparent continuation estimator only after the choice
framework is stable.

First use action-specific continuation features for `Charge`, `Glimmer`, and
`PhotonCut`. Then, if evaluation still shows broad play-order errors, implement a
lightweight `IStateValueEstimator` for current-turn search leaves.

Candidate features should reuse state already maintained by the simulator:

- next-hand and known draw-top quality;
- draw/discard/exhaust composition;
- turns to next reshuffle;
- next-turn energy, draw, block, and Stars;
- installed Powers and pending triggers;
- retained and unplayed hand quality;
- unique engine components still reachable;
- generated or transformed card reachability before the horizon;
- expected wasted energy and hand overflow.

Use a linear or table-driven estimator with versioned weights. It must be cheap
enough to evaluate at existing leaf nodes and must not perform nested multi-turn
rollouts.

Do not start with a neural model. Existing learned-state infrastructure may be
used later for an offline comparison, but stale teacher data is not a runtime
prior and should not determine the first human-alignment fix.

Evaluation:

- replay the expert decision corpus;
- measure reward top-1 agreement, pairwise ordering, and rank correlation;
- separately inspect simple damage/block cards to prevent regressions;
- record node count and p95 time, not only EV changes.

Success criteria:

- general play-policy agreement improves beyond the action-specific cases;
- runtime node count stays inside the established budget;
- features remain explainable enough to diagnose a wrong route.

## Priority 6: Human Objective Calibration

Only after metric, sampling, choice, and play-policy correctness are stable
should the underlying objective move closer to actual fight value.

The current objective can overvalue unlimited damage or block because it sums
abstract value without a concrete remaining enemy HP or incoming-damage cap.
Introduce this in two bounded steps.

### Encounter-pressure profiles

Sample one lightweight encounter profile per paired run using existing
layer/encounter data:

- hallway, elite, and boss mixture for the current layer;
- representative enemy HP or expected damage budget;
- expected incoming damage by turn;
- expected fight-length pressure.

The baseline and normal side of a pair receive the same sampled profile. This
adds no extra profile multiplier to run count.

### Capped utility

Track enough abstract fight state to apply:

- useful block capped by expected incoming damage after relevant modifiers;
- damage capped or diminished after expected lethal;
- value for earlier lethal when it prevents later incoming pressure;
- optional penalty for expected HP loss or failure to meet survival pressure;
- downside summaries such as paired P10 or probability of positive dEV.

Keep raw short/mid/long mean dEV visible. If a later horizon-weighted pick score
is introduced, it is an additional decision aid and never replaces the three raw
dEV rows.

Calibrate the small number of pressure/utility parameters against human rankings,
not invented numeric card values. Split evaluation by run so decisions from the
same deck do not appear in both tuning and validation sets.

Success criteria:

- defensive cards rise under real damage pressure without receiving unlimited
  block credit;
- slow scaling is rewarded in boss-like profiles but not automatically in short
  hallway profiles;
- overkill and post-lethal value no longer inflate long dEV;
- held-out expert ranking improves without unacceptable simple-card regressions.

## Cross-Phase Verification Matrix

Every implementation phase should run:

```powershell
dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
dotnet build CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -v minimal
dotnet publish CardValueOverlay.csproj -v minimal
```

Also verify by phase:

- semantic: add/remove-one/replace formulas and UI labels;
- deterministic: same seed/config produces identical samples;
- extension: adaptive batches equal a direct run range;
- statistical: interval uses paired differences;
- solver: `Charge`, `Glimmer`, `PhotonCut`, simple attacks, simple block,
  resource cards, Powers, duplicates, enchantments, Ethereal, and generated cards;
- scheduler: fairness, cancellation, early stop, cap behavior, cache reload;
- performance: p50/p95 reward time, deck-view time, run-stream count, search
  nodes, allocations, and combat responsiveness.

Do not launch Slay the Spire 2 unless the user explicitly requests interactive
validation. Publish/package checks and existing logs are the Codex-side runtime
verification boundary.

## Recommended Implementation Order

The order is intentionally strict:

1. lock evidence, formulas, labels, and budgets;
2. remove calc and realtime blocked computation completely;
3. add paired samples and counterfactual-stable randomness;
4. fix bounded target/put-back choices;
5. enable adaptive scheduling and interval display;
6. improve the general play continuation model;
7. calibrate encounter-aware human utility.

The rationale is:

- Priority 1 immediately removes the unwanted metric and recovers compute.
- Priority 2 makes later comparisons lower-noise and measurable.
- Priority 3 attacks the demonstrated policy bias rather than merely measuring
  it more precisely.
- Priority 4 spends compute according to the now-known variance and branch cost.
- Priorities 5 and 6 change broader decision policy and objective meaning, so
  they require the earlier regression and performance foundation.

## Explicit Non-Goals For The First Refactor

- no replacement calc metric;
- no realtime blocked simulation under a renamed API;
- no retraining or installation of stale `est` data;
- no unrestricted choice combinatorics;
- no nested multi-turn rollout inside realtime search;
- no neural model as the first play-policy fix;
- no full enemy combat engine in the dEV cleanup patch;
- no forced confident sign when the 60-run interval remains uncertain.

## Cross-Machine Handoff

The current workspace contains the dEV-only refactor, centralized
`CardBehaviorCatalog`, card-object continuation rules, generator `Top-B union
+k` admission, and independent-horizon scheduler. Generated benchmark outputs
under `data/generated/` remain local and Git-ignored; the durable branch summary
is `.agents/docs/search-branch-diagnostics-20260714.md`.

Before continuing on another machine:

1. pull `main`;
2. confirm the expected committed or intentionally dirty workspace state;
3. read this document and the card-object/card-behavior framework docs;
4. run Core and Modeling tests;
5. use `scripts/publish-local.ps1` for an interactive game check;
6. do not carry the superseded calc/action-advantage or shared-prefix design
   back into code.
