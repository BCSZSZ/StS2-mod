---
name: sts2-select-training-decks
description: Select and persist reproducible training deck samples for CardValueOverlay StS2 value simulations. Use when Codex needs to choose deck subsets from history-analysis deck JSON, lock a random seed, or verify selected deck counts for a user-specified group such as floor8, act2Start, or final.
---

# StS2 Select Training Decks

Use this for the deck-sampling atom of a direct play-value workflow. The deck
group is a user-provided experiment parameter, not a skill constant.

## Inputs

- `deckSource`: source history-analysis deck JSON.
- `deckGroup`: required user intent, for example `floor8`, `act2Start`, or
  `final`.
- `deckCount`: required sample size.
- `deckSeed`: required or defaulted reproducibility seed.
- `selectedDecksOutput`: output path named for the chosen group and count.
- `limitDecks`: optional smoke-test limit; do not use for the committed sample.

## Workflow

1. Confirm `deckSource` exists and contains at least `deckCount` entries in
   `deckGroup`.
2. Use the repository Tools estimator command for deterministic random
   selection until a more generic command name exists. Always pass the group
   and output path explicitly:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --deck-source <deckSource> --deck-group <deckGroup> --deck-count <deckCount> --deck-seed <deckSeed> --selected-decks-output <selectedDecksOutput> --limit-decks 1 --limit-forms 1 --runs 1 --max-branch 1
```

3. Inspect `selectedDecksOutput`; it must contain exactly `deckCount` decks and
   all must have `group == deckGroup`.
4. Keep the selected deck file as the reproducibility artifact for later runs.

## Acceptance

- The selected deck file is written to the requested path.
- Re-running with the same input parameters produces the same deck list.
- The command output reports the requested `selectedDecks` count.
