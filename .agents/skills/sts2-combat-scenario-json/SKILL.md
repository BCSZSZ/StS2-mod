---
name: sts2-combat-scenario-json
description: Create CardValueOverlay combat-aware deck and scenario JSON fixtures with concrete HP, encounter, monster, intent, horizon, and support expectations. Use when Codex needs reproducible 4/8/12-turn smoke or Exact-oracle fixtures for the information-state simulator.
---

# StS2 Combat Scenario JSON

Read `.agents/docs/combat-aware-simulation-contract.md`. These files are
deterministic fixtures for semantics and solver validation, not a substitute for
the twelve-cell training portfolio.

## Files

Create:

- one deck under `data/manual-tags/simulation_decks/`;
- matching shortline, midline, and longline scenarios under
  `data/manual-tags/simulation_scenarios/`;
- horizons of exactly 4, 8, and 12 turns unless the user requests an additional
  diagnostic fixture.

Use a shared deck, seed policy, and combat setup across the three horizons so
differences are attributable to horizon length.

## Scenario Contract

Keep the existing scenario envelope (`name`, `description`, `deckFile`,
`options`, and `variants`) and add a `combatPhase1` object containing:

```json
{
  "ascension": 10,
  "playerHp": 72,
  "playerMaxHp": 80,
  "encounterId": "concrete encounter id",
  "monsterHp": 59,
  "initialIntentStateId": "visible initial intent state",
  "hpContextId": "act1-weak",
  "expectedStatus": "supported",
  "oracle": "data/generated/monster_encounter_damage_matrices.generated.json"
}
```

Use source-backed monster HP, intent transitions, and encounter identity.
`expectedStatus` must be `unsupported` when any physical transition is unknown;
do not fill unknown actions with zero or invented probabilities.

## Physical Semantics

- Damage is actual enemy HP loss; cap overkill at remaining HP.
- Enemy block, healing, death, and slot state belong to the combat state.
- Player block has no terminal value and expires according to game rules.
- Player HP/max HP and HP calibration context are explicit inputs.
- Card identity and zones are preserved across draw, discard, exhaust, create,
  transform, and pile movement.
- Random outcomes are chance nodes with sourced probabilities.

Do not encode static card values, `blockToDamage`, source credits, or intrinsic
scores in a combat fixture.

## Validation

Run the modeling tests and the portfolio validator after editing fixtures:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  validate-combat-portfolio --verbose --output data\generated\combat_aware
```

For a supported Exact fixture, also compare 4/8/12 output against its committed
semantic expectations. A long runtime is a performance finding, not permission
to truncate stochastic branches silently.

## Output

Report all created paths, source evidence for HP/intent fields, hashes, expected
support status, Exact result, elapsed time, allocations, and any unresolved
physical semantics. Do not install values from a fixture-only result.
