# Combat-Aware Simulation Contract

This file is the canonical agent contract for new CardValueOverlay card-value
simulation work. It supersedes the legacy direct-play/source-credit workflow as
the primary modeling direction.

## Current Status

- The combat-aware path is offline research code under
  `CardValueOverlay.Modeling/Combat`.
- Phase 1 is `No-Go` for training and runtime cutover.
- `CardValueOverlay/data/card_values.json` must remain unchanged unless a later
  report has `runtimeCandidate: true` and the user explicitly approves install.
- Do not publish the mod or launch the game for offline modeling work.

Read `docs/modeling/combat-information-state-phase1-review.md` for the latest
measured coverage and performance state. Treat generated reports under
`data/generated/combat_aware/` as the current run evidence, not as committed
runtime values.

## Value Semantics

Use one physical combat value equation:

```text
combatEV = actualEnemyHpLost - enemyHpRestored
         + Phi(finalPlayerHp) - Phi(initialPlayerHp)
```

Apply these rules at every layer:

- One point of actual enemy HP loss is one value.
- Enemy block, overkill, attempted damage, broken block, and unused player block
  have no direct value.
- Player block has value only when it prevents actual player HP loss and thereby
  improves the final `Phi(HP)` term.
- Weak, Vulnerable, Frail, Strength, Dexterity, Powers, Stars, Forge, draw, and
  card movement change physical state or future legal actions. Do not assign
  them independent static value inside the combat-aware solver.
- Killing a monster has no arbitrary victory bonus. Its value comes from actual
  HP removed and from preventing future monster actions.
- A horizon continuation may return future player HP/death risk. It must not
  grant unplayed future attack value.

`Phi` is a monotone HP continuation utility with higher marginal cost near the
risk region. Phase 1 coefficients are sensitivity priors only. Do not infer a
single HP price from winning-run average damage. Empirical calibration requires
failure-inclusive combat HP traces plus HEAL/SMITH and other HP-trade revealed
preferences.

## Card Value

Use paired deck delta EV as the primary card value:

```text
dEV = EV(candidate deck, sample) - EV(reference deck, same sample)
```

- Pair the same deck snapshot, encounter realization, initial HP, visible
  intent, horizon, and semantic random streams.
- Aggregate dEV across the declared combat portfolio.
- Do not divide by direct play count.
- Do not use source-credit, play-delta-per-play, setup value, or card-specific
  attribution as a second primary scale.
- Physical ledgers may be reported as diagnostics, but they do not replace deck
  dEV.

## Portfolio And Sampling

The primary portfolio is the Cartesian set:

```text
Act 1/2/3 x Weak/Normal/Elite/Boss
```

Run horizons 4, 8, and 12 independently. Each sample must identify:

- portfolio cell and target weight;
- stage-matched deck snapshot;
- player current HP and max HP;
- concrete encounter realization and monster HP;
- visible initial monster intent;
- deterministic semantic run key.

Use failure-inclusive histories for HP calibration and representative sampling.
Unsupported samples remain in the denominator and preserve their target mass.
Never redistribute missing mass onto the supported subset.

## Solver Roles

Use the information-state model: the policy may observe the hand, public combat
state, known draw-top information, and current monster intent. It must not
observe hidden deck order or future random intents.

- `Exact` is the correctness oracle for small supported states.
- Exact budget exhaustion returns an explicit non-value result.
- Never disguise branch-one search, hidden-order determinization, or a truncated
  chance tree as exact.
- A production approximation must have a distinct mode and result status,
  deterministic semantic seeds, measured bias against Exact fixtures, and
  explicit time/allocation/error telemetry.
- The Phase 1 `Sparse` implementation that keeps only the highest-probability
  outcomes and renormalizes them is not approved for HP-risk evaluation. Replace
  it before using approximate results as primary dEV.

Retain Exact and an approximate solver only because they have different declared
roles: Exact is the oracle; the approximation is the scalable candidate. Do not
retain multiple competing production semantics.

## Support And Cutover Gates

Unknown card actions, card identities, monster transitions, encounter slots, or
transition probabilities are `unsupported`, never zero-value or uniform by
default.

Before any training cutover require all of the following:

- at least two fully supported encounters in every portfolio cell;
- at least 70 percent supported sample mass in every cell;
- at least 80 percent supported target-weight mass overall;
- paired dEV confidence/ESS gates pass for all requested horizons;
- the approximate solver passes Exact-oracle bias and risk-tail gates;
- HP calibration is empirical and user-approved;
- primary portfolio weights are user-approved;
- the generated report explicitly sets `runtimeCandidate: true`.

Runtime installation and mod publishing are separate, later gates.

## Current Commands

Run from the repository root:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }

& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  validate-combat-portfolio --output data\generated\combat_aware

& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  replay-monster-intents --encounter <modelIdOrTypeName> --turns 12

& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  benchmark-information-state-solver --iterations 20 --workers 1,2,4 `
  --output data\generated\combat_aware

& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  estimate-combat-aware-deck-delta --candidate <modelIdOrTypeName> `
  --horizons 4,8,12 --output data\generated\combat_aware
```

`validate-combat-portfolio` is expected to return non-zero while the coverage
gate is blocked; inspect its JSON/Markdown artifacts instead of treating the
current `No-Go` exit as a tool crash.

## Legacy Boundary

`DeckMonteCarloSimulator`, `simulate-deck-scenario`, direct-play attribution,
source-credit, blocked-play probes, setup-value heuristics, and search-policy
teacher data remain legacy implementation surfaces while existing code is being
replaced. They may be used only for an explicitly requested legacy regression or
migration comparison. They must not produce new primary training values or
justify combat-aware solver correctness.
