from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

import pandas as pd


SHOP_SOURCES = {"shop_offer", "unknown_shop_offer"}

STRATEGY_TABLE_ORDER = [
    "data_credibility_summary",
    "sample_threshold_audit",
    "version_sanity_check",
    "version_card_pick_check",
    "version_skip_rate_check",
    "macro_distribution",
    "macro_relationships",
    "opening_card_stats",
    "opening_strong_signals",
    "opening_low_priority",
    "opening_stage_shift",
    "card_adoption_classes",
    "candidate_lists",
    "pairwise_frequent",
    "pairwise_dominant",
    "pairwise_controversial",
    "pairwise_act_reversal",
    "pairwise_card_profile",
    "upgrade_priority",
    "non_campfire_upgrade_noise",
    "remove_rhythm",
    "special_event_region",
    "skip_rhythm",
    "strata_macro_summary",
    "strata_pick_rate_check",
    "floor_segment_pick_check",
    "strata_skip_rate_check",
    "card_evidence",
    "rules_library",
    "rule_validation",
    "case_library",
]

STRATEGY_CSV_ORDER = {
    name: f"{index:02d}_{name}.csv" for index, name in enumerate(STRATEGY_TABLE_ORDER, start=1)
}

REQUIRED_BASE_TABLES = {
    "runs_check",
    "run_summary",
    "node_history",
    "card_offers",
    "card_stats_overall",
    "skip_menu_summary",
    "card_skip_summary",
    "early_3_offer_detail",
    "early_3_card_stats",
    "card_pairwise_summary",
    "final_deck_card_summary",
    "campfire_card_upgrade_summary",
    "non_campfire_card_upgrade_summary",
    "card_remove_summary",
    "special_event_detail",
    "special_event_summary",
}


@dataclass(frozen=True)
class StrategyReportResult:
    tables: dict[str, pd.DataFrame]
    output_dir: Path
    workbook_path: Path
    markdown_paths: dict[str, Path]
    validation_path: Path


def generate_strategy_reports(report_dir: Path, output_dir: Path) -> StrategyReportResult:
    base_tables = load_report_tables(report_dir)
    strategy_tables = build_strategy_tables(base_tables)
    validate_strategy_tables(strategy_tables)

    output_dir.mkdir(parents=True, exist_ok=True)
    for stale in output_dir.glob("*.csv"):
        stale.unlink()
    for stale in output_dir.glob("*.md"):
        stale.unlink()
    for stale in output_dir.glob("*.xlsx"):
        stale.unlink()

    for table_name in STRATEGY_TABLE_ORDER:
        strategy_tables[table_name].to_csv(
            output_dir / STRATEGY_CSV_ORDER[table_name],
            index=False,
            encoding="utf-8-sig",
        )

    workbook_path = output_dir / "dashen_regent_77_strategy.xlsx"
    with pd.ExcelWriter(workbook_path, engine="openpyxl") as writer:
        for table_name in STRATEGY_TABLE_ORDER:
            strategy_tables[table_name].to_excel(
                writer,
                sheet_name=excel_sheet_name(table_name),
                index=False,
            )

    markdown_sources = build_strategy_markdowns(strategy_tables)
    markdown_paths: dict[str, Path] = {}
    for file_name, content in markdown_sources.items():
        path = output_dir / file_name
        path.write_text(content, encoding="utf-8")
        markdown_paths[file_name] = path

    validation = {
        "input_report_dir": str(report_dir),
        "strategy_table_row_counts": {
            table_name: int(len(strategy_tables[table_name])) for table_name in STRATEGY_TABLE_ORDER
        },
        "markdown_files": sorted(markdown_paths),
    }
    validation_path = output_dir / "strategy_validation.json"
    validation_path.write_text(json.dumps(validation, ensure_ascii=False, indent=2), encoding="utf-8")

    return StrategyReportResult(
        tables=strategy_tables,
        output_dir=output_dir,
        workbook_path=workbook_path,
        markdown_paths=markdown_paths,
        validation_path=validation_path,
    )


def load_report_tables(report_dir: Path) -> dict[str, pd.DataFrame]:
    tables: dict[str, pd.DataFrame] = {}
    if not report_dir.exists():
        raise FileNotFoundError(f"Report directory does not exist: {report_dir}")

    for path in sorted(report_dir.glob("*.csv")):
        if "_" not in path.stem:
            continue
        table_name = path.stem.split("_", 1)[1]
        frame = pd.read_csv(path, encoding="utf-8-sig", low_memory=False)
        for column in ["was_picked", "was_skipped_menu", "skipped", "win", "was_abandoned", "is_starting_card"]:
            if column in frame.columns:
                frame[column] = frame[column].map(normalize_bool)
        tables[table_name] = frame

    missing = sorted(REQUIRED_BASE_TABLES - set(tables))
    if missing:
        raise ValueError(f"Missing base report CSV tables: {missing}")
    return tables


def build_strategy_tables(base: dict[str, pd.DataFrame]) -> dict[str, pd.DataFrame]:
    card_metrics = build_card_metrics(base)
    pairwise_tables = build_pairwise_tables(base["card_pairwise_summary"], card_metrics)
    strata = add_run_strata(base["runs_check"], base["run_summary"])
    opening_tables = build_opening_tables(base["early_3_card_stats"])
    rhythm_tables = build_rhythm_tables(base)
    adoption_classes = build_card_adoption_classes(card_metrics)
    candidate_lists = build_candidate_lists(
        card_metrics,
        opening_tables["opening_strong_signals"],
        pairwise_tables["pairwise_dominant"],
    )
    focus_cards = select_focus_cards(card_metrics, opening_tables["opening_card_stats"])
    card_evidence = build_card_evidence(
        card_metrics,
        opening_tables["opening_card_stats"],
        pairwise_tables["pairwise_card_profile"],
        focus_cards,
    )
    rules_library = build_rules_library(
        opening_tables,
        adoption_classes,
        pairwise_tables,
        rhythm_tables,
        card_evidence,
    )

    tables = {
        "data_credibility_summary": build_data_credibility_summary(base),
        "sample_threshold_audit": build_sample_threshold_audit(base["card_stats_overall"], base["card_pairwise_summary"]),
        "version_sanity_check": build_version_sanity_check(strata),
        "version_card_pick_check": build_version_card_pick_check(base["card_offers"], strata, focus_cards),
        "version_skip_rate_check": build_version_skip_rate_check(base["card_offers"], strata),
        "macro_distribution": build_macro_distribution(strata),
        "macro_relationships": build_macro_relationships(strata),
        "opening_card_stats": opening_tables["opening_card_stats"],
        "opening_strong_signals": opening_tables["opening_strong_signals"],
        "opening_low_priority": opening_tables["opening_low_priority"],
        "opening_stage_shift": opening_tables["opening_stage_shift"],
        "card_adoption_classes": adoption_classes,
        "candidate_lists": candidate_lists,
        "pairwise_frequent": pairwise_tables["pairwise_frequent"],
        "pairwise_dominant": pairwise_tables["pairwise_dominant"],
        "pairwise_controversial": pairwise_tables["pairwise_controversial"],
        "pairwise_act_reversal": pairwise_tables["pairwise_act_reversal"],
        "pairwise_card_profile": pairwise_tables["pairwise_card_profile"],
        "upgrade_priority": rhythm_tables["upgrade_priority"],
        "non_campfire_upgrade_noise": rhythm_tables["non_campfire_upgrade_noise"],
        "remove_rhythm": rhythm_tables["remove_rhythm"],
        "special_event_region": rhythm_tables["special_event_region"],
        "skip_rhythm": rhythm_tables["skip_rhythm"],
        "strata_macro_summary": build_strata_macro_summary(strata),
        "strata_pick_rate_check": build_strata_pick_rate_check(base["card_offers"], strata, focus_cards),
        "floor_segment_pick_check": build_floor_segment_pick_check(base["card_offers"], focus_cards),
        "strata_skip_rate_check": build_strata_skip_rate_check(base["card_offers"], strata),
        "card_evidence": card_evidence,
        "rules_library": rules_library,
        "rule_validation": build_rule_validation(rules_library),
        "case_library": build_case_library(strata),
    }
    return {name: tables[name] for name in STRATEGY_TABLE_ORDER}


def build_card_metrics(base: dict[str, pd.DataFrame]) -> pd.DataFrame:
    metrics = base["card_stats_overall"].copy()
    metrics = merge_on_card(metrics, base["final_deck_card_summary"], "final")
    metrics = merge_on_card(metrics, base["campfire_card_upgrade_summary"], "campfire")
    metrics = merge_on_card(metrics, base["card_skip_summary"], "skip")
    metrics = merge_on_card(metrics, base["card_remove_summary"], "remove")

    name_columns = [column for column in metrics.columns if column.endswith("card_name_zh")]
    if "card_name_zh" not in metrics.columns:
        metrics["card_name_zh"] = ""
    for column in name_columns:
        if column != "card_name_zh":
            metrics["card_name_zh"] = metrics["card_name_zh"].where(
                metrics["card_name_zh"].astype(str).ne(""),
                metrics[column],
            )

    numeric_defaults = {
        "offer_count": 0,
        "pick_count": 0,
        "pick_rate_when_offered": 0.0,
        "final_deck_run_count": 0,
        "final_deck_copy_count": 0,
        "final_final_run_count": 0,
        "final_final_copy_count": 0,
        "final_upgrade_rate_in_final": 0.0,
        "campfire_upgrade_count": 0,
        "campfire_first_upgrade_count": 0,
        "skip_non_shop_offer_count": 0,
        "skip_skipped_offer_count": 0,
        "skip_skip_exposure_rate": 0.0,
        "remove_remove_count": 0,
    }
    for column, default in numeric_defaults.items():
        if column not in metrics.columns:
            metrics[column] = default
        metrics[column] = pd.to_numeric(metrics[column], errors="coerce").fillna(default)

    metrics["final_run_count_effective"] = metrics[["final_deck_run_count", "final_final_run_count"]].max(axis=1)
    metrics["final_copy_count_effective"] = metrics[["final_deck_copy_count", "final_final_copy_count"]].max(axis=1)
    metrics["support_bucket"] = metrics["offer_count"].map(card_support_bucket)
    return metrics


def merge_on_card(left: pd.DataFrame, right: pd.DataFrame, prefix: str) -> pd.DataFrame:
    if right.empty:
        return left
    renamed = right.rename(
        columns={column: f"{prefix}_{column}" for column in right.columns if column != "card_id"}
    )
    return left.merge(renamed, on="card_id", how="left")


def build_data_credibility_summary(base: dict[str, pd.DataFrame]) -> pd.DataFrame:
    runs = base["runs_check"]
    pairwise = base["card_pairwise_summary"]
    all_pairwise = base.get("card_pairwise_all_non_shop_summary", pd.DataFrame())
    rows = [
        {
            "stage": "1 数据可信度确认",
            "check": "样本边界确认",
            "status": "通过" if len(runs) == 77 else "需复核",
            "evidence": f"{len(runs)} 局；版本 {join_values(runs['build_id'].dropna().unique())}",
            "frozen_scope": "储君 A10 胜利样本，不含放弃局。",
        },
        {
            "stage": "1 数据可信度确认",
            "check": "来源口径冻结",
            "status": "通过",
            "evidence": "主选牌结论优先使用 card_pairwise_summary；商店和事件只作补充。",
            "frozen_scope": "标准奖励对位为非商店、3/4 选 1，整跳菜单不进入两两对位。",
        },
        {
            "stage": "1 数据可信度确认",
            "check": "标准对位样本",
            "status": "通过" if int(pairwise["total_meetings"].sum()) > 0 else "需复核",
            "evidence": f"{int(pairwise['total_meetings'].sum())} 条有效标准偏好对；{len(pairwise)} 组卡牌对。",
            "frozen_scope": "对位规则默认要求 total_meetings >= 6；3-5 只列案例。",
        },
        {
            "stage": "1 数据可信度确认",
            "check": "全部非商店对位样本",
            "status": "参考",
            "evidence": f"{int(all_pairwise['total_meetings'].sum()) if not all_pairwise.empty else 0} 条有效非商店偏好对。",
            "frozen_scope": "样本更多但上下文更杂，默认不进入强规则。",
        },
        {
            "stage": "1 数据可信度确认",
            "check": "样本量门槛冻结",
            "status": "通过",
            "evidence": "offer_count <5 只列事实；5-14 弱参考；15-29 中等参考；>=30 可进入主结论。",
            "frozen_scope": "所有单卡证据页都带 support_bucket。",
        },
    ]
    return pd.DataFrame(rows)


def build_sample_threshold_audit(card_stats: pd.DataFrame, pairwise: pd.DataFrame) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for bucket, group in card_stats.assign(bucket=card_stats["offer_count"].map(card_support_bucket)).groupby("bucket"):
        rows.append(
            {
                "entity_type": "card_offer",
                "support_bucket": bucket,
                "row_count": int(len(group)),
                "threshold": card_support_threshold_text(bucket),
                "guidance": card_support_guidance(bucket),
            }
        )
    for bucket, group in pairwise.assign(bucket=pairwise["total_meetings"].map(pairwise_support_bucket)).groupby("bucket"):
        rows.append(
            {
                "entity_type": "pairwise",
                "support_bucket": bucket,
                "row_count": int(len(group)),
                "threshold": pairwise_support_threshold_text(bucket),
                "guidance": pairwise_support_guidance(bucket),
            }
        )
    return pd.DataFrame(rows).sort_values(["entity_type", "support_bucket"], ignore_index=True)


def add_run_strata(runs: pd.DataFrame, run_summary: pd.DataFrame) -> pd.DataFrame:
    frame = runs.merge(run_summary, on="run_id", how="left")
    frame["act1_pressure_bucket"] = tertile_bucket(frame["act1_damage"], "低压局", "中压局", "高压局")
    frame["elite_route_bucket"] = frame["elite_count"].map(elite_route_bucket)
    frame["rest_bucket"] = frame["heal_count"].map(lambda value: "0 次休息" if value == 0 else ("1 次休息" if value == 1 else "2+ 次休息"))
    frame["remove_bucket"] = frame["card_remove_count"].map(
        lambda value: "0-3 次删牌" if value <= 3 else ("4-5 次删牌" if value <= 5 else "6+ 次删牌")
    )
    return frame


def build_version_sanity_check(strata: pd.DataFrame) -> pd.DataFrame:
    metrics = [
        "elite_count",
        "shop_count",
        "act1_damage",
        "total_damage",
        "heal_count",
        "card_remove_count",
        "campfire_card_upgrade_count",
        "final_deck_size",
    ]
    rows = []
    for build_id, group in strata.groupby("build_id"):
        row: dict[str, Any] = {
            "build_id": build_id,
            "run_count": int(len(group)),
        }
        for metric in metrics:
            row[f"avg_{metric}"] = safe_mean(group[metric])
        rows.append(row)
    return pd.DataFrame(rows).sort_values("build_id", ignore_index=True)


def build_macro_distribution(strata: pd.DataFrame) -> pd.DataFrame:
    metrics = {
        "elite_count": "每局精英数",
        "shop_count": "每局商店数",
        "heal_count": "每局休息次数",
        "act1_damage": "第一幕掉血",
        "total_damage": "全局掉血",
        "card_remove_count": "删牌次数",
        "campfire_card_upgrade_count": "火堆升级次数",
        "final_deck_size": "最终卡组大小",
    }
    rows = []
    for metric, label in metrics.items():
        series = pd.to_numeric(strata[metric], errors="coerce").dropna()
        rows.append(
            {
                "metric": metric,
                "label": label,
                "count": int(series.count()),
                "mean": series.mean(),
                "median": series.median(),
                "p25": series.quantile(0.25),
                "p75": series.quantile(0.75),
                "min": series.min(),
                "max": series.max(),
            }
        )
    return pd.DataFrame(rows)


def build_macro_relationships(strata: pd.DataFrame) -> pd.DataFrame:
    pairs = [
        ("elite_count", "act1_damage", "精英数 vs 第一幕掉血"),
        ("elite_count", "final_deck_size", "精英数 vs 最终卡组大小"),
        ("heal_count", "campfire_card_upgrade_count", "休息次数 vs 火堆升级次数"),
        ("card_remove_count", "final_deck_size", "删牌次数 vs 最终卡组大小"),
        ("card_remove_count", "heal_count", "删牌次数 vs 休息次数"),
        ("total_damage", "heal_count", "全局掉血 vs 休息次数"),
    ]
    rows = []
    for x_col, y_col, label in pairs:
        values = strata[[x_col, y_col]].apply(pd.to_numeric, errors="coerce").dropna()
        corr = values[x_col].corr(values[y_col]) if len(values) >= 2 else None
        rows.append(
            {
                "relationship": label,
                "x_metric": x_col,
                "y_metric": y_col,
                "sample_count": int(len(values)),
                "pearson_corr": corr,
                "reading": correlation_reading(corr),
            }
        )
    return pd.DataFrame(rows)


def build_opening_tables(early_stats: pd.DataFrame) -> dict[str, pd.DataFrame]:
    opening = early_stats.copy()
    for index in (1, 2, 3):
        offer_col = f"early_menu{index}_offer_count"
        pick_col = f"early_menu{index}_pick_count"
        opening[f"early_menu{index}_pick_rate"] = opening.apply(
            lambda row, o=offer_col, p=pick_col: safe_rate(row[p], row[o]),
            axis=1,
        )
    opening["early_menu23_offer_count"] = opening["early_menu2_offer_count"] + opening["early_menu3_offer_count"]
    opening["early_menu23_pick_count"] = opening["early_menu2_pick_count"] + opening["early_menu3_pick_count"]
    opening["early_menu23_pick_rate"] = opening.apply(
        lambda row: safe_rate(row["early_menu23_pick_count"], row["early_menu23_offer_count"]),
        axis=1,
    )
    opening["menu23_minus_menu1_pick_rate"] = opening["early_menu23_pick_rate"] - opening["early_menu1_pick_rate"]
    opening["support_bucket"] = opening["early_offer_count"].map(opening_support_bucket)

    strong = opening[
        (opening["early_offer_count"] >= 7)
        & (opening["early_pick_rate"] >= 0.60)
    ].sort_values(["early_pick_rate", "early_offer_count", "card_id"], ascending=[False, False, True])

    low_priority = opening[
        (opening["early_offer_count"] >= 15)
        & (opening["early_pick_rate"] <= 0.35)
    ].sort_values(["early_offer_count", "early_pick_rate", "card_id"], ascending=[False, True, True])

    stage_shift = opening[
        (opening["early_offer_count"] >= 7)
        & (opening["early_menu1_offer_count"] >= 2)
        & (opening["early_menu23_offer_count"] >= 3)
        & (opening["menu23_minus_menu1_pick_rate"].abs() >= 0.25)
    ].copy()
    stage_shift["shift_direction"] = stage_shift["menu23_minus_menu1_pick_rate"].map(
        lambda value: "第 2/3 次更愿意抓" if value > 0 else "第 1 次更愿意抓"
    )
    stage_shift = stage_shift.sort_values(
        ["menu23_minus_menu1_pick_rate", "early_offer_count", "card_id"],
        ascending=[False, False, True],
    )

    return {
        "opening_card_stats": opening.sort_values(["early_pick_count", "early_offer_count", "card_id"], ascending=[False, False, True]),
        "opening_strong_signals": strong.reset_index(drop=True),
        "opening_low_priority": low_priority.reset_index(drop=True),
        "opening_stage_shift": stage_shift.reset_index(drop=True),
    }


def build_card_adoption_classes(card_metrics: pd.DataFrame) -> pd.DataFrame:
    definitions = [
        (
            "A 高抓高留牌",
            (card_metrics["offer_count"] >= 20)
            & (card_metrics["pick_rate_when_offered"] >= 0.35)
            & (card_metrics["final_run_count_effective"] >= 20),
            "看到后经常拿，拿了后也经常进入最终卡组。",
        ),
        (
            "B 高抓但低留牌",
            (card_metrics["offer_count"] >= 15)
            & (card_metrics["pick_rate_when_offered"] >= 0.35)
            & (card_metrics["final_run_count_effective"] < 15),
            "可能是过渡、临时解法、被删/变，或样本还不够。",
        ),
        (
            "C 低抓但高留牌",
            (card_metrics["offer_count"] >= 30)
            & (card_metrics["pick_rate_when_offered"] <= 0.15)
            & (card_metrics["final_run_count_effective"] >= 15),
            "可能来自非标准来源，或一旦进入卡组就常保留。",
        ),
        (
            "D 高出现低抓牌",
            (card_metrics["offer_count"] >= 100)
            & (card_metrics["pick_rate_when_offered"] <= 0.10),
            "经常被看到，但通常不是选择目标。",
        ),
        (
            "E 高升级牌",
            (card_metrics["final_run_count_effective"] >= 15)
            & (card_metrics["final_upgrade_rate_in_final"] >= 0.75),
            "不仅常留，而且最终通常要升级。",
        ),
        (
            "F 高整跳暴露牌",
            (card_metrics["skip_non_shop_offer_count"] >= 50)
            & (card_metrics["skip_skip_exposure_rate"] >= 0.45),
            "经常出现在整张菜单都不拿的奖励里。",
        ),
    ]
    rows = []
    for category, mask, meaning in definitions:
        group = card_metrics[mask].sort_values(
            ["pick_count", "offer_count", "final_run_count_effective", "card_id"],
            ascending=[False, False, False, True],
        )
        for rank, row in enumerate(group.to_dict("records"), start=1):
            rows.append(card_class_row(category, rank, row, meaning))
    return pd.DataFrame(rows)


def card_class_row(category: str, rank: int, row: dict[str, Any], meaning: str) -> dict[str, Any]:
    return {
        "category": category,
        "rank": rank,
        "card_id": row["card_id"],
        "card_name_zh": row.get("card_name_zh", ""),
        "offer_count": int(row.get("offer_count", 0)),
        "pick_count": int(row.get("pick_count", 0)),
        "pick_rate_when_offered": row.get("pick_rate_when_offered", 0),
        "final_run_count": int(row.get("final_run_count_effective", 0)),
        "final_copy_count": int(row.get("final_copy_count_effective", 0)),
        "upgrade_rate_in_final": row.get("final_upgrade_rate_in_final", 0),
        "campfire_upgrade_count": int(row.get("campfire_upgrade_count", 0)),
        "skip_exposure_rate": row.get("skip_skip_exposure_rate", 0),
        "meaning": meaning,
    }


def build_pairwise_tables(pairwise: pd.DataFrame, card_metrics: pd.DataFrame) -> dict[str, pd.DataFrame]:
    frequent = pairwise[pairwise["total_meetings"] >= 5].sort_values(
        ["total_meetings", "card_a", "card_b"],
        ascending=[False, True, True],
    )

    dominant_rows = []
    for row in pairwise.to_dict("records"):
        total = int(row["total_meetings"])
        smooth = float(row["smooth_a_pick_rate_vs_b"])
        if total < 4 or not (smooth >= 0.80 or smooth <= 0.20):
            continue
        if smooth >= 0.80:
            dominant_rows.append(oriented_pairwise_row(row, a_is_winner=True))
        else:
            dominant_rows.append(oriented_pairwise_row(row, a_is_winner=False))
    dominant = pd.DataFrame(dominant_rows)
    if not dominant.empty:
        dominant = dominant.sort_values(
            ["total_meetings", "chosen_smooth_pick_rate", "chosen_card"],
            ascending=[False, False, True],
            ignore_index=True,
        )

    controversial = pairwise[
        (pairwise["total_meetings"] >= 4)
        & (pairwise["smooth_a_pick_rate_vs_b"] >= 0.35)
        & (pairwise["smooth_a_pick_rate_vs_b"] <= 0.65)
    ].sort_values(["total_meetings", "card_a", "card_b"], ascending=[False, True, True])

    profile = build_pairwise_card_profile(pairwise, card_metrics)
    return {
        "pairwise_frequent": frequent.reset_index(drop=True),
        "pairwise_dominant": dominant.reset_index(drop=True) if not dominant.empty else empty_dominant_pairwise_frame(),
        "pairwise_controversial": controversial.reset_index(drop=True),
        "pairwise_act_reversal": build_pairwise_act_reversal(pairwise),
        "pairwise_card_profile": profile,
    }


def oriented_pairwise_row(row: dict[str, Any], a_is_winner: bool) -> dict[str, Any]:
    if a_is_winner:
        chosen_card = row["card_a"]
        chosen_name = row.get("card_a_name_zh", "")
        passed_card = row["card_b"]
        passed_name = row.get("card_b_name_zh", "")
        chosen_wins = int(row["a_beats_b"])
        passed_wins = int(row["b_beats_a"])
        chosen_rate = float(row["smooth_a_pick_rate_vs_b"])
    else:
        chosen_card = row["card_b"]
        chosen_name = row.get("card_b_name_zh", "")
        passed_card = row["card_a"]
        passed_name = row.get("card_a_name_zh", "")
        chosen_wins = int(row["b_beats_a"])
        passed_wins = int(row["a_beats_b"])
        chosen_rate = 1.0 - float(row["smooth_a_pick_rate_vs_b"])
    total = int(row["total_meetings"])
    return {
        "chosen_card": chosen_card,
        "chosen_card_name_zh": chosen_name,
        "passed_card": passed_card,
        "passed_card_name_zh": passed_name,
        "chosen_wins": chosen_wins,
        "passed_wins": passed_wins,
        "total_meetings": total,
        "chosen_smooth_pick_rate": chosen_rate,
        "support_bucket": pairwise_support_bucket(total),
        "rule_strength": "强" if total >= 6 else "中",
        "scope": "标准奖励，非商店，3/4 选 1，不含整跳菜单",
    }


def empty_dominant_pairwise_frame() -> pd.DataFrame:
    return pd.DataFrame(
        columns=[
            "chosen_card",
            "chosen_card_name_zh",
            "passed_card",
            "passed_card_name_zh",
            "chosen_wins",
            "passed_wins",
            "total_meetings",
            "chosen_smooth_pick_rate",
            "support_bucket",
            "rule_strength",
            "scope",
        ]
    )


def build_pairwise_card_profile(pairwise: pd.DataFrame, card_metrics: pd.DataFrame) -> pd.DataFrame:
    names = dict(zip(card_metrics["card_id"], card_metrics["card_name_zh"]))
    rows = []
    expanded = []
    for row in pairwise.to_dict("records"):
        expanded.append(
            {
                "card_id": row["card_a"],
                "opponent_card": row["card_b"],
                "wins": int(row["a_beats_b"]),
                "losses": int(row["b_beats_a"]),
                "total": int(row["total_meetings"]),
            }
        )
        expanded.append(
            {
                "card_id": row["card_b"],
                "opponent_card": row["card_a"],
                "wins": int(row["b_beats_a"]),
                "losses": int(row["a_beats_b"]),
                "total": int(row["total_meetings"]),
            }
        )
    expanded_frame = pd.DataFrame(expanded)
    for card_id, group in expanded_frame.groupby("card_id"):
        wins = int(group["wins"].sum())
        losses = int(group["losses"].sum())
        rows.append(
            {
                "card_id": card_id,
                "card_name_zh": names.get(card_id, ""),
                "pairwise_win_count": wins,
                "pairwise_loss_count": losses,
                "pairwise_total": wins + losses,
                "pairwise_win_rate": safe_rate(wins, wins + losses),
                "common_wins": summarize_matchups(group[group["wins"] > group["losses"]], names, "wins", "losses"),
                "common_losses": summarize_matchups(group[group["losses"] > group["wins"]], names, "losses", "wins"),
            }
        )
    return pd.DataFrame(rows).sort_values(
        ["pairwise_total", "pairwise_win_rate", "card_id"],
        ascending=[False, False, True],
        ignore_index=True,
    )


def build_pairwise_act_reversal(pairwise: pd.DataFrame) -> pd.DataFrame:
    rows = []
    for row in pairwise.to_dict("records"):
        act1_total = int(row["act1_a_beats_b"]) + int(row["act1_b_beats_a"])
        late_a = int(row["act2_a_beats_b"]) + int(row["act3_a_beats_b"])
        late_b = int(row["act2_b_beats_a"]) + int(row["act3_b_beats_a"])
        late_total = late_a + late_b
        if act1_total < 2 or late_total < 2:
            continue
        act1_rate = safe_rate(row["act1_a_beats_b"], act1_total)
        late_rate = safe_rate(late_a, late_total)
        if abs(act1_rate - late_rate) < 0.50:
            continue
        rows.append(
            {
                "card_a": row["card_a"],
                "card_a_name_zh": row.get("card_a_name_zh", ""),
                "card_b": row["card_b"],
                "card_b_name_zh": row.get("card_b_name_zh", ""),
                "act1_a_pick_rate": act1_rate,
                "late_a_pick_rate": late_rate,
                "act1_total": act1_total,
                "late_total": late_total,
                "direction": "Act1 更偏 A，后期更偏 B" if act1_rate > late_rate else "Act1 更偏 B，后期更偏 A",
            }
        )
    if not rows:
        return pd.DataFrame(
            columns=[
                "card_a",
                "card_a_name_zh",
                "card_b",
                "card_b_name_zh",
                "act1_a_pick_rate",
                "late_a_pick_rate",
                "act1_total",
                "late_total",
                "direction",
            ]
        )
    return pd.DataFrame(rows).sort_values(
        ["act1_total", "late_total", "card_a", "card_b"],
        ascending=[False, False, True, True],
        ignore_index=True,
    )


def build_candidate_lists(
    card_metrics: pd.DataFrame,
    opening_strong: pd.DataFrame,
    pairwise_dominant: pd.DataFrame,
) -> pd.DataFrame:
    rows = []
    groups = [
        (
            "1 高抓高留牌",
            card_metrics[
                (card_metrics["offer_count"] >= 20)
                & (card_metrics["pick_rate_when_offered"] >= 0.35)
                & (card_metrics["final_run_count_effective"] >= 20)
            ].sort_values(["pick_count", "final_run_count_effective"], ascending=[False, False]),
            "pick_rate_when_offered",
        ),
        ("2 前三奖励强信号牌", opening_strong, "early_pick_rate"),
        (
            "5 高火堆升级牌",
            card_metrics[card_metrics["campfire_upgrade_count"] >= 10].sort_values(
                ["campfire_upgrade_count", "card_id"], ascending=[False, True]
            ),
            "campfire_upgrade_count",
        ),
        (
            "6 高整跳暴露牌",
            card_metrics[
                (card_metrics["skip_non_shop_offer_count"] >= 50)
                & (card_metrics["skip_skip_exposure_rate"] >= 0.45)
            ].sort_values(["skip_skip_exposure_rate", "skip_non_shop_offer_count"], ascending=[False, False]),
            "skip_skip_exposure_rate",
        ),
    ]
    for group_name, frame, metric_col in groups:
        for rank, row in enumerate(frame.head(40).to_dict("records"), start=1):
            rows.append(candidate_row(group_name, rank, row, metric_col))

    for rank, row in enumerate(pairwise_dominant.head(40).to_dict("records"), start=1):
        rows.append(
            {
                "candidate_group": "3 高频同屏赢家",
                "rank": rank,
                "card_id": row["chosen_card"],
                "card_name_zh": row.get("chosen_card_name_zh", ""),
                "primary_metric": row["chosen_smooth_pick_rate"],
                "support_count": row["total_meetings"],
                "related_card": row["passed_card"],
                "related_card_name_zh": row.get("passed_card_name_zh", ""),
                "notes": f"同屏 {row['chosen_wins']}:{row['passed_wins']} 优先。",
            }
        )
        rows.append(
            {
                "candidate_group": "4 高频同屏输家",
                "rank": rank,
                "card_id": row["passed_card"],
                "card_name_zh": row.get("passed_card_name_zh", ""),
                "primary_metric": 1.0 - row["chosen_smooth_pick_rate"],
                "support_count": row["total_meetings"],
                "related_card": row["chosen_card"],
                "related_card_name_zh": row.get("chosen_card_name_zh", ""),
                "notes": f"同屏 {row['passed_wins']}:{row['chosen_wins']} 劣后。",
            }
        )
    return pd.DataFrame(rows)


def candidate_row(group_name: str, rank: int, row: dict[str, Any], metric_col: str) -> dict[str, Any]:
    if metric_col == "campfire_upgrade_count":
        support_count = row.get("campfire_upgrade_count", 0)
    elif metric_col == "skip_skip_exposure_rate":
        support_count = row.get("skip_non_shop_offer_count", 0)
    elif metric_col == "early_pick_rate":
        support_count = row.get("early_offer_count", 0)
    else:
        support_count = row.get("offer_count", 0)
    return {
        "candidate_group": group_name,
        "rank": rank,
        "card_id": row["card_id"],
        "card_name_zh": row.get("card_name_zh", ""),
        "primary_metric": row.get(metric_col, 0),
        "support_count": int(support_count),
        "related_card": "",
        "related_card_name_zh": "",
        "notes": "",
    }


def build_rhythm_tables(base: dict[str, pd.DataFrame]) -> dict[str, pd.DataFrame]:
    return {
        "upgrade_priority": build_upgrade_priority(
            base["campfire_card_upgrade_summary"],
            base["final_deck_card_summary"],
        ),
        "non_campfire_upgrade_noise": build_non_campfire_upgrade_noise(base["non_campfire_card_upgrade_summary"]),
        "remove_rhythm": build_remove_rhythm(base["card_remove_summary"]),
        "special_event_region": build_special_event_region(base["special_event_summary"]),
        "skip_rhythm": build_skip_rhythm(base["card_offers"], base["skip_menu_summary"]),
    }


def build_upgrade_priority(campfire: pd.DataFrame, final_summary: pd.DataFrame) -> pd.DataFrame:
    frame = campfire.merge(
        final_summary[["card_id", "final_run_count", "final_copy_count", "upgrade_rate_in_final"]],
        on="card_id",
        how="left",
    )
    frame[["final_run_count", "final_copy_count", "upgrade_rate_in_final"]] = frame[
        ["final_run_count", "final_copy_count", "upgrade_rate_in_final"]
    ].fillna(0)
    frame["priority_signal"] = frame.apply(upgrade_priority_signal, axis=1)
    return frame.sort_values(["upgrade_count", "first_upgrade_count", "card_id"], ascending=[False, False, True]).reset_index(drop=True)


def upgrade_priority_signal(row: pd.Series) -> str:
    if row["first_upgrade_count"] >= 5 or (row["act1_upgrade_count"] >= 10 and row["avg_upgrade_floor"] <= 18):
        return "第一幕首升/早敲候选"
    if row["upgrade_count"] >= 20:
        return "高频主动升级"
    if row["upgrade_rate_in_final"] >= 0.75 and row["final_run_count"] >= 15:
        return "最终高升级率"
    return "观察"


def build_remove_rhythm(remove_summary: pd.DataFrame) -> pd.DataFrame:
    frame = remove_summary.copy()
    frame["remove_timing"] = frame.apply(remove_timing_label, axis=1)
    frame["active_scope"] = "当前 V1 公开底表为卡牌汇总粒度；每局删牌细节需提升删除事件表后再复盘。"
    return frame.sort_values(["remove_count", "avg_remove_floor", "card_id"], ascending=[False, True, True]).reset_index(drop=True)


def build_non_campfire_upgrade_noise(non_campfire: pd.DataFrame) -> pd.DataFrame:
    frame = non_campfire.copy()
    if frame.empty:
        return frame
    frame["noise_reading"] = frame.apply(non_campfire_noise_label, axis=1)
    return frame.sort_values(["upgrade_count", "card_id"], ascending=[False, True]).reset_index(drop=True)


def non_campfire_noise_label(row: pd.Series) -> str:
    if row["card_id"] in {"CARD.STRIKE_REGENT", "CARD.DEFEND_REGENT"}:
        return "起始牌自动升级噪声，不代表火堆优先级"
    if row["upgrade_count"] >= 5:
        return "非火堆升级来源较多，解释最终升级率时需剥离"
    return "低频非火堆升级"


def build_special_event_region(special_summary: pd.DataFrame) -> pd.DataFrame:
    frame = special_summary.copy()
    if frame.empty:
        return frame
    frame["strategy_reading"] = frame.apply(
        lambda row: (
            f"{row['event_label']}：遭遇 {int(row['encounter_count'])} 次，"
            f"拿取 {int(row['take_count'])} 次，归还 {int(row['return_count'])} 次，"
            f"任务完成 {int(row['completion_count'])} 次；不计入主动删牌。"
        ),
        axis=1,
    )
    return frame


def remove_timing_label(row: pd.Series) -> str:
    if row["act1_remove_count"] >= row["act2_remove_count"] and row["act1_remove_count"] >= row["act3_remove_count"]:
        return "第一幕偏早删除"
    if row["act2_remove_count"] >= row["act3_remove_count"]:
        return "第二幕集中删除"
    return "第三幕/后期删除"


def build_skip_rhythm(card_offers: pd.DataFrame, skip_summary: pd.DataFrame) -> pd.DataFrame:
    menus = menu_dataframe(card_offers)
    menus = menus[~menus["source_type"].isin(SHOP_SOURCES)].copy()
    menus["skipped"] = menus["picked_count"] == 0
    first_skip = menus[menus["skipped"]].sort_values(["run_id", "global_node"]).groupby("run_id").first()
    first_skip_floor_by_run = first_skip["global_node"].to_dict()
    frame = skip_summary.copy()
    frame["first_skip_run_count"] = frame.apply(
        lambda row: int(
            menus[
                (menus["source_type"] == row["source_type"])
                & (menus["act"] == row["act"])
                & (menus["skipped"])
                & (menus["global_node"].isin(first_skip_floor_by_run.values()))
            ]["run_id"].nunique()
        ),
        axis=1,
    )
    frame["rhythm_reading"] = frame["skip_rate"].map(
        lambda value: "整跳高发" if value >= 0.50 else ("低整跳/继续补牌" if value <= 0.20 else "中等整跳")
    )
    return frame.sort_values(["act", "source_type"], ignore_index=True)


def build_strata_macro_summary(strata: pd.DataFrame) -> pd.DataFrame:
    rows = []
    for stratum_type, column in [
        ("version", "build_id"),
        ("act1_pressure", "act1_pressure_bucket"),
        ("elite_route", "elite_route_bucket"),
        ("rest_count", "rest_bucket"),
        ("remove_count", "remove_bucket"),
    ]:
        for value, group in strata.groupby(column):
            rows.append(
                {
                    "stratum_type": stratum_type,
                    "stratum_value": value,
                    "run_count": int(len(group)),
                    "avg_elite_count": safe_mean(group["elite_count"]),
                    "avg_act1_damage": safe_mean(group["act1_damage"]),
                    "avg_total_damage": safe_mean(group["total_damage"]),
                    "avg_heal_count": safe_mean(group["heal_count"]),
                    "avg_card_remove_count": safe_mean(group["card_remove_count"]),
                    "avg_final_deck_size": safe_mean(group["final_deck_size"]),
                }
            )
    return pd.DataFrame(rows)


def build_strata_pick_rate_check(card_offers: pd.DataFrame, strata: pd.DataFrame, focus_cards: list[str]) -> pd.DataFrame:
    offers = card_offers[card_offers["card_id"].isin(focus_cards)].merge(
        strata[
            [
                "run_id",
                "build_id",
                "act1_pressure_bucket",
                "elite_route_bucket",
                "rest_bucket",
                "remove_bucket",
            ]
        ],
        on="run_id",
        how="left",
    )
    rows = []
    stratum_columns = {
        "act": "act",
        "version": "build_id",
        "act1_pressure": "act1_pressure_bucket",
        "elite_route": "elite_route_bucket",
        "rest_count": "rest_bucket",
        "remove_count": "remove_bucket",
    }
    for stratum_type, column in stratum_columns.items():
        for (card_id, card_name, value), group in offers.groupby(["card_id", "card_name_zh", column], dropna=False):
            offer_count = int(len(group))
            pick_count = int(group["was_picked"].sum())
            rows.append(
                {
                    "stratum_type": stratum_type,
                    "stratum_value": value,
                    "card_id": card_id,
                    "card_name_zh": card_name,
                    "offer_count": offer_count,
                    "pick_count": pick_count,
                    "pick_rate": safe_rate(pick_count, offer_count),
                    "support_bucket": card_support_bucket(offer_count),
                }
            )
    return pd.DataFrame(rows).sort_values(
        ["stratum_type", "card_id", "stratum_value"],
        ignore_index=True,
    )


def build_version_card_pick_check(card_offers: pd.DataFrame, strata: pd.DataFrame, focus_cards: list[str]) -> pd.DataFrame:
    offers = card_offers[card_offers["card_id"].isin(focus_cards)].merge(
        strata[["run_id", "build_id"]],
        on="run_id",
        how="left",
    )
    rows = []
    for (card_id, card_name, build_id), group in offers.groupby(["card_id", "card_name_zh", "build_id"], dropna=False):
        offer_count = int(len(group))
        pick_count = int(group["was_picked"].sum())
        rows.append(
            {
                "card_id": card_id,
                "card_name_zh": card_name,
                "build_id": build_id,
                "offer_count": offer_count,
                "pick_count": pick_count,
                "pick_rate": safe_rate(pick_count, offer_count),
                "support_bucket": card_support_bucket(offer_count),
            }
        )
    return pd.DataFrame(rows).sort_values(["card_id", "build_id"], ignore_index=True)


def build_version_skip_rate_check(card_offers: pd.DataFrame, strata: pd.DataFrame) -> pd.DataFrame:
    menus = menu_dataframe(card_offers)
    menus = menus[~menus["source_type"].isin(SHOP_SOURCES)].merge(
        strata[["run_id", "build_id"]],
        on="run_id",
        how="left",
    )
    menus["skipped"] = menus["picked_count"] == 0
    rows = []
    for (build_id, source_type, act), group in menus.groupby(["build_id", "source_type", "act"], dropna=False):
        skipped = int(group["skipped"].sum())
        rows.append(
            {
                "build_id": build_id,
                "source_type": source_type,
                "act": act,
                "menu_count": int(len(group)),
                "skipped_menu_count": skipped,
                "skip_rate": safe_rate(skipped, len(group)),
            }
        )
    return pd.DataFrame(rows).sort_values(["build_id", "act", "source_type"], ignore_index=True)


def build_floor_segment_pick_check(card_offers: pd.DataFrame, focus_cards: list[str]) -> pd.DataFrame:
    offers = card_offers[card_offers["card_id"].isin(focus_cards)].copy()
    offers["floor_segment"] = offers["global_node"].map(floor_segment)
    rows = []
    for (card_id, card_name, segment), group in offers.groupby(["card_id", "card_name_zh", "floor_segment"], dropna=False):
        offer_count = int(len(group))
        pick_count = int(group["was_picked"].sum())
        rows.append(
            {
                "card_id": card_id,
                "card_name_zh": card_name,
                "floor_segment": segment,
                "offer_count": offer_count,
                "pick_count": pick_count,
                "pick_rate": safe_rate(pick_count, offer_count),
                "support_bucket": card_support_bucket(offer_count),
            }
        )
    return pd.DataFrame(rows).sort_values(["card_id", "floor_segment"], ignore_index=True)


def build_strata_skip_rate_check(card_offers: pd.DataFrame, strata: pd.DataFrame) -> pd.DataFrame:
    menus = menu_dataframe(card_offers)
    menus = menus[~menus["source_type"].isin(SHOP_SOURCES)].merge(
        strata[["run_id", "build_id", "act1_pressure_bucket", "elite_route_bucket"]],
        on="run_id",
        how="left",
    )
    menus["skipped"] = menus["picked_count"] == 0
    rows = []
    stratum_columns = {
        "act": "act",
        "version": "build_id",
        "act1_pressure": "act1_pressure_bucket",
        "elite_route": "elite_route_bucket",
    }
    for stratum_type, column in stratum_columns.items():
        for value, group in menus.groupby(column, dropna=False):
            menu_count = int(len(group))
            skipped = int(group["skipped"].sum())
            rows.append(
                {
                    "stratum_type": stratum_type,
                    "stratum_value": value,
                    "menu_count": menu_count,
                    "skipped_menu_count": skipped,
                    "skip_rate": safe_rate(skipped, menu_count),
                }
            )
    return pd.DataFrame(rows).sort_values(["stratum_type", "stratum_value"], ignore_index=True)


def select_focus_cards(card_metrics: pd.DataFrame, opening: pd.DataFrame, limit: int = 30) -> list[str]:
    scores: dict[str, float] = {}
    sources = [
        ("pick_count", card_metrics.sort_values("pick_count", ascending=False).head(20)["card_id"]),
        (
            "pick_rate",
            card_metrics[card_metrics["offer_count"] >= 5]
            .sort_values(["pick_rate_when_offered", "offer_count"], ascending=[False, False])
            .head(20)["card_id"],
        ),
        ("final", card_metrics.sort_values("final_run_count_effective", ascending=False).head(20)["card_id"]),
        ("campfire", card_metrics.sort_values("campfire_upgrade_count", ascending=False).head(20)["card_id"]),
        ("early", opening.sort_values("early_pick_count", ascending=False).head(20)["card_id"]),
    ]
    for weight, (_, cards) in enumerate(sources, start=1):
        for rank, card_id in enumerate(cards, start=1):
            scores[str(card_id)] = scores.get(str(card_id), 0.0) + (25 - rank) + weight
    return [card_id for card_id, _ in sorted(scores.items(), key=lambda item: (-item[1], item[0]))[:limit]]


def build_card_evidence(
    card_metrics: pd.DataFrame,
    opening: pd.DataFrame,
    pairwise_profile: pd.DataFrame,
    focus_cards: list[str],
) -> pd.DataFrame:
    metrics = card_metrics.set_index("card_id")
    opening_index = opening.set_index("card_id")
    pairwise_index = pairwise_profile.set_index("card_id")
    rows = []
    for card_id in focus_cards:
        if card_id not in metrics.index:
            continue
        metric = metrics.loc[card_id]
        early = opening_index.loc[card_id] if card_id in opening_index.index else pd.Series(dtype=object)
        pairwise = pairwise_index.loc[card_id] if card_id in pairwise_index.index else pd.Series(dtype=object)
        row = {
            "card_id": card_id,
            "card_name_zh": metric.get("card_name_zh", ""),
            "offer_count": int(metric.get("offer_count", 0)),
            "pick_count": int(metric.get("pick_count", 0)),
            "pick_rate_when_offered": metric.get("pick_rate_when_offered", 0),
            "act1_offer_count": int(metric.get("act1_offer_count", 0)),
            "act1_pick_rate": metric.get("act1_pick_rate", 0),
            "early_offer_count": int(early.get("early_offer_count", 0) or 0),
            "early_pick_count": int(early.get("early_pick_count", 0) or 0),
            "early_pick_rate": early.get("early_pick_rate", 0) or 0,
            "final_run_count": int(metric.get("final_run_count_effective", 0)),
            "final_copy_count": int(metric.get("final_copy_count_effective", 0)),
            "upgrade_rate_in_final": metric.get("final_upgrade_rate_in_final", 0),
            "campfire_upgrade_count": int(metric.get("campfire_upgrade_count", 0)),
            "skip_exposure_rate": metric.get("skip_skip_exposure_rate", 0),
            "pairwise_win_count": int(pairwise.get("pairwise_win_count", 0) or 0),
            "pairwise_loss_count": int(pairwise.get("pairwise_loss_count", 0) or 0),
            "pairwise_win_rate": pairwise.get("pairwise_win_rate", 0) or 0,
            "common_wins": pairwise.get("common_wins", ""),
            "common_losses": pairwise.get("common_losses", ""),
            "support_bucket": metric.get("support_bucket", ""),
        }
        row["evidence_grade"] = evidence_grade(row)
        row["conclusion"] = evidence_conclusion(row)
        rows.append(row)
    frame = pd.DataFrame(rows)
    frame["grade_sort"] = frame["evidence_grade"].map({"S": 0, "A": 1, "B": 2, "C": 3, "D": 4}).fillna(9)
    frame = frame.sort_values(["grade_sort", "pick_count", "card_id"], ascending=[True, False, True])
    return frame.drop(columns=["grade_sort"]).reset_index(drop=True)


def evidence_grade(row: dict[str, Any]) -> str:
    if row["offer_count"] >= 100 and row["pick_rate_when_offered"] <= 0.10 and row["final_run_count"] < 15:
        return "D"
    if row["offer_count"] < 15:
        return "C"
    score = 0
    score += int(row["offer_count"] >= 30)
    score += int(row["pick_rate_when_offered"] >= 0.35)
    score += int(row["final_run_count"] >= 20)
    score += int(row["campfire_upgrade_count"] >= 10 or row["upgrade_rate_in_final"] >= 0.75)
    score += int(row["pairwise_win_count"] >= row["pairwise_loss_count"] and (row["pairwise_win_count"] + row["pairwise_loss_count"]) >= 6)
    if score >= 5:
        return "S"
    if score >= 3:
        return "A"
    if score >= 2:
        return "B"
    return "C"


def evidence_conclusion(row: dict[str, Any]) -> str:
    if row["evidence_grade"] == "D":
        return "高出现低采用，当前样本更支持谨慎或跳过。"
    if row["pick_rate_when_offered"] >= 0.35 and row["final_run_count"] >= 20:
        return "高采用且高沉淀，属于核心候选。"
    if row["early_pick_rate"] >= 0.60 and row["early_offer_count"] >= 7:
        return "前三奖励强信号，开局看到时历史上倾向抓取。"
    if row["campfire_upgrade_count"] >= 10:
        return "主动火堆升级频繁，拿到后通常需要升级资源。"
    return "有可观察信号，但需要结合上下文。"


def build_rules_library(
    opening_tables: dict[str, pd.DataFrame],
    adoption_classes: pd.DataFrame,
    pairwise_tables: dict[str, pd.DataFrame],
    rhythm_tables: dict[str, pd.DataFrame],
    card_evidence: pd.DataFrame,
) -> pd.DataFrame:
    rows = []
    for row in opening_tables["opening_strong_signals"].head(12).to_dict("records"):
        rows.append(
            {
                "rule_group": "开局抓牌",
                "rule": f"前三奖励看到 {card_label(row)} 时，历史样本倾向抓取。",
                "evidence": f"前三出现 {row['early_offer_count']}，抓 {row['early_pick_count']}，抓取率 {format_rate(row['early_pick_rate'])}。",
                "strength": "强" if row["early_offer_count"] >= 15 else "中",
                "source_table": "opening_strong_signals",
            }
        )
    for row in opening_tables["opening_low_priority"].head(8).to_dict("records"):
        rows.append(
            {
                "rule_group": "开局谨慎",
                "rule": f"前三奖励看到 {card_label(row)} 时，不应仅因常见而优先抓。",
                "evidence": f"前三出现 {row['early_offer_count']}，抓取率 {format_rate(row['early_pick_rate'])}。",
                "strength": "中",
                "source_table": "opening_low_priority",
            }
        )
    for row in pairwise_tables["pairwise_dominant"].head(15).to_dict("records"):
        rows.append(
            {
                "rule_group": "同屏对位",
                "rule": f"{row['chosen_card_name_zh'] or row['chosen_card']} 与 {row['passed_card_name_zh'] or row['passed_card']} 同屏时，样本中通常选前者。",
                "evidence": f"标准奖励对位 {row['chosen_wins']}:{row['passed_wins']}，平滑选择率 {format_rate(row['chosen_smooth_pick_rate'])}。",
                "strength": row["rule_strength"],
                "source_table": "pairwise_dominant",
            }
        )
    for row in rhythm_tables["upgrade_priority"].head(12).to_dict("records"):
        rows.append(
            {
                "rule_group": "升级",
                "rule": f"{row.get('card_name_zh') or row['card_id']} 属于 {row['priority_signal']}。",
                "evidence": f"火堆升级 {row['upgrade_count']} 次，第一幕 {row['act1_upgrade_count']} 次，首升 {row['first_upgrade_count']} 次。",
                "strength": "强" if row["upgrade_count"] >= 20 else "中",
                "source_table": "upgrade_priority",
            }
        )
    remove = rhythm_tables["remove_rhythm"].set_index("card_id", drop=False)
    for card_id in ["CARD.STRIKE_REGENT", "CARD.DEFEND_REGENT"]:
        if card_id in remove.index:
            row = remove.loc[card_id]
            rows.append(
                {
                    "rule_group": "删除",
                    "rule": f"{row.get('card_name_zh') or card_id} 的删除节奏为：{row['remove_timing']}。",
                    "evidence": f"删除 {row['remove_count']} 次；平均楼层 {round(row['avg_remove_floor'], 2)}；Act1/2/3 = {row['act1_remove_count']}/{row['act2_remove_count']}/{row['act3_remove_count']}。",
                    "strength": "强",
                    "source_table": "remove_rhythm",
                }
            )
    for row in rhythm_tables["special_event_region"].to_dict("records"):
        rows.append(
            {
                "rule_group": "特殊事件",
                "rule": f"{row['event_label']}应作为特殊事件区域讨论，不作为主动删牌对象。",
                "evidence": row["strategy_reading"],
                "strength": "强",
                "source_table": "special_event_region",
            }
        )
    for row in rhythm_tables["non_campfire_upgrade_noise"].head(8).to_dict("records"):
        rows.append(
            {
                "rule_group": "升级噪声",
                "rule": f"{row.get('card_name_zh') or row['card_id']} 的非火堆升级不代表主动敲牌优先级。",
                "evidence": f"非火堆升级 {row['upgrade_count']} 次；{row['noise_reading']}。",
                "strength": "中" if row["upgrade_count"] >= 5 else "弱",
                "source_table": "non_campfire_upgrade_noise",
            }
        )
    high_skip = rhythm_tables["skip_rhythm"][
        (rhythm_tables["skip_rhythm"]["act"] >= 2) & (rhythm_tables["skip_rhythm"]["skip_rate"] >= 0.50)
    ].head(6)
    for row in high_skip.to_dict("records"):
        rows.append(
            {
                "rule_group": "跳过",
                "rule": f"Act {int(row['act'])} 的 {row['source_type']} 奖励进入高整跳区间。",
                "evidence": f"菜单 {row['menu_count']}，整跳 {row['skipped_menu_count']}，整跳率 {format_rate(row['skip_rate'])}。",
                "strength": "强" if row["menu_count"] >= 50 else "中",
                "source_table": "skip_rhythm",
            }
        )
    for row in card_evidence[card_evidence["evidence_grade"].isin(["S", "A"])].head(12).to_dict("records"):
        rows.append(
            {
                "rule_group": "单卡证据",
                "rule": f"{row.get('card_name_zh') or row['card_id']}：{row['conclusion']}",
                "evidence": f"证据等级 {row['evidence_grade']}；出现 {row['offer_count']}，抓取率 {format_rate(row['pick_rate_when_offered'])}，最终 {row['final_run_count']} 局。",
                "strength": "强" if row["evidence_grade"] == "S" else "中",
                "source_table": "card_evidence",
            }
        )
    return pd.DataFrame(rows)


def build_rule_validation(rules: pd.DataFrame) -> pd.DataFrame:
    if rules.empty:
        return pd.DataFrame(columns=["rule_group", "rule", "validation_status", "final_report_entry", "validation_note"])
    frame = rules.copy()
    frame["validation_status"] = frame["strength"].map(
        lambda value: "通过" if value == "强" else ("候选" if value == "中" else "仅提示")
    )
    frame["final_report_entry"] = frame["validation_status"].eq("通过")
    frame["validation_note"] = frame["validation_status"].map(
        {
            "通过": "样本量和方向足以进入最终策略报告。",
            "候选": "可进入候选区，需要人工复盘或更多分层验证。",
            "仅提示": "只作为观察，不写成策略规则。",
        }
    )
    return frame[
        ["rule_group", "rule", "evidence", "strength", "validation_status", "final_report_entry", "validation_note", "source_table"]
    ]


def build_case_library(strata: pd.DataFrame) -> pd.DataFrame:
    case_specs = [
        ("典型顺风局", strata.sort_values(["act1_damage", "total_damage", "run_id"], ascending=[True, True, True]).head(5)),
        ("典型高压局", strata.sort_values(["act1_damage", "total_damage", "run_id"], ascending=[False, False, True]).head(5)),
        ("高精英局", strata.sort_values(["elite_count", "run_id"], ascending=[False, True]).head(5)),
        ("多休息仍胜局", strata.sort_values(["heal_count", "run_id"], ascending=[False, True]).head(5)),
        ("极端删牌局", strata.sort_values(["card_remove_count", "run_id"], ascending=[False, True]).head(5)),
        ("极端大卡组局", strata.sort_values(["final_deck_size", "run_id"], ascending=[False, True]).head(5)),
    ]
    rows = []
    for case_type, frame in case_specs:
        for row in frame.to_dict("records"):
            rows.append(
                {
                    "case_type": case_type,
                    "run_id": row["run_id"],
                    "build_id": row["build_id"],
                    "elite_count": row["elite_count"],
                    "act1_damage": row["act1_damage"],
                    "total_damage": row["total_damage"],
                    "heal_count": row["heal_count"],
                    "card_remove_count": row["card_remove_count"],
                    "final_deck_size": row["final_deck_size"],
                }
            )
    return pd.DataFrame(rows)


def build_strategy_markdowns(tables: dict[str, pd.DataFrame]) -> dict[str, str]:
    return {
        "summary.md": build_strategy_summary_markdown(tables),
        "00_strategy_overview.md": build_strategy_overview_markdown(tables),
        "01_candidate_lists.md": build_candidate_lists_markdown(tables),
        "02_card_evidence_handbook.md": build_card_evidence_markdown(tables),
        "03_opening_pick_rules.md": build_opening_rules_markdown(tables),
        "04_pairwise_rules.md": build_pairwise_rules_markdown(tables),
        "05_rhythm_rules.md": build_rhythm_rules_markdown(tables),
        "06_stratification_checks.md": build_stratification_markdown(tables),
        "07_rules_library.md": build_rules_library_markdown(tables),
    }


def build_strategy_summary_markdown(tables: dict[str, pd.DataFrame]) -> str:
    macro = tables["macro_distribution"].set_index("metric")
    version = tables["version_sanity_check"]
    evidence = tables["card_evidence"]
    strong_opening = tables["opening_strong_signals"]
    low_opening = tables["opening_low_priority"]
    dominant = tables["pairwise_dominant"]
    reversal = tables["pairwise_act_reversal"]
    upgrade = tables["upgrade_priority"]
    non_campfire = tables["non_campfire_upgrade_noise"]
    remove = tables["remove_rhythm"]
    special_events = tables["special_event_region"]
    high_skip = tables["skip_rhythm"][
        (tables["skip_rhythm"]["act"] >= 2) & (tables["skip_rhythm"]["skip_rate"] >= 0.50)
    ]
    validated_rules = tables["rule_validation"][tables["rule_validation"]["final_report_entry"]]

    lines = [
        "# 储君 77 连胜策略总结",
        "",
        "本总结由 V2 策略分析代码自动生成，数据来源为 V1 CSV 底表。V2 不重算原始 `.run`，只消费 V1 的可信底表。",
        "",
        "## 样本与口径",
        "",
        f"- 样本：77 局储君 A10 胜利，不含放弃局。",
        f"- 版本：{join_values(version['build_id'])}。",
        "- 主选牌结论优先使用标准奖励口径：非商店、3/4 选 1、不含整跳菜单。",
        "- 主动删牌口径已排除任务完成回收；藏宝图和灯火钥匙进入特殊事件区域。",
        "- 证据等级 S/A/B/C/D 表示结论可信度，不是卡牌强度榜。",
        "",
        "## 九阶段覆盖矩阵",
        "",
        small_table(build_stage_coverage_table(tables)),
        "",
        "## 宏观打法基线",
        "",
        f"- 平均精英数：{metric_mean(macro, 'elite_count')}。",
        f"- 平均商店数：{metric_mean(macro, 'shop_count')}。",
        f"- 平均火堆升级次数：{metric_mean(macro, 'campfire_card_upgrade_count')}。",
        f"- 平均休息次数：{metric_mean(macro, 'heal_count')}。",
        f"- 第一幕平均掉血：{metric_mean(macro, 'act1_damage')}。",
        f"- 全局平均掉血：{metric_mean(macro, 'total_damage')}。",
        f"- 平均删牌次数：{metric_mean(macro, 'card_remove_count')}。",
        f"- 平均最终卡组大小：{metric_mean(macro, 'final_deck_size')}。",
        "",
        "## 特殊事件区域",
        "",
        *summary_special_event_bullets(special_events),
        "",
        "## 开局抓牌信号",
        "",
        "开局强信号牌：",
        *summary_card_bullets(strong_opening, "early_offer_count", "early_pick_count", "early_pick_rate", limit=10),
        "",
        "开局常见但低优先牌：",
        *summary_card_bullets(low_opening, "early_offer_count", "early_pick_count", "early_pick_rate", limit=10),
        "",
        "## 单卡证据摘要",
        "",
        *summary_evidence_bullets(evidence, limit=12),
        "",
        "## 同屏对位摘要",
        "",
        *summary_pairwise_bullets(dominant, limit=12),
        "",
        "分幕反转对位候选：",
        *summary_pairwise_reversal_bullets(reversal, limit=8),
        "",
        "## 升级、删除、跳过",
        "",
        "主动火堆升级优先级：",
        *summary_upgrade_bullets(upgrade, limit=10),
        "",
        "非火堆升级噪声：",
        *summary_non_campfire_upgrade_bullets(non_campfire, limit=8),
        "",
        "删除节奏：",
        *summary_remove_bullets(remove, limit=8),
        "",
        "Act 2/3 高整跳来源：",
        *summary_skip_bullets(high_skip, limit=8),
        "",
        "## 分层验证与最终规则",
        "",
        f"- 版本主卡抓取率检查：{len(tables['version_card_pick_check'])} 行。",
        f"- 版本跳过率检查：{len(tables['version_skip_rate_check'])} 行。",
        f"- 楼层段抓取率检查：{len(tables['floor_segment_pick_check'])} 行。",
        f"- 规则验证表：{len(tables['rule_validation'])} 条候选，其中 {len(validated_rules)} 条进入最终报告。",
        "",
        "已通过规则示例：",
        *summary_validated_rule_bullets(validated_rules, limit=12),
        "",
        "## 后续复盘入口",
        "",
        "- 完整策略报告：`00_strategy_overview.md`",
        "- 候选清单：`01_candidate_lists.md`",
        "- 卡牌证据手册：`02_card_evidence_handbook.md`",
        "- 开局抓牌表：`03_opening_pick_rules.md`",
        "- 同屏对位表：`04_pairwise_rules.md`",
        "- 节奏规则页：`05_rhythm_rules.md`",
        "- 分层验证：`06_stratification_checks.md`",
        "- 规则库：`07_rules_library.md`",
        "- CSV/Excel：同目录下 `*.csv` 与 `dashen_regent_77_strategy.xlsx`",
    ]
    return "\n".join(lines) + "\n"


def build_strategy_overview_markdown(tables: dict[str, pd.DataFrame]) -> str:
    lines = [
        "# 储君 77 连胜策略分析报告",
        "",
        "本报告是 V2 派生分析层：它只读取 V1 生成的 CSV 底表，自动生成候选清单、证据页、规则页和分层验证表。",
        "",
        "## 1. 数据可信度确认",
        "",
        small_table(tables["data_credibility_summary"]),
        "",
        "## 2. 宏观打法画像",
        "",
        small_table(tables["macro_distribution"]),
        "",
        "## 3. 开局策略画像",
        "",
        "### 前三奖励强信号牌",
        "",
        small_table(tables["opening_strong_signals"].head(20)),
        "",
        "### 前三奖励高出现低抓取牌",
        "",
        small_table(tables["opening_low_priority"].head(20)),
        "",
        "## 4. 卡牌采用画像",
        "",
        small_table(tables["card_adoption_classes"].head(60)),
        "",
        "## 5. 同屏对位画像",
        "",
        small_table(tables["pairwise_dominant"].head(30)),
        "",
        "## 6. 升级 / 删除 / 跳过画像",
        "",
        "### 火堆升级优先级",
        "",
        small_table(tables["upgrade_priority"].head(20)),
        "",
        "### 非火堆升级噪声",
        "",
        small_table(tables["non_campfire_upgrade_noise"].head(20)),
        "",
        "### 删除节奏",
        "",
        small_table(tables["remove_rhythm"].head(20)),
        "",
        "### 特殊事件区域",
        "",
        small_table(tables["special_event_region"]),
        "",
        "### 跳过节奏",
        "",
        small_table(tables["skip_rhythm"]),
        "",
        "## 7. 分层验证",
        "",
        small_table(tables["strata_macro_summary"].head(40)),
        "",
        "## 8. 证据链整合",
        "",
        small_table(tables["card_evidence"].head(30)),
        "",
        "## 9. 最终规则库",
        "",
        small_table(tables["rule_validation"].head(80)),
        "",
        "## 复盘案例库入口",
        "",
        small_table(tables["case_library"].head(80)),
    ]
    return "\n".join(lines) + "\n"


def build_candidate_lists_markdown(tables: dict[str, pd.DataFrame]) -> str:
    candidates = tables["candidate_lists"]
    lines = ["# 结论候选清单", ""]
    for group, frame in candidates.groupby("candidate_group"):
        lines.extend([f"## {group}", "", small_table(frame.head(30)), ""])
    return "\n".join(lines) + "\n"


def build_card_evidence_markdown(tables: dict[str, pd.DataFrame]) -> str:
    evidence = tables["card_evidence"]
    lines = ["# 卡牌证据手册", ""]
    for row in evidence.to_dict("records"):
        title = row.get("card_name_zh") or row["card_id"]
        lines.extend(
            [
                f"## {title}",
                "",
                f"- 卡牌：`{row['card_id']}`",
                f"- 结论等级：{row['evidence_grade']}（这是证据可信度，不是卡牌强度榜）",
                f"- 结论：{row['conclusion']}",
                f"- 出现/抓取：{row['offer_count']} / {row['pick_count']}，抓取率 {format_rate(row['pick_rate_when_offered'])}",
                f"- 前三奖励：出现 {row['early_offer_count']}，抓 {row['early_pick_count']}，抓取率 {format_rate(row['early_pick_rate'])}",
                f"- 最终卡组：{row['final_run_count']} 局，{row['final_copy_count']} 拷贝，最终升级率 {format_rate(row['upgrade_rate_in_final'])}",
                f"- 火堆升级：{row['campfire_upgrade_count']} 次；整跳暴露率 {format_rate(row['skip_exposure_rate'])}",
                f"- 同屏胜负：{row['pairwise_win_count']} 胜 / {row['pairwise_loss_count']} 负；常赢：{row.get('common_wins') or '无'}；常输：{row.get('common_losses') or '无'}",
                "",
            ]
        )
    return "\n".join(lines) + "\n"


def build_opening_rules_markdown(tables: dict[str, pd.DataFrame]) -> str:
    lines = [
        "# 开局抓牌表",
        "",
        "## 开局强抓信号",
        "",
        small_table(tables["opening_strong_signals"]),
        "",
        "## 开局谨慎牌",
        "",
        small_table(tables["opening_low_priority"]),
        "",
        "## 开局分歧牌",
        "",
        small_table(tables["opening_stage_shift"]),
    ]
    return "\n".join(lines) + "\n"


def build_pairwise_rules_markdown(tables: dict[str, pd.DataFrame]) -> str:
    lines = [
        "# 同屏对位表",
        "",
        "## 高频对位",
        "",
        small_table(tables["pairwise_frequent"].head(60)),
        "",
        "## 碾压对位",
        "",
        small_table(tables["pairwise_dominant"].head(80)),
        "",
        "## 争议对位",
        "",
        small_table(tables["pairwise_controversial"].head(80)),
        "",
        "## 分幕反转对位",
        "",
        small_table(tables["pairwise_act_reversal"].head(80)),
        "",
        "## 单卡对位画像",
        "",
        small_table(tables["pairwise_card_profile"].head(80)),
    ]
    return "\n".join(lines) + "\n"


def build_rhythm_rules_markdown(tables: dict[str, pd.DataFrame]) -> str:
    lines = [
        "# 节奏规则页",
        "",
        "## 火堆升级",
        "",
        small_table(tables["upgrade_priority"].head(50)),
        "",
        "## 删除节奏",
        "",
        small_table(tables["remove_rhythm"].head(50)),
        "",
        "## 特殊事件区域",
        "",
        small_table(tables["special_event_region"]),
        "",
        "## 非火堆升级噪声",
        "",
        small_table(tables["non_campfire_upgrade_noise"].head(50)),
        "",
        "## 跳过节奏",
        "",
        small_table(tables["skip_rhythm"]),
    ]
    return "\n".join(lines) + "\n"


def build_stratification_markdown(tables: dict[str, pd.DataFrame]) -> str:
    lines = [
        "# 分层验证",
        "",
        "## 宏观分层",
        "",
        small_table(tables["strata_macro_summary"]),
        "",
        "## 重点牌抓取率分层",
        "",
        small_table(tables["strata_pick_rate_check"].head(120)),
        "",
        "## 楼层段抓取率分层",
        "",
        small_table(tables["floor_segment_pick_check"].head(120)),
        "",
        "## 版本主卡抓取率检查",
        "",
        small_table(tables["version_card_pick_check"].head(120)),
        "",
        "## 版本跳过率检查",
        "",
        small_table(tables["version_skip_rate_check"]),
        "",
        "## 跳过率分层",
        "",
        small_table(tables["strata_skip_rate_check"]),
    ]
    return "\n".join(lines) + "\n"


def build_rules_library_markdown(tables: dict[str, pd.DataFrame]) -> str:
    lines = [
        "# 规则库",
        "",
        "## 候选规则",
        "",
        small_table(tables["rules_library"]),
        "",
        "## 规则验证",
        "",
        small_table(tables["rule_validation"]),
        "",
        "## 案例库入口",
        "",
        small_table(tables["case_library"]),
    ]
    return "\n".join(lines) + "\n"


def validate_strategy_tables(tables: dict[str, pd.DataFrame]) -> None:
    missing = sorted(set(STRATEGY_TABLE_ORDER) - set(tables))
    if missing:
        raise ValueError(f"Missing strategy tables: {missing}")
    for name in STRATEGY_TABLE_ORDER:
        if not isinstance(tables[name], pd.DataFrame):
            raise TypeError(f"Strategy table {name} is not a DataFrame")
    assert_columns(tables["candidate_lists"], ["candidate_group", "card_id", "primary_metric"])
    assert_columns(tables["card_evidence"], ["card_id", "evidence_grade", "conclusion"])
    assert_columns(tables["rules_library"], ["rule_group", "rule", "evidence", "strength"])
    assert_columns(tables["special_event_region"], ["special_card_id", "take_count", "completion_count", "strategy_reading"])
    assert_columns(tables["rule_validation"], ["rule_group", "validation_status", "final_report_entry"])
    assert_columns(tables["case_library"], ["case_type", "run_id"])


def assert_columns(frame: pd.DataFrame, columns: list[str]) -> None:
    missing = [column for column in columns if column not in frame.columns]
    if missing:
        raise ValueError(f"Missing columns {missing}")


def menu_dataframe(card_offers: pd.DataFrame) -> pd.DataFrame:
    rows = []
    for menu_id, group in card_offers.groupby("menu_id"):
        first = group.iloc[0]
        rows.append(
            {
                "run_id": first["run_id"],
                "global_node": int(first["global_node"]),
                "act": int(first["act"]),
                "source_type": first["source_type"],
                "menu_id": menu_id,
                "offer_size": int(first["offer_size"]),
                "picked_count": int(first["picked_count"]),
                "was_skipped_menu": bool(first["was_skipped_menu"]),
                "offered_cards": ", ".join(group["card_id"].astype(str)),
                "picked_cards": ", ".join(group.loc[group["was_picked"], "card_id"].astype(str)),
            }
        )
    return pd.DataFrame(rows)


def normalize_bool(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    if pd.isna(value):
        return False
    return str(value).strip().lower() in {"true", "1", "yes"}


def safe_rate(numerator: Any, denominator: Any) -> float:
    denominator_value = float(denominator or 0)
    if denominator_value == 0:
        return 0.0
    return float(numerator or 0) / denominator_value


def safe_mean(values: Iterable[Any]) -> float | None:
    series = pd.to_numeric(pd.Series(list(values)), errors="coerce").dropna()
    if series.empty:
        return None
    return float(series.mean())


def tertile_bucket(series: pd.Series, low_label: str, mid_label: str, high_label: str) -> pd.Series:
    ranked = series.rank(method="first", pct=True)
    return ranked.map(lambda value: low_label if value <= 1 / 3 else (mid_label if value <= 2 / 3 else high_label))


def elite_route_bucket(value: Any) -> str:
    number = int(value)
    if number <= 3:
        return "低精英局"
    if number <= 5:
        return "中精英局"
    return "高精英局"


def floor_segment(value: Any) -> str:
    floor = int(value or 0)
    if floor <= 6:
        return "F1-F6 开局"
    if floor <= 16:
        return "F7-F16 第一幕中后段"
    if floor <= 33:
        return "F17-F33 第二幕"
    if floor <= 50:
        return "F34-F50 第三幕"
    return "F51+ 后续"


def card_support_bucket(offer_count: Any) -> str:
    count = int(offer_count or 0)
    if count < 5:
        return "事实记录"
    if count < 15:
        return "弱参考"
    if count < 30:
        return "中等参考"
    return "主结论候选"


def opening_support_bucket(offer_count: Any) -> str:
    count = int(offer_count or 0)
    if count < 5:
        return "事实记录"
    if count < 7:
        return "弱参考"
    if count < 15:
        return "中等参考"
    return "开局主结论候选"


def pairwise_support_bucket(total_meetings: Any) -> str:
    total = int(total_meetings or 0)
    if total < 3:
        return "不下结论"
    if total < 6:
        return "案例"
    return "对位规则候选"


def card_support_threshold_text(bucket: str) -> str:
    return {
        "事实记录": "offer_count < 5",
        "弱参考": "offer_count 5-14",
        "中等参考": "offer_count 15-29",
        "主结论候选": "offer_count >= 30",
    }.get(bucket, "")


def card_support_guidance(bucket: str) -> str:
    return {
        "事实记录": "只列事实，不下结论。",
        "弱参考": "可以作为样本提示。",
        "中等参考": "可以进入候选清单，但需分层验证。",
        "主结论候选": "可以进入主结论候选。",
    }.get(bucket, "")


def pairwise_support_threshold_text(bucket: str) -> str:
    return {
        "不下结论": "total_meetings < 3",
        "案例": "total_meetings 3-5",
        "对位规则候选": "total_meetings >= 6",
    }.get(bucket, "")


def pairwise_support_guidance(bucket: str) -> str:
    return {
        "不下结论": "样本太少，不下结论。",
        "案例": "可作为复盘案例。",
        "对位规则候选": "可以进入对位规则候选。",
    }.get(bucket, "")


def correlation_reading(value: Any) -> str:
    if value is None or pd.isna(value):
        return "样本不足"
    magnitude = abs(float(value))
    if magnitude >= 0.6:
        strength = "强"
    elif magnitude >= 0.3:
        strength = "中"
    elif magnitude >= 0.15:
        strength = "弱"
    else:
        strength = "很弱"
    direction = "正相关" if value >= 0 else "负相关"
    return f"{strength}{direction}"


def summarize_matchups(group: pd.DataFrame, names: dict[str, str], win_col: str, loss_col: str) -> str:
    if group.empty:
        return ""
    view = group.sort_values([win_col, "total", "opponent_card"], ascending=[False, False, True]).head(3)
    parts = []
    for row in view.to_dict("records"):
        opponent = names.get(row["opponent_card"], row["opponent_card"])
        parts.append(f"{opponent} {int(row[win_col])}-{int(row[loss_col])}")
    return "；".join(parts)


def card_label(row: dict[str, Any] | pd.Series) -> str:
    name = row.get("card_name_zh", "")
    card_id = row.get("card_id", "")
    return f"{name}（{card_id}）" if name else str(card_id)


def join_values(values: Iterable[Any]) -> str:
    return ", ".join(str(value) for value in values)


def format_rate(value: Any) -> str:
    if value is None or pd.isna(value):
        return "n/a"
    return f"{float(value):.3f}"


def small_table(frame: pd.DataFrame) -> str:
    if frame.empty:
        return "_无数据。_"
    view = frame.copy()
    for column in view.columns:
        if pd.api.types.is_float_dtype(view[column]):
            view[column] = view[column].round(3)
    return view.to_markdown(index=False)


def excel_sheet_name(table_name: str) -> str:
    return table_name[:31]


def metric_mean(macro: pd.DataFrame, metric: str) -> str:
    if metric not in macro.index:
        return "n/a"
    value = macro.loc[metric, "mean"]
    return f"{float(value):.3f}"


def build_stage_coverage_table(tables: dict[str, pd.DataFrame]) -> pd.DataFrame:
    rows = [
        {
            "stage": "1 数据可信度确认",
            "implemented": "样本边界、样本量门槛、版本宏观/抓取/跳过检查、指标口径冻结",
            "outputs": "data_credibility_summary; version_*; sample_threshold_audit",
            "gap": "需要人工阅读版本差异后决定是否拆版本报告",
            "status": "基本完成",
        },
        {
            "stage": "2 宏观打法画像",
            "implemented": "路线强度、掉血压力、火堆、删牌、最终卡组大小分布与相关性",
            "outputs": "macro_distribution; macro_relationships",
            "gap": "暂无 PNG 图，只输出 CSV/Markdown 表",
            "status": "基本完成",
        },
        {
            "stage": "3 开局策略画像",
            "implemented": "前三奖励强信号、低优先、第 1/2/3 次差异",
            "outputs": "opening_*",
            "gap": "最终优先级仍需人工审阅上下文",
            "status": "基本完成",
        },
        {
            "stage": "4 卡牌采用画像",
            "implemented": "高抓高留、高抓低留、低抓高留、高出现低抓、高升级、高整跳暴露",
            "outputs": "card_adoption_classes; card_evidence",
            "gap": "部分低抓高留需进一步追来源",
            "status": "部分完成",
        },
        {
            "stage": "5 同屏对位画像",
            "implemented": "高频、碾压、争议、单卡胜负、分幕反转候选",
            "outputs": "pairwise_*",
            "gap": "高价值对位仍需逐局复盘",
            "status": "基本完成",
        },
        {
            "stage": "6 升级/删除/跳过画像",
            "implemented": "火堆升级、非火堆噪声、主动删牌、特殊事件、整跳节奏",
            "outputs": "upgrade_priority; non_campfire_upgrade_noise; remove_rhythm; special_event_region; skip_rhythm",
            "gap": "主动删牌按卡汇总，尚无删牌事件明细页",
            "status": "基本完成",
        },
        {
            "stage": "7 分层验证",
            "implemented": "Act、楼层段、版本、精英、第一幕压力、休息/删牌分层",
            "outputs": "strata_*; floor_segment_pick_check",
            "gap": "规则 pass/fail 仍是机械门槛，需人工确认",
            "status": "部分完成",
        },
        {
            "stage": "8 证据链整合",
            "implemented": "单卡证据页、开局/对位/升级/删除/特殊事件规则",
            "outputs": "card_evidence; rules_library; rule_validation",
            "gap": "未覆盖所有卡，只覆盖重点卡和规则候选",
            "status": "部分完成",
        },
        {
            "stage": "9 最终策略报告",
            "implemented": "summary、overview、规则库、案例库入口",
            "outputs": "summary.md; 00_strategy_overview.md; rule_validation; case_library",
            "gap": "仍需人工润色成最终文章",
            "status": "草稿完成",
        },
    ]
    return pd.DataFrame(rows)


def summary_card_bullets(
    frame: pd.DataFrame,
    offer_column: str,
    pick_column: str,
    rate_column: str,
    limit: int,
) -> list[str]:
    if frame.empty:
        return ["- 无。"]
    lines = []
    for row in frame.head(limit).to_dict("records"):
        lines.append(
            f"- {row.get('card_name_zh') or row['card_id']}：出现 {int(row[offer_column])}，抓 {int(row[pick_column])}，抓取率 {format_rate(row[rate_column])}。"
        )
    return lines


def summary_evidence_bullets(frame: pd.DataFrame, limit: int) -> list[str]:
    if frame.empty:
        return ["- 无。"]
    rows = frame[frame["evidence_grade"].isin(["S", "A", "B"])].head(limit)
    if rows.empty:
        rows = frame.head(limit)
    return [
        f"- {row.get('card_name_zh') or row['card_id']}：{row['evidence_grade']} 级证据；{row['conclusion']} 出现 {int(row['offer_count'])}，抓取率 {format_rate(row['pick_rate_when_offered'])}，最终 {int(row['final_run_count'])} 局。"
        for row in rows.to_dict("records")
    ]


def summary_pairwise_bullets(frame: pd.DataFrame, limit: int) -> list[str]:
    if frame.empty:
        return ["- 无。"]
    return [
        f"- {row.get('chosen_card_name_zh') or row['chosen_card']} > {row.get('passed_card_name_zh') or row['passed_card']}：{int(row['chosen_wins'])}:{int(row['passed_wins'])}，平滑选择率 {format_rate(row['chosen_smooth_pick_rate'])}。"
        for row in frame.head(limit).to_dict("records")
    ]


def summary_pairwise_reversal_bullets(frame: pd.DataFrame, limit: int) -> list[str]:
    if frame.empty:
        return ["- 暂无满足样本门槛的明显分幕反转。"]
    return [
        f"- {row.get('card_a_name_zh') or row['card_a']} / {row.get('card_b_name_zh') or row['card_b']}：{row['direction']}；Act1 A 选择率 {format_rate(row['act1_a_pick_rate'])}，后期 {format_rate(row['late_a_pick_rate'])}。"
        for row in frame.head(limit).to_dict("records")
    ]


def summary_upgrade_bullets(frame: pd.DataFrame, limit: int) -> list[str]:
    if frame.empty:
        return ["- 无。"]
    return [
        f"- {row.get('card_name_zh') or row['card_id']}：火堆升级 {int(row['upgrade_count'])} 次，第一幕 {int(row['act1_upgrade_count'])} 次，首升 {int(row['first_upgrade_count'])} 次。"
        for row in frame.head(limit).to_dict("records")
    ]


def summary_non_campfire_upgrade_bullets(frame: pd.DataFrame, limit: int) -> list[str]:
    if frame.empty:
        return ["- 无。"]
    return [
        f"- {row.get('card_name_zh') or row['card_id']}：非火堆升级 {int(row['upgrade_count'])} 次；{row['noise_reading']}。"
        for row in frame.head(limit).to_dict("records")
    ]


def summary_remove_bullets(frame: pd.DataFrame, limit: int) -> list[str]:
    if frame.empty:
        return ["- 无。"]
    return [
        f"- {row.get('card_name_zh') or row['card_id']}：删除 {int(row['remove_count'])} 次，平均楼层 {float(row['avg_remove_floor']):.2f}，{row['remove_timing']}。"
        for row in frame.head(limit).to_dict("records")
    ]


def summary_special_event_bullets(frame: pd.DataFrame) -> list[str]:
    if frame.empty:
        return ["- 无特殊事件记录。"]
    return [f"- {row['strategy_reading']}" for row in frame.to_dict("records")]


def summary_skip_bullets(frame: pd.DataFrame, limit: int) -> list[str]:
    if frame.empty:
        return ["- 无。"]
    return [
        f"- Act {int(row['act'])} {row['source_type']}：菜单 {int(row['menu_count'])}，整跳 {int(row['skipped_menu_count'])}，整跳率 {format_rate(row['skip_rate'])}。"
        for row in frame.head(limit).to_dict("records")
    ]


def summary_validated_rule_bullets(frame: pd.DataFrame, limit: int) -> list[str]:
    if frame.empty:
        return ["- 暂无通过规则。"]
    return [
        f"- [{row['rule_group']}] {row['rule']} 证据：{row['evidence']}"
        for row in frame.head(limit).to_dict("records")
    ]
