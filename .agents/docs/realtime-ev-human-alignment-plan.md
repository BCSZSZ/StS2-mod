# Realtime `calc` And `dEV` Human-Alignment Plan

Status: planning only. This document intentionally contains no implementation.

Date: 2026-07-13.

## Purpose

Improve the in-game realtime `calc` and `dEV` values so they better match how a
strong human player evaluates a card, while keeping reward-screen computation
responsive and preserving headroom for Slay the Spire 2.

The installed `est` values are out of scope for the first implementation. They
have not been retrained after later simulator changes and must be treated as
stale reference data, not as a correctness target or as a realtime search
prior.

This plan was produced after investigating two supplied Regent reward screens:

- `Glimmer+` was nearly exactly reproduced on the reconstructed 14-card deck.
  At 36 runs and layer 18, reproduced `calc` was `-7.8/-8.8/-6.1` versus the
  screenshot's `-7.5/-9.5/-6.9`; reproduced `dEV` was
  `-12.1/-43.6/-42.6` versus `-12.9/-43.3/-45.3`.
- `Charge` transform tracing showed the old lowest-static-score policy
  transforming `Quasar+` every time it appeared as a candidate in the probe
  run (`82/82`), plus many transformations of `PhotonCut+`, `RefineBlade`,
  `HiddenCache+`, `Comet+`, and other components.
- An ablation that changed only the `Charge` target-priority policy moved its
  14-turn deck delta from `-21.2` to `+83.2` in the reconstructed deck. An
  ablation that changed only Ethereal/Eternal handling did not change that
  result. Target policy is therefore a direct cause, not merely correlated
  cleanup.
- A blanket `Glimmer` rule that puts back the lowest-score card was worse than
  the current highest-score approximation. The correct fix is joint,
  state-aware choice and play evaluation, not reversing one sort direction.

## Scope Boundary

The first implementation should change only realtime simulation and the shared
simulator mechanics required to make it correct.

In scope:

- redefine realtime `calc`;
- preserve and stabilize realtime `dEV`;
- make select/move/transform decisions state-aware;
- make the play decision account for the selected targets;
- reduce variance through stronger counterfactual pairing;
- use adaptive run counts and progressive UI updates;
- reuse current baseline precomputation, caches, and non-combat parallelism;
- add reproducible regression fixtures for the reported decks.

Not in the first implementation:

- retraining or installing `est` values;
- changing the runtime card-value JSON to hand-correct these examples;
- full enemy-by-enemy combat simulation;
- unrestricted multi-turn tree search;
- a new neural model;
- publishing or launching the game before the implementation and normal
  validation stages are complete.

Offline direct-play training may continue to use blocked-play experiments.
Removing the blocked run in this plan applies first to realtime `calc`, not to
all modeling commands.

## Current Realtime Semantics

The current realtime service computes three related simulations:

1. `baseline`: current deck without the candidate card;
2. `normal`: baseline plus the candidate, played normally;
3. `blocked`: the normal deck, but the candidate can never be played.

The displayed values are:

```text
calc = (normalEV - blockedEV) * runs / normalPlayCount
dEV  = normalEV - baselineEV
```

The `dEV` definition asks the correct card-pick question. The current `calc`
does not cleanly ask a direct-play question because the blocked card remains a
dead draw for the entire simulated combat. Its result mixes:

- the value of the selected play;
- the value of avoiding a dead card in hand;
- later dead draws of that card;
- long-term divergence between two policies;
- the candidate's play frequency.

This is why a card can show a large positive long-horizon `calc` while adding
the card still has a negative `dEV`.

## Target Metric Semantics

### New `calc`: conditional action advantage

Define horizon-specific `calc` as the average action advantage at states where
the chosen policy actually plays the tracked card:

```text
calc_h = mean(
  Q_h(state, play tracked card with chosen targets)
  - best Q_h(state, alternative action that does not play that instance)
)
```

Interpretation:

> Given that this card was drawn and the policy chose to play it, how much
> better was that play than the best available alternative at that decision?

Requirements:

- compare alternatives at the same state and with the same remaining
  resources;
- use the concrete tracked instance, not all copies of the model id;
- include the selected move/transform targets in the action;
- attach action-advantage events to `SearchResult` so only the final chosen
  route contributes; never record exploratory branches globally;
- retain per-horizon play count and play-on-draw rate alongside the mean;
- when the tracked card is not chosen, do not invent a per-play value;
- keep short/mid/long as separate 4/8/14-turn interpretations; monotonicity is
  not a mathematical requirement.

The current within-turn search already evaluates sibling play routes. Preserve
the best tracked-card route and the best alternative route at the relevant
node, then propagate the chosen route's action-advantage events with the
existing search result. The common case should require bookkeeping, not a
second full-combat simulation.

### Preserve `dEV`: paired deck-inclusion delta

Keep:

```text
dEV_h = EV_h(deck + one candidate copy under the improved policy)
       - EV_h(deck under its improved policy)
```

Interpretation:

> How much does adding this card change total deck value over this fight
> horizon, including draw dilution, times not played, setup cost, and all
> downstream effects?

The normal and baseline policies must each optimize their own legal lines. Do
not force them to play the same cards merely to obtain a cleaner delta.

### Optional human-readable decomposition

Record enough information to explain divergence between the two metrics:

```text
play contribution per run ~= calc * expected plays per run
other deck effects          = dEV - play contribution per run
```

The residual includes draw dilution, unplayed-card tax, pile manipulation,
transform losses/gains, cycle changes, and approximation error. The compact UI
can keep `calc` and `dEV`; an expanded diagnostic view can label a result such
as "good when played, bad to add" and show the dominant residual category.

## Runtime Solver Design

### 1. Eliminate the realtime blocked simulation

For a three-card reward, the current full calculation is approximately:

```text
1 shared baseline + 3 normal + 3 blocked = 7 full simulations
```

The target is:

```text
1 shared baseline + 3 normal-with-action-advantage = 4 full simulations
```

This is about a 43% reduction in full simulation calls before accounting for
bookkeeping. Use the released budget for targeted choice branches, variance
reduction, and adaptive sampling. Do not remove the existing deck-delta-only
path; it already skips blocked calculation.

Expected code shape:

- extend tracked simulation reports with per-turn chosen action-advantage
  sums, counts, and optional reason components;
- in `Search`, retain the best alternative route at a node when the selected
  first play is the tracked instance;
- carry suffix action-advantage events when the tracked play occurs later in
  the chosen line;
- summarize prefixes at turns 4, 8, and 14 in the existing single 14-turn
  pass;
- remove `blockedByProbe` use from realtime full mode after equivalence and
  performance tests pass;
- bump the realtime cache semantic key so blocked-era values cannot reappear.

### 2. Introduce bounded, card-specific choice policies

Card-specific identity and parameters should remain declarative in
`CardBehaviorCatalog`. Selection lifecycle and search integration should remain
centralized in `DeckMonteCarloSimulator`.

Add a reusable choice-policy layer with concepts equivalent to:

```text
EnumerateCandidateChoices(card, state, maxChoices)
ScoreChoiceContinuation(card, state, choice, horizon)
ApplyChoice(card, state, choice)
```

The API does not have to use these exact names. It must support:

- choosing one or more concrete card instances;
- exact game-required choice counts;
- a small list of representative choices rather than full combinatorics;
- the same choice object being used for selection, play-line scoring, action
  advantage, reporting, and diagnostics;
- deterministic tie-breaking.

Default limits:

- ordinary cards: no extra choice branches;
- registered select/move/transform cards: `maxChoices = 2` initially;
- allow at most 3 outside combat after benchmarks;
- never nest an unbounded choice expansion inside every play branch.

### 3. `Charge` policy

Preserve real semantics: when played, transform the required number of cards.
Do not silently transform fewer cards because only one target looks favorable.

For each candidate target, estimate:

```text
targetDelta = futureValue(MinionDiveBomb replacement)
            - futureKeepValue(target card)
```

Then include:

```text
chargeChoiceDelta = sum(targetDelta for required targets)
                  - current energy opportunity cost
```

This delta affects search decision value. If every legal target set is bad,
the legal `Charge` route remains available but should lose to the best route
that does not play `Charge`.

The target evaluator must consider at least:

- remaining horizon and probability of drawing the replacement in time;
- whether the target would naturally Exhaust or become Ethereal waste;
- immediate and future play value;
- Power, generator, resource, and scaling roles;
- upgrade and enchantment state;
- copy redundancy versus a unique component;
- active Power and deck synergies;
- whether the replacement is already unlikely to be played before combat end.

Initial representative target sets may include:

1. the lowest state-aware future-loss pair;
2. a disposable-fodder-priority pair;
3. optionally, a synergy-preserving pair if distinct and within budget.

Static category priority may remain a candidate generator. It must not be the
final value judgment.

### 4. `Glimmer` and other put-back policies

Do not use a blanket highest-score or lowest-score hand-card rule.

After drawing, evaluate candidate put-back cards with a value equivalent to:

```text
putBackDelta = next-turn guaranteed-draw benefit
             - current-turn opportunity loss
             + deck-cycle continuation effect
```

Candidate generation should include at most:

- a valuable card that cannot realistically be played with remaining current
  resources;
- the lowest current-turn opportunity-cost card;
- a card with unusually high next-turn synergy.

Evaluate the remaining current-turn play line after applying each candidate
choice. `PhotonCut` and other hand-to-draw-top actions should use the same
framework rather than acquiring another isolated rule.

### 5. Cheap continuation value before any full rollout

The first choice-aware implementation should use a transparent, inexpensive
continuation estimator. It may be specialized to the action instead of running
at every search leaf.

Useful state features include:

- draw-top and next-hand quality;
- draw/discard/exhaust pile composition;
- remaining deck-cycle length;
- next-turn energy, draw, block, and Stars;
- installed Powers and pending triggers;
- retained and unplayed hand quality;
- unique engine components still present;
- generated or transformed token reachability before the horizon.

Only after the specialized action estimator is validated should realtime
enable a general leaf `StateValue`. A later leaf evaluator should be a cheap
linear or table-driven function over already-maintained state features. Do not
begin with a neural model or a nested multi-turn rollout.

## Variance Reduction

### Counterfactual-stable shuffle

Using the same sequential RNG seed is not enough when one deck contains an
extra card: a conventional shuffle changes the relative order of many shared
cards.

Assign each stable card instance a deterministic random priority per run and
shuffle cycle:

```text
priority = Hash(runSeed, shuffleCycle, stableCardInstanceIdentity)
```

Sort by that priority. The marginal order remains a uniform random permutation,
while shared cards retain their relative order across baseline and normal decks
until the candidate's effects genuinely make the states diverge.

Requirements:

- starting duplicate cards receive stable distinct identities;
- the added probe receives a new identity without renumbering baseline cards;
- transformed cards retain instance identity;
- generated cards derive deterministic identities from their source event;
- reshuffles include a cycle number;
- same seed and semantics remain exactly reproducible;
- changing the random stream must bump cache semantics and update deterministic
  tests.

### Paired per-run statistics

Store compact per-run prefix totals or equivalent paired accumulators for
normal and baseline at turns 4, 8, and 14. Report:

- paired mean `dEV`;
- paired standard error;
- 95% confidence interval;
- completed run count.

Avoid estimating `dEV` uncertainty by treating normal and baseline as
independent samples.

## Adaptive Realtime Sampling

Replace one fixed run count with resumable batches that stay within the current
20-to-100 runtime configuration range:

```text
20 runs -> quick preview
40 runs -> first refinement when uncertain
80 runs -> high-variance refinement
100 runs -> hard realtime cap
```

Continue when any of these is true:

- the paired `dEV` confidence interval crosses zero;
- relative standard error exceeds the configured target;
- the card uses select/move/transform/create-card mechanics;
- choice diagnostics show multiple materially different candidate targets;
- `calc` action-advantage sample count is too small.

Stop early when the sign and practical magnitude are already clear. Ordinary
damage/block cards should usually stop at 20. High-variance cards such as
`Charge` may use the full budget.

Implementation requirements:

- simulation run seeds are indexable so a later batch extends rather than
  repeats earlier runs;
- cache entries store run count, sums, squared sums, and play/advantage counts;
- the UI displays preview values and updates them without flicker;
- queued work for an obsolete deck signature remains cancellable/skippable;
- no nested outer/inner parallelism oversubscription is introduced.

## Performance Budget

Use the current full realtime three-card reward computation as the wall-time
budget, not as a reason to increase every search knob.

Target envelopes:

- three ordinary candidates: faster than the current full calculation;
- one complex choice card plus two ordinary cards: no slower than the current
  full calculation at the same maximum run count;
- three complex choice cards: bounded by adaptive runs and a strict choice-node
  budget, with progressive results rather than a long blank wait;
- combat-time work remains BelowNormal priority and leaves the current reserved
  cores free;
- reward/map/event work may continue to use non-combat cores;
- baseline precomputation during combat remains enabled so the following reward
  screen reuses it.

Recommended initial limits:

```text
play beam                 existing realtime setting, default 3
play depth                existing realtime setting, default 8
choice alternatives       2
adaptive runs             20 -> 40 -> 80 -> 100
full multi-turn rollout   disabled
```

Add a global per-turn search-node budget before increasing choice alternatives
to 3. When the budget is exhausted, fall back deterministically to the best
state-aware candidate rather than reverting to an unrelated static rule.

## Human-Objective Alignment After Solver Correctness

Even a correct deck counterfactual cannot fully match expert judgment while EV
is an uncapped sum of abstract damage and defense. After the choice/search work
is stable, evaluate these additions separately:

- cap useful Block by layer/encounter incoming-damage pressure;
- model fight end or diminishing damage after expected lethal;
- sample fast hallway, elite, and boss pressure profiles within the existing
  run budget rather than multiplying runs by profile count;
- report downside or survival-sensitive risk alongside mean EV;
- derive an optional horizon-weighted pick score while retaining raw 4/8/14
  values for transparency.

Do not combine these calibration changes with the first choice-policy patch.
They change the meaning of the objective and need separate before/after reports.

## Display Contract

Recommended compact concepts:

```text
play delta   conditional action advantage per chosen play (`calc` replacement)
plays/run    expected direct plays, plus play-on-draw rate
deck delta   cumulative deck-inclusion value (`dEV`)
confidence   run count and paired uncertainty/stability marker
```

The existing short/mid/long rows remain useful. Labels and help text must state
that per-play action advantage is not expected to be monotonic with horizon.

Expanded diagnostics should be able to answer:

- which cards `Charge` transformed and when;
- which card `Glimmer` put back;
- why the chosen target set beat its alternatives;
- whether a negative deck delta came from draw dilution, target loss, low play
  rate, or realized combat value;
- whether the sign is statistically stable.

## Implementation Phases

### Phase 0: lock evidence and benchmarks

- Commit reusable deck/scenario fixtures for the reconstructed `Glimmer+` and
  `Charge` screenshots.
- Preserve the old screenshot-version outputs as ignored benchmark artifacts or
  concise committed expected summaries.
- Add transform/move choice diagnostics sufficient to explain target counts.
- Record default realtime wall time for one baseline plus three full candidate
  calculations on representative small, medium, and large decks.
- Make no metric semantic change in this phase.

Exit criteria:

- reported decks are reproducible from committed JSON;
- old behavior reproduces within Monte Carlo tolerance;
- benchmark command and machine profile are recorded.

### Phase 1: paired RNG and resumable statistics

- Implement stable card identities and counterfactual-stable shuffle.
- Return paired per-run 4/8/14 prefix totals.
- Add resumable aggregate structures and confidence intervals.
- Keep fixed run counts initially so this phase isolates variance changes.

Exit criteria:

- deterministic tests pass;
- baseline/normal marginals remain unbiased;
- 20/40/80/100 prefix results converge by extension;
- `Charge` 36-versus-100 instability is materially reduced.

### Phase 2: inline action-advantage `calc`

- Add chosen-route action-advantage events to search results.
- Produce realtime `calc` from those events.
- Remove realtime blocked simulation and blocked cache entries.
- Preserve offline blocked-play estimation commands.
- Bump cache semantics and update overlay labels/help.

Exit criteria:

- three-card reward uses one shared baseline and one normal simulation per
  candidate;
- ordinary numeric cards agree with direct realized value within expected
  tolerance;
- a dead-draw blocked card can no longer inflate `calc` through future repeated
  dead draws;
- wall time is below the old full calculation before choice branching is added.

### Phase 3: bounded choice-aware `Charge`, `Glimmer`, and `PhotonCut`

- Add the reusable candidate-choice framework.
- Implement `Charge` exact-count target sets and target-loss decision delta.
- Implement hand-to-draw-top candidate choices for `Glimmer` and `PhotonCut`.
- Attach choice data to action advantage and diagnostics.
- Use at most two choice alternatives initially.

Exit criteria:

- `Charge` no longer transforms `Quasar+` merely because its static score is
  slightly below `DefendRegent`;
- if all required `Charge` targets are strategically bad, the search can choose
  not to play `Charge` without violating transform count when it does play;
- `Glimmer` target choice changes with remaining energy and hand composition;
- no blanket highest/lowest put-back rule remains;
- three-card reward wall time remains within the old full-computation budget.

### Phase 4: adaptive realtime scheduling

- Add 20/40/80/100 progressive batches.
- Prioritize visible reward cards and the current deck signature.
- Persist resumable aggregates.
- Add stability/confidence display.

Exit criteria:

- clear ordinary cards stop early;
- high-variance choice cards receive more runs automatically;
- UI never waits for all 100 runs before showing a preview;
- combat remains responsive under the existing reserved-core policy.

### Phase 5: cheap continuation and human-objective calibration

- Evaluate action-specific continuation scoring first.
- Enable a general lightweight leaf `StateValue` only if action-specific scoring
  is insufficient.
- Separately test overblock, expected lethal, encounter mixtures, and downside
  risk.

Exit criteria:

- each added objective component has an isolated ablation;
- no stale `est` table is used as an unreviewed search oracle;
- final metric meaning is documented and reflected in overlay help text.

## Verification Matrix

Every semantic implementation phase should run:

```powershell
dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
dotnet build CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -v minimal
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
```

Required focused tests:

- same seed reproduces the same paired stream;
- adding a probe preserves relative order of shared starting cards before
  genuine state divergence;
- transformed cards keep stable instance identity;
- action-advantage events come only from the selected route;
- tracked duplicate copies remain instance-specific;
- `Charge` always transforms the correct legal count when played;
- `Charge` may be skipped when every target set is bad;
- `Glimmer` and `PhotonCut` evaluate multiple bounded put-back candidates;
- short/mid prefixes come from the same 14-turn runs;
- adaptive extension from 20 to 100 equals a direct 100-run execution;
- cache semantic mismatches invalidate old values;
- no double-parallel oversubscription.

Required reports:

- reconstructed `Glimmer+` screenshot deck, old versus each new phase;
- reconstructed `Charge` screenshot deck with transform target tables;
- at least one ordinary damage card and one ordinary block card as controls;
- small, medium, and large decks;
- 20/40/80/100 convergence tables;
- elapsed time and CPU behavior inside and outside combat.

Do not encode "the expert expects this card to be positive" as a unit test.
Tests should enforce correct mechanics, counterfactual semantics, stable
sampling, and explainable decisions. Expert judgment is validation evidence,
not a hard-coded numeric override.

## Open Decisions For The Implementation Session

1. Keep the overlay label `calc`, or rename it to `play dEV` / `play delta`?
2. Should action advantage use only the rest of the current turn in Phase 2, or
   wait until the cheap choice-continuation score exists?
3. What practical effect threshold should stop adaptive sampling early?
4. Should confidence be shown numerically, or as a compact stable/uncertain
   marker?
5. Should the optional residual explanation appear directly over reward cards
   or only in expanded diagnostics?
6. Should three choice alternatives be allowed automatically only outside
   combat, or remain a user setting after benchmarks?

Recommended starting decisions:

- implement Phase 2 action advantage with the current-turn route plus an
  explicit hook for later continuation value;
- use two choice alternatives;
- use 95% paired confidence intervals;
- start adaptive batches at 20 and cap at 100;
- keep raw `dEV` visible even if a later composite pick score is added;
- show a compact uncertainty marker and put detailed decomposition in debug
  output first.

## Cross-Machine Handoff

This upload includes the simulator and generated-data changes that were already
present in the `liao-home` worktree when the plan was written, including the
`CardBehaviorCatalog` refactor. It does not implement the realtime `calc`/`dEV`
plan described above. Treat the uploaded simulator state as the starting
baseline for Phase 0, not as a partial implementation of this plan.

On the next computer:

```powershell
git pull origin main
git status -sb
Get-Content -Raw .agents\docs\realtime-ev-human-alignment-plan.md
```

Before starting implementation:

1. confirm the checkout is on `main` and uses the correct personal remote for
   that machine;
2. inspect the uploaded `CardBehaviorCatalog`, card-object simulation document,
   realtime service, and simulator search code before starting Phase 0;
3. start with Phase 0 fixtures and benchmarks;
4. keep each semantic phase separate and delete superseded approaches cleanly;
5. do not retrain/install `est` values until realtime policy semantics settle.
