---
name: sts2-estimate-floor8-play-values
description: Run dry, benchmark, or formal floor8 direct play-value simulations for CardValueOverlay. Use when Codex needs to estimate 4-turn shortline and 8-turn midline value per direct play from selected floor8 decks with configurable runs, branch size, horizons, candidates, batching, and parallelism.
---

# StS2 Estimate Floor8 Play Values

Use this for the simulation atom of the floor8 direct play-value workflow. It
produces generated JSON/MD only; install into the mod with
`sts2-install-floor8-play-values`.

## Inputs

- Deck sample: `--deck-source`, `--deck-group`, `--deck-count`,
  `--deck-seed`, `--selected-decks-output`, `--limit-decks`.
- Candidate scope: `--candidate`, `--skip-forms`, `--limit-forms`.
- Simulation size: `--runs`, `--turns`, `--short-turns`, `--mid-turns`.
- Search controls: `--max-branch`, `--max-plays`, `--search-policy`,
  `--search-policy-model`.
- Combat defaults: `--hand-size`, `--max-hand-size`, `--energy`, `--stars`.
- Execution: `--degree-of-parallelism`, `--resume`, `--profile`.
- Outputs: `--output`, `--output-json`, `--output-md`.

Default formal values are:

```text
deck-source=history-analysis/data/dashen_77_selected_100_decks.json
deck-group=floor8
deck-count=16
deck-seed=20260629
runs=400
turns=8
short-turns=4
mid-turns=8
max-branch=4
max-plays=8
degree-of-parallelism=8 when the machine can sustain it
```

## Dry Run

Run before any long simulation:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --limit-decks 1 --limit-forms 2 --runs 20 --max-branch 4 --profile --degree-of-parallelism 1
```

Accept when the command writes JSON/MD and reports nonzero `baseCandidates`,
`allForms`, and `eligibleForms`.

## Benchmark

Run a projected-cost benchmark before a formal run:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --limit-forms 12 --runs 80 --max-branch 4 --profile --degree-of-parallelism 8 --output-json data/generated/floor8_play_values/benchmark.generated.json --output-md data/generated/floor8_play_values/benchmark.generated.md
```

Use elapsed seconds to project the formal run. The June 29 run measured about
`73s` for `80 runs`, `12 forms`, `16 decks`, `branch=4`, `DOP=8`.

## Formal Run

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --runs 400 --max-branch 4 --profile --degree-of-parallelism 8
```

If interrupted, rerun with `--resume` and the same parameters. Do not lower
`runs`, `max-branch`, or deck count unless the user explicitly chooses a faster
lower-confidence run.

## Output Contract

- `data/generated/floor8_play_values/latest.generated.json`
- `data/generated/floor8_play_values/latest.generated.md`
- timestamped archives in the same directory
- `history-analysis/data/dashen_77_floor8_random_16_decks.json`

The JSON values keep `0.001` precision. Runtime rounding happens only in the
install skill.
