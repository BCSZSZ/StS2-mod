# Star Decision Ablation Report (2026-07-19)

## Scope

This report compares five policies on the same eight Regent decks:

- `B`: baseline before the five star-decision changes.
- `S1`: the zero-Energy forced-play rule applies only when effective Star cost is also zero. Star spenders remain ordinary search candidates.
- `S12`: `S1` plus Royal Gamble Star reservation and ordering.
- `S12345`: `S12` plus star-debt beam value, conservative draw-pile setup value, and Void Form reserving its first free slot for Royal Gamble.
- `S12345-T`: all five rules plus generic tapered ordinary search. It searches width 3 for the first four choice decisions, width 2 for the next two, then width 1. A score-gap rule may reduce an unprotected third candidate earlier. The taper is card-agnostic: zero-Energy Star spenders receive no forced play, planner, representative, or special pruning.

Every policy used 50 runs per deck and horizon, seed 1, four run workers, and the 4/8/12-turn horizons: 24 deck/horizon cells and 1,200 run observations per policy. No policy performs an extra rollout or nested simulation to decide a play.

The locked run IDs are `1781809012` and `1781960468`. Every selected deck contains at least three Star-gain and three Star-cost cards:

| Stage | Layer | Run 1781809012 gain/cost | Run 1781960468 gain/cost |
| --- | ---: | ---: | ---: |
| floor8 | 8 | 3 / 3 | 4 / 3 |
| act2Start | 17 | 3 / 4 | 4 / 5 |
| preAct2Boss | 32 | 3 / 4 | 6 / 5 |
| final | 47 | 5 / 6 | 7 / 4 |

Royal Gamble is present in every `1781960468` deck and in the final `1781809012` deck. None of these decks contains Void Form, so that ordering rule is covered by deterministic tests but cannot affect this Monte Carlo sample.

### Future-cycle reachability refinement

After the formal 50-run ablation, Royal Gamble reservation was narrowed from
"present anywhere in a non-exhaust pile" to an explicit current-cycle rule:

- Royal Gamble in hand activates the strong reserve immediately.
- Royal Gamble in the draw pile activates it only when current effective Stars,
  queued next-turn Stars, and net Star gains in the hand plus draw pile can meet
  its Star cost before the next reshuffle.
- Royal Gamble in the discard pile does not activate it.
- The profile is computed once per turn/current shuffle cycle and inherited by
  search branches. It is invalidated on reshuffle, structural pile changes, and
  after Royal Gamble is played. No decision rollout was added.

The generic Star-debt rule includes both hand and draw-pile payoffs. A later
rank-priority follow-up (reported below) added a cached draw-prefix profile:
gain cards receive a decision-only bonus when they unlock a ranked target, and
lower-ranked spenders receive a decision-only penalty only when their play
would make a reachable higher-ranked target unaffordable. It does not widen
the beam, change realized EV, or invoke another rollout.

The tables below describe the stage-selection experiment before this semantic
refinement and were not relabeled as new measurements. A separate paired
50-run comparison used the same eight locked decks, 4/8/12-turn horizons,
seed 1, and four run workers. The only difference was the Royal Gamble
activation rule:

| Royal rule | Total EV | Mean EV/turn | Gain play/draw | Cost play/draw | First gain | Missed/run | Blocked runs | Seconds |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Old all-pile reserve | 12,358.598 | 60.398 | 11,725/16,392 (71.53%) | 11,075/16,608 (66.68%) | 589/1,200 (49.08%) | 458/865 (52.95%) | 865 | 231.542 |
| Current-cycle reachable reserve | 12,272.772 | 60.049 | 11,677/16,336 (71.48%) | 11,011/16,578 (66.42%) | 587/1,200 (48.92%) | 460/867 (53.06%) | 867 | 228.945 |

The current-cycle rule lowered total EV by 85.826 (-0.69%) and mean EV/turn by
0.349. The behavioral diagnostics were essentially flat but all moved slightly
against it. The 4-turn aggregate changed by only -0.570 EV; the losses grew to
-27.130 at 8 turns and -58.130 at 12 turns. The largest cell loss was
`preAct2Boss/1781960468/12` (-65.050), partially offset by
`act2Start/1781960468/12` (+20.250). This horizon shape suggests that the old
rule's accidental planning across a future reshuffle had some value; the paired
test does not isolate discard-pile reservation from unreachable draw-pile
reservation, so that explanation remains an inference rather than a measured
component attribution.

Against the previously reported production `S12345-T` result rather than the
same-lifecycle control, total EV moved from 12,398.029 to 12,272.772: -125.257
(-1.01%), with mean EV/turn moving from 60.542 to 60.049 (-0.493). This is the
appropriate overall before/after number for the complete implementation; the
-0.69% paired control above isolates the reachability activation condition from
the accompanying turn/reshuffle cache-lifecycle correction.

Performance did not regress: the current-cycle run was 1.1% faster. A smaller
same-machine 5-run, 12-turn A/B/A check also measured the new rule at 33.743 and
37.939 internal seconds around a 40.307-second old-rule control, with 2,260,707
search nodes versus 2,278,416 for the control.

### Three-parameter tuning follow-up

Three paired 50-run trials then tuned decision-only parameters on top of the
current-cycle baseline. Every trial used the same eight decks, 4/8/12-turn
horizons, seed 1, and four run workers. `FallingStar` is reported separately
because its lower payoff makes declining it less concerning than declining a
premium Star spender.

| Policy | Beam | Future play | Royal draw | Total EV | Gain play/draw | All cost play/draw | Non-Falling cost | FallingStar | First gain | Missed/run | Blocked runs |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Current-cycle baseline | 1.00 | 0.50 | 0.55 | 12,272.772 | 71.480% | 66.419% | 61.813% | 81.091% | 48.917% | 53.057% | 867 |
| Trial 1 | 1.00 | 0.75 | 0.55 | 12,228.180 | 71.860% | 66.780% | 62.132% | 81.615% | 48.500% | 52.144% | 863 |
| Trial 2 (selected) | 1.20 | 0.75 | 0.55 | 12,319.160 | 71.829% | 67.032% | 62.315% | 82.135% | 48.417% | 53.349% | 866 |
| Trial 3 | 1.20 | 0.75 | 0.75 | 12,120.759 | 71.518% | 66.824% | 62.116% | 81.919% | 49.167% | 53.588% | 864 |

Trial 2 is retained. Relative to the current-cycle baseline, it gains 46.388
total EV (+0.38%), raises gain play/draw by 0.349 points, all-spender play/draw
by 0.613 points, and non-Falling spender play/draw by 0.502 points. First-gain
probability falls 0.500 points and missed-before-block rises 0.292 points, so it
does not solve every diagnostic. Trial 1 is the only variant that improves the
missed-before-block metric, but it loses 44.592 EV and also lowers first-gain.
Trial 3 improves first-gain by 0.250 points but loses 152.013 EV and worsens the
missed metric. The evidence therefore rejects stronger Royal reservation as the
way to optimize these aggregates and favors the EV-positive generic Star-debt
tuning. No trial changes realized EV formulas, branch width, node budget, or
adds a rollout.

### Rank-priority and draw-prefix follow-up

The final follow-up tested the user-supplied spender ranks on five additional
Star-dense final decks. Every deck contains both Gamma Blast and Falling Star;
the selected run IDs and gain/cost counts are:

| Run | Star gain cards | Star cost cards |
| ---: | ---: | ---: |
| 1781425333 | 9 | 8 |
| 1781460760 | 4 | 6 |
| 1781801471 | 5 | 6 |
| 1782070530 | 5 | 7 |
| 1782148112 | 5 | 6 |

Control, V1, V2, and V3 used the same five decks, 20 runs, seed 1, four run
workers, and 4/8/12-turn horizons. Strength was 0, 0.35, 0.70, and 1.00.
The control was rerun after fixing a pre-existing Beat Down multi-auto-play
instance-alias bug, so all four policies use the same corrected simulator.

| Policy | Strength | Total EV | EV vs control | Gain play/draw | Cost play/draw | First gain | Missed/block | Seconds | Time/control |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Control | 0.00 | 14,372.625 | - | 68.98% | 68.91% | 32.00% | 54.09% | 35.489 | 1.000x |
| V1 | 0.35 | 14,495.436 | +0.85% | 70.13% | 68.93% | 37.67% | 48.54% | 32.076 | 0.904x |
| V2 (selected) | 0.70 | 14,931.146 | +3.89% | 70.87% | 70.35% | 40.00% | 48.55% | 37.741 | 1.063x |
| V3 | 1.00 | 14,866.814 | +3.44% | 71.87% | 70.69% | 42.00% | 41.98% | 29.167 | 0.822x |

The aggregate direct-play/draw rates by supplied rank moved as follows:

| Policy | S | A | B | C | Lowest |
| --- | ---: | ---: | ---: | ---: | ---: |
| Control | 54.76% | 42.80% | 68.38% | 79.94% | 77.26% |
| V1 | 55.15% | 44.86% | 69.32% | 76.18% | 76.24% |
| V2 (selected) | 62.19% | 50.11% | 70.99% | 73.75% | 75.89% |
| V3 | 65.25% | 51.35% | 72.01% | 73.62% | 74.83% |

These raw rank aggregates are not expected to become strictly monotone:
draws where a card is illegal still remain in the denominator. In particular,
Decisions Decisions requires a valid auto-play target, while Royal Gamble has
a five-Star gate. Artificially forcing the raw table to be monotone would mean
declining profitable low-rank plays even when no higher-rank card is reachable.
The condition rule instead enforces rank at the resource-conflict point.

Gamma Blast versus Falling Star provides the clean within-deck check requested
for this sample:

| Run | Control Gamma | Control Falling | V2 Gamma | V2 Falling | V2 Gamma-Falling | V2 EV vs control |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1781425333 | 80.61% | 77.78% | 82.86% | 75.18% | +7.67 pp | +0.39% |
| 1781460760 | 89.36% | 86.42% | 89.88% | 81.53% | +8.35 pp | +3.76% |
| 1781801471 | 84.74% | 91.21% | 83.90% | 78.29% | +5.62 pp | +2.86% |
| 1782070530 | 43.52% | 59.24% | 51.05% | 54.05% | -3.00 pp | +1.92% |
| 1782148112 | 106.17% | 70.53% | 122.37% | 71.88% | +50.49 pp | +11.01% |

V2 passes the stated protection criterion on all five decks: Gamma is above
Falling on four and only 3.00 points below on the fifth, inside the allowed
10-point tolerance. V3 makes Gamma higher on all five, but its aggregate EV is
0.43% below V2. V2 therefore becomes the production default. Its 6.3% measured
runtime increase is far below the rejected 2.9x behavior and comes from changed
search paths, not a larger beam or extra simulation.

## Metric definitions

- **Total EV** is the sum of the 24 deck/horizon total expected values.
- **EV/turn** is the unweighted mean of the 24 reported values per turn.
- **Gain play/draw** and **cost play/draw** aggregate direct plays divided by draws for the corresponding Star-card class.
- **First gain** is the probability that the first directly played Star card is a Star-gain card among runs with a Star-card play. All 1,200 observations in this experiment had one.
- **Missed/run** is conditional on a run having a Star-shortage block: a legally playable Star-gain card had appeared earlier on the chosen path but was not played before the block.

## Aggregate comparison

| Policy | Total EV | Mean EV/turn | Gain play/draw | Cost play/draw | First gain | Missed/run | Blocked runs | Simulation seconds |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| B | 11,620.38 | 56.91 | 10,530/16,387 (64.3%) | 10,222/16,961 (60.3%) | 512/1,200 (42.7%) | 578/927 (62.4%) | 927 | 102.52 |
| S1 | 12,556.25 | 61.02 | 11,178/16,588 (67.4%) | 10,592/17,507 (60.5%) | 620/1,200 (51.7%) | 557/904 (61.6%) | 904 | 305.93 |
| S12 | 12,581.19 | 61.05 | 11,156/16,516 (67.5%) | 10,592/17,249 (61.4%) | 671/1,200 (55.9%) | 564/903 (62.5%) | 903 | 277.36 |
| S12345 | 13,142.78 | 63.39 | 12,077/16,884 (71.5%) | 11,359/17,311 (65.6%) | 589/1,200 (49.1%) | 478/871 (54.9%) | 871 | 295.16 |
| S12345-T | 12,398.03 | 60.54 | 11,730/16,403 (71.5%) | 11,081/16,619 (66.7%) | 589/1,200 (49.1%) | 459/865 (53.1%) | 865 | 122.10 |

Relative to baseline, `S12345-T` raises total EV by 6.7% and mean EV/turn by 6.4%. Gain play/draw rises 7.2 percentage points, cost play/draw rises 6.4 points, first-gain probability rises 6.4 points, missed-before-block falls 9.3 points, and blocked runs fall by 62.

The original exhaustive `S12345` search reports 5.7% more aggregate EV than the taper, but takes 2.42 times as long. It does not improve the behavioral diagnostics: gain and first-gain rates are equal, while the taper has higher cost play/draw, fewer blocked runs, and fewer missed prior gain opportunities. The largest EV differences occur in long-horizon Quasar decks, where exploring more action permutations also exposes more generated-card RNG outcomes to search selection.

Most importantly, internal runtime is now 1.19 times baseline instead of 2.88 times baseline. External wall time was about 123.6 seconds versus 104.3 seconds for baseline and 297.8 seconds for exhaustive `S12345`, also 1.19 times baseline and 0.42 times the exhaustive search.

## Comparison by horizon

| Turns | Policy | Total EV | Mean EV/turn | Gain play/draw | Cost play/draw | First gain | Missed/run | Blocked runs |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | B | 1,486.24 | 46.45 | 62.1% | 63.9% | 40.8% | 61.5% | 260 |
| 4 | S1 | 1,556.35 | 48.64 | 64.7% | 64.3% | 48.2% | 60.8% | 240 |
| 4 | S12 | 1,551.56 | 48.49 | 64.1% | 64.0% | 50.8% | 61.7% | 235 |
| 4 | S12345 | 1,588.49 | 49.64 | 68.4% | 68.9% | 47.0% | 44.7% | 215 |
| 4 | S12345-T | 1,573.18 | 49.16 | 69.1% | 69.8% | 47.0% | 42.3% | 215 |
| 8 | B | 3,595.00 | 56.17 | 63.2% | 60.5% | 43.5% | 62.4% | 319 |
| 8 | S1 | 3,810.23 | 59.53 | 66.3% | 60.2% | 52.8% | 64.3% | 319 |
| 8 | S12 | 3,796.69 | 59.32 | 66.1% | 61.3% | 58.2% | 65.2% | 322 |
| 8 | S12345 | 3,872.19 | 60.50 | 70.4% | 65.6% | 49.0% | 58.2% | 311 |
| 8 | S12345-T | 3,783.62 | 59.12 | 70.4% | 67.1% | 49.2% | 55.4% | 307 |
| 12 | B | 6,539.14 | 68.12 | 65.6% | 59.1% | 43.8% | 62.9% | 348 |
| 12 | S1 | 7,189.68 | 74.89 | 68.9% | 59.6% | 54.0% | 59.7% | 345 |
| 12 | S12 | 7,232.94 | 75.34 | 69.5% | 60.8% | 58.8% | 60.4% | 346 |
| 12 | S12345 | 7,682.10 | 80.02 | 73.1% | 64.7% | 51.2% | 58.3% | 345 |
| 12 | S12345-T | 7,041.24 | 73.35 | 72.9% | 65.6% | 51.0% | 57.7% | 343 |

## EV/turn for every deck/horizon cell

| Stage | Run | Turns | B | S1 | S12 | S12345 | S12345-T |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| floor8 | 1781809012 | 4 | 33.994 | 34.235 | 34.235 | 34.310 | 34.250 |
| floor8 | 1781809012 | 8 | 33.572 | 34.144 | 34.144 | 34.278 | 34.263 |
| floor8 | 1781809012 | 12 | 32.780 | 33.808 | 33.808 | 33.328 | 33.313 |
| floor8 | 1781960468 | 4 | 26.789 | 27.553 | 27.336 | 27.281 | 27.281 |
| floor8 | 1781960468 | 8 | 31.060 | 31.470 | 31.443 | 31.384 | 31.384 |
| floor8 | 1781960468 | 12 | 36.506 | 35.884 | 36.273 | 36.140 | 36.140 |
| act2Start | 1781809012 | 4 | 34.664 | 34.547 | 34.547 | 34.181 | 34.181 |
| act2Start | 1781809012 | 8 | 36.743 | 36.703 | 36.703 | 36.750 | 36.750 |
| act2Start | 1781809012 | 12 | 37.097 | 37.323 | 37.323 | 37.404 | 37.404 |
| act2Start | 1781960468 | 4 | 36.163 | 39.971 | 39.523 | 39.857 | 39.755 |
| act2Start | 1781960468 | 8 | 46.976 | 54.140 | 54.595 | 55.466 | 50.985 |
| act2Start | 1781960468 | 12 | 79.885 | 106.640 | 91.440 | 95.018 | 83.123 |
| preAct2Boss | 1781809012 | 4 | 44.550 | 44.560 | 44.560 | 44.535 | 44.558 |
| preAct2Boss | 1781809012 | 8 | 50.781 | 50.424 | 50.424 | 51.047 | 51.102 |
| preAct2Boss | 1781809012 | 12 | 53.009 | 54.353 | 54.353 | 53.773 | 53.733 |
| preAct2Boss | 1781960468 | 4 | 64.739 | 66.391 | 66.634 | 68.793 | 65.239 |
| preAct2Boss | 1781960468 | 8 | 72.575 | 81.138 | 82.966 | 82.799 | 77.326 |
| preAct2Boss | 1781960468 | 12 | 95.573 | 97.857 | 97.720 | 118.878 | 105.068 |
| final | 1781809012 | 4 | 66.638 | 73.372 | 74.001 | 83.067 | 83.292 |
| final | 1781809012 | 8 | 83.720 | 88.387 | 89.461 | 97.932 | 101.299 |
| final | 1781809012 | 12 | 95.839 | 102.311 | 104.309 | 107.209 | 106.696 |
| final | 1781960468 | 4 | 64.025 | 68.459 | 67.055 | 65.101 | 64.739 |
| final | 1781960468 | 8 | 93.949 | 99.872 | 94.849 | 94.368 | 89.844 |
| final | 1781960468 | 12 | 114.241 | 130.964 | 147.522 | 158.425 | 131.294 |

## Decision

Retain `S12345-T` as the production policy.

1. The semantic boundary is exact: only `0 Energy + 0 Stars` enters the generic zero-cost forced-play stages. A positive effective Star cost always remains an ordinary candidate, except for separately specified identity rules such as Royal Gamble.
2. Every ordinary candidate follows the same `3 -> 2 -> 1` contraction. There is no Star-spender knapsack, deterministic representative, special normalization, or extra simulation.
3. It retains all five requested star-decision rules and improves every aggregate behavior diagnostic versus baseline.
4. It reduces the unacceptable 2.88-times runtime to 1.19 times baseline while retaining a 6.7% total-EV improvement over baseline.
5. Compared with exhaustive `S12345`, it gives up 5.7% reported EV but is 2.42 times faster and matches or improves all aggregate Star execution diagnostics. Given the generated-RNG exposure in the slow long-horizon cells, the exhaustive EV should not justify its performance cost.
6. Use the draw-prefix rank policy at strength 0.70. On the five additional dense decks it raised total EV by 3.89%, moved Gamma above Falling in four decks and within 3.00 points in the fifth, and ran at 1.063 times its corrected control.
