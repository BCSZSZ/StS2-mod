# Finite-horizon Phase 1 benchmark (2026-07-14)

Follow-up: the first optimization pass and runtime-shaped timing clarification are documented in
`finite-horizon-phase1-performance-optimization-20260714.md`. The optimized implementation is the
current code; this file preserves the original Phase-1 before/after evidence.

## Decision

Phase 1 is implemented and improves aggregate realized EV, but **do not enter Phase 2 yet**.
The next iteration should optimize Phase 1 and repeat this exact benchmark. The reasons are:

1. Representative wall time rose by 54-158%, and the Power stress test rose by 56-111% at
   4/8 turns.
2. The new relative Power dEV is materially lower in the 4-turn and small 12-turn samples.
   Candidate absolute EV improves on average, but there are isolated candidate regressions and the
   longline shift deserves a larger paired confirmation after optimization.
3. Four of sixteen 12-turn decks lost realized EV, including two material losses, despite the
   aggregate mean improving.

Adding a learned/general `V(state, remainingTurns)` now would compound the unresolved runtime cost
and make it harder to isolate whether later quality changes come from Phase 1 or Phase 2.

## Reproducible benchmark shape

- Training decks: `history-analysis/data/dashen_77_selected_16_decks.json`.
- Total-EV screen: all 16 decks, 15 runs, seed 1, Branch 3, full-branch depth 8, independent
  4/8/12-turn solves, outer parallelism 4.
- Power dEV screen at 4/8 turns: four fixed decks, 15 runs, seed 20260714, both forms of
  BlackHole, ChildOfTheStars, Genesis, SpectrumShift, Tyranny, and VoidForm. `auto` strategy is
  used, but quality comparison here reads `joinDeckValuePerAdd`.
- Power dEV screen at 12 turns: the pre-declared smaller stress set (two fixed decks, both forms of
  BlackHole, ChildOfTheStars, and Genesis, source-credit strategy). `joinDeckValuePerAdd` remains
  a normal-versus-reference deck delta and is independent of the source-credit diagnostic.
- Before and after runs use identical decks, seeds, run counts, and search limits. Fifteen runs are
  a paired regression screen, not a final confidence-quality valuation run.

## Phase 1 implementation

- Added explicit `FiniteHorizonContext(HorizonTurns, CurrentTurn)` with `RemainingTurns` and
  `FutureTurns`.
- Removed the `99` Power floor from setup resolution, simulator point-of-use logic, both setup JSON
  copies, DIY scenario defaults, and tests.
- All Power cards now receive `OncePerHandAvailability` search admission. This keeps reachability
  separate from value: Top-B is unioned with a Power's first legal availability, while its route
  wins only on finite-horizon value.
- Search stop/leaf states include exact current turn-end Power value, queued next-turn resources
  only when a future turn exists, and an analytic persistent-state continuation bounded by
  `FutureTurns`.
- The continuation framework covers common stat modifiers, typed persistent triggers, generated-
  card Powers, per-turn Powers, delayed countdowns, and flow/resource Powers without card-name
  branches in the search lifecycle.
- Terminal next-turn effects are zero. A pure future-only Power is skipped on the last turn, while
  Plating/Thorns-style current turn-end payoff can still make a final-turn play worthwhile.
- Exact ties now prefer fewer plays and less resource spend. Zero-value Status/generated cards no
  longer win merely because the old tie-break preferred playing more cards.
- Card-object continuation previews explicitly opt out of the universal finite-horizon leaf term;
  the preview already resolves turn end and plays the following turn, so including both would
  double-count continuation value.
- Phase 2's general or learned `V(state, remainingTurns)` is not implemented.

## Total EV quality

Mean total EV is the arithmetic mean of sixteen per-deck expected totals.

| horizon | before mean | after mean | delta | delta % | lower / equal / higher decks |
|---:|---:|---:|---:|---:|---:|
| 4 | 143.39 | 163.66 | +20.27 | +14.14% | 0 / 1 / 15 |
| 8 | 367.57 | 416.13 | +48.56 | +13.21% | 1 / 0 / 15 |
| 12 | 762.69 | 857.56 | +94.87 | +12.44% | 4 / 0 / 12 |

There is no aggregate realized-EV regression. The 4-turn result is uniformly non-worse and the
8-turn result has one lower deck (-16.76). The 12-turn distribution is less uniform: the two
largest losses are -127.14 and -61.77, while several other decks
gain strongly. This is a localized longline risk, not an aggregate decline.

## Power dEV quality

`dEV` below is `EV(deck + one Power) - EV(reference deck)`, averaged over the fixed decks.

| horizon | forms | before mean dEV | after mean dEV | delta | lower / higher forms |
|---:|---:|---:|---:|---:|---:|
| 4 | 12 | -7.899 | -11.315 | -3.416 | 11 / 1 |
| 8 | 12 | 66.148 | 72.209 | +6.061 | 7 / 5 |
| 12 | 6 | 89.814 | 1.495 | -88.319 | 6 / 0 |

The 4/12-turn dEV drop is real under the new policy, but it does not by itself prove that absolute
simulation quality fell. Across all candidate/deck comparisons, candidate normal EV rose by an
average +20.67 at 4 turns, +77.62 at 8 turns, and +119.25 in the small 12-turn set. The matching
reference EV rose by +24.09, +71.55, and +207.57. Therefore the 4-turn and especially the 12-turn
marginal deltas fall mainly because the reference policy improves more; at 8 turns the candidates
improve more and mean dEV rises.

This is not uniform: candidate normal EV is lower in 1/48 comparisons at 4 turns, 7/48 at 8 turns,
and 2/12 in the small 12-turn set. The aggregate direction is positive, but those local regressions
and the six-of-six longline dEV decline remain investigation targets.

This is consistent with removing the old always-play-99 distortion: a Power now bears its draw,
energy, and branch opportunity cost relative to a much stronger baseline policy. The 12-turn set is
only two decks, so its magnitude must not be promoted into a final card-value claim.

## Performance

### Representative 16-deck run

| horizon | before wall | after wall | wall delta | decision-node delta | median per-deck delta | extra-admission node rate |
|---:|---:|---:|---:|---:|---:|---:|
| 4 | 3.348s | 8.630s | +157.8% | +90.8% | +118.8% | 1.095% -> 1.665% |
| 8 | 28.118s | 72.035s | +156.2% | +46.6% | +107.9% | 2.017% -> 2.970% |
| 12 | 121.024s | 186.866s | +54.4% | -26.1% | +129.2% | 2.569% -> 2.611% |

Decision nodes are the more reproducible search-volume signal; wall time also contains machine load
and thermal noise. Even with 26.1% fewer 12-turn nodes, its wall and median time rise because each
leaf now performs analytic continuation work. At 4/8 turns, both node count and median time rise
materially. The regression is therefore not only additional guaranteed branches: leaf cost is also
too high.

### Power stress run

| horizon | before wall | after wall | delta |
|---:|---:|---:|---:|
| 4 | 23.185s | 48.861s | +110.7% |
| 8 | 461.204s | 718.308s | +55.7% |
| 12 (small set) | 324.427s | 109.600s | -66.2% |

The 4/8 Power-specific regression is the acceptance blocker. Guaranteed admission adds branches,
and branches containing an installed Power invoke the analytic leaf continuation repeatedly. The
extra-admission rate alone is small; the expanded descendant tree and leaf work are the larger cost.

## Required Phase 1 optimization before Phase 2

Priority order:

1. Split stable per-turn opportunity statistics from mutable Power state. Compute the stable deck/
   hand opportunity profile once per turn or cache it by a compact pile signature; leaf evaluation
   should update only the small active-Power/stat part.
2. Add a cheap terminal eligibility test before recursion so pure future-only Powers and delayed
   actions are not expanded when `FutureTurns == 0`.
3. Bound simultaneous guaranteed Power additions at one node without losing first availability.
   Prefer a continuation-ranked admission queue (one new guaranteed Power per node, unconsumed
   candidates remain armed) over displacing ordinary Top-B cards.
4. Re-run this exact paired screen. Suggested gate before Phase 2: no aggregate EV loss greater
   than 2%; investigate any single-deck loss over 10%; representative 4/8 wall overhead at most
   15%; Power stress overhead at most 20%.
5. After the gate passes, increase paired runs/decks for the longline Power dEV check. Only then
   decide whether the remaining bias warrants a learned `V(state, remainingTurns)`.

## Generated artifacts

All benchmark artifacts are under `data/generated/deck_benchmarks/` and intentionally ignored by
Git. The durable conclusions and exact command shape are preserved in this document.
