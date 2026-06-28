---
name: sts2-history-card-adoption
description: "Classify card adoption evidence for StS2 Regent history analysis. Use for high-pick, low-pick, final-retain, picked-but-not-retained, and commonly skipped cards."
---

# StS2 Card Adoption

Use this skill for stage 4: card adoption from offer, pick, skip, and final-deck evidence.

## Inputs

- V1: `05_card_stats_overall.csv`, `07_card_skip_summary.csv`, `14_final_deck_cards.csv`, `15_final_deck_card_summary.csv`
- V2: `12_card_adoption_classes.csv`, `13_candidate_lists.csv`, `28_card_evidence.csv`

## Task Flow

1. Separate exposure from preference: seen often, picked often, skipped often, retained often.
2. Classify cards into adoption groups: high-pick, low-pick, final-common, picked-but-not-retained, common-but-not-picked, and ambiguous.
3. Add sample gates and version/floor stability checks before making any card rule.
4. For each important card, collect evidence references from V1/V2 rows.
5. Draft single-card conclusion candidates with `claim`, `evidence`, `counter_evidence`, `strength`, and `needs_reasoning`.

## Code Boundary

Use `src/history_analysis/strategy.py` for reusable classification logic. Add a new table if a card class cannot be recovered from current CSVs.

## Reasoning Boundary

Adoption is not causality. A high final-deck rate may mean the card is strong, replaceable, forced, or simply common. Require review before promoting single-card explanations into the final report.
