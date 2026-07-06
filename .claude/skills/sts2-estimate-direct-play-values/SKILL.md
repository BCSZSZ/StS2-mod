---
name: sts2-estimate-direct-play-values
description: Run dry, benchmark, or formal direct play-value simulations for CardValueOverlay. Use when Claude needs to estimate value per direct play from user-specified training deck groups, horizons, run counts, branch size, candidates, batching, and parallelism.
---

# StS2 Estimate Direct Play Values

Use this for the simulation atom of a direct play-value workflow. Deck group
and horizons come from the user request. Do not assume `floor8`, `shortline`,
or `midline` unless the user specifies them.

## Inputs

- Deck sample: `--deck-source`, `--deck-group`, `--deck-count`,
  `--deck-seed`, `--selected-decks-output`, `--limit-decks`.
- Candidate scope: `--candidate`, `--skip-forms`, `--limit-forms`.
- Simulation size: `--runs`, `--turns`, and one or more horizon prefixes.
  Current command support exposes `--short-turns` and `--mid-turns`; if the
  user requests a different horizon set, first extend the command rather than
  silently mapping it to short/mid.
- Search controls: `--max-branch`, `--max-plays`, `--search-policy`,
  `--search-policy-model`.
- Combat defaults: `--hand-size`, `--max-hand-size`, `--energy`, `--stars`.
- Execution: `--degree-of-parallelism`, `--resume`, `--profile`.
- Outputs: `--output`, `--output-json`, `--output-md`.

Require the user or calling workflow to provide the experimental defaults. A
known prior run used `deck-group=floor8`, `deck-count=16`, `runs=400`,
`turns=8`, `short-turns=4`, `mid-turns=8`, and `max-branch=4`; treat those as
historical values, not skill defaults.

## Strategy Selection (source-credit vs play-delta)

- A probe whose **every** term is concretely value-attributable (damage, block,
  energy, star, forge, power) is simulated normally and valued by
  **source-credit** (value per direct play).
- A probe with **at least one non-numerically-attributable term** - notably card
  **draw** (`BigBang` is the canonical example), plus create-card / transform /
  move-pile / select - must be valued by **play-delta**: `normalEV - blockedEV`,
  where the blocked run keeps the probe in the deck but in `BlockedPlayModelIds`
  so it is drawn but never played. Source-credit has no draw channel and would
  under-count these cards.
- `--value-strategy auto` applies this rule automatically; pass
  `--value-strategy play-delta` to force it. `estimate-direct-play-values` is the
  command that supports play-delta; `estimate-floor8-play-values` is
  source-credit-only.
- `--degree-of-parallelism` (default 4) parallelizes across cards; `--run-degree`
  (default 4) parallelizes one deck's runs and engages only when the per-card
  layer has nothing to spread across (single deck / single candidate), so the two
  never oversubscribe. For a single-deck probe sweep, pass
  `--degree-of-parallelism 1 --run-degree 4` to put all cores on the runs.

## Dry Run

Run before any long simulation, filling in user-specified deck group and
horizons:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --deck-source <deckSource> --deck-group <deckGroup> --deck-count <deckCount> --deck-seed <deckSeed> --turns <turns> --short-turns <firstPrefixTurns> --mid-turns <secondPrefixTurns> --limit-decks 1 --limit-forms 2 --runs 20 --max-branch <maxBranch> --profile --degree-of-parallelism 1 --output-json <dryRunJson> --output-md <dryRunMd>
```

Accept when the command writes JSON/MD and reports nonzero `baseCandidates`,
`allForms`, and `eligibleForms`.

## Benchmark

Run a projected-cost benchmark before a formal run:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --deck-source <deckSource> --deck-group <deckGroup> --deck-count <deckCount> --deck-seed <deckSeed> --turns <turns> --short-turns <firstPrefixTurns> --mid-turns <secondPrefixTurns> --limit-forms 12 --runs <benchmarkRuns> --max-branch <maxBranch> --profile --degree-of-parallelism <dop> --output-json <benchmarkJson> --output-md <benchmarkMd>
```

Use elapsed seconds to project the formal run. Record the projection in the
work log or final response.

## Formal Run

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --deck-source <deckSource> --deck-group <deckGroup> --deck-count <deckCount> --deck-seed <deckSeed> --selected-decks-output <selectedDecksOutput> --turns <turns> --short-turns <firstPrefixTurns> --mid-turns <secondPrefixTurns> --runs <runs> --max-branch <maxBranch> --profile --degree-of-parallelism <dop> --output-json <formalJson> --output-md <formalMd>
```

If interrupted, rerun with `--resume` and the same parameters. Do not lower
`runs`, `max-branch`, deck count, or requested horizons unless the user
explicitly chooses a faster lower-confidence run.

## Output Contract

- Generated JSON and MD are written to the user-specified output paths.
- Timestamped archives may also be written by the current command under
  `data/generated/floor8_play_values/`; do not treat that directory name as the
  experiment definition.
- The selected deck sample JSON is a reproducibility artifact.
- Generated JSON keeps `0.001` precision. Runtime rounding belongs to the
  install skill.
