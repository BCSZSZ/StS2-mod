from __future__ import annotations

from pathlib import Path

import pandas as pd

from history_analysis.analysis import (
    TABLE_ORDER,
    build_summary_markdown,
    build_validation,
    build_tables,
    find_longest_regent_a10_win_streak,
    load_run_records,
)
from history_analysis.strategy import STRATEGY_TABLE_ORDER, build_strategy_markdowns, build_strategy_tables


REPO_ROOT = Path(__file__).resolve().parents[2]
HISTORY_ROOT = REPO_ROOT / "history-dashen"


def selected_records():
    records = load_run_records(HISTORY_ROOT)
    return records, find_longest_regent_a10_win_streak(records, 77)


def test_finds_expected_77_run_streak() -> None:
    records, selected = selected_records()

    assert len(records) == 260
    assert len(selected) == 77
    assert selected[0].run_id == "1781171276"
    assert selected[-1].run_id == "1782383458"
    assert all(record.character == "CHARACTER.REGENT" for record in selected)
    assert all(record.ascension == 10 for record in selected)
    assert all(record.win for record in selected)


def test_builds_all_required_tables_with_core_columns() -> None:
    _, selected = selected_records()
    tables = build_tables(selected)

    assert set(TABLE_ORDER) == set(tables)
    assert len(tables["runs_check"]) == 77
    assert {"run_id", "seed", "build_id", "final_deck_size"}.issubset(tables["runs_check"].columns)
    assert {"run_id", "global_node", "source_type", "card_id", "was_picked"}.issubset(
        tables["card_offers"].columns
    )
    assert {"card_id", "offer_count", "pick_count", "final_deck_run_count"}.issubset(
        tables["card_stats_overall"].columns
    )
    assert {"source_type", "act", "menu_count", "skipped_menu_count"}.issubset(
        tables["skip_menu_summary"].columns
    )
    assert {"card_id", "card_name_zh", "skipped_offer_count", "skip_exposure_rate"}.issubset(
        tables["card_skip_summary"].columns
    )
    assert {"original_card", "final_card", "transform_count"}.issubset(
        tables["card_transform_summary"].columns
    )
    assert {"card_id", "card_name_zh", "upgrade_count", "first_upgrade_count"}.issubset(
        tables["campfire_card_upgrade_summary"].columns
    )
    assert {"card_id", "card_name_zh", "upgrade_count", "event_upgrade_count"}.issubset(
        tables["non_campfire_card_upgrade_summary"].columns
    )
    assert {"special_card_id", "action", "global_node", "completed_quest"}.issubset(
        tables["special_event_detail"].columns
    )
    assert {"special_card_id", "encounter_count", "take_count", "completion_count"}.issubset(
        tables["special_event_summary"].columns
    )
    assert {"relic_id", "final_run_count", "final_copy_count"}.issubset(tables["relic_summary"].columns)
    assert "card_name_zh" in tables["card_stats_overall"].columns
    assert "relic_name_zh" in tables["relic_summary"].columns
    assert "winner_card_name_zh" in tables["card_pairwise_choices"].columns
    assert "monster_names_zh" in tables["node_history"].columns


def test_derived_table_invariants_hold() -> None:
    _, selected = selected_records()
    tables = build_tables(selected)

    offers = tables["card_offers"]
    stats = tables["card_stats_overall"].set_index("card_id")
    for card_id, group in offers.groupby("card_id"):
        assert int(stats.loc[card_id, "offer_count"]) == len(group)
        assert int(stats.loc[card_id, "pick_count"]) == int(group["was_picked"].sum())

    final_deck_cards = tables["final_deck_cards"]
    deck_sizes = tables["runs_check"].set_index("run_id")["final_deck_size"]
    generated_sizes = final_deck_cards.groupby("run_id").size()
    pd.testing.assert_series_equal(
        generated_sizes.sort_index(),
        deck_sizes.sort_index(),
        check_names=False,
    )

    early_detail = tables["early_3_offer_detail"]
    assert early_detail.groupby("run_id").size().max() <= 3

    pairwise = tables["card_pairwise_choices"]
    assert not pairwise["source_type"].isin({"shop_offer", "unknown_shop_offer"}).any()
    assert pairwise["source_type"].isin(
        {"combat_reward", "elite_reward", "boss_reward", "unknown_monster_reward"}
    ).all()
    assert pairwise["offer_size"].isin([3, 4]).all()
    assert not pairwise["winner_card"].eq("SKIP").any()
    assert not pairwise["loser_card"].eq("SKIP").any()

    pairwise_summary = tables["card_pairwise_summary"]
    assert not pairwise_summary["card_a"].eq("SKIP").any()
    assert not pairwise_summary["card_b"].eq("SKIP").any()

    skip_summary = tables["skip_menu_summary"]
    assert not skip_summary["source_type"].isin({"shop_offer", "unknown_shop_offer"}).any()
    assert int(skip_summary["skipped_menu_count"].sum()) > 0


def test_upgrade_tables_separate_campfire_from_non_campfire_sources() -> None:
    _, selected = selected_records()
    tables = build_tables(selected)

    all_upgrades = tables["card_upgrade_summary"].set_index("card_id")
    campfire_upgrades = tables["campfire_card_upgrade_summary"].set_index("card_id")
    non_campfire_upgrades = tables["non_campfire_card_upgrade_summary"].set_index("card_id")

    assert int(all_upgrades["upgrade_count"].sum()) == (
        int(campfire_upgrades["upgrade_count"].sum()) + int(non_campfire_upgrades["upgrade_count"].sum())
    )
    assert int(tables["run_summary"]["campfire_card_upgrade_count"].sum()) == int(
        campfire_upgrades["upgrade_count"].sum()
    )
    assert "CARD.STRIKE_REGENT" not in campfire_upgrades.index
    assert int(non_campfire_upgrades.loc["CARD.STRIKE_REGENT", "upgrade_count"]) == 18
    assert int(non_campfire_upgrades.loc["CARD.STRIKE_REGENT", "event_upgrade_count"]) > 0


def test_card_remove_summary_excludes_temporary_stolen_card_returns() -> None:
    _, selected = selected_records()
    tables = build_tables(selected)

    remove_summary = tables["card_remove_summary"].set_index("card_id")
    assert int(remove_summary.loc["CARD.BULWARK", "remove_count"]) == 1
    assert int(remove_summary.loc["CARD.BULWARK", "act3_remove_count"]) == 1
    assert "CARD.SPOILS_MAP" not in remove_summary.index
    assert "CARD.LANTERN_KEY" not in remove_summary.index
    assert int(remove_summary.loc["CARD.STRIKE_REGENT", "remove_count"]) == 178
    assert int(remove_summary.loc["CARD.DEFEND_REGENT", "remove_count"]) == 88

    run_remove_total = int(tables["run_summary"]["card_remove_count"].sum())
    card_remove_total = int(remove_summary["remove_count"].sum())
    assert run_remove_total == card_remove_total
    assert run_remove_total == 315


def test_special_event_quest_cards_are_reported_outside_remove_rhythm() -> None:
    _, selected = selected_records()
    tables = build_tables(selected)

    special = tables["special_event_summary"].set_index("special_card_id")
    assert int(special.loc["CARD.SPOILS_MAP", "encounter_count"]) == 5
    assert int(special.loc["CARD.SPOILS_MAP", "take_count"]) == 5
    assert int(special.loc["CARD.SPOILS_MAP", "completion_count"]) == 5
    assert int(special.loc["CARD.LANTERN_KEY", "encounter_count"]) == 7
    assert int(special.loc["CARD.LANTERN_KEY", "take_count"]) == 3
    assert int(special.loc["CARD.LANTERN_KEY", "return_count"]) == 4
    assert int(special.loc["CARD.LANTERN_KEY", "completion_count"]) == 3


def test_summary_is_chinese_and_documents_table_modules() -> None:
    records, selected = selected_records()
    tables = build_tables(selected)
    validation = build_validation(records, selected, tables, 77)

    summary = build_summary_markdown(tables, validation)

    assert "# Dashen 储君 77 连胜统计报告" in summary
    assert "## 表格模块说明" in summary
    assert "每个地图节点一行" in summary
    assert "卡牌选择菜单表" in summary
    assert "标准奖励同屏对位汇总" in summary
    assert "本报告由 `history-analysis` Python 代码" in summary
    assert "中文名" in summary
    assert "跳过相关统计" in summary
    assert "真实卡牌同屏对位相遇次数 Top 20（不含 SKIP）" in summary
    assert "火堆升级次数 Top 20" in summary
    assert "非火堆升级次数 Top 20" in summary


def test_localized_names_are_loaded_from_cached_json() -> None:
    _, selected = selected_records()
    tables = build_tables(selected)

    card_stats = tables["card_stats_overall"].set_index("card_id")
    assert card_stats.loc["CARD.CELESTIAL_MIGHT", "card_name_zh"] == "天穹之力"

    relic_summary = tables["relic_summary"].set_index("relic_id")
    assert relic_summary.loc["RELIC.DIVINE_RIGHT", "relic_name_zh"] == "天赋君权"

    card_skip_summary = tables["card_skip_summary"].set_index("card_id")
    assert card_skip_summary.loc["CARD.CELESTIAL_MIGHT", "card_name_zh"] == "天穹之力"
    assert card_skip_summary.loc["CARD.CELESTIAL_MIGHT", "skipped_offer_count"] > 0


def test_builds_strategy_analysis_layer_from_base_tables() -> None:
    _, selected = selected_records()
    base_tables = build_tables(selected)
    strategy_tables = build_strategy_tables(base_tables)

    assert set(STRATEGY_TABLE_ORDER) == set(strategy_tables)
    assert {"candidate_group", "card_id", "primary_metric"}.issubset(
        strategy_tables["candidate_lists"].columns
    )
    assert {"card_id", "evidence_grade", "conclusion"}.issubset(
        strategy_tables["card_evidence"].columns
    )
    assert {"rule_group", "rule", "evidence", "strength"}.issubset(
        strategy_tables["rules_library"].columns
    )
    assert {"special_card_id", "strategy_reading"}.issubset(
        strategy_tables["special_event_region"].columns
    )
    assert {"rule_group", "validation_status", "final_report_entry"}.issubset(
        strategy_tables["rule_validation"].columns
    )
    assert {"candidate_id", "stage", "subject_type", "claim", "evidence_refs", "review_status"}.issubset(
        strategy_tables["conclusion_candidates"].columns
    )
    assert {"candidate_id", "review_status", "final_report_entry", "review_reason"}.issubset(
        strategy_tables["reviewed_conclusions"].columns
    )
    assert {"section_id", "section_title", "included_conclusion_count"}.issubset(
        strategy_tables["final_report_sections"].columns
    )
    assert len(strategy_tables["opening_strong_signals"]) > 0
    assert len(strategy_tables["pairwise_dominant"]) > 0
    assert len(strategy_tables["card_evidence"]) >= 20
    assert len(strategy_tables["conclusion_candidates"]) > len(strategy_tables["rule_validation"])
    assert strategy_tables["reviewed_conclusions"]["final_report_entry"].sum() > 0
    assert strategy_tables["remove_rhythm"]["card_id"].isin(["CARD.SPOILS_MAP", "CARD.LANTERN_KEY"]).sum() == 0
    assert set(strategy_tables["special_event_region"]["special_card_id"]) == {
        "CARD.SPOILS_MAP",
        "CARD.LANTERN_KEY",
    }

    markdowns = build_strategy_markdowns(strategy_tables)
    assert "summary.md" in markdowns
    assert "08_conclusion_review.md" in markdowns
    assert "final_strategy_report.md" in markdowns
    assert "# 储君 77 连胜策略总结" in markdowns["summary.md"]
    assert "特殊事件区域" in markdowns["summary.md"]
    assert "藏宝图" in markdowns["summary.md"]
    assert "灯火钥匙" in markdowns["summary.md"]
    assert "结论审稿流水线" in markdowns["08_conclusion_review.md"]
    assert "skill 试运行版" in markdowns["final_strategy_report.md"]
