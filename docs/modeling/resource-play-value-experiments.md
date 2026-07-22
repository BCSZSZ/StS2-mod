# Resource Play-Value Experiments

> Historical legacy experiment (2026-07-22): the resource/source-credit and
> static block conversions below remain diagnostic context for the old simulator.
> They are not direct terms in physical combat EV and must not produce new
> combat-aware training values. See
> `.agents/docs/combat-aware-simulation-contract.md`.

This document records stable experimental estimates for resource play value in
the simulator. Rounded versions of these values are used as simulator
search/selector reference values for concrete draw, energy, and star effects;
they are not automatically installed into the runtime mod.

## 2026-06-28 Resource Probe

On 2026-06-28, resource play values were estimated by replacing one sampled
deck copy with a DIY probe copy that preserves the original card and adds one
resource effect:

- `energyGain`: original card + `EnergyGain + 1`
- `draw`: original card + `Draw + 1`
- `starGain`: original card + `StarGain + 1`

For each probe, value per direct play is:

```text
(variant prefix EV - baseline prefix EV) / average probe plays per run
```

Equivalently, because simulator EV is already averaged per run:

```text
(variant prefix EV - baseline prefix EV) * runs / total probe play count
```

The accepted run used:

- deck file: `history-analysis/data/dashen_77_selected_32_decks.json`
- deck mix: `floor8=6`, `act2Start=22`, `final=4`
- runs: `400`
- sampled playable non-multiplayer card copies per deck: `10`
- search branch: `4`
- horizons: shortline `4` turns, midline `8` turns, longline `14` turns
- output: `data/generated/resource_play_values/latest.generated.json`
- archive: `data/generated/resource_play_values/20260628T121040Z_dashen_77_selected_32_decks_resource_probe_d32_r400_b4_s10.generated.json`

`max-branch=5` was rejected for this experiment because the initial benchmark
timed out after 30 minutes. Slow benchmark decks were replaced inside their
original groups before the accepted run. The accepted replacement benchmark
reported `slowDecks=0`.

## Accepted Estimates

Primary estimates use the weighted value per play:

| Resource | Shortline | Midline | Longline |
| --- | ---: | ---: | ---: |
| `energyGain` | 8.753 | 10.027 | 11.188 |
| `draw` | 5.054 | 5.184 | 5.061 |
| `starGain` | 2.737 | 5.306 | 6.286 |

## Rounded Selector Reference Values

For simulator search and selector reference values, the accepted estimates are
rounded to one decimal:

| Resource | Shortline | Midline | Longline |
| --- | ---: | ---: | ---: |
| `energyGain` | 8.8 | 10.0 | 11.2 |
| `draw` | 5.1 | 5.2 | 5.1 |
| `starGain` | 2.7 | 5.3 | 6.3 |

`data/manual-tags/model_calibration.json` stores the midline values for static
card estimates. `DeckMonteCarloSimulator` uses `DeckSimulationOptions.Turns`
to select shortline, midline, or longline reference values during play-search
scoring. Concrete next-turn resource effects use the same value with the
existing `0.75` delayed-resource multiplier. Non-concrete resource effects,
such as persistent Power-style resource engines, remain modeled by their
existing mechanics rather than by these explicit resource constants.

Interpretation:

- immediate energy is the highest-value resource in these decks, especially at
  longer horizons;
- draw is stable around roughly `5` value per direct play;
- stars are weaker shortline but rise strongly by midline and longline because
  they persist across turns.

## Defense Value Used By The Experiment

The simulator did not use a single global block value. It used the layered
calibration in `data/manual-tags/model_calibration.json`:

```text
1 block value = blockToDamage(layer) * damageUnitValue(layer)
```

`damageUnitValue` is `1.0` at every current calibration layer, so the practical
value is the interpolated `blockToDamage` value. `ValueCalibration.GetLayeredValue`
linearly interpolates between calibration breakpoints.

For the accepted 32-deck resource experiment, the relevant deck layers were:

| Deck group | Simulator layer | 1 block value |
| --- | ---: | ---: |
| `floor8` | 8 | 1.540 |
| `act2Start` | 17 | 1.757 |
| `final` | 47 | 3.243 |

Direct damage remained fixed at:

```text
1 damage = 1 value
```

Only block/defense value changed by layer.

After review on 2026-06-30, the current modeling calibration scaled the
`blockToDamage` curve down so layer 8 interpolates to `1 block = 1.2 value`.
That changes future simulations and static estimates, but does not change the
historical inputs of the 2026-06-28 resource probe above. The current reference
points are:

| Deck group | Simulator layer | Current 1 block value |
| --- | ---: | ---: |
| `floor8` | 8 | 1.200 |
| `act2Start` | 17 | 1.369 |
| `final` | 47 | 2.527 |
