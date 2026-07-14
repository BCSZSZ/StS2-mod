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
- ordinary transform actions choose the lowest-value card objects;
- cards with a registered transform policy use that policy from
  `CardBehaviorCatalog`.

Ordinary actions still use the static `CardSearchScore`. Registered
`CardObjectDecision` actions instead use a state-aware continuation value and
may compare a bounded set of target plans. The continuation value includes the
card's current decision value, instance Replay multiplier, and any registered
generated-choice continuation. It is used only to choose or retain card
objects; reported EV remains realized combat value.

The choice score is `CardSearchScore`, so it includes direct value, the card's
static `BeamSetupValue` (resolved from `card_setup_values.json`), registered
dynamic beam setup, and light resource/action heuristics. This is selection
policy only; reported EV still comes from realized plays and credits.

## Move Semantics

`moveCardBetweenPiles` removes selected card objects from the source pile and
adds them to the target pile. `position:Top` inserts selected cards at the top
of the target pile while preserving the selected order.

Current examples:

- `Glimmer`: draw cards, then compare up to three put-back candidates using
  the rest of the current turn plus one next-turn continuation. A currently
  playable card pays an opportunity cost when it is put back.
- `CosmicIndifference`: compare up to three discard-pile cards using the same
  one-next-turn continuation before putting the selected card on draw top.
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

`Begone`, `Charge`, and `Guards` use the registered `DisposableFodder` policy:

1. Ethereal cards are excluded because they disappear naturally when left in
   hand at turn end.
2. Non-Ethereal Status cards are selected first.
3. An unenchanted `StrikeRegent` is selected second.
4. `DefendRegent` is selected after an unenchanted Strike.
5. A `StrikeRegent` carrying `TEZCATARAS_EMBER` falls back to the remaining-card
   tier because it is a zero-cost 9-damage card, not disposable starter fodder.
6. Remaining eligible cards use the state-aware, instance-aware continuation
   value from low to high. For `Charge`, each remaining-tier target must be
   individually worth less than its `MinionDiveBomb` replacement.

`Guards` additionally transforms all eligible cards whose keep score is lower
than the `MinionSacrifice` replacement score. `Eternal` is a permanent-deck
removal restriction and has no combat-pile meaning in this simulator.

`Begone` and `Guards` compare target plans through the end of the current turn.
`Charge` compares up to three legal target sets through the end of the current
turn and one additional turn. Status/plain-Strike/Defend priority is a hard
tier boundary: branching compares cards only inside the last required priority
tier and never trades away a higher-priority fodder tier for a lower-priority
one.

`Charge` keeps its fixed play-setup value of 31 and uses
`OncePerHandAvailability`, so its first legal opportunity extends the ordinary
Top-B candidate set by at most one branch instead of displacing the next-ranked
ordinary card. It is legal only when the draw pile contains two suitable
non-Ethereal targets. Status, unenchanted `StrikeRegent`, and `DefendRegent`
are always suitable; each remaining-tier card, including an Ember-enchanted
Strike, is suitable only when its state-aware continuation value is below
`MinionDiveBomb`. Both targets must pass independently, and a one-card draw pile
does not reduce the play gate from two to one.

`Charge` then applies source-specific target constraints declared through the
shared transform-constraint framework:

- after removing a star-gain target, protect it when remaining static star cost
  is greater than remaining static star gain plus 2;
- protect `FallingStar` unless the remaining active deck has reusable,
  non-Ethereal coverage for both Weak and Vulnerable (for example upgraded
  `KnowThyPlace`); exhausting coverage does not qualify;
- protect Power targets except on the penultimate simulated turn;
- always protect `Stratagem` from Charge;
- never play Charge on the final simulated turn.

These constraints belong only to Charge's catalog entry. `Begone` and `Guards`
keep their existing target eligibility and timing.

The static play-setup prior remains disabled for the other registered
card-object decisions. Those lines must win through realized effects, the
playable suffix, and (where declared) next-turn marginal value. A tiny tie
penalty makes an exactly neutral object-action line lose to stopping, while any
genuine positive margin still wins.

## Generated-choice Key Cards

Cards such as `Quasar` register `GeneratedChoiceContinuation` in the same
catalog. When its star cost is already available or reachable from active deck
resources, its keep value includes the exact expected maximum of its generated
choice pool. For a pool of `N` distinct candidates, drawing `K` without
replacement, and values sorted ascending as `v[i]`, the expectation is:

```text
sum(i = K-1..N-1) v[i] * C(i, K-1) / C(N, K)
```

This protects a key card because of the payoff represented by its declared
pool, not because the transform executor contains a `Quasar` name check. If
the pool is unavailable or the required resource is unreachable, the ordinary
continuation value is used.

## Bounded Search Cost

Object lookahead is deliberately local:

- at most the profile's three target plans/candidates;
- at most six remaining plays in each preview;
- a two-card preview beam, narrowing to one continuation after depth two;
- exactly one future turn; nested previews set lookahead to zero.

The limits live on `DeckSimulationOptions`, so realtime callers can reduce or
disable the extra search without changing card identity rules.

## Extension Rules

When adding another card-object action:

1. Extend `CardFactParser` only enough to record the source fact, amount,
   dynamic var, involved piles, target card, and raw evidence.
2. Declare card-specific behavior and parameters in `CardBehaviorCatalog`;
   keep the lifecycle implementation centralized in `DeckMonteCarloSimulator`.
3. Prefer a real target card for explicit `TransformTo<T>`.
4. Use a named simulator placeholder only for unresolved or random outcomes.
5. If the card mutates or moves card objects, declare a
   `CardObjectDecisionProfile`; use dynamic setup only for non-object setup
   priors.
6. Add tests at three levels: parser facts, facts-to-simulation builder
   warnings, and simulator entity behavior.

See `.agents/docs/card-specific-behavior-framework.md` for the general rule
used by all card-specific simulator behavior, not only card-object selection.
