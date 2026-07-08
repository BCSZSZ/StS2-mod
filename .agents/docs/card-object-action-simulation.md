# Card Object Action Simulation

This document records simulator rules for actions that choose and mutate card
objects in combat piles. Keep this logic in the modeling/simulation layer.
Facts extraction should describe source operations and evidence, not EV or play
priority.

## Supported Actions

`CardFactParser` should preserve enough source facts for these runtime actions:

- `selectCards` with `from:Hand`, `from:Draw`, `from:Discard`, or
  `from:Exhaust`;
- `moveCardBetweenPiles` with `from:<pile>;to:<pile>` and optional
  `position:Top`;
- named source-less `CardPileCmd.Add` cases whose real source is hard-coded in
  the simulator, such as `Anointed` moving random Rare draw-pile cards to hand;
- `transformCard` with optional `from:<pile>` and `card:<targetTypeName>`.

The simulator currently supports combat piles `Hand`, `Draw`, `Discard`, and
`Exhaust`.

## Selection Policy

When a card action asks the simulator to choose card objects:

- moving to `Hand` or `Draw` chooses the highest-value card objects;
- moving to `Discard` or `Exhaust` chooses the lowest-value card objects;
- transforming card objects chooses the lowest-value card objects.

The choice score is `CardSearchScore`, so it includes direct value, the card's
static `BeamSetupValue` (resolved from `card_setup_values.json`), registered
dynamic beam setup, and light resource/action heuristics. This is selection
policy only; reported EV still comes from realized plays and credits.

## Move Semantics

`moveCardBetweenPiles` removes selected card objects from the source pile and
adds them to the target pile. `position:Top` inserts selected cards at the top
of the target pile while preserving the selected order.

Current examples:

- `Glimmer`: draw cards, then move the highest-score selected hand card to the
  top of the draw pile.
- `Anointed`: move random Rare cards from the draw pile to hand up to the
  available hand-space limit. This is random, not highest-value tutor
  selection. Its beam/play setup is dynamic:
  `anointed.rareDrawAverageDecisionValue`, the average decision value of Rare
  cards currently in the draw pile.
- discard/exhaust effects: move the lowest-score selected hand card to discard
  or exhaust.

## Transform Semantics

`transformCard` replaces selected card objects in place.

- `TransformTo<T>` first resolves `T` against the simulation card library and
  uses the real parsed simulation card when available.
- random or unresolved transform uses `SIM.TRANSFORMED_CARD`, a generic
  1-cost attack worth 11 damage-equivalent value.

If a played source card transforms itself, the play and value credit remain on
the original source card, and the transformed replacement enters the discard
pile. This preserves source attribution for cards such as `RefineBlade`.

## Extension Rules

When adding another card-object action:

1. Extend `CardFactParser` only enough to record the source fact, amount,
   dynamic var, involved piles, target card, and raw evidence.
2. Keep selection policy centralized in `DeckMonteCarloSimulator`.
3. Prefer a real target card for explicit `TransformTo<T>`.
4. Use a named simulator placeholder only for unresolved or random outcomes.
5. If the card needs state-dependent play priority, register it through
   `.agents/docs/dynamic-setup-simulation.md` instead of hard-coding an
   isolated setup tweak.
6. Add tests at three levels: parser facts, facts-to-simulation builder
   warnings, and simulator entity behavior.
