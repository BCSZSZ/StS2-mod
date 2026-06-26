---
name: sts2-simulation-deck-json
description: Create CardValueOverlay simulator deck and shortline/midline/longline scenario JSON files from selected StS2 card lists or extracted run-history deck JSON. Use when Codex needs to turn runId/card/count/typeName information into data/manual-tags/simulation_decks/*.json plus matching data/manual-tags/simulation_scenarios/*.json for simulate-deck-scenario usage.
---

# StS2 Simulation Deck JSON

Use this skill after the user selects a deck or provides card-count information. The output is a committed deck fixture under `data/manual-tags/simulation_decks/` and matching committed shortline, midline, and longline scenario fixtures under `data/manual-tags/simulation_scenarios/`.

## Workflow

1. Prefer structured input from `sts2-run-history-deck`:
   `tmp/run-history-decks.json` with one or more reconstructed runs.
2. Run the `CardValueOverlay.Tools` C# command with a stable, descriptive `--name`.
3. Use names that encode source and scope, for example:
   `regent_run_history_1781920615_floor5_a10`.
4. Preserve `modelId`, `typeName`, `count`, `upgrade`, and source notes. Do not patch card values in deck JSON.
5. If upgrade levels appear, write them to the `upgrade` field. Scenario decks resolve upgraded forms from card facts; use `notes` only for source context or unresolved behavior.
6. Create three matching scenario JSON files in `data/manual-tags/simulation_scenarios/` that reference the deck with `deckFile`, record assumptions, and encode the default horizons in their names:
   `{name}_shortline.json`, `{name}_midline.json`, and `{name}_longline.json`.
7. When running a simulation for a deck, run all three default horizons:
   shortline `--turns 4`, midline `--turns 8`, and longline `--turns 16`; keep `--runs 1000` and `--seed 1` unless the user asks for another configuration.
8. In summaries, report credited/attributed value as value per direct play, not value per run. Per-run is acceptable only as secondary context.

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

Then create matching scenario fixtures. This example shows the midline file;
also create `_shortline.json` with `turns = 4` and `_longline.json` with
`turns = 16`:

```json
{
  "name": "regent_run_history_1781920615_floor5_a10_midline",
  "description": "Winning Ascension 10 Regent run-history deck reconstructed after floor 5.",
  "deckFile": "../simulation_decks/regent_run_history_1781920615_floor5_a10.json",
  "options": {
    "turns": 8,
    "runs": 1000,
    "seed": 1,
    "handSize": 5,
    "baseEnergy": 3,
    "baseStars": 0,
    "starsPersistBetweenTurns": false,
    "maxCardsPlayedPerTurn": 16,
    "maxBranchingCards": 8,
    "pmfBucketSize": 1
  },
  "variants": [
    {
      "id": "base",
      "label": "Base deck"
    }
  ],
  "assumptions": [
    "Deck values are not patched in this scenario."
  ]
}
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

## Simulation Runs

After creating the deck and scenario, run the three default horizons:

```powershell
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- simulate-deck-scenario --scenario data\manual-tags\simulation_scenarios\regent_run_history_1781920615_floor5_a10_shortline.json
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- simulate-deck-scenario --scenario data\manual-tags\simulation_scenarios\regent_run_history_1781920615_floor5_a10_midline.json
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- simulate-deck-scenario --scenario data\manual-tags\simulation_scenarios\regent_run_history_1781920615_floor5_a10_longline.json
```

The generated Markdown reports should be read with credited values as value per
direct play. This is the primary metric for card-play payoff; total values are
audit columns, and per-run values should not be the headline attribution metric.
