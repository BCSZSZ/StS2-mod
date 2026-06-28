# History Analysis

Reproducible Python reports for the `history-dashen` Slay the Spire 2 run history.

The command finds the longest contiguous Regent A10 win streak in the input
directory, validates that it contains 77 `.run` files, and generates CSV,
Excel, and Markdown reports from the raw JSON files.

```powershell
uv sync
uv run history-analysis --refresh-localization
uv run history-analysis --history-root ..\history-dashen --output-dir reports
uv run history-analysis --strategy-only --output-dir reports --strategy-output-dir reports\strategy
uv run history-analysis --history-root ..\history-dashen --output-dir reports --generate-strategy
uv run pytest
```

`--refresh-localization` reads `SlayTheSpire2.pck` once and writes
`data/localized_names_en_zhs.json`. Normal report generation reads that cached
English/Simplified Chinese name map and does not need to touch the game PCK.

Generated outputs are written under `reports/`:

- `dashen_regent_77_reports.xlsx`
- one CSV per table
- `summary.md`
- `validation.json`

The V1 table set includes active remove rhythm and special quest-card events as
separate concepts: `card_remove_summary` excludes quest-completion returns, and
`special_event_detail` / `special_event_summary` report cards such as
`CARD.SPOILS_MAP` and `CARD.LANTERN_KEY`.

The V2 strategy layer reads those generated CSV files and writes reusable
analysis products under `reports/strategy/`:

- `dashen_regent_77_strategy.xlsx`
- one CSV per strategy analysis table
- `summary.md`
- `00_strategy_overview.md`
- `01_candidate_lists.md`
- `02_card_evidence_handbook.md`
- `03_opening_pick_rules.md`
- `04_pairwise_rules.md`
- `05_rhythm_rules.md`
- `06_stratification_checks.md`
- `07_rules_library.md`
- `08_conclusion_review.md`
- `final_strategy_report.md`

The V2 CSV set includes special event regions, version checks, floor-segment
checks, act-reversal pairwise candidates, rule validation, conclusion
candidates, reviewed conclusions, final-report section coverage, and replay
case library entry points.
