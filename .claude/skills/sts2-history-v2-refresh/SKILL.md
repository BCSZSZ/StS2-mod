---
name: sts2-history-v2-refresh
description: "Regenerate and verify history-analysis V2 strategy tables from V1 reports. Use when refreshing strategy outputs, changing V2 candidate logic, adding strategy CSVs, or updating reports/strategy/summary.md."
---

# StS2 History V2 Refresh

Use this skill for the V2 strategy-analysis layer. V2 consumes corrected V1 facts and produces candidate rules, validation tables, and strategy summaries.

## Workflow

1. Work from `history-analysis`.
2. If V1 is current, regenerate only V2:

```powershell
uv run history-analysis --strategy-only --output-dir reports --strategy-output-dir reports\strategy
```

3. If parser or V1 tables changed, regenerate both layers:

```powershell
uv run history-analysis --history-root ..\history-dashen --output-dir reports --generate-strategy --strategy-output-dir reports\strategy
```

4. Run regression tests:

```powershell
uv run pytest
```

## Required Outputs

- `reports/strategy/summary.md`
- `reports/strategy/strategy_validation.json`
- `reports/strategy/dashen_regent_77_strategy.xlsx`
- numbered V2 CSVs from `01_data_credibility_summary.csv` through `31_case_library.csv`
- markdown pages: overview, candidate lists, card evidence, opening rules, pairwise rules, rhythm rules, stratification checks, and rules library

## Invariants

- V2 must consume corrected V1 tables. Do not filter out known V1 mistakes only in V2.
- `remove_rhythm` must not contain `CARD.SPOILS_MAP` or `CARD.LANTERN_KEY`; those belong in `special_event_region`.
- V2 `summary.md` is a strategy summary, not the final polished strategy report.
- Rule tables are candidates unless a review step explicitly accepts them.

## When Logic Is Missing

Add reusable Python code in `src/history_analysis/strategy.py`, add or update validation in `tests/test_reports.py`, then regenerate V2. Do not hand-edit generated strategy CSVs or markdown summaries.
