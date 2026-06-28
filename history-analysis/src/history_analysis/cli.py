from __future__ import annotations

import argparse
from pathlib import Path

from .analysis import DEFAULT_LOCALIZATION_JSON, generate_reports, write_localization_json
from .strategy import generate_strategy_reports


def default_project_root() -> Path:
    return Path(__file__).resolve().parents[2]


def main() -> int:
    project_root = default_project_root()
    parser = argparse.ArgumentParser(description="Generate Regent run-history reports.")
    parser.add_argument(
        "--history-root",
        type=Path,
        default=project_root.parent / "history-dashen",
        help="Directory containing StS2 .run files.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=project_root / "reports",
        help="Directory for generated CSV/XLSX/Markdown reports.",
    )
    parser.add_argument(
        "--expected-streak-length",
        type=int,
        default=77,
        help="Required length for the selected Regent A10 win streak.",
    )
    parser.add_argument(
        "--refresh-localization",
        action="store_true",
        help="Regenerate the cached English/Simplified Chinese localization JSON from SlayTheSpire2.pck.",
    )
    parser.add_argument(
        "--pck-path",
        type=Path,
        default=None,
        help="Optional SlayTheSpire2.pck path for --refresh-localization.",
    )
    parser.add_argument(
        "--localization-json",
        type=Path,
        default=DEFAULT_LOCALIZATION_JSON,
        help="Output path for --refresh-localization.",
    )
    parser.add_argument(
        "--generate-strategy",
        action="store_true",
        help="Also generate the V2 strategy analysis layer from the generated CSV reports.",
    )
    parser.add_argument(
        "--strategy-only",
        action="store_true",
        help="Read existing CSV reports from --output-dir and generate only the V2 strategy analysis layer.",
    )
    parser.add_argument(
        "--strategy-output-dir",
        type=Path,
        default=None,
        help="Directory for V2 strategy CSV/XLSX/Markdown outputs. Defaults to --output-dir/strategy.",
    )
    args = parser.parse_args()

    if args.refresh_localization:
        output_path = write_localization_json(args.localization_json, args.pck_path)
        print(f"localization_json={output_path}")
        return 0

    strategy_output_dir = args.strategy_output_dir or args.output_dir / "strategy"

    if args.strategy_only:
        strategy_result = generate_strategy_reports(
            report_dir=args.output_dir,
            output_dir=strategy_output_dir,
        )
        print(f"strategy_workbook={strategy_result.workbook_path}")
        print(f"strategy_dir={strategy_result.output_dir}")
        return 0

    result = generate_reports(
        history_root=args.history_root,
        output_dir=args.output_dir,
        expected_streak_length=args.expected_streak_length,
    )
    print(f"selected_runs={result.validation['selected_run_count']}")
    print(f"streak_start={result.validation['streak_start_run_id']}")
    print(f"streak_end={result.validation['streak_end_run_id']}")
    print(f"workbook={result.workbook_path}")
    print(f"summary={result.summary_path}")
    if args.generate_strategy:
        strategy_result = generate_strategy_reports(
            report_dir=args.output_dir,
            output_dir=strategy_output_dir,
        )
        print(f"strategy_workbook={strategy_result.workbook_path}")
        print(f"strategy_dir={strategy_result.output_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
