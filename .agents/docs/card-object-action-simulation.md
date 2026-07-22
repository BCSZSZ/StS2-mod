# Combat-Aware Card-Object Actions

This document defines selection, movement, creation, and transformation of card
objects for the combat-aware information-state simulator.

## Boundary

- Preserve factual source operations in `card_facts.generated.json`.
- Put generic physical semantics in the combat transition kernel.
- Register identity-specific eligibility or target rules in the combat behavior
  catalog rather than scattering type-name branches.
- Do not use legacy `CardSearchScore`, setup-value constants, source credits, or
  generic damage-equivalent placeholders.
- Mark an action unsupported unless every reachable selection, target,
  replacement, and random outcome has concrete semantics.

## Card Identity And Zones

Track stable card-instance identity across Hand, Draw, Discard, Exhaust, and any
other modeled combat zone. A card instance must belong to exactly one zone.

Include every state that changes future legality or payoff in:

- canonical encoding and memo keys;
- mutation journal apply/undo;
- clone/integrity checks where cloning is used;
- semantic random keys for random selection.

Never replace instance-aware behavior with definition-count aggregation when
the cards differ by enchantment, Forge state, cost, replay count, upgrade,
retain/exhaust state, or another future-relevant property.

The current implementation assigns every physical card a stable internal
instance id, but deliberately excludes arbitrary labels from canonical solver
keys. Canonical identity is the future-relevant physical key (currently card
definition plus mutable Forge damage). Exact draw enumeration groups cards with
the same physical key and restores their combined hypergeometric probability;
it does not create one branch per interchangeable label. Draw-cache keys retain
the internal ids as an apply/undo safety detail, while policy actions select a
physical key and resolve one matching instance in the visible zone.

## Selection Is A Decision Or Chance Node

- Player-selected cards create legal decision branches using only visible
  information.
- Random selections create chance outcomes with sourced probabilities.
- Deterministic source rules select exactly the cards specified by the game.
- Do not choose the highest- or lowest-static-value card as a silent substitute
  for a player decision.
- Do not inspect hidden draw order while choosing from an unordered or hidden
  pile.

If the legal target set is too large for Exact, return an explicit budget result
or use a separately declared approximation that is measured against Exact
oracles.

## Move Semantics

`moveCardBetweenPiles` must:

1. resolve a legal source-zone card instance;
2. remove that exact instance from the source;
3. insert it into the destination at the real position/order;
4. fire any relevant movement events;
5. preserve identity and all mutable card state.

Top/bottom insertion and multi-card order must match source behavior. Unknown
ordering is unsupported rather than sorted for convenience.

## Create And Transform Semantics

- Resolve explicit created/transformed cards through the combat card catalog.
- Resolve random generation through the effect's source-specific curated pool.
- Keep separate pool ids for separate game effects even when Phase 2 fixtures
  temporarily share contents.
- Exclude multiplayer-only outcomes unless the scenario explicitly models
  multiplayer.
- `TransformTo<T>` replaces the selected instance with the real `T` definition
  and its correct mutable state.
- Random or unresolved transform must branch over a sourced pool. Do not use a
  generic 11-damage or other hand-priced placeholder.

## Value Semantics

Selection, movement, creation, and transformation receive no independent value.
Their contribution is captured when they alter actual enemy HP, player HP
continuation, or future legal actions. Primary card value remains paired deck
dEV; do not divide it by direct play count.

## Extension Workflow

1. Verify source operation, target set, ordering, and probability evidence.
2. Extend factual extraction only as needed.
3. Add generic transition semantics and identity-specific catalog constraints.
4. Add state encoding, mutation undo, and integrity support.
5. Test exact zone/identity preservation and apply/undo behavior.
6. Add player-choice and random-choice oracle fixtures.
7. Add a paired dEV sign test for the source card.
8. Re-run the strict coverage report.

## Current Status

The legacy simulator supports several card-object heuristics. They are not
combat-aware support evidence.

The combat-aware path now has stable instance ownership across Hand, known and
unknown Draw, Discard, Exhaust, and Play; reversible instance mutation; and
exchangeability-preserving chance enumeration. Static Forge creates an
unupgraded Sovereign Blade when no non-exhausted blade exists, then mutates the
damage state of every blade including exhausted copies. Dynamic Forge such as
Beat Into Shape remains fail-closed.

The next candidate slice introduces a pending player-choice continuation for
one-card visible moves to draw-pile top. It is intended for Cosmic Indifference,
Glimmer, Headbutt, and Thinking Ahead. This candidate is not coverage evidence
until its executable tests and strict coverage report run under the active
Application Control policy. Create, transform, multi-card selection, random
selection, and all other move shapes remain unsupported.
