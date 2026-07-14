# Finite-horizon Phase 1 performance optimization (2026-07-14)

## Timing-unit clarification

The previously reported 121.024 seconds was not one Monte Carlo run and was not one in-game card
request. It was the complete pre-Phase-1 12-turn benchmark:

- 16 fixed decks;
- 15 runs per deck;
- 12 independently simulated turns per run;
- 240 sampled combat trajectories in total;
- four-way parallelism across decks, which intentionally forced each deck's inner run degree to 1.

After Phase 1 and before this optimization pass, the same batch took 186.866 seconds. The in-game
service simulates only the current deck, batches 15 runs at a time, and parallelizes those runs. It
also reuses baseline series across cards and attempts to precompute the baseline during combat.

On this 20-logical-processor machine, the final code's slowest selected 12-turn deck took:

| runtime-shaped sample | runs | run workers | wall time |
|---|---:|---:|---:|
| map/reward approximation | 15 | 17 | 18.743s |
| combat approximation | 15 | 5 | 25.631s |

These are one-sided sample-series times. A dEV request with a warm baseline normally needs only the
candidate side. A cold request needs baseline and candidate sequentially and can exceed 30 seconds.
Complex cards can also continue to 30/45/60 runs when confidence stopping does not fire.

## Retained optimizations

1. Cache the immutable future-turn opportunity profile on `SimulationState` and inherit it through
   search clones. A normal Hand-to-Discard play preserves the cache. Exhaust, generation, transform,
   Forge, and movement across the Exhaust boundary invalidate it.
2. Treat search values within `1e-9` as numerically equal. The old exact-double comparison allowed
   approximately `1e-15` summation-order noise to select a different full route, which made a valid
   profile cache appear semantically unstable.
3. Admit at most one guaranteed candidate beyond ordinary Top-B at a node. Other unconsumed
   first-availability cards stay armed for descendant nodes. This changes Top-B+k into Top-B+1,
   does not displace ordinary candidates, and does not delete a guaranteed candidate.
4. Store generated-library continuation statistics in the cached opportunity profile and create a
   modified trigger profile only when Genesis, queued stars, or generated-card counts actually
   change it.
5. Replace the Top-B selector's two arrays plus result list with one compact candidate array and an
   explicit count. This is output-identical and reduces hot-node allocation.
6. Increment the realtime disk-cache semantic namespace from `sem9` to `sem10`; old samples cannot
   be combined with the new search semantics.

The attempted special fast path for simple persistent Powers was removed because its eligibility
scan offset the saved arithmetic and produced no measurable 4/8-turn benefit.

## Representative total-EV benchmark

Same 16 decks, 15 runs, seed 1, Branch 3, full-branch play depth 8, and 4/8/12 independent horizons.
The 4-turn before timing was rerun immediately before optimization; 8/12 use the final Phase-1
artifacts from the preceding benchmark.

| horizon | before wall | after wall | wall delta | before mean EV | after mean EV | EV delta | node delta | max branch |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 4 | 6.665s | 4.272s | -35.9% | 163.658 | 163.589 | -0.04% | +0.2% | 5 -> 4 |
| 8 | 72.035s | 32.246s | -55.2% | 416.129 | 413.059 | -0.74% | -6.3% | 5 -> 4 |
| 12 | 186.866s | 77.903s | -58.3% | 857.558 | 863.239 | +0.66% | -6.9% | 6 -> 4 |

The predefined quality gate was no aggregate EV loss greater than 2%. All horizons pass. The worst
single-deck changes are -1.87%, -3.22%, and -6.80% at 4/8/12 turns, respectively; no deck crosses the
10% investigation gate.

The much larger wall-time reduction than node reduction confirms that repeated leaf-profile work
was the first bottleneck. The remaining 12-turn cost is search-tree work: 7,828,432 decision nodes,
with the slowest deck dominating the four-worker batch.

## Power dEV stress screen

The 4/8-turn screen uses the same four fixed decks, both forms of six Powers, 15 runs, seed
20260714, and `auto` value strategy as the preceding Phase-1 report.

| horizon | before wall | after wall | wall delta | before mean dEV | after mean dEV | candidate absolute-EV delta |
|---:|---:|---:|---:|---:|---:|---:|
| 4 | 48.861s | 38.097s | -22.0% | -11.315 | -10.508 | -0.10% |
| 8 | 718.308s | 665.667s | -7.3% | 72.209 | 66.744 | -0.68% |

At 8 turns, 9/12 forms have lower relative dEV. Candidate absolute EV is down only 0.68% on average,
while the shared reference is up about 0.42%; the relative metric therefore moves more than the
underlying candidate quality. This passes the aggregate 2% gate but remains a Power-specific
regression signal.

The weak 8-turn Power speedup is informative: generation, transform, Forge, and card-object actions
correctly invalidate the stable opportunity cache. Dynamic card-state work, repeated counterfactual
variants, and search-state cloning are now the dominant Power-probe costs.

## Decision and next optimization boundary

Keep this optimization set. Do not add Phase 2 `V(state, remainingTurns)` yet.

The next performance work should profile and optimize exact state cloning and dynamic card-object
continuations, not reduce runs or horizons by default. A transposition table or copy-on-write pile
state could reduce the remaining search cost, but either is a larger correctness surface and needs
its own paired benchmark. The current pass intentionally stops before that architectural change.

Generated benchmark artifacts are under `data/generated/deck_benchmarks/` and remain ignored by
Git.
