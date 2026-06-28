---
name: sts2-history-stratification
description: "Validate StS2 history-analysis rules across acts, floor segments, versions, elites, damage pressure, rests, and deletion counts. Use when testing whether candidate rules are stable."
---

# StS2 Stratification

Use this skill for stage 7: checking whether candidate rules survive important slices of the sample.

## Inputs

- V1: `02_run_summary.csv`, `04_card_offers.csv`, `08_early_3_offer_detail.csv`, `10_card_pairwise_choices.csv`
- V2: `03_version_sanity_check.csv`, `04_version_card_pick_check.csv`, `05_version_skip_rate_check.csv`, `17_pairwise_act_reversal.csv`, `24_strata_macro_summary.csv`, `25_strata_pick_rate_check.csv`, `26_floor_segment_pick_check.csv`, `27_strata_skip_rate_check.csv`

## Task Flow

1. Validate by act.
2. Validate by floor segment.
3. Validate by version/build.
4. Validate by elite count.
5. Validate by act 1 damage pressure.
6. Validate by rest count and active deletion count.
7. Mark each candidate rule as stable, fragile, reversed, low-sample, or untested.

## Code Boundary

The split calculations should be generated in Python. If a required stratum does not exist, add it in `src/history_analysis/strategy.py` and cover it in tests.

## Reasoning Boundary

Code can show deltas and reversals. Deciding whether a rule survives a noisy split requires review, especially when one stratum has low sample size.
