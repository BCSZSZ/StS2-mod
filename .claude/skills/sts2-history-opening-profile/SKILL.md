---
name: sts2-history-opening-profile
description: "Analyze first-three reward choices and opening pick priorities for StS2 Regent history analysis. Use for early high-pick, high-skip, reward-order, and opening rule work."
---

# StS2 Opening Profile

Use this skill for stage 3: opening strategy from the first three card rewards.

## Inputs

- V1: `08_early_3_offer_detail.csv`, `09_early_3_card_stats.csv`, `05_card_stats_overall.csv`
- V2: `08_opening_card_stats.csv`, `09_opening_strong_signals.csv`, `10_opening_low_priority.csv`, `11_opening_stage_shift.csv`, `04_version_card_pick_check.csv`, `26_floor_segment_pick_check.csv`

## Task Flow

1. Split reward number 1, 2, and 3 instead of only using aggregate early stats.
2. Identify high-pick opening cards with minimum sample gates.
3. Identify high-rejection or low-priority cards.
4. Compare opening behavior to all-run adoption and final deck retention.
5. Build an opening priority table with `card_id`, `opening_slot`, `pick_rate`, `sample_count`, `final_retention_signal`, `strength`, and `review_note`.
6. Draft candidate rules: "snap pick", "usually take", "context take", "low priority", or "sample hint".

## Code Boundary

Rankings and deltas should come from Python in `src/history_analysis/strategy.py`. If the priority table is missing, add it and test it.

## Reasoning Boundary

Code can rank cards, but the reason a card belongs in a priority tier requires interpretation. Separate survival picks, scaling picks, and speculative picks when writing conclusions.
