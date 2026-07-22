---
name: sts2-deck-simulation
description: Run, benchmark, and interpret CardValueOverlay combat-aware information-state deck simulations. Use for twelve-cell combat portfolios, monster intents, physical HP/block semantics, Exact oracle runs, explicit approximate-solver comparisons, coverage gates, 4/8/12 horizons, and paired candidate-versus-reference deck dEV.
---

# StS2 Combat-Aware Deck Simulation

Read `.agents/docs/combat-aware-simulation-contract.md` before acting. It is
the authority when legacy simulator code, older reports, or other documentation
disagrees.

## Current Boundary

- Treat the combat-aware simulator as offline research.
- Treat Exact as a small-state correctness oracle, not a production engine.
- Do not use the Phase 1 top-probability `Sparse` truncation as primary dEV.
- Keep `runtimeCandidate: false` while any coverage, solver, HP calibration, or
  portfolio-weight gate fails.
- Do not change `CardValueOverlay/data/card_values.json`, publish the mod, or
  launch the game unless the user separately approves a later runtime cutover.

## Value And Card-Value Semantics

Use:

```text
combatEV = actualEnemyHpLost - enemyHpRestored
         + Phi(finalPlayerHp) - Phi(initialPlayerHp)

dEV = combatEV(candidate deck) - combatEV(reference deck)
```

Pair both decks on the same combat sample and semantic random streams. Never
divide dEV by direct play count. Source credits, setup values, and realized
value per play are not combat-aware card values.

Report actual enemy HP damage, enemy healing, player HP loss, death, overkill,
unused block, turns to kill, and other physical ledgers as diagnostics. Block,
overkill, attempted damage, and unused block receive no direct reward.

## Data Inputs

Use the committed inputs:

- `data/manual-tags/combat_value_portfolios.json`
- `data/manual-tags/hp_continuation_calibration.json`
- `data/manual-tags/combat_encounter_overrides.json`
- `data/manual-tags/monster_move_overrides.json`
- `data/extracted/card_facts.generated.json`
- `data/extracted/monster_move_profiles.generated.json`
- `data/extracted/encounter_patterns.generated.json`
- stage-matched deck/HP snapshots selected by
  `sts2-select-combat-portfolio-samples`

Write generated review artifacts under `data/generated/combat_aware/`.

## Workflow

1. Inspect the latest Phase 1 review and generated coverage report.
2. Validate card, monster, encounter, deck, and sample support before running
   dEV.
3. Reproduce monster intent transitions for every vertical-slice encounter.
4. Run the 4/8/12 Exact oracle fixtures that fit explicit budgets.
5. Run an approximate solver only when it has a separate mode/status and a
   same-semantics Exact comparison.
6. Run paired candidate/reference dEV only on declared portfolio samples.
7. Report coverage and unsupported target mass before numeric results.
8. Stop at research output unless all cutover gates pass.

## Commands

Resolve the configured SDK first:

```powershell
$profileName = [Environment]::GetEnvironmentVariable("STS2_MOD_PROFILE", "User")
$profileJson = if ($profileName) {
  [Environment]::GetEnvironmentVariable($profileName, "User")
}
$profile = if ($profileJson) { $profileJson | ConvertFrom-Json }
$dotnet = if ($env:LIAO_DOTNET) {
  $env:LIAO_DOTNET
} elseif ($profile.dotnetPath) {
  $profile.dotnetPath
} else {
  "dotnet"
}
```

Coverage and smoke:

```powershell
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  validate-combat-portfolio --output data\generated\combat_aware
```

The command currently returns non-zero when the No-Go gate is working. Inspect
`phase1_coverage.*` and `phase1_smoke.*` before classifying it as a crash.

Intent replay:

```powershell
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  replay-monster-intents --encounter <modelIdOrTypeName> --turns 12
```

Solver benchmark:

```powershell
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  benchmark-information-state-solver --iterations 20 --workers 1,2,4 `
  --output data\generated\combat_aware
```

Paired deck delta:

```powershell
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  estimate-combat-aware-deck-delta --candidate <modelIdOrTypeName> `
  --horizons 4,8,12 --output data\generated\combat_aware
```

## Solver Comparison

Compare Exact and approximation on identical supported fixtures and report:

- root value and selected action;
- absolute and relative EV error;
- paired dEV error;
- death probability, loss-budget exceedance, P90, and CVaR error;
- canonical states, decision/chance nodes, outcome branches, and memo hits;
- wall time and allocated bytes;
- explicit budget-exceeded or unsupported counts.

Reject an approximation that looks fast only because it removes rare high-loss
outcomes. Never compare against a different combat model and call the speedup an
oracle-equivalent result.

## Portfolio Interpretation

Run Act 1/2/3 x Weak/Normal/Elite/Boss and horizons 4/8/12 independently.
Preserve every cell's target weight and unsupported mass. Do not report a
conditional-on-supported average as the primary portfolio dEV.

Use failure-inclusive HP snapshots for HP calibration. Winning-only floor
snapshots may be diagnostic deck examples but cannot establish `Phi`.

## Legacy Boundary

`simulate-deck-scenario`, `DeckMonteCarloSimulator`, source-credit,
play-delta-per-play, setup values, forced-Power ordering, branch 3/2/1, and
search-policy teacher collection are legacy-only. Use them only when the user
explicitly requests a legacy regression or migration comparison.

## Verification

After combat-aware code, fixture, or contract changes run:

```powershell
& $dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
& $dotnet build CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -v minimal
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
```

Also run the smallest representative Exact fixture first. Do not launch the game
for offline modeling validation.
