---
name: sts2-history-rhythm-profile
description: "Analyze upgrade, active deletion, skip, and special-event rhythms for StS2 Regent history analysis. Use for stage 6 rhythm rules and special quest-card?? checks."
---

# StS2 Rhythm Profile

Use this skill for stage 6: upgrade, delete, skip, and special-event behavior.

## Inputs

- V1: `06_skip_menu_summary.csv`, `07_card_skip_summary.csv`, `16_campfire_card_upgrade_summary.csv`, `17_non_campfire_card_upgrade_summary.csv`, `18_card_upgrade_summary.csv`, `19_card_remove_summary.csv`, `20_special_event_detail.csv`, `21_special_event_summary.csv`
- V2: `19_upgrade_priority.csv`, `20_non_campfire_upgrade_noise.csv`, `21_remove_rhythm.csv`, `22_special_event_region.csv`, `23_skip_rhythm.csv`

## Task Flow

1. Build active campfire upgrade priority from campfire-only upgrade data.
2. Isolate non-campfire upgrade noise and avoid treating it as player upgrade preference without support.
3. Read deletion rhythm from corrected active deletions only.
4. Keep `CARD.SPOILS_MAP` and `CARD.LANTERN_KEY` in the special-event region, not delete rhythm.
5. Read whole-menu skip timing by source and act.
6. Draft upgrade, deletion, skip, and special-event candidate rules with sample gates and evidence refs.

## Code Boundary

If first-campfire rules, delete event detail, or skip timing detail are missing, add reusable Python tables in V1/V2 and tests. Do not hand-filter generated CSVs.

## Reasoning Boundary

Deletion and skip behavior are not direct card-quality labels. Review whether a rhythm reflects deck state, event availability, shop economy, or strategic preference before finalizing.
