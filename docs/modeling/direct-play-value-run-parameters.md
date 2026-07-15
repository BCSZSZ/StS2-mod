# Direct Play-Value Run Parameters

## 2026-07-15 Standard Batch Parameters

After the 40-deck benchmark on `Bulwark`, `BigBang`, and `Begone`, and the
follow-up overnight all-candidate run, standard direct play-value runs use:

The longline horizon was shortened from 14 to 12 turns on 2026-07-15. The
remaining parameters retain the 2026-07-01 benchmark choices.

- Deck sample: `history-analysis/data/dashen_77_selected_40_f16_a16_final8_seed20260630.json`
- Deck groups: 16 `floor8`, 16 `act2Start`, 8 `final`
- Value strategy: `auto`
- Search: `--max-branch 3 --max-full-branch-plays 6 --max-plays 64`
- Runs: `200`
- Turns: `12`
- Horizons: `shortline:4,midline:8,longline:12`
- Probe handling: `--pin-probe-branch`
- Parallelism: `--degree-of-parallelism 1 --run-degree 8`

Runtime install weights for mixed-group output:

- `shortline`: `floor8 70%`, `act2Start 20%`, `final 10%`
- `midline`: `floor8 10%`, `act2Start 70%`, `final 20%`
- `longline`: `floor8 10%`, `act2Start 15%`, `final 75%`

The benchmark treated `600` runs as a temporary high-run reference. `300` runs
had about `0.20` mean absolute deviation from the `600`-run output across the
three probe cards and both upgrade forms, while staying materially faster than
`400-600` runs. For the full 139-card sweep, `200` runs was accepted as the
current performance tradeoff and completed overnight with the parameters above.

## 2026-06-30 Defense Calibration

Direct play-value runs after this point use the scaled defense curve in
`data/manual-tags/model_calibration.json`:

```text
block value = blockToDamage(layer) * damageUnitValue(layer)
damageUnitValue(layer) = 1.0
```

The `blockToDamage` table was scaled by `0.7791196` from the previous curve so
the interpolated floor8/layer8 value is:

```text
1 block at layer 8 = 1.2 value
```

Reference points:

| Context | Layer | 1 block value | Defend 5 block | Defend+ 8 block |
| --- | ---: | ---: | ---: | ---: |
| `floor8` | 8 | 1.200 | 6.000 | 9.600 |
| `act2Start` | 17 | 1.369 | 6.845 | 10.951 |
| `final` | 47 | 2.527 | 12.633 | 20.213 |
