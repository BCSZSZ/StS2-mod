# Dynamic Setup Simulation

Dynamic setup is a runtime setup prior whose value depends on the current
combat state. It is separate from the static `BeamSetupValue` and
`PlaySetupValue` resolved from `card_setup_values.json`.

## Boundary

- Static setup is installed by `CardSetupValueCatalog` and
  `SetupValueResolver`, then stored on `SimulationCard.BeamSetupValue` and
  `SimulationCard.PlaySetupValue`.
- Dynamic setup is declared in `DynamicSetupCatalog` and attached to
  `SimulationCard.DynamicSetups` by `SimulationCardLibraryBuilder`.
- Dynamic setup evaluation is installed in
  `DeckMonteCarloSimulator.DynamicSetupRules`.
- Dynamic setup contributes only to search/decision scores:
  `BeamSetupDecisionValue`, `PlaySetupDecisionValue`, and search-policy action
  features.
- Dynamic setup never enters `PlayValue`, realized `Value`, or source-credit
  accounting directly. Deck EV changes only through the real plays selected by
  the search.

## Install Standard

When adding a dynamic setup rule:

1. Add a descriptor to `DynamicSetupCatalog`.
2. Use a stable key, e.g. `cardName.effectDecisionValue`.
3. Declare the affected slots with `beam`, `play`, or both.
4. Document the formula, runtime basis, and reporting note in the descriptor.
5. Add the evaluator to `DeckMonteCarloSimulator.DynamicSetupRules`.
6. Keep nested dynamic setup disabled inside evaluator lookups if the formula
   inspects other cards, to avoid recursive self-amplification.
7. Add tests for builder metadata and simulator search behavior.
8. Confirm generated `simulation_card_library.generated.json` and
   `simulation_card_library.md` expose the descriptor.

## Reporting Standard

Setup tables should show these fields when available:

- `beamSetupValue`: static beam setup.
- `playSetupValue`: static play setup.
- `dynamicSetups.key`: stable dynamic setup key.
- `dynamicSetups.slots`: beam/play coverage.
- `dynamicSetups.formula`: state-dependent formula.
- `dynamicSetups.runtimeBasis`: state data inspected by the evaluator.
- state-specific dynamic setup values, if the report is produced from a
  concrete simulation state.

Do not fold dynamic setup into the static beam/play columns. A card can have
static `0/0` and still have dynamic setup.

## Current Rules

| Card | Key | Slots | Formula | Runtime basis |
| --- | --- | --- | --- | --- |
| Anointed | `anointed.rareDrawAverageDecisionValue` | beam/play | Average decision value of Rare cards currently in draw pile | Draw pile cards with `Rarity == Rare` |

For Anointed, the static setup remains `0/0`. The dynamic setup makes narrow
beam search recognize the value of tutoring Rare draw-pile cards without
recording that value as Anointed source credit. Its card value estimate should
therefore use play-delta.

`CosmicIndifference` no longer has a card-specific dynamic setup constant. Its
discard-to-draw-top choice is evaluated by the shared card-object continuation
framework through the next turn. Do not restore the former
`0.8 * maxDeckPlayValue` prior; it double-counts a payoff that the object-action
preview now evaluates directly.
