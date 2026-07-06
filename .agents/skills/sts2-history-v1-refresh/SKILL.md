---
name: sts2-history-v1-refresh
description: "Regenerate and verify history-analysis V1 base reports for StS2 Regent run histories. Use when refreshing V1 reports, changing .run parsing, fixing active remove semantics, handling special quest cards, or updating V1 summary/table outputs."
---

# StS2 History V1 Refresh

Use this skill for the V1 data foundation under `history-analysis`. V1 is factual extraction and table generation, not strategy interpretation.

## Workflow

1. Work from `history-analysis`.
2. Regenerate V1 from local run files:

```powershell
uv run history-analysis --history-root ..\history-dashen --output-dir reports
```

3. If V2 must stay in sync, regenerate both layers:

```powershell
uv run history-analysis --history-root ..\history-dashen --output-dir reports --generate-strategy --strategy-output-dir reports\strategy
```

4. Run regression tests:

```powershell
uv run pytest
```

## Required Outputs

- `reports/summary.md`
- `reports/validation.json`
- `reports/dashen_regent_77_reports.xlsx`
- numbered V1 CSVs, including `19_card_remove_summary.csv`, `20_special_event_detail.csv`, and `21_special_event_summary.csv`

## Invariants

- Keep V1 and V2 summaries separate: V1 uses `reports/summary.md`; V2 uses `reports/strategy/summary.md`.
- Treat `card_remove_summary` as active player deletions only.
- Exclude quest-completion returns of `CARD.SPOILS_MAP` and `CARD.LANTERN_KEY` from active deletion counts.
- Report those cards through `special_event_detail` and `special_event_summary`, including encounter, take, return, completion, reward, take floor, and completion floor.
- Do not paper over a V1?? bug in V2. Fix the parser/table source in `src/history_analysis/analysis.py`.

## When Data Is Missing

Add reusable Python code in `src/history_analysis/analysis.py`, add validation in `tests/test_reports.py`, then regenerate reports. Do not hand-edit generated CSV, workbook, or summary files.
