---
name: sts2-simulation-deck-json
description: Create CardValueOverlay simulator deck JSON files from selected StS2 card lists or extracted run-history deck JSON. Use when Codex needs to turn runId/card/count/typeName information into data/manual-tags/simulation_decks/*.json for simulate-deck-scenario deckFile usage.
---

# StS2 Simulation Deck JSON

Use this skill after the user selects a deck or provides card-count information. The output is a committed fixture under `data/manual-tags/simulation_decks/`.

## Workflow

1. Prefer structured input from `sts2-run-history-deck`:
   `tmp/run-history-decks.json` with one or more reconstructed runs.
2. Run the `CardValueOverlay.Tools` C# command with a stable, descriptive `--name`.
3. Use names that encode source and scope, for example:
   `regent_run_history_1781920615_floor5_a10`.
4. Preserve `modelId`, `typeName`, `count`, and source notes. Do not patch card values in deck JSON.
5. If upgrade levels appear, record them in `notes`; current scenario decks use base simulation cards unless a separate scenario patch models the upgrade.

## Commands

From a run-history extraction JSON:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  write-simulation-deck `
  --input tmp\run-history-decks.json `
  --run-id 1781920615 `
  --name regent_run_history_1781920615_floor5_a10 `
  --description "Winning Ascension 10 Regent run-history deck reconstructed after floor 5." `
  --output data\manual-tags\simulation_decks\regent_run_history_1781920615_floor5_a10.json
```

From a plain text table:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  write-simulation-deck `
  --input tmp\selected-deck.txt `
  --name regent_manual_floor5_choice `
  --output data\manual-tags\simulation_decks\regent_manual_floor5_choice.json
```

Accepted text lines look like:

```text
4 CARD.DEFEND_REGENT DefendRegent
1 CARD.VENERATE Venerate
1 CARD.COLLISION_COURSE+1 CollisionCourse
```
