---
name: sts2-deck-simulation
description: Run, modify scenario variants, and interpret CardValueOverlay StS2 deck simulations in this repo, including deckFile scenarios, scenario-local DIY cards, Monte Carlo runs, resource effects, card value credits, variance/covariance, and value-per-direct-play attribution.
---

# StS2 Deck Simulation

Use the repo simulator and scenario JSON. Do not hard-code one-off experiments
in C# unless the simulator lacks a reusable mechanic the scenario format cannot
express.

## Current Data Shape

- Deck fixtures live under `data/manual-tags/simulation_decks/`.
- Scenario fixtures live under `data/manual-tags/simulation_scenarios/`.
- Scenario outputs live under ignored `data/generated/`.
- Card source facts come from `data/extracted/card_facts.generated.json`, not
  the removed `card_effect_terms.generated.json`.
- Prefer existing `deckFile` scenarios that point to committed deck fixtures.
- Use `sts2-simulation-deck-json` to create reusable deck/scenario fixtures
  from card lists or run-history JSON.
- Use `sts2-run-history-deck` to discover or reconstruct local `.run` decks.

## Default Runs

Unless the user asks for another horizon, run all three standard horizons:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- simulate-deck-scenario --scenario data\manual-tags\simulation_scenarios\<name>_shortline.json
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- simulate-deck-scenario --scenario data\manual-tags\simulation_scenarios\<name>_midline.json
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- simulate-deck-scenario --scenario data\manual-tags\simulation_scenarios\<name>_longline.json
```

The default horizons are shortline `4`, midline `8`, and longline `16` turns,
with `runs = 1000`, `seed = 1`, and the scenario's configured branching unless
the user specifies otherwise.

## Variant And DIY Edits

Do not reconstruct run-history decks or create reusable deck fixtures in this
skill. Work only inside an existing scenario when comparing what-if variants.

Use variant-local edits:

- `variants[].removeCards[]` removes matching cards.
- `variants[].addCards[]` adds scenario-local cards.
- `variants[].cardPatches[]` changes cards already in the scenario deck.

Represent DIY cards and patches through scenario-local `patch` fields such as
identity, cost/resource changes, `damage`, `block`, `intrinsicValue`,
keywords, and `addWarnings`. Prefer `damage` and `block` for simple DIY value
estimates. Use `intrinsicValue` only when the value is intentionally
hand-authored. Do not alter extracted card facts for what-if experiments.

## Solver Semantics

The simulator uses Monte Carlo deck draws plus recursive sequential play search.
For each sampled hand it:

1. Finds currently playable cards under current energy and stars.
2. Tries legal next plays in search-score order.
3. Applies costs, star gain, energy gain, draw, next-turn resources, Forge,
   Vulnerable, supported card-object actions, and supported persistent powers.
4. Recurses on the updated state.
5. Chooses the sequence with the highest decision value, while reports credit
   realized value.

This is better than a static 0-1 knapsack for StS2 hands because order matters.
It can play star-gain cards before star-cost cards, but it rejects that route
when unrelated value cards are better.

## Value And Attribution

Report `EV/turn` as the primary scale. Total EV is secondary context.

Always look at:

- `EV/turn`
- variance and standard deviation on the EV/turn scale when available
- total variance and covariance contribution
- PMF / percentiles for risk shape when relevant
- `CardValueCreditSummary`, especially `AverageCreditedValuePerPlay`

Use card value attribution as value per direct play. Per-run attribution is
secondary context. `CardValueCreditSummary` splits:

- `DirectValue`
- `ForgeRealizedValue`
- `PowerRealizedValue`
- `TotalCreditedValue`

For resource experiments, interpret pure resource cards through the delta they
produce, not through direct intrinsic value. A card like `Venerate` should have
no arbitrary direct value for stars; stars become valuable only when they enable
better later plays.

## Current Mechanics

- Draw effects reshuffle the discard pile when draw pile is empty, including
  draws caused during the turn.
- Vulnerable is simulated dynamically: attacks gain
  `floor(damageValue * 0.5)` while enemy Vulnerable is active.
- Weak remains a layer-dependent static estimate until enemy attack modeling is
  added.
- Forge is credited to the Forge source through realized value.
- Supported persistent powers currently include `ChildOfTheStars` and
  `BlackHole`; see `.agents/docs/persistent-power-simulation.md` before
  extending this area.
- Supported card-object actions include pile selection/move and transform; see
  `.agents/docs/card-object-action-simulation.md` before extending this area.

## Verification

After scenario or simulator changes, run:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
& $dotnet build CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -v minimal
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
```

When editing simulator mechanics, also run a representative scenario with a
small run count first, then the requested run count.
