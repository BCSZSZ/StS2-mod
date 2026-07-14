# Persistent Power Simulation

This document records the simulator architecture for cards whose value comes
from a played Power that remains active across later events. Keep this logic in
the modeling/simulation layer. Facts extraction should only describe game-source
facts and evidence.

## Current Supported Powers

The authoritative list is `SupportedRuntimePowerKeys` in
`SimulationCardLibraryBuilder`, with install behavior in
`DeckMonteCarloSimulator`. Current supported groups are:

- Event-driven persistent powers: `ChildOfTheStars`, `BlackHole`.
- Stat and future-card modifiers: `Strength`, `Dexterity`, `Fasten`,
  `PrepTime`, `Parry`, `SeekingEdge`, `SwordSage`.
- Active resource, draw, and turn-state powers: `Automation`, `Furnace`,
  `Genesis`, `Mayhem`, `Nostalgia`, `Orbit`, `PaleBlueDot`, `Panache`,
  `Plating`, `RollingBoulder`, `Stratagem`, `TheSealedThrone`, `Tyranny`,
  `VoidForm`.
- Generated-card powers and generated-card payoff powers: `Calamity`,
  `SpectrumShift`, `Arsenal`, `PillarOfCreation`.

`ChildOfTheStars` gains block after stars are spent, equal to
`starsSpent * powerAmount`. The simulator converts that block through the
current layer's `blockToDamage * damageUnitValue`.

`BlackHole` deals AOE damage after star-gain and star-spend events, equal to
`powerAmount`. The simulator values that as
`powerAmount * damageUnitValue * aoeDamageMultiplier`.

The AOE multiplier is read from
`model_calibration.json` key `targetingPenalties.aoeDamageMultiplier`, with a
default of `1.3`.

Generated-card effects should use source-specific generation pools from a
manual JSON fixture under `data/manual-tags/`, rather than filtering the full
combat card library at resolution time. Keep pool IDs separate even when
simplified v1 contents overlap. The current agreed minimal pool choices are
`Calamity -> SolarStrike`, and `SpectrumShift` / `BundleOfJoy` /
`ManifestAuthority` / `Quasar -> UltimateDefend, UltimateStrike`; exclude
multiplayer-only cards from these pools. `BundleOfJoy`, `ManifestAuthority`,
and `Quasar` are Skills, but they use the same generated-card pool
architecture. Future accuracy work should expand the relevant JSON pool
contents while keeping the source-specific pool lookup model intact.

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
- resolves each card's static setup value from `card_setup_values.json` into
  `BeamSetupValue` / `PlaySetupValue`; every `CardType.Power` card receives
  `OncePerHandAvailability` search admission, while its play decision is made by
  finite-horizon continuation rather than a numeric floor;
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
- `DecisionValue` adds non-Power static/dynamic play setup and, at a turn-end
  leaf, the exact current turn-end Power payoff plus an analytic continuation
  bounded by `FutureTurns`. Power reachability is independent: the first legal
  availability remains armed until the Power is included in Top-B or admitted
  as the node's single extra candidate, even if its beam score is outside Top-B.
- delayed draw/energy/star/block contributes only when a next turn remains; a
  pure future-only Power on the terminal turn therefore loses to stopping, while
  a Power with a current turn-end trigger can still win.
- realized power value is credited to the source Power card through
  `PowerRealizedValue`.

This keeps `CardValueCreditSummary` readable:

- `DirectValue`: value from cards as they are played;
- `ForgeRealizedValue`: later value produced by Forge and credited to the Forge
  source;
- `PowerRealizedValue`: later value produced by active powers and credited to
  the Power source.

For estimating a Power card's value, use deck-level delta EV from adding one
copy of the Power to the matching reference deck. Source credits are diagnostic
gross realized payoff, not the primary value estimate. This distinction is
intentional:

- shortline delta can be negative for setup Powers and still be correct;
- numeric Powers can produce positive `PowerRealizedValue`,
  `EnergyRealizedValue`, `StarRealizedValue`, or `ForgeRealizedValue` while
  net deck delta is smaller or negative;
- flow and playability Powers can materially change delta EV while source
  credit remains zero;
- mixed Powers still use delta EV as card value, while source credits explain
  only the modeled numeric/resource components.

Keep source attribution in reports because it is useful for debugging, future
credit modeling, and explaining why the deck delta moved.

## Extension Rules

When adding another Power:

1. Extend `CardFactParser` only enough to extract source facts and raw evidence.
2. Add a supported `persistentPowerTrigger` parameter that names the game event
   and effect, for example `AfterStarsSpent:gainBlockPerStarSpent`.
3. Add a simulator behavior class behind `ISimulationPowerBehavior`.
4. Keep reachability and value separate: all `CardType.Power` cards use the common
   one-shot search-admission policy; add future payoff through the shared active-
   Power continuation framework, not a per-card always-play constant.
5. Add tests at three levels: parser facts, facts-to-simulation builder, and
   simulator realized-value accounting.

Do not mark unsupported powers as simulated. Preserve their facts and evidence,
let estimators or simulators warn, and implement support incrementally.
