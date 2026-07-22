---
name: sts2-monster-matrix
description: Regenerate and audit CardValueOverlay monster move, encounter, deterministic pressure-matrix, and stochastic combat-intent artifacts. Use when Codex needs to repair extraction, prove intent transition probabilities, replay an encounter, or keep diagnostic matrices separate from the combat-aware transition model.
---

# StS2 Monster Intent And Matrix Data

Read `.agents/docs/monster-matrix-lessons.md` and
`.agents/docs/combat-aware-simulation-contract.md` before changing this
pipeline.

## Two Consumers, Two Contracts

The generated turn-action, damage-detail, and damage-matrix reports are
deterministic pressure diagnostics. Their representative sequences are useful
for auditing and static calibration reports, but they are not probability
distributions for the combat simulator.

The combat-aware intent compiler must instead preserve:

- concrete monster and encounter identity;
- current visible intent state and hidden state needed for future transitions;
- all reachable follow-up states;
- sourced branch probabilities and conditional predicates;
- multi-monster slot state, shared random variables, death, block, healing, and
  summons when relevant;
- explicit unsupported status for any unresolved transition.

Never copy a deterministic matrix representative sequence into a stochastic
combat transition and never invent a uniform distribution for an unknown branch.

## Relevant Artifacts

- `data/extracted/monster_move_profiles.generated.json`
- `data/extracted/encounter_patterns.generated.json`
- `data/manual-tags/monster_move_overrides.json`
- `data/manual-tags/combat_encounter_overrides.json`
- `data/generated/monster_encounter_turn_actions.*`
- `data/generated/monster_encounter_damage_details.*`
- `data/generated/monster_encounter_damage_matrices.*`
- `CardValueOverlay.Modeling/Extraction/MonsterMoveParser.cs`
- `CardValueOverlay.Modeling/Combat/`
- `scripts/generate_monster_encounter_*.py`

Generated extraction output is local evidence. Fix parser/compiler logic or
source-backed manual overrides; do not hand-edit a generated JSON result.

## Investigation Order

1. Reproduce the failing monster/encounter and record Ascension 10.
2. Trace encounter realization, slot selection, monster profile, current state,
   follow-up edge, branch probability, and action payload.
3. Compare extracted/decompiled source with any manual override.
4. Classify the issue as extraction, deterministic-matrix projection, stochastic
   intent compilation, or unsupported game semantics.
5. Fix the narrow upstream source and regenerate all dependent artifacts.
6. Replay the intent distribution, then run modeling tests and coverage audit.

## Strict Hazards

- Inline `FollowUpState = new MoveState(...)` assignments create graph edges.
- `RandomBranchState` branches need their actual `AddBranch` proportions.
- `ConditionalBranchState` remains unsupported until its predicate is modeled.
- Inherited state machines must be resolved through the direct base class.
- A missing next-state edge is not permission to repeat the current state.
- Shared starter indexes and slot offsets are joint random state, not independent
  per-monster rolls.
- Dynamic damage expressions must resolve at Ascension 10 or fail support.
- Event helpers, deprecated encounters, and empty patterns should be excluded
  with explicit reasons, not emitted as zero-pressure fights.
- Healing, block, summons, escape, invulnerability, and phase changes are
  physical actions; they cannot be reduced to attempted damage.

For deterministic matrices only, weighted representative sequences may remain
when documented in `monster-matrix-lessons.md`. That exception does not apply to
combat EV.

## Regeneration

Run from the repository root:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-monster-moves
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-encounter-patterns
python scripts\generate_monster_encounter_turn_actions.py
python scripts\generate_monster_encounter_damage_details.py
python scripts\generate_monster_encounter_damage_matrices.py
```

If a generator reports `status: failed`, stop and report its errors. Do not
describe a partially generated matrix as valid.

## Intent Replay And Verification

Replay the concrete encounter before trusting it:

```powershell
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  replay-monster-intents --encounter <modelIdOrTypeName> --turns 12
& $dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  validate-combat-portfolio --verbose --output data\generated\combat_aware
```

For deterministic reports, all-zero table and all-zero exact-slot counts must be
zero unless an exclusion is explicitly documented. For combat intent, verify
probability mass, reachable states, seeded replay, unsupported reasons, and
encounter-slot coupling. Coverage can remain No-Go after a correct narrow fix.

## Output

Report changed source paths, affected monsters/encounters, source evidence,
probability mass, replay sequence/distribution, matrix audit, test result, and
coverage impact. State explicitly whether evidence is deterministic diagnostic
or a stochastic combat transition.
