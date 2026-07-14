# Search Branch Diagnostics - 2026-07-14

This document preserves the original `Top-B union +k` measurement. The retained
optimization now bounds admission to `Top-B+1` and defers other flagged cards;
see `finite-horizon-phase1-performance-optimization-20260714.md` for the paired
quality and performance results.

## Question

Measure the actual search width after introducing generator admission as
`Top-B union +k`, before deciding whether realtime Branch 3 should be reduced.

## Method

- decks: all 16 decks in `history-analysis/data/dashen_77_selected_16_decks.json`;
- independent horizons: 4, 8, and 12 turns;
- 15 runs per deck and horizon;
- `MaxBranchingCards=3`;
- full branching for the first 8 direct plays per turn, then greedy width 1;
- four-way deck parallelism;
- diagnostics record only aggregate counters and histograms; no per-node objects;
- generated benchmark artifacts are under `data/generated/deck_benchmarks/` and
  remain Git-ignored.

“All-node average” includes the greedy tail after eight plays. “Full-phase
average” isolates nodes where configured Branch 3 is still active. Both report
actual selected candidates after `+k`, not the configured cap.

## Results

| Horizon | Search nodes | Full-phase nodes | All-node avg | Full-phase avg | Avg `+k` | Nodes with `+k` | Full p95 | Max | Wall time |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 4 | 318,706 | 135,903 | 1.439 | 2.014 | 0.011 | 1.095% | 3 | 5 | 2.587 s |
| 8 | 2,499,619 | 690,155 | 1.311 | 2.081 | 0.020 | 2.017% | 3 | 5 | 19.812 s |
| 12 | 11,377,328 | 2,380,997 | 1.256 | 2.144 | 0.026 | 2.569% | 3 | 5 | 86.583 s |
| combined | 14,195,653 | 3,207,055 | 1.270 | 2.125 | 0.025 | 2.439% | 3 | 5 | 108.982 s |

Across full-phase nodes, `+k` adds 0.039 candidates on average. The combined
actual selected-width distribution is:

| Selected candidates | Full-phase nodes | Share |
|---:|---:|---:|
| 1 | 323,200 | 10.078% |
| 2 | 2,165,931 | 67.536% |
| 3 | 711,985 | 22.201% |
| 4 | 5,876 | 0.183% |
| 5 | 63 | 0.002% |

## Conclusion

The new generator admission is not the main average-width cost. It raises the
full-phase average from 2.086 base candidates to 2.125 selected candidates, and
widths above 3 occur at only 0.185% of full-phase nodes. Capping the union at 3
would therefore save little while breaking the guarantee that the ordinary
Top-B set is not displaced.

Reducing configured Branch 3 to Branch 2 is a different and potentially large
change: about 22% of full-phase nodes currently explore at least three actual
candidates. It may save substantial work through multiplicative tree reduction,
but this diagnostic does not measure route-quality loss. Keep Branch 3 for now;
before changing it, run a paired Branch-2-versus-Branch-3 benchmark that compares
chosen-line EV and known generator/engine-card regression scenarios, not speed
alone.

The largest cost is the 12-turn search-node count and its slow-deck tail, not the
rare `+k` spike. Future performance work should first target redundant longline
search expansion, state deduplication, or a quality-checked Branch 2 policy.
