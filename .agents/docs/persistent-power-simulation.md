# Combat-Aware Persistent Power Simulation

This document defines persistent-Power behavior for the combat-aware
information-state simulator. It does not define legacy source-credit or setup-
value behavior.

## Boundary

- Implement combat-aware Powers under `CardValueOverlay.Modeling/Combat`.
- Keep extracted card and Power facts factual: identity, amount, trigger,
  target, timing, and source evidence.
- Do not put EV, play priority, static setup value, or attribution credit in
  extracted facts.
- A Power is supported only when every trigger that can affect the simulated
  horizon has concrete physical semantics.
- Leave partially modeled Powers `unsupported`; never keep the known portion and
  silently ignore the rest.

## Physical Semantics

Playing a Power must mutate combat state:

1. Pay its real Energy/Star cost.
2. Remove the card from the ordinary deck cycle as the game does.
3. Install a typed active-Power state with source identity and amount.
4. Dispatch future events in source/game order.
5. Apply resulting damage, block, resources, draw, card creation/movement,
   status, or other physical effects through the shared transition kernel.

Do not assign a Power direct value merely because it was installed. Its value is
captured by paired deck dEV after its future effects change actual enemy HP,
player HP continuation, or future legal actions.

Examples:

- `ChildOfTheStars` gains real player block after Stars are spent. Gaining block
  gives zero immediate reward; it matters only if later attacks would otherwise
  reduce player HP.
- A Strength/Dexterity Power mutates player stats and changes later physical
  damage/block resolution. It has no independent stat price.
- A generated-card Power resolves through its source-specific curated pool and
  a chance node. It receives no average generated-card constant.
- A Forge Power mutates concrete Forge/blade state. It receives no
  `ForgeRealizedValue` credit in the combat-aware objective.

## Event Contract

Represent events explicitly, including at least:

- turn start and turn end;
- before/after card played;
- Attack/Skill/Power played;
- Energy or Stars gained/spent;
- damage attempted, block absorbed, and HP lost;
- draw, shuffle, exhaust, create, move, and transform;
- monster intent resolved and monster death.

Document trigger ordering from decompiled source. When ordering, target choice,
or random probabilities are unresolved, mark the Power unsupported.

## Information-State Rule

Power decisions may use only currently visible state. Random generated cards,
random targets, and future triggers become chance outcomes at the point the game
reveals them. Do not inspect future hidden order to decide whether to play a
Power.

## Solver Rule

- Exact must enumerate every supported Power outcome on oracle fixtures.
- An approximate solver must use the same transition kernel and value equation.
- Do not force-play Powers or use fixed Power ordering as combat-aware policy.
  Playing or skipping a Power is an ordinary legal decision evaluated through
  continuation value.
- Do not grant future payoff analytically unless the approximation declares a
  separately validated value-function boundary.

## Extension Workflow

1. Extract or verify source facts and trigger order.
2. Add a typed active-Power state and include it in canonical encoding, copying,
   integrity validation, and mutation undo.
3. Implement every reachable trigger through the physical transition kernel.
4. Add parser/compiler tests that distinguish supported and unsupported forms.
5. Add deterministic physical tests for trigger order and actual HP/block
   effects.
6. Add apply/undo and canonical-key tests.
7. Add a small Exact oracle test and a paired deck dEV sign test.
8. Re-run portfolio coverage; do not manually mark the card supported.

## Current Status

The legacy `DeckMonteCarloSimulator` contains many Power approximations, but
those implementations do not establish combat-aware support. Port each Power
only after the requirements above are met. Until then, the combat-aware compiler
must report it as unsupported.
