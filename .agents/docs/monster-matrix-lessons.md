# Monster Matrix Lessons

This note records the 2026-07-08 repair pass for
`monster_encounter_damage_matrices`. The hard rule is that a monster damage
matrix must not silently show all zeroes. A real zero-damage encounter must be
explicitly excluded or explained; an ordinary enemy matrix with all-zero rows or
slots is a parser/generator bug until proven otherwise.

## Fixed Incidents

The initial all-zero tables included:

- `LagavulinMatriarchBoss`
- `CultistsNormal`
- `SlitheringStranglerNormal` combo 4
- `DecimillipedeElite`
- `HunterKillerNormal`
- `TheObscuraNormal`
- `AxebotsNormal`
- `SlimedBerserkerNormal`
- `TheLostAndForgottenNormal`
- `DevotedSculptorWeak`
- `DeprecatedEncounter`
- `BattlewornDummyEventEncounter` combos 1, 2, and 3
- `MysteriousKnightEventEncounter`
- `TheArchitectEventEncounter`

The initial all-zero exact slots included:

- `Axebot/BOOT_UP_MOVE`
- `CalcifiedCultist/INCANTATION_MOVE`
- `DampCultist/INCANTATION_MOVE`
- `DevotedSculptor/FORBIDDEN_INCANTATION_MOVE`
- `Exoskeleton/ENRAGE_MOVE`
- `HunterKiller/TENDERIZING_GOOP_MOVE`
- `LagavulinMatriarch/SLEEP_MOVE`
- `Queen/PUPPET_STRINGS_MOVE`
- `SlimedBerserker/VOMIT_ICHOR_MOVE`
- `TwigSlimeM/STICKY_SHOT_MOVE`
- `SlitheringStrangler/CONSTRICT`
- `TheLost/DEBILITATING_SMOG`
- `TheForgotten/MIASMA`
- `TheObscura/ILLUSION_MOVE`
- event helper monsters `Architect`, `BattleFriendV1`, `BattleFriendV2`,
  and `BattleFriendV3`

## Root Causes

- Inline follow-ups were being missed. C# patterns such as
  `moveState.FollowUpState = new MoveState(...)` and assigning that inline
  result to another variable must create a real edge in the state graph.
- Branch follow-ups were being missed. `RandomBranchState` and
  `ConditionalBranchState` must be flattened into all concrete `MoveState`
  targets, including targets added through `AddState(...)`.
- `RandomBranchState` is per monster. Its source uses the owner creature's own
  `MonsterMoveStateMachine.StateLog` plus the passed `Rng`; there is no source
  evidence that one monster choosing branch A forces another monster to choose
  branch B. Matrix generation therefore uses a deterministic representative
  sequence from the source `AddBranch` weights, offset by slot position. A 50/50
  two-branch state becomes `ABAB...` for slot 1 and `BABA...` for slot 2. When
  repeat/cooldown rules exhaust all legal choices on that representative path,
  the sequence continues by weight order as a modeling assumption instead of
  failing the matrix.
- Inherited monster state machines were not followed. `MysteriousKnight` and
  Decimillipede segment subclasses inherit their move logic from base monster
  classes, so extraction must inspect the direct base class when the child has
  no local `MoveState` definitions.
- Exact matrix simulation stayed on the same state when the next state was
  ambiguous or missing. That creates fake all-zero exact tables. If a future
  path is not uniquely known, generation must fail with the encounter, slot,
  state, and turn in the error message.
- Decimillipede uses a shared random starter index plus per-segment offsets.
  The matrix needs explicit starter-offset tables instead of treating every
  segment start independently.
- Some encounters use a shared random starter index only to rotate identical
  monsters through the same starter order. `TwoTailedRatsNormal` and
  `ScrollsOfBitingWeak` can be represented with starter-offset tables and
  cyclic symmetry collapse; `ScrollsOfBitingNormal` has three rotated scrolls
  plus a fourth fixed index-2 scroll, so use `StarterMoveIdx=0` as the
  representative table for the symmetric first three slots.
- A single monster whose initial state is a real random branch, such as
  `Flyconid` or `LeafSlimeS`, is not solved by choosing an arbitrary branch.
  Leave it as a strict failure until the random-start policy is explicitly
  chosen.
- Event helper encounters and empty/deprecated encounters can produce all-zero
  matrices even though they are not pressure encounters. These should be
  excluded from the matrix report and listed in `excludedEncounters`.
- Some damage values are exposed through multi-line ascension getters, such as
  `DreadDamage`. The parser must extract the base and ascension numbers instead
  of falling back to zero.

## Regeneration Order

Run these from the repository root when changing monster action extraction or
encounter matrix logic:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-monster-moves
python scripts\generate_monster_encounter_turn_actions.py
python scripts\generate_monster_encounter_damage_details.py
python scripts\generate_monster_encounter_damage_matrices.py
```

The matrix generator depends on both the turn-action report and the damage
details report. Rebuilding only the matrix after parser changes can leave stale
intermediate data in place.

The matrix generator is strict. If any exact slot path, composition, or damage
expression cannot be resolved, it writes a failure report with `status:
failed`, lists every collected error, and exits non-zero. Do not treat that
file as a valid matrix.

## Required Audit

After regenerating matrices successfully, run an all-zero audit. Both counts
must be zero. If the JSON has `status: failed`, inspect `errors` first instead
of running the all-zero audit.

```powershell
$mat = Get-Content data\generated\monster_encounter_damage_matrices.generated.json -Raw | ConvertFrom-Json
$allZeroTables = @()
$allZeroSlots = @()
foreach ($enc in $mat.encounters) {
  foreach ($table in $enc.tables) {
    $rows = @($table.rows)
    if ($rows.Count -eq 0) { continue }
    $nonNullRows = @($rows | Where-Object { $_.totalDamage -ne $null })
    if ($nonNullRows.Count -gt 0 -and -not ($nonNullRows | Where-Object { [double]$_.totalDamage -ne 0.0 })) {
      $allZeroTables += [pscustomobject]@{ Encounter = $enc.typeName; Table = $table.title; Mode = $table.mode }
    }
    foreach ($plan in @($table.slotPlans)) {
      $pos = $plan.position
      $cells = @($rows | ForEach-Object { $_.cells | Where-Object { $_.position -eq $pos } })
      if ($cells.Count -eq 0) { continue }
      if (-not ($cells | Where-Object { $_.damage -ne $null -and [double]$_.damage -ne 0.0 })) {
        $allZeroSlots += [pscustomobject]@{ Encounter = $enc.typeName; Table = $table.title; Monster = $plan.monsterTypeName; Start = $plan.startStateId; Position = $pos }
      }
    }
  }
}
"ALL_ZERO_TABLES=$($allZeroTables.Count)"
$allZeroTables | Format-Table -AutoSize -Wrap
"ALL_ZERO_SLOTS=$($allZeroSlots.Count)"
$allZeroSlots | Sort-Object Encounter,Monster,Position | Format-Table -AutoSize -Wrap
```

Also run the modeling tests after parser changes:

```powershell
dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
```

## Review Heuristics

- A no-damage setup move is allowed only if its follow-up path reaches damage.
  If the follow-up path cannot be resolved exactly, fail the generation run.
- A branch state should never appear as a terminal zero-damage loop unless the
  source code really loops there.
- A monster subclass with no local `MoveState` definitions should trigger a
  base-class source inspection before being marked empty.
- If an encounter is excluded, the generated report must say why.
- New unknown dynamic damage entries must fail matrix generation. They should
  not become numeric zero silently.
