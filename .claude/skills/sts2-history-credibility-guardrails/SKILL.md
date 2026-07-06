---
name: sts2-history-credibility-guardrails
description: "Audit sample scope, version stability, sample thresholds, and metric definitions for StS2 history-analysis conclusions. Use before promoting V1/V2 findings into strategy rules."
---

# StS2 Credibility Guardrails

Use this skill to decide which conclusions may be stated strongly and which must remain sample hints.

## Inputs

- V1: `01_runs_check.csv`, `02_run_summary.csv`, `04_card_offers.csv`, `05_card_stats_overall.csv`, `10_card_pairwise_choices.csv`, `11_card_pairwise_summary.csv`
- V2: `01_data_credibility_summary.csv`, `02_sample_threshold_audit.csv`, `03_version_sanity_check.csv`, `04_version_card_pick_check.csv`, `05_version_skip_rate_check.csv`

## Task Flow

1. Confirm the sample boundary: character, ascension, win/streak filter, selected run count, date/build/version spread.
2. Confirm metric??: offer, pick, skip, final-retain, active remove, quest-card event, campfire upgrade, non-campfire upgrade.
3. Apply sample gates before conclusion writing. Mark low-count rows as `sample_hint`, not final rules.
4. Compare version-level pick/skip/macro differences. If mixed, downgrade the affected conclusions or require version split.
5. Emit or update a guardrail table with `claim_scope`, `source_table`, `sample_count`, `version_risk`, `metric_risk`, `allowed_strength`, and `review_note`.

## Code Boundary

If the needed guardrail table is missing, implement it in `src/history_analysis/strategy.py` and test it. This step should be mostly code-generated; only the final "versions can be merged" verdict may require reasoning when the data is mixed.

## Output Rule

Every downstream conclusion should carry one of: `strong`, `medium`, `sample_hint`, or `blocked`. Never promote an unguarded statistic directly into the final report.
