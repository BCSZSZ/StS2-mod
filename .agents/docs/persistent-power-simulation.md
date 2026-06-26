# Persistent Power Simulation

This document records the simulator architecture for cards whose value comes
from a played Power that remains active across later events. Keep this logic in
the modeling/simulation layer. Facts extraction should only describe game-source
facts and evidence.

## Current Supported Powers

- `ChildOfTheStars`: after stars are spent, gain block equal to
  `starsSpent * powerAmount`. The simulator converts that block through the
  current layer's `blockToDamage * damageUnitValue`.
- `BlackHole`: after a star-gain event or star-spend event, deal AOE damage
  equal to `powerAmount`. The simulator values that as
  `powerAmount * damageUnitValue * aoeDamageMultiplier`.

The AOE multiplier is read from
`model_calibration.json` key `targetingPenalties.aoeDamageMultiplier`, with a
default of `1.3`.

## Source Fact Boundary

`card_facts.generated.json` may contain:

- the card's base game fields: id, type, cost, rarity, target, keywords, tags;
- dynamic vars such as `DynamicVar("BlockForStars", 2m)` and
  `PowerVar<BlackHolePower>(3m)`;
- upgrade source operations such as `DynamicVars["BlackHolePower"].UpgradeValueBy(1m)`;
- semantic facts such as `power` and `persistentPowerTrigger`;
- raw source evidence for the card and related power class.

It must not contain EV, play priority, simulation support flags, or realized
value. Those are consumer decisions.

## Simulator Architecture

`SimulationCardLibraryBuilder` converts a `CardForm` into a `SimulationCard`.
For supported persistent powers it:

- keeps the original action facts on the simulation card;
- suppresses static generic `power` contribution from `IntrinsicValue` to avoid
  double counting;
- assigns `SetupPriorityValue = 99` to every `CardType.Power` card for play
  search only;
- stores value conversion inputs such as `BlockValuePerBlock`,
  `DamageUnitValue`, and `AoeDamageMultiplier`.

`DeckMonteCarloSimulator` installs an `ActivePower` after a supported source
Power card is played. Power cards then leave the normal deck cycle through the
exhaust pile, while the active behavior persists across later turns.

The simulator dispatches typed events to installed powers:

- `StarSpent`: emitted once after a played card spends stars, regardless of the
  number of stars spent.
- `StarGained`: emitted once after a played card gains stars, regardless of the
  amount gained.
- `StarGained`: emitted once at the start of a turn when queued next-turn stars
  are applied.

Base stars at turn start and carried stars are not treated as gained events.

## Accounting

Reported value and decision value are intentionally separate:

- `Value` counts only realized effects during the simulated line.
- `DecisionValue` adds `SetupPriorityValue`; every Power receives setup
  priority `99` so the lookahead search strongly prefers playing Powers before
  measuring later payoff.
- realized power value is credited to the source Power card through
  `PowerRealizedValue`.

This keeps `CardValueCreditSummary` readable:

- `DirectValue`: value from cards as they are played;
- `ForgeRealizedValue`: later value produced by Forge and credited to the Forge
  source;
- `PowerRealizedValue`: later value produced by active powers and credited to
  the Power source.

## Extension Rules

When adding another Power:

1. Extend `CardFactParser` only enough to extract source facts and raw evidence.
2. Add a supported `persistentPowerTrigger` parameter that names the game event
   and effect, for example `AfterStarsSpent:gainBlockPerStarSpent`.
3. Add a simulator behavior class behind `ISimulationPowerBehavior`.
4. Keep setup priority rule centralized: all `CardType.Power` cards receive
   `SetupPriorityValue = 99`; do not add per-Power priority estimates.
5. Add tests at three levels: parser facts, facts-to-simulation builder, and
   simulator realized-value accounting.

Do not mark unsupported powers as simulated. Preserve their facts and evidence,
let estimators or simulators warn, and implement support incrementally.
