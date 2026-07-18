# Star Decision Ablation Report (2026-07-19)

## Scope

This report compares four simulator policies on the same eight Regent decks:

- `B`: baseline before the five star-decision changes.
- `S1`: star-cost cards are no longer treated as deterministic zero-cost plays.
- `S12`: `S1` plus Royal Gamble star reservation and play ordering.
- `S12345`: `S12` plus generic star-debt beam value, conservative draw-pile
  play setup value, and Void Form's first free slot reserved for Royal Gamble.

Each policy used 50 runs per deck and horizon, seed 1, four run workers, and
the standard 4/8/12-turn horizons. This is 24 deck/horizon cells and 1,200 run
observations per policy. No policy makes an additional rollout or nested
simulation to decide a play.

The two locked history run IDs are `1781809012` and `1781960468`. The selected
decks all contain at least three star-gain cards and at least three star-cost
cards:

| Stage | Layer | Run 1781809012 gain/cost | Run 1781960468 gain/cost |
| --- | ---: | ---: | ---: |
| floor8 | 8 | 3 / 3 | 4 / 3 |
| act2Start | 17 | 3 / 4 | 4 / 5 |
| preAct2Boss | 32 | 3 / 4 | 6 / 5 |
| final | 47 | 5 / 6 | 7 / 4 |

Royal Gamble is present in every `1781960468` deck and in the final
`1781809012` deck. None of these eight decks contains Void Form, so the Void
Form ordering rule is covered by deterministic tests but cannot affect this
Monte Carlo sample.

## Metric definitions

- **Total EV** is the sum of the 24 reported deck/horizon total expected values.
- **EV/turn** is the mean of the 24 reported values per turn.
- **Gain play/draw** and **cost play/draw** aggregate direct plays divided by
  draws for the corresponding star-card class.
- **First gain** is the probability that the first directly played star card in
  a run is a star-gain card. Runs without a star-card play remain in the
  denominator, matching the CLI report.
- **Missed/run** is conditional on a run having at least one star-shortage
  block: it is the probability that a legally playable star-gain card had been
  available earlier on that chosen path but was not played before the block.

## Aggregate comparison

| Policy | Total EV | Mean EV/turn | Gain play/draw | Cost play/draw | First gain | Missed/run | Blocked runs | Simulation seconds |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| B | 11,620.38 | 56.91 | 10,530/16,387 (64.3%) | 10,222/16,961 (60.3%) | 512/1,200 (42.7%) | 578/927 (62.4%) | 927 | 102.52 |
| S1 | 12,556.25 | 61.02 | 11,178/16,588 (67.4%) | 10,592/17,507 (60.5%) | 620/1,200 (51.7%) | 557/904 (61.6%) | 904 | 305.93 |
| S12 | 12,581.19 | 61.05 | 11,156/16,516 (67.5%) | 10,592/17,249 (61.4%) | 671/1,200 (55.9%) | 564/903 (62.5%) | 903 | 277.36 |
| S12345 | 13,142.77 | 63.39 | 12,077/16,884 (71.5%) | 11,359/17,311 (65.6%) | 589/1,200 (49.1%) | 478/871 (54.9%) | 871 | 295.16 |

Relative to baseline, `S12345` increases total EV by 13.1% and mean EV/turn by
11.4%. Gain play/draw rises 7.2 percentage points, cost play/draw rises 5.3
points, and missed prior gain opportunities fall 7.5 points among blocked
runs. It is positive in 21 of the 24 deck/horizon EV cells and negative in
three. The three losses are small: -0.366, -0.483, and -0.015 EV/turn.

`S1` is the source of almost all measured runtime cost because it returns
star-cost zero-energy cards to ordinary branching. `S12345` adds no rollout
and finishes slightly faster than `S1` in this sample. The fixed per-turn node
budget and its branch-one degradation behavior remain unchanged.

## Comparison by horizon

| Turns | Policy | Total EV | Mean EV/turn | Gain play/draw | Cost play/draw | First gain | Missed/run |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | B | 1,486.24 | 46.45 | 62.1% | 63.9% | 40.8% | 61.5% |
| 4 | S1 | 1,556.35 | 48.64 | 64.7% | 64.3% | 48.2% | 60.8% |
| 4 | S12 | 1,551.56 | 48.49 | 64.1% | 64.0% | 50.8% | 61.7% |
| 4 | S12345 | 1,588.49 | 49.64 | 68.4% | 68.9% | 47.0% | 44.7% |
| 8 | B | 3,595.00 | 56.17 | 63.2% | 60.5% | 43.5% | 62.4% |
| 8 | S1 | 3,810.23 | 59.53 | 66.3% | 60.2% | 52.8% | 64.3% |
| 8 | S12 | 3,796.69 | 59.32 | 66.1% | 61.3% | 58.2% | 65.2% |
| 8 | S12345 | 3,872.18 | 60.50 | 70.4% | 65.6% | 49.0% | 58.2% |
| 12 | B | 6,539.14 | 68.12 | 65.6% | 59.1% | 43.8% | 62.9% |
| 12 | S1 | 7,189.68 | 74.89 | 68.9% | 59.6% | 54.0% | 59.7% |
| 12 | S12 | 7,232.94 | 75.34 | 69.5% | 60.8% | 58.8% | 60.4% |
| 12 | S12345 | 7,682.10 | 80.02 | 73.1% | 64.7% | 51.2% | 58.3% |

## EV/turn for every deck/horizon cell

| Stage | Run | Turns | B | S1 | S12 | S12345 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| floor8 | 1781809012 | 4 | 33.994 | 34.235 | 34.235 | 34.310 |
| floor8 | 1781809012 | 8 | 33.572 | 34.144 | 34.144 | 34.278 |
| floor8 | 1781809012 | 12 | 32.780 | 33.808 | 33.808 | 33.328 |
| floor8 | 1781960468 | 4 | 26.789 | 27.553 | 27.336 | 27.281 |
| floor8 | 1781960468 | 8 | 31.060 | 31.470 | 31.443 | 31.384 |
| floor8 | 1781960468 | 12 | 36.506 | 35.884 | 36.273 | 36.140 |
| act2Start | 1781809012 | 4 | 34.664 | 34.547 | 34.547 | 34.181 |
| act2Start | 1781809012 | 8 | 36.743 | 36.703 | 36.703 | 36.750 |
| act2Start | 1781809012 | 12 | 37.097 | 37.323 | 37.323 | 37.404 |
| act2Start | 1781960468 | 4 | 36.163 | 39.971 | 39.523 | 39.857 |
| act2Start | 1781960468 | 8 | 46.976 | 54.140 | 54.595 | 55.466 |
| act2Start | 1781960468 | 12 | 79.885 | 106.640 | 91.440 | 95.018 |
| preAct2Boss | 1781809012 | 4 | 44.550 | 44.560 | 44.560 | 44.535 |
| preAct2Boss | 1781809012 | 8 | 50.781 | 50.424 | 50.424 | 51.047 |
| preAct2Boss | 1781809012 | 12 | 53.009 | 54.353 | 54.353 | 53.773 |
| preAct2Boss | 1781960468 | 4 | 64.739 | 66.391 | 66.634 | 68.793 |
| preAct2Boss | 1781960468 | 8 | 72.575 | 81.138 | 82.966 | 82.799 |
| preAct2Boss | 1781960468 | 12 | 95.573 | 97.857 | 97.720 | 118.878 |
| final | 1781809012 | 4 | 66.638 | 73.372 | 74.001 | 83.067 |
| final | 1781809012 | 8 | 83.720 | 88.387 | 89.461 | 97.932 |
| final | 1781809012 | 12 | 95.839 | 102.311 | 104.309 | 107.209 |
| final | 1781960468 | 4 | 64.025 | 68.459 | 67.055 | 65.101 |
| final | 1781960468 | 8 | 93.949 | 99.872 | 94.849 | 94.368 |
| final | 1781960468 | 12 | 114.241 | 130.964 | 147.522 | 158.425 |

## Star-gain play/draw for every cell

Values are `B / S1 / S12 / S12345`, in percent.

| Stage | Run | 4 turns | 8 turns | 12 turns |
| --- | ---: | ---: | ---: | ---: |
| floor8 | 1781809012 | 78.4 / 76.9 / 76.9 / 79.4 | 86.9 / 87.9 / 87.9 / 91.0 | 93.8 / 92.4 / 92.4 / 94.0 |
| floor8 | 1781960468 | 79.1 / 82.6 / 83.1 / 84.7 | 81.1 / 84.1 / 83.9 / 83.6 | 84.4 / 86.5 / 86.4 / 86.6 |
| act2Start | 1781809012 | 82.7 / 83.5 / 83.5 / 83.6 | 93.2 / 94.3 / 94.3 / 95.6 | 95.2 / 96.0 / 96.0 / 95.9 |
| act2Start | 1781960468 | 58.1 / 61.2 / 59.4 / 63.0 | 56.3 / 60.4 / 59.1 / 64.8 | 56.5 / 63.9 / 64.2 / 68.4 |
| preAct2Boss | 1781809012 | 77.2 / 77.6 / 77.6 / 78.3 | 91.1 / 90.1 / 90.1 / 90.1 | 94.0 / 93.2 / 93.2 / 94.1 |
| preAct2Boss | 1781960468 | 49.2 / 52.1 / 51.8 / 59.9 | 48.4 / 50.4 / 51.0 / 58.7 | 52.0 / 51.9 / 54.6 / 63.9 |
| final | 1781809012 | 57.0 / 60.5 / 59.4 / 70.7 | 62.4 / 69.2 / 69.4 / 75.9 | 63.4 / 75.3 / 76.7 / 79.0 |
| final | 1781960468 | 45.2 / 49.3 / 48.5 / 49.0 | 37.8 / 41.7 / 41.0 / 47.0 | 43.4 / 47.4 / 46.1 / 50.3 |

## Star-cost play/draw for every cell

Values are `B / S1 / S12 / S12345`, in percent.

| Stage | Run | 4 turns | 8 turns | 12 turns |
| --- | ---: | ---: | ---: | ---: |
| floor8 | 1781809012 | 87.3 / 86.1 / 86.1 / 86.9 | 85.3 / 83.3 / 83.3 / 84.5 | 86.5 / 84.2 / 84.2 / 83.7 |
| floor8 | 1781960468 | 88.0 / 82.1 / 81.9 / 82.4 | 96.2 / 89.3 / 89.6 / 89.5 | 97.9 / 91.8 / 92.3 / 91.8 |
| act2Start | 1781809012 | 72.2 / 71.1 / 71.1 / 71.1 | 73.0 / 71.5 / 71.5 / 71.8 | 72.6 / 71.0 / 71.0 / 71.2 |
| act2Start | 1781960468 | 60.5 / 62.4 / 60.5 / 62.3 | 51.5 / 50.9 / 53.0 / 56.0 | 46.0 / 49.2 / 49.5 / 55.9 |
| preAct2Boss | 1781809012 | 69.7 / 66.3 / 66.3 / 67.1 | 72.6 / 68.8 / 68.8 / 71.3 | 74.3 / 73.0 / 73.0 / 72.6 |
| preAct2Boss | 1781960468 | 53.4 / 59.3 / 61.1 / 75.2 | 52.0 / 53.2 / 55.8 / 66.3 | 54.1 / 53.6 / 56.5 / 66.6 |
| final | 1781809012 | 48.0 / 50.4 / 50.5 / 56.1 | 44.9 / 47.4 / 47.5 / 51.5 | 44.3 / 47.5 / 48.7 / 49.4 |
| final | 1781960468 | 69.0 / 64.3 / 62.5 / 72.9 | 60.4 / 64.2 / 64.3 / 73.5 | 61.5 / 65.0 / 67.0 / 72.1 |

## Probability that the first star play is a gain card

Values are `B / S1 / S12 / S12345`, in percent.

| Stage | Run | 4 turns | 8 turns | 12 turns |
| --- | ---: | ---: | ---: | ---: |
| floor8 | 1781809012 | 30 / 38 / 38 / 30 | 36 / 44 / 44 / 36 | 36 / 46 / 46 / 36 |
| floor8 | 1781960468 | 30 / 40 / 42 / 46 | 34 / 46 / 52 / 50 | 34 / 46 / 52 / 56 |
| act2Start | 1781809012 | 40 / 44 / 44 / 40 | 42 / 46 / 46 / 42 | 42 / 46 / 46 / 42 |
| act2Start | 1781960468 | 18 / 36 / 36 / 38 | 20 / 40 / 48 / 38 | 20 / 38 / 50 / 50 |
| preAct2Boss | 1781809012 | 28 / 36 / 36 / 28 | 32 / 46 / 46 / 28 | 34 / 46 / 46 / 28 |
| preAct2Boss | 1781960468 | 42 / 48 / 54 / 46 | 44 / 46 / 62 / 50 | 44 / 54 / 64 / 52 |
| final | 1781809012 | 38 / 44 / 56 / 48 | 40 / 54 / 68 / 48 | 40 / 56 / 66 / 46 |
| final | 1781960468 | 100 / 100 / 100 / 100 | 100 / 100 / 100 / 100 | 100 / 100 / 100 / 100 |

The complete policy does not maximize this diagnostic. That is expected: the
generic setup terms also unlock and then play more valuable spenders, instead
of optimizing only for “gain first.” The stronger outcome measures are total
EV, both play/draw rates, the number of blocked runs, and missed opportunities
before a block; all favor `S12345`.

## Prior gain opportunity missed before a star-shortage block

This is the conditional run-level probability described above. Values are
`B / S1 / S12 / S12345`, in percent.

| Stage | Run | 4 turns | 8 turns | 12 turns |
| --- | ---: | ---: | ---: | ---: |
| floor8 | 1781809012 | 50.0 / 51.9 / 51.9 / 33.3 | 35.7 / 39.1 / 39.1 / 25.0 | 23.9 / 18.8 / 18.8 / 14.9 |
| floor8 | 1781960468 | 30.4 / 18.8 / 18.8 / 13.3 | 23.8 / 25.0 / 25.0 / 25.0 | 33.3 / 25.0 / 25.0 / 25.0 |
| act2Start | 1781809012 | 54.2 / 50.0 / 50.0 / 43.5 | 10.3 / 6.5 / 6.5 / 6.7 | 5.0 / 9.8 / 9.8 / 11.9 |
| act2Start | 1781960468 | 61.0 / 63.6 / 73.5 / 56.3 | 93.6 / 94.0 / 98.0 / 96.0 | 100 / 100 / 100 / 100 |
| preAct2Boss | 1781809012 | 39.3 / 45.2 / 45.2 / 31.0 | 20.5 / 30.2 / 30.2 / 23.8 | 27.3 / 17.8 / 17.8 / 20.0 |
| preAct2Boss | 1781960468 | 92.7 / 82.9 / 78.1 / 57.7 | 100 / 97.8 / 97.9 / 93.6 | 100 / 100 / 100 / 92.0 |
| final | 1781809012 | 58.7 / 65.2 / 71.1 / 40.0 | 70.0 / 77.6 / 82.0 / 63.3 | 80.0 / 74.0 / 76.0 / 76.0 |
| final | 1781960468 | 83.9 / 84.6 / 79.2 / 73.9 | 95.5 / 100 / 94.9 / 93.9 | 100 / 97.8 / 100 / 93.3 |

The remaining high values are concentrated in star-dense decks where a gain
opportunity can be missed even after the policy has already funded and played
other high-value spenders. This diagnostic is deliberately path-based, not a
claim that every missed gain would have improved EV.

## Decision

Retain `S12345` as the production policy.

1. It has the best aggregate EV and the best 12-turn EV, where setup value
   should matter most.
2. It improves both gain and spender execution rather than increasing one by
   starving the other.
3. It produces the lowest count of star-blocked runs and the lowest conditional
   missed-before-block rate.
4. `S12` improves Royal Gamble ordering but adds only 0.03 mean EV/turn over
   `S1`; stopping there leaves the generic star-debt problem unresolved.
5. The complete policy uses cached scalar/pair profiles and constant-time
   scoring after cache fill. It adds no recursive simulation, rollout, or new
   node budget. Its measured runtime is in the same band as `S1` and `S12`.

The cost is that the required correctness fix in `S1` expands ordinary search
and makes these offline 50-run jobs about 2.7–3.0 times slower than the old
forced-play baseline. This should remain explicit in performance reviews. The
simulator's existing hard node budget bounds worst-case work, and the later
setup rules do not multiply it.
