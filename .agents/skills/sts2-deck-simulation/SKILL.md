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
- Generated-card pool config belongs in manually curated JSON under
  `data/manual-tags/`; each random generation source gets its own pool id, and
  future completeness work should expand the JSON contents instead of replacing
  the source-specific pool architecture.
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

The default horizons are shortline `4`, midline `8`, and longline `14` turns,
with `runs = 2000`, `seed = 1`, `maxBranchingCards = 64`, hand size `5`,
base energy `3`, and Regent base stars `3` unless the user specifies
otherwise.

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
- total EV
- variance and standard deviation on the EV/turn scale when available
- total variance, per-turn variance sum, and covariance contribution
- PMF / percentiles for risk shape when relevant
- `CardValueCreditSummary`, especially `AverageCreditedValuePerPlay`

Use card value attribution as value per direct play. Per-run attribution is
secondary context. `CardValueCreditSummary` splits:

- `DirectValue`
- `ForgeRealizedValue`
- `PowerRealizedValue`
- `EnergyRealizedValue`
- `StarRealizedValue`
- `TotalCreditedValue`

For Power card valuation, use the deck-level delta from adding that Power to
the matching baseline deck as the primary value estimate. Source attribution
credits are still important diagnostics, but they are gross realized payoff
after the Power is played, not the net card value. Numeric Powers can have
positive or large source credit while shortline delta is negative because of
draw dilution, energy cost, and setup timing. Flow or playability Powers such
as `Tyranny`, `VoidForm`, `Stratagem`, and generated-card Powers can have zero
source credit while the deck delta changes materially. Mixed Powers still use
delta EV for value; source credits explain components and may be smaller or
larger than the net delta.

Default simulation writeups should include all three horizons (`4`, `8`, `14`)
and, for cards beyond ordinary/basic starter cards, credited play EV by horizon.
Include generated or token cards such as `SovereignBlade` when they carry
material realized value.

Energy attribution is a reporting credit, not a search objective. Immediate
energy gain and next-turn energy gain use the same realized-credit rule:

```text
valuePerEnergy = turn played value / (baseEnergy + credited extra energy)
sourceEnergyCredit = valuePerEnergy * energyProvidedBySource
```

When multiple cards provide extra energy for the same turn, split the credit
by the amount of energy each source provided.

Stars are persistent. Do not attribute value to generated stars merely because
the turn had value. Attribute stars only when they are actually spent or when
the gain event directly triggers value.

```text
starSpendCredit = spent-card-or-trigger value * creditedGeneratedStarsSpent / totalStarsSpent
starGainTriggerCredit = triggered value split by stars provided by each gain source
```

Regent starts from `baseStars = 3`, and those first three spent stars are free
base resources that receive no card attribution. Generated stars are tracked
across turns and credited only from the fourth spent star onward. Star-producing
cards receive setup priority of about `5` EV per star (`StarGain +
StarNextTurn`) so the search is willing to play cards such as `Venerate` before
their payoff is visible.

For resource experiments, interpret pure resource cards through the delta they
produce, not through direct intrinsic value. A card like `Venerate` should have
no arbitrary direct value for stars; stars become valuable only when they enable
better later plays.

## Current Mechanics

- Draw effects reshuffle the discard pile when draw pile is empty, including
  draws caused during the turn.
- Stars persist across turns by default.
- Next-turn block is queued and credited on the following turn to the source
  card as delayed direct value.
- Vulnerable is simulated dynamically: attacks gain
  `floor(damageValue * 0.5)` while enemy Vulnerable is active.
- Weak remains a layer-dependent static estimate until enemy attack modeling is
  added.
- Forge is credited to the Forge source through realized value.
- All `CardType.Power` cards use setup priority `99` in simulator play search,
  so simulations strongly prefer playing Powers before observing later payoff.
- Runtime-supported Power mechanics include persistent star triggers,
  strength/dexterity-style modifiers, generated-card Powers, resource/flow
  Powers, and generated-card payoff Powers; see
  `.agents/docs/persistent-power-simulation.md` before extending this area.
- Generated-card cards and generated-card Powers must resolve random generated
  cards through their source-specific JSON pool, not by filtering the whole
  simulation card library at resolution time. Keep separate pool ids for effects
  such as `Calamity`, `SpectrumShift`, `BundleOfJoy`, `ManifestAuthority`, and
  `Quasar`, even when a simplified pool only contains one or two cards. Exclude
  multiplayer-only cards unless the scenario is explicitly modeling
  multiplayer. When a pool needs to become more accurate, update that pool's
  JSON card list and keep the simulator resolution logic unchanged.
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
