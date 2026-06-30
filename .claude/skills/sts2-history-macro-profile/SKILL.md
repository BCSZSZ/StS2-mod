---
name: sts2-history-macro-profile
description: "Build the macro playstyle profile for StS2 Regent history analysis. Use for route strength, damage pressure, campfire behavior, active remove rhythm, and deck size rhythm."
---

# StS2 Macro Profile

Use this skill for stage 2 of the analysis: turning per-run V1 facts into a macro playstyle picture.

## Inputs

- V1: `02_run_summary.csv`, `03_node_history.csv`, `19_card_remove_summary.csv`, `20_special_event_detail.csv`, `21_special_event_summary.csv`
- V2: `06_macro_distribution.csv`, `07_macro_relationships.csv`, `24_strata_macro_summary.csv`

## Task Flow

1. Summarize route strength: elites, shops, rests, fights, boss path shape where available.
2. Summarize pressure: act 1 damage, total damage, heal/rest count, low/high pressure bands.
3. Summarize campfire behavior: upgrade vs rest, campfire upgrade count, first upgrade tendency if available.
4. Summarize active deletion rhythm from corrected active removes only.
5. Summarize deck expansion/compression: final deck size, pick count, skip rhythm, remove count.
6. Convert distributions into candidate macro claims with evidence references and confidence tags.

## Code Boundary

If percentile bands, relationship checks, or deck-size rhythm are missing, add reusable V2 code in `src/history_analysis/strategy.py`. Keep macro claims evidence-backed; do not write "aggressive" or "conservative" without the metrics that justify it.

## Reasoning Boundary

Code can generate bands, deltas, and correlations. The strategic label, such as "high-pressure route" or "controlled compression", should be reviewed before it enters the final report.
