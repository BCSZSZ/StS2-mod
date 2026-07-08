---
name: sts2-monster-matrix
description: Regenerate, audit, and repair CardValueOverlay StS2 monster encounter turn-action, damage-detail, and damage-matrix reports, especially all-zero matrices, monster follow-up parsing, inherited move state machines, and strict failure on unresolved matrix paths.
---

# StS2 Monster Matrix

## Overview

Use this skill when working on monster encounter matrices or their upstream
generation path:

- `data/generated/monster_encounter_turn_actions.*`
- `data/generated/monster_encounter_damage_details.*`
- `data/generated/monster_encounter_damage_matrices.*`
- `data/extracted/monster_move_profiles.generated.json`
- `CardValueOverlay.Modeling/Extraction/MonsterMoveParser.cs`
- `CardValueOverlay.Modeling/Extraction/MonsterMoveProfileExtractor.cs`
- `scripts/generate_monster_encounter_*.py`

Before changing all-zero matrix behavior, read
`.agents/docs/monster-matrix-lessons.md`.

## Workflow

1. Inspect the generated matrix JSON and identify whether the problem is an
   all-zero table, an all-zero exact slot, or a strict generation failure.
2. Trace upstream in this order: matrix table, damage details row, turn-action
   slot, monster move profile, extracted/decompiled monster source.
3. Fix parser or generator logic, not just the generated JSON, unless the
   source data itself is intentionally hand-authored.
4. Regenerate all dependent artifacts in order.
5. Run the required all-zero audit and modeling tests before reporting the
   result.

If matrix generation writes `status: failed`, stop and report the listed
errors. Do not run the all-zero audit or describe the matrix as valid.

## Known Matrix Hazards

- Inline `FollowUpState = new MoveState(...)` assignments must create state
  graph edges.
- `RandomBranchState` and `ConditionalBranchState` must be flattened into all
  concrete move targets, including `AddState(...)` targets.
- `RandomBranchState` has no cross-monster balancing in the decompiled source.
  Resolve it in matrices with a deterministic weighted representative sequence
  derived from `AddBranch` proportions and offset by slot position. Continue to
  fail on unresolved `ConditionalBranchState` until its condition is modeled.
- Monster subclasses with no local `MoveState` definitions can inherit the
  state machine from a direct base class.
- Exact sequence simulation must fail when a future state path is ambiguous.
  Repeating the current state on `null` next-state data creates fake zero loops.
- Decimillipede segments share a random starter index with per-segment offsets.
- Shared random starter indexes for identical monsters can be represented by
  starter-offset tables when the alternatives are only slot rotations. Current
  examples: `TwoTailedRatsNormal`, `ScrollsOfBitingWeak`, and the first three
  slots of `ScrollsOfBitingNormal` with slot 4 fixed to index 2.
- Event helper monsters and empty/deprecated encounters should be excluded and
  listed, not emitted as all-zero pressure tables.
- Multi-line ascension getters, such as `DreadDamage`, must resolve to numeric
  base and ascension values.
- Unresolved dynamic damage expressions must fail generation instead of being
  emitted as zero.

## Regeneration

Run from the repository root:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-monster-moves
python scripts\generate_monster_encounter_turn_actions.py
python scripts\generate_monster_encounter_damage_details.py
python scripts\generate_monster_encounter_damage_matrices.py
```

## Verification

Always run modeling tests after changing C# extraction logic:

```powershell
dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
```

Then audit `data/generated/monster_encounter_damage_matrices.generated.json`.
The all-zero table count and all-zero exact slot count must both be zero. If
exact generation fails, report the emitted failure errors instead of treating
the matrix as valid.
