# Card Value JSON Schema

`CardValueOverlay/data/card_values.json` is the packaged runtime value config.
The current schema version is `3`.

Runtime overlay rendering resolves only `cards[*].trainingValues` for the active
card upgrade state and horizon. The generation fields described below are
metadata for local tooling, audits, and stale-value tracking; they must not
change in-game display behavior.

## Top-Level Shape

```json
{
  "schemaVersion": 3,
  "overlay": {
    "displayMode": "trainingValue",
    "valueHorizon": "midline"
  },
  "training": {
    "source": "dashen_77_selected_100",
    "generatedAt": "2026-06-29T00:00:00Z",
    "deckCount": 100,
    "runsPerDeck": 1000,
    "maxCardsPlayedPerTurn": 8,
    "maxBranchingCards": 2,
    "horizons": {
      "shortline": 4,
      "midline": 8,
      "longline": 14
    }
  },
  "cards": {},
  "commonParameters": {}
}
```

`training.generatedAt` is batch-level metadata. It does not replace per-card
generation timestamps, because individual card values may be refreshed at
different times.

## Card Entry Shape

```json
"CARD.ALCHEMIZE": {
  "typeName": "Alchemize",
  "pools": [
    "Colorless"
  ],
  "trainingValues": {
    "unupgraded": {
      "shortline": -2.507,
      "midline": -15.106,
      "longline": -10.18
    },
    "upgraded": {
      "shortline": -2.507,
      "midline": -26.091,
      "longline": -30.055
    }
  },
  "generation": {
    "method": "monteCarlo",
    "updatedAt": {
      "shortline": "2026-06-29T00:00:00Z",
      "midline": "2026-06-29T00:00:00Z",
      "longline": "2026-06-29T00:00:00Z"
    }
  },
  "note": "Deck-level delta EV averaged across the Dashen 100-deck Regent training set."
}
```

`trainingValues` are required for display. `generation` is optional for backward
compatibility, but new generated card entries should include it.

## Generation Metadata

`generation.method` is a string, not a hard-failing enum. Known values:

- `monteCarlo`: produced by `train-card-values`, using deck-level Monte Carlo
  delta EV against matching training deck baselines.
- `estimate`: produced or imported from static/approximate estimator output
  when a card cannot yet be modeled well enough by simulation.

Unknown methods should produce validation warnings, not make the mod config
invalid.

`generation.updatedAt.shortline`, `.midline`, and `.longline` are ISO-8601
timestamps with offsets. They are horizon-level timestamps so tooling can track
which values are stale when only one horizon is refreshed.

## Tooling Contract

`CardValueOverlay.Tools train-card-values` writes
`data/generated/training_card_values.generated.json` by default. When
`--write-config` is passed, it writes the same card entries into
`CardValueOverlay/data/card_values.json`.

Every card entry produced by `train-card-values` should set:

```json
"generation": {
  "method": "monteCarlo",
  "updatedAt": {
    "shortline": "<run timestamp>",
    "midline": "<run timestamp>",
    "longline": "<run timestamp>"
  }
}
```

Future estimate import commands should set `generation.method = "estimate"` and
update only the horizons actually refreshed.
