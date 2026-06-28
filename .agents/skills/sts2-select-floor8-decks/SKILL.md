---
name: sts2-select-floor8-decks
description: Select and persist reproducible floor8 training deck samples for CardValueOverlay StS2 value simulations. Use when Codex needs to choose deck subsets from history-analysis/data/dashen_77_selected_100_decks.json, lock a random seed, or verify selected deck counts before floor8 play-value runs.
---

# StS2 Select Floor8 Decks

Use this for the deck-sampling atom of the floor8 direct play-value workflow.
The output is a committed deck sample JSON that later simulation tasks can
reuse exactly.

## Inputs

- `deckSource`: default `history-analysis/data/dashen_77_selected_100_decks.json`.
- `deckGroup`: default `floor8`.
- `deckCount`: default `16`.
- `deckSeed`: default `20260629`.
- `selectedDecksOutput`: default `history-analysis/data/dashen_77_floor8_random_16_decks.json`.
- `limitDecks`: optional smoke-test limit; do not use for the committed sample.

## Workflow

1. Confirm the source file exists and contains at least `deckCount` entries in
   `deckGroup`.
2. Use the repository Tools command that performs deterministic random
   selection as part of the estimator:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --deck-source <deckSource> --deck-group <deckGroup> --deck-count <deckCount> --deck-seed <deckSeed> --selected-decks-output <selectedDecksOutput> --limit-decks 1 --limit-forms 1 --runs 1 --max-branch 1
```

3. Inspect `selectedDecksOutput`; it must contain exactly `deckCount` decks and
   all must have `group == deckGroup`.
4. Keep the selected deck file as the reproducibility artifact for later runs.

## Acceptance

- The selected deck file is written under `history-analysis/data/`.
- Re-running with the same input parameters produces the same deck list.
- The command output reports `selectedDecks: 16` for the default workflow.
