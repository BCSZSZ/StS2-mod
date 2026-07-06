from __future__ import annotations

import json
import os
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from functools import lru_cache
from pathlib import Path
from statistics import median
from typing import Any, Iterable

import pandas as pd


CHARACTER = "CHARACTER.REGENT"
ASCENSION = 10
SKIP = "SKIP"
PROJECT_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_LOCALIZATION_JSON = PROJECT_ROOT / "data" / "localized_names_en_zhs.json"

STARTING_CARDS = {
    "CARD.STRIKE_REGENT",
    "CARD.DEFEND_REGENT",
    "CARD.FALLING_STAR",
    "CARD.VENERATE",
    "CARD.ASCENDERS_BANE",
}

MAIN_PAIRWISE_SOURCES = {
    "combat_reward",
    "elite_reward",
    "boss_reward",
    "unknown_monster_reward",
}

SHOP_SOURCES = {"shop_offer", "unknown_shop_offer"}

SPECIAL_QUEST_CARDS = {
    "CARD.SPOILS_MAP": {
        "event_key_prefix": "THE_LEGENDS_WERE_TRUE",
        "event_label": "藏宝图",
        "take_key_fragment": "NAB_THE_MAP",
        "return_key_fragment": "",
    },
    "CARD.LANTERN_KEY": {
        "event_key_prefix": "THE_LANTERN_KEY",
        "event_label": "灯火钥匙",
        "take_key_fragment": "KEEP_THE_KEY",
        "return_key_fragment": "RETURN_THE_KEY",
    },
}

TABLE_ORDER = [
    "runs_check",
    "run_summary",
    "node_history",
    "card_offers",
    "card_stats_overall",
    "skip_menu_summary",
    "card_skip_summary",
    "early_3_offer_detail",
    "early_3_card_stats",
    "card_pairwise_choices",
    "card_pairwise_summary",
    "card_pairwise_all_non_shop_choices",
    "card_pairwise_all_non_shop_summary",
    "final_deck_cards",
    "final_deck_card_summary",
    "campfire_card_upgrade_summary",
    "non_campfire_card_upgrade_summary",
    "card_upgrade_summary",
    "card_remove_summary",
    "special_event_detail",
    "special_event_summary",
    "card_transform_summary",
    "relic_summary",
]

CSV_ORDER = {name: f"{index:02d}_{name}.csv" for index, name in enumerate(TABLE_ORDER, start=1)}

LOCALIZED_NAME_COLUMNS = {
    "character": "character_name_zh",
    "act_1": "act_1_name_zh",
    "act_2": "act_2_name_zh",
    "act_3": "act_3_name_zh",
    "monster_ids": "monster_names_zh",
    "card_id": "card_name_zh",
    "winner_card": "winner_card_name_zh",
    "loser_card": "loser_card_name_zh",
    "card_a": "card_a_name_zh",
    "card_b": "card_b_name_zh",
    "special_card_id": "special_card_name_zh",
    "original_card": "original_card_name_zh",
    "final_card": "final_card_name_zh",
    "reward_relic_id": "reward_relic_name_zh",
    "completion_reward_relic_ids": "completion_reward_relic_names_zh",
    "relic_id": "relic_name_zh",
    "enchantment_id": "enchantment_name_zh",
}

TITLE_LINE_RE = re.compile(
    r'"(?P<key>[A-Z0-9_]+)\.(?:title|name)"\s*:\s*"(?P<value>(?:\\.|[^"\\])*)"'
)

TABLE_DESCRIPTIONS = {
    "runs_check": {
        "label": "数据校验表",
        "grain": "每局一行",
        "content": "记录 run_id、种子、版本、角色、进阶、胜负、放弃状态、三幕路线、终局血量、最终卡组和遗物数量。",
        "purpose": "确认 77 场样本没有混入非储君、非 A10、失败局或放弃局，并提示版本是否需要分组。",
    },
    "run_summary": {
        "label": "路线与宏观节奏表",
        "grain": "每局一行",
        "content": "汇总普通怪、精英、Boss、商店、火堆、问号、掉血、火堆升级、非火堆升级、休息、有效删牌、金币收支；同节点移除后又返还的偷牌事件不计入删牌。",
        "purpose": "观察连胜样本的整体打法节奏，例如精英密度、休息频率、删牌频率和每幕压力。",
    },
    "node_history": {
        "label": "逐层节点明细表",
        "grain": "每个地图节点一行",
        "content": "记录节点序号、幕、地图点类型、实际房间类型、怪物 ID、节点后血量和金币、掉血回血、奖励/遗物/火堆信息。",
        "purpose": "作为定位问题和派生其他统计的底表，可追查某次奖励、掉血或路线选择发生在哪一层。",
    },
    "card_offers": {
        "label": "卡牌选择菜单表",
        "grain": "每张被展示的牌一行",
        "content": "展开每次 card_choices，记录来源分类、菜单 ID、展示数量、抓取数量、卡牌 ID、是否被选、是否整菜单跳过。",
        "purpose": "计算出现次数、抓取次数、见到后抓取率、跳过次数、按来源和幕数拆分的抓取偏好。",
    },
    "card_stats_overall": {
        "label": "卡牌总览统计表",
        "grain": "每张牌一行",
        "content": "聚合 offer_count、pick_count、pick_rate、分幕抓取、平均出现/抓取楼层、最终卡组出现次数；跳过相关正式统计见 skip 表。",
        "purpose": "回答连胜样本中哪些牌常见、常抓、见到后高抓，以及哪些牌最终留在卡组里。",
    },
    "skip_menu_summary": {
        "label": "整跳菜单汇总表",
        "grain": "每个来源和幕数一行",
        "content": "只统计非商店卡牌菜单，汇总菜单数、整跳菜单数、整跳率、涉及局数和被整跳展示牌数。",
        "purpose": "回答哪些来源、哪一幕更常出现整张菜单都不拿的情况；避免把 SKIP 混进卡牌对位。",
    },
    "card_skip_summary": {
        "label": "卡牌出现在整跳菜单统计表",
        "grain": "每张牌一行",
        "content": "只统计非商店卡牌菜单，记录该牌出现次数、出现在整跳菜单中的次数、占比和分幕整跳暴露次数。",
        "purpose": "回答哪些牌经常伴随整菜单跳过；这不是该牌输给另一张牌，也不是卡牌胜率。",
    },
    "early_3_offer_detail": {
        "label": "前 3 次非商店卡牌奖励明细",
        "grain": "每局最多三行",
        "content": "记录每局前 3 次非商店卡牌奖励菜单的楼层、来源、展示牌、抓取牌和是否跳过。",
        "purpose": "专门观察开局前三次奖励的实际选择上下文，而不是混入商店或后期事件。",
    },
    "early_3_card_stats": {
        "label": "前 3 次奖励卡牌统计",
        "grain": "每张牌一行",
        "content": "统计前 3 次奖励中的出现次数、抓取次数、抓取率，以及第 1/2/3 次奖励的分布。",
        "purpose": "回答开局最常见到、最常抓、见到后最容易抓的牌。",
    },
    "card_pairwise_choices": {
        "label": "标准奖励同屏对位明细",
        "grain": "每个有效偏好对一行",
        "content": "只统计非商店、3/4 选 1 标准奖励，将被选牌对未选牌展开为 winner/loser；整跳菜单不进入本表。",
        "purpose": "用最干净的奖励样本回答两张真实卡牌同屏出现时选谁。",
    },
    "card_pairwise_summary": {
        "label": "标准奖励同屏对位汇总",
        "grain": "每组卡牌对一行",
        "content": "汇总 A 胜 B、B 胜 A、总相遇次数、原始选择率、平滑选择率和分幕胜负。",
        "purpose": "做卡牌相对优先级分析，例如 A 与 B 同屏时更常选择哪一张。",
    },
    "card_pairwise_all_non_shop_choices": {
        "label": "全部非商店同屏对位明细",
        "grain": "每个有效偏好对一行",
        "content": "包含所有非商店 card_choices，包括事件和多选菜单；仍然排除商店，且整跳菜单不进入本表。",
        "purpose": "作为标准对位表的补充，保留更多样本但上下文更杂。",
    },
    "card_pairwise_all_non_shop_summary": {
        "label": "全部非商店同屏对位汇总",
        "grain": "每组卡牌对一行",
        "content": "对全部非商店对位明细做两两胜负和分幕汇总。",
        "purpose": "在需要更高样本量时参考，但结论要比标准奖励主表更谨慎。",
    },
    "final_deck_cards": {
        "label": "最终卡组明细表",
        "grain": "最终卡组每张牌一行",
        "content": "记录卡牌 ID、加入楼层、最终升级等级、附魔 ID/数值、是否初始牌。",
        "purpose": "追踪哪些牌最终留在卡组，以及它们通常何时加入、是否升级或附魔。",
    },
    "final_deck_card_summary": {
        "label": "最终卡组卡牌汇总",
        "grain": "每张牌一行",
        "content": "统计最终出现局数、总拷贝数、出现时平均拷贝数、平均/中位加入楼层、升级率、附魔数。",
        "purpose": "回答最终卡组真正沉淀了哪些牌，哪些牌常多抓、早拿或经常升级。",
    },
    "campfire_card_upgrade_summary": {
        "label": "火堆升级牌统计表",
        "grain": "每张被升级的牌一行",
        "content": "只统计休息点选择 SMITH 时升级的牌，记录火堆升级次数、平均楼层、分幕次数和每局首个火堆升级次数。",
        "purpose": "回答真正由玩家主动选择的升级优先级；这是判断敲牌价值最有指导意义的主表。",
    },
    "non_campfire_card_upgrade_summary": {
        "label": "非火堆升级牌统计表",
        "grain": "每张被升级的牌一行",
        "content": "统计非 SMITH 来源的升级，并按事件、精英、普通战斗、宝箱、商店等节点来源拆分次数。",
        "purpose": "解释打击、防御等起始牌为何会大量出现在升级记录里；这些多来自事件、遗物或奖励效果，不代表火堆优先级。",
    },
    "card_upgrade_summary": {
        "label": "全部升级牌审计表",
        "grain": "每张被升级的牌一行",
        "content": "保留所有 upgraded_cards 事件的全量汇总，含火堆和非火堆来源。",
        "purpose": "作为审计底表，方便核对最终升级总量；实际策略分析优先看火堆升级表。",
    },
    "card_remove_summary": {
        "label": "删牌统计表",
        "grain": "每张被删除的牌一行",
        "content": "统计有效删除次数、平均删除楼层和分幕删除次数；排除偷窃草蜢这类同节点移除后又返还的临时牌移动。",
        "purpose": "回答最常删什么、何时开始删牌，以及储君样本里攻击/防御/其他牌的删除倾向。",
    },
    "special_event_detail": {
        "label": "特殊事件明细表",
        "grain": "每次特殊事件动作一行",
        "content": "记录藏宝图和灯火钥匙的遭遇、拿取、归还、任务完成和奖励获得；任务完成回收不计入主动删牌。",
        "purpose": "把事件给牌/事件回收从删牌节奏中剥离，单独观察这些特殊区域的出现和选择。",
    },
    "special_event_summary": {
        "label": "特殊事件汇总表",
        "grain": "每张特殊任务牌一行",
        "content": "汇总特殊事件遭遇次数、拿取次数、归还次数、任务完成次数、平均获得楼层和平均完成楼层。",
        "purpose": "回答藏宝图和灯火钥匙事件各遇到几次、拿了几次、完成了几次，而不是把它们当作删牌对象。",
    },
    "card_transform_summary": {
        "label": "变牌统计表",
        "grain": "每组原牌到结果牌一行",
        "content": "统计 original_card、final_card、变牌次数和平均变牌楼层。",
        "purpose": "保留变牌事实，辅助解释最终卡组里非正常奖励来源的牌。",
    },
    "relic_summary": {
        "label": "遗物统计表",
        "grain": "每个遗物一行",
        "content": "统计最终出现局数、总拷贝数、平均和中位获得楼层。",
        "purpose": "提供连胜样本的遗物背景，方便之后解释路线、卡牌选择和卡组表现。",
    },
}


@dataclass(frozen=True)
class RunRecord:
    path: Path
    run_id: str
    data: dict[str, Any]
    start_time: int
    character: str
    ascension: int
    win: bool


@dataclass(frozen=True)
class ReportResult:
    tables: dict[str, pd.DataFrame]
    validation: dict[str, Any]
    workbook_path: Path
    summary_path: Path
    validation_path: Path


@dataclass(frozen=True)
class NodeContext:
    record: RunRecord
    node: dict[str, Any]
    act: int
    act_node: int
    global_node: int
    map_point_type: str
    room_type: str
    canonical_room_type: str
    monster_ids: str
    stats: list[dict[str, Any]]


def generate_reports(
    history_root: Path,
    output_dir: Path,
    expected_streak_length: int = 77,
) -> ReportResult:
    records = load_run_records(history_root)
    selected = find_longest_regent_a10_win_streak(records, expected_streak_length)
    tables = build_tables(selected)
    validation = build_validation(records, selected, tables, expected_streak_length)
    validate_tables(tables, validation)

    output_dir.mkdir(parents=True, exist_ok=True)
    for stale_csv in output_dir.glob("*.csv"):
        stale_csv.unlink()
    for table_name in TABLE_ORDER:
        tables[table_name].to_csv(output_dir / CSV_ORDER[table_name], index=False, encoding="utf-8-sig")

    workbook_path = output_dir / "dashen_regent_77_reports.xlsx"
    with pd.ExcelWriter(workbook_path, engine="openpyxl") as writer:
        for table_name in TABLE_ORDER:
            sheet_name = excel_sheet_name(table_name)
            tables[table_name].to_excel(writer, sheet_name=sheet_name, index=False)

    validation_path = output_dir / "validation.json"
    validation_path.write_text(
        json.dumps(validation, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    summary_path = output_dir / "summary.md"
    summary_path.write_text(build_summary_markdown(tables, validation), encoding="utf-8")

    return ReportResult(
        tables=tables,
        validation=validation,
        workbook_path=workbook_path,
        summary_path=summary_path,
        validation_path=validation_path,
    )


def load_run_records(history_root: Path) -> list[RunRecord]:
    if not history_root.exists():
        raise FileNotFoundError(f"History root does not exist: {history_root}")

    records: list[RunRecord] = []
    for path in sorted(history_root.glob("*.run")):
        data = json.loads(path.read_text(encoding="utf-8"))
        player = first_player(data)
        records.append(
            RunRecord(
                path=path,
                run_id=path.stem,
                data=data,
                start_time=int(data.get("start_time") or path.stem),
                character=str(player.get("character", "")),
                ascension=int(data.get("ascension") or 0),
                win=bool(data.get("win")),
            )
        )
    return sorted(records, key=lambda item: (item.start_time, item.run_id))


def find_longest_regent_a10_win_streak(records: list[RunRecord], expected_length: int) -> list[RunRecord]:
    best: list[RunRecord] = []
    current: list[RunRecord] = []
    for record in records:
        if record.character == CHARACTER and record.ascension == ASCENSION and record.win:
            current.append(record)
            continue
        if len(current) > len(best):
            best = current
        current = []
    if len(current) > len(best):
        best = current

    if len(best) != expected_length:
        raise ValueError(
            f"Expected longest {CHARACTER} A{ASCENSION} win streak to be "
            f"{expected_length}, found {len(best)}."
        )
    return best


def build_tables(records: list[RunRecord]) -> dict[str, pd.DataFrame]:
    runs_check = build_runs_check(records)
    node_history = build_node_history(records)
    run_summary = build_run_summary(records, node_history)
    card_offers = build_card_offers(records)
    final_deck_cards = build_final_deck_cards(records)
    card_stats_overall = build_card_stats_overall(card_offers, final_deck_cards)
    skip_menu_summary = build_skip_menu_summary(card_offers)
    card_skip_summary = build_card_skip_summary(card_offers)
    early_3_offer_detail = build_early_3_offer_detail(card_offers)
    early_3_card_stats = build_early_3_card_stats(card_offers, early_3_offer_detail)
    card_pairwise_choices = build_pairwise_choices(card_offers, standard_only=True)
    card_pairwise_summary = build_pairwise_summary(card_pairwise_choices)
    all_pairwise_choices = build_pairwise_choices(card_offers, standard_only=False)
    all_pairwise_summary = build_pairwise_summary(all_pairwise_choices)
    upgrade_events = collect_card_upgrade_events(records)
    campfire_upgrade_summary = build_card_upgrade_summary(
        [event for event in upgrade_events if event["upgrade_source_group"] == "campfire"]
    )
    non_campfire_upgrade_summary = build_non_campfire_card_upgrade_summary(
        [event for event in upgrade_events if event["upgrade_source_group"] != "campfire"]
    )
    upgrade_summary = build_card_upgrade_summary(upgrade_events)
    remove_summary = build_card_remove_summary(records)
    special_event_detail = build_special_event_detail(records)
    special_event_summary = build_special_event_summary(special_event_detail)
    transform_summary = build_card_transform_summary(records)
    relic_summary = build_relic_summary(records)

    tables = {
        "runs_check": runs_check,
        "run_summary": run_summary,
        "node_history": node_history,
        "card_offers": card_offers,
        "card_stats_overall": card_stats_overall,
        "skip_menu_summary": skip_menu_summary,
        "card_skip_summary": card_skip_summary,
        "early_3_offer_detail": early_3_offer_detail,
        "early_3_card_stats": early_3_card_stats,
        "card_pairwise_choices": card_pairwise_choices,
        "card_pairwise_summary": card_pairwise_summary,
        "card_pairwise_all_non_shop_choices": all_pairwise_choices,
        "card_pairwise_all_non_shop_summary": all_pairwise_summary,
        "final_deck_cards": final_deck_cards,
        "final_deck_card_summary": build_final_deck_card_summary(final_deck_cards),
        "campfire_card_upgrade_summary": campfire_upgrade_summary,
        "non_campfire_card_upgrade_summary": non_campfire_upgrade_summary,
        "card_upgrade_summary": upgrade_summary,
        "card_remove_summary": remove_summary,
        "special_event_detail": special_event_detail,
        "special_event_summary": special_event_summary,
        "card_transform_summary": transform_summary,
        "relic_summary": relic_summary,
    }
    return add_localized_name_columns(tables)


def add_localized_name_columns(tables: dict[str, pd.DataFrame]) -> dict[str, pd.DataFrame]:
    names = load_localized_names()
    localized: dict[str, pd.DataFrame] = {}
    for table_name, frame in tables.items():
        result = frame.copy()
        for source_column, name_column in LOCALIZED_NAME_COLUMNS.items():
            if source_column not in result.columns or name_column in result.columns:
                continue
            insert_at = result.columns.get_loc(source_column) + 1
            result.insert(
                insert_at,
                name_column,
                result[source_column].map(lambda value: localized_name_for(value, names)),
            )
        localized[table_name] = result
    return localized


@lru_cache(maxsize=1)
def load_localized_names() -> dict[str, str]:
    entries = load_localized_entries()
    names = {
        "SKIP": "跳过",
    }
    for key, entry in entries.items():
        zhs = entry.get("zhs", "")
        if zhs:
            names[key] = zhs
    return names


@lru_cache(maxsize=1)
def load_localized_entries() -> dict[str, dict[str, str]]:
    if DEFAULT_LOCALIZATION_JSON.exists():
        raw = json.loads(DEFAULT_LOCALIZATION_JSON.read_text(encoding="utf-8"))
        entries = raw.get("entries", {})
        if isinstance(entries, dict):
            return {
                str(key): {
                    "en": str(value.get("en", "")),
                    "zhs": str(value.get("zhs", "")),
                }
                for key, value in entries.items()
                if isinstance(value, dict)
            }

    pck_path = resolve_pck_path()
    if pck_path is None:
        return {}
    return extract_localized_entries_from_pck(pck_path)


def write_localization_json(output_path: Path = DEFAULT_LOCALIZATION_JSON, pck_path: Path | None = None) -> Path:
    source = pck_path or resolve_pck_path()
    if source is None:
        raise FileNotFoundError("Could not find SlayTheSpire2.pck. Set STS2_PCK_PATH or STS2_PATH.")

    entries = extract_localized_entries_from_pck(source)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "schema_version": 1,
        "source": source.name,
        "selection_rule": "For each localization key, en is the second observed title/name occurrence and zhs is the last observed occurrence in SlayTheSpire2.pck.",
        "entry_count": len(entries),
        "entries": dict(sorted(entries.items())),
    }
    output_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    load_localized_entries.cache_clear()
    load_localized_names.cache_clear()
    return output_path


def extract_localized_entries_from_pck(pck_path: Path) -> dict[str, dict[str, str]]:
    occurrences: dict[str, list[str]] = defaultdict(list)

    try:
        with pck_path.open("rb") as file:
            for raw_line in file:
                line = raw_line.decode("utf-8", errors="ignore")
                for match in TITLE_LINE_RE.finditer(line):
                    value = decode_json_string(match.group("value"))
                    if value:
                        occurrences[match.group("key")].append(value)
    except OSError:
        return {}

    entries: dict[str, dict[str, str]] = {
        "SKIP": {"en": "Skip", "zhs": "跳过"},
    }
    for key, values in occurrences.items():
        if not values:
            continue
        en = values[1] if len(values) > 1 else values[0]
        zhs = values[-1]
        entries[key] = {"en": en, "zhs": zhs}
    return entries


def resolve_pck_path() -> Path | None:
    candidates: list[Path] = []
    explicit = os.environ.get("STS2_PCK_PATH")
    if explicit:
        candidates.append(Path(explicit))

    install_root = os.environ.get("STS2_PATH")
    if install_root:
        candidates.append(Path(install_root) / "SlayTheSpire2.pck")

    profile_name = os.environ.get("STS2_MOD_PROFILE")
    if profile_name:
        profile_raw = os.environ.get(profile_name)
        if profile_raw:
            try:
                profile = json.loads(profile_raw)
                if profile.get("sts2Path"):
                    candidates.append(Path(profile["sts2Path"]) / "SlayTheSpire2.pck")
            except json.JSONDecodeError:
                pass

    candidates.append(Path(r"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck"))

    for candidate in candidates:
        if candidate.exists():
            return candidate
    return None


def decode_json_string(raw_value: str) -> str:
    try:
        return json.loads(f'"{raw_value}"')
    except json.JSONDecodeError:
        return raw_value


def localized_name_for(value: Any, names: dict[str, str]) -> str:
    if value is None:
        return ""
    text = str(value)
    if not text:
        return ""
    if ";" in text:
        return ";".join(localized_name_for(part, names) for part in text.split(";"))
    key = localization_key_from_id(text)
    return names.get(key, "")


def localization_key_from_id(value: str) -> str:
    if value == SKIP:
        return SKIP
    key = value
    if "." in key:
        key = key.split(".")[-1]
    if "+" in key:
        key = key.split("+", 1)[0]
    return key


def build_runs_check(records: list[RunRecord]) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for record in records:
        data = record.data
        player = first_player(data)
        final_stats = last_player_stats(data)
        acts = list(data.get("acts") or [])
        rows.append(
            {
                "run_id": record.run_id,
                "seed": data.get("seed", ""),
                "start_time": record.start_time,
                "start_time_utc": unix_to_utc(record.start_time),
                "build_id": data.get("build_id", ""),
                "character": record.character,
                "ascension": record.ascension,
                "win": record.win,
                "was_abandoned": bool(data.get("was_abandoned")),
                "run_time": int(data.get("run_time") or 0),
                "act_1": acts[0] if len(acts) > 0 else "",
                "act_2": acts[1] if len(acts) > 1 else "",
                "act_3": acts[2] if len(acts) > 2 else "",
                "final_hp": final_stats.get("current_hp", ""),
                "final_max_hp": final_stats.get("max_hp", ""),
                "final_deck_size": len(player.get("deck") or []),
                "final_relic_count": len(player.get("relics") or []),
            }
        )
    return pd.DataFrame(rows)


def build_node_history(records: list[RunRecord]) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for context in iter_node_contexts(records):
        stats = context.stats
        rows.append(
            {
                "run_id": context.record.run_id,
                "global_node": context.global_node,
                "act": context.act,
                "act_node": context.act_node,
                "map_point_type": context.map_point_type,
                "room_type": context.room_type,
                "canonical_room_type": context.canonical_room_type,
                "monster_ids": context.monster_ids,
                "current_hp": last_stat_value(stats, "current_hp"),
                "max_hp": last_stat_value(stats, "max_hp"),
                "damage_taken": sum_int(stats, "damage_taken"),
                "hp_healed": sum_int(stats, "hp_healed"),
                "current_gold": last_stat_value(stats, "current_gold"),
                "gold_gained": sum_int(stats, "gold_gained"),
                "gold_spent": sum_int(stats, "gold_spent"),
                "has_card_choices": has_any(stats, "card_choices"),
                "has_cards_gained": has_any(stats, "cards_gained"),
                "has_relic_choices": has_any(stats, "relic_choices"),
                "rest_choice": join_values(flatten_stat_values(stats, "rest_site_choices")),
            }
        )
    return pd.DataFrame(rows)


def build_run_summary(records: list[RunRecord], node_history: pd.DataFrame) -> pd.DataFrame:
    summary_rows: list[dict[str, Any]] = []
    node_groups = {run_id: group for run_id, group in node_history.groupby("run_id")}
    for record in records:
        nodes = node_groups.get(record.run_id, pd.DataFrame())
        row = {
            "run_id": record.run_id,
            "total_nodes": int(len(nodes)),
            "monster_count": count_nodes(nodes, "monster"),
            "elite_count": count_nodes(nodes, "elite"),
            "boss_count": count_nodes(nodes, "boss"),
            "shop_count": count_nodes(nodes, "shop"),
            "rest_count": count_nodes(nodes, "rest_site"),
            "treasure_count": count_nodes(nodes, "treasure"),
            "unknown_count": int((nodes["map_point_type"] == "unknown").sum()) if not nodes.empty else 0,
            "act1_damage": sum_by_act(nodes, 1, "damage_taken"),
            "act2_damage": sum_by_act(nodes, 2, "damage_taken"),
            "act3_damage": sum_by_act(nodes, 3, "damage_taken"),
            "total_damage": int(nodes["damage_taken"].sum()) if not nodes.empty else 0,
            "smith_count": count_rest_choice(nodes, "SMITH"),
            "heal_count": count_rest_choice(nodes, "HEAL"),
            "card_remove_count": 0,
            "card_upgrade_count": 0,
            "campfire_card_upgrade_count": 0,
            "non_campfire_card_upgrade_count": 0,
            "gold_gained_total": int(nodes["gold_gained"].sum()) if not nodes.empty else 0,
            "gold_spent_total": int(nodes["gold_spent"].sum()) if not nodes.empty else 0,
        }
        for context in iter_node_contexts([record]):
            for stats in context.stats:
                row["card_remove_count"] += count_effective_card_removals(stats)
                upgrade_count = len(as_list(stats.get("upgraded_cards")))
                row["card_upgrade_count"] += upgrade_count
                if is_campfire_smith_stats(stats):
                    row["campfire_card_upgrade_count"] += upgrade_count
                else:
                    row["non_campfire_card_upgrade_count"] += upgrade_count
        summary_rows.append(row)
    return pd.DataFrame(summary_rows)


def build_card_offers(records: list[RunRecord]) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for context in iter_node_contexts(records):
        menu_index = 0
        for stats in context.stats:
            choices = [choice for choice in as_list(stats.get("card_choices")) if card_id_from_choice(choice)]
            if not choices:
                continue
            menu_index += 1
            picked_count = sum(1 for choice in choices if bool(choice.get("was_picked")))
            offer_size = len(choices)
            source_type = classify_source_type(context, offer_size, picked_count)
            menu_id = f"{context.record.run_id}_{context.global_node:02d}_{menu_index}"
            was_skipped = picked_count == 0
            for choice in choices:
                rows.append(
                    {
                        "run_id": context.record.run_id,
                        "global_node": context.global_node,
                        "act": context.act,
                        "act_node": context.act_node,
                        "source_type": source_type,
                        "menu_id": menu_id,
                        "offer_size": offer_size,
                        "picked_count": picked_count,
                        "card_id": card_id_from_choice(choice),
                        "was_picked": bool(choice.get("was_picked")),
                        "was_skipped_menu": was_skipped,
                        "map_point_type": context.map_point_type,
                        "room_type": context.room_type,
                    }
                )
    return pd.DataFrame(rows)


def build_card_stats_overall(card_offers: pd.DataFrame, final_deck_cards: pd.DataFrame) -> pd.DataFrame:
    final_counts = final_deck_cards.groupby("card_id").agg(
        final_deck_run_count=("run_id", "nunique"),
        final_deck_copy_count=("card_id", "count"),
    )

    rows: list[dict[str, Any]] = []
    for card_id, group in card_offers.groupby("card_id"):
        picked = group[group["was_picked"]]
        row = {
            "card_id": card_id,
            "offer_count": int(len(group)),
            "pick_count": int(len(picked)),
            "pass_count": int(len(group) - len(picked)),
            "pick_rate_when_offered": safe_rate(len(picked), len(group)),
            "skipped_loss_count": int(group["was_skipped_menu"].sum()),
            "act1_offer_count": int((group["act"] == 1).sum()),
            "act1_pick_count": int(((group["act"] == 1) & group["was_picked"]).sum()),
            "act1_pick_rate": safe_rate(
                int(((group["act"] == 1) & group["was_picked"]).sum()),
                int((group["act"] == 1).sum()),
            ),
            "act2_offer_count": int((group["act"] == 2).sum()),
            "act2_pick_count": int(((group["act"] == 2) & group["was_picked"]).sum()),
            "act3_offer_count": int((group["act"] == 3).sum()),
            "act3_pick_count": int(((group["act"] == 3) & group["was_picked"]).sum()),
            "avg_offer_floor": safe_mean(group["global_node"]),
            "avg_pick_floor": safe_mean(picked["global_node"]) if not picked.empty else None,
            "first_pick_run_count": int(picked["run_id"].nunique()),
            "final_deck_run_count": 0,
            "final_deck_copy_count": 0,
        }
        if card_id in final_counts.index:
            row["final_deck_run_count"] = int(final_counts.loc[card_id, "final_deck_run_count"])
            row["final_deck_copy_count"] = int(final_counts.loc[card_id, "final_deck_copy_count"])
        rows.append(row)

    return pd.DataFrame(rows).sort_values(
        ["pick_count", "offer_count", "card_id"],
        ascending=[False, False, True],
        ignore_index=True,
    )


def build_skip_menu_summary(card_offers: pd.DataFrame) -> pd.DataFrame:
    if card_offers.empty:
        return pd.DataFrame(
            columns=[
                "source_type",
                "act",
                "menu_count",
                "skipped_menu_count",
                "skip_rate",
                "skipped_run_count",
                "skipped_offered_card_count",
                "avg_skipped_global_node",
            ]
        )

    menus = menu_dataframe(card_offers)
    menus = menus[~menus["source_type"].isin(SHOP_SOURCES)].copy()
    menus["skipped"] = menus["picked_count"] == 0
    rows: list[dict[str, Any]] = []
    for (source_type, act), group in menus.groupby(["source_type", "act"]):
        skipped = group[group["skipped"]]
        rows.append(
            {
                "source_type": source_type,
                "act": int(act),
                "menu_count": int(len(group)),
                "skipped_menu_count": int(len(skipped)),
                "skip_rate": safe_rate(len(skipped), len(group)),
                "skipped_run_count": int(skipped["run_id"].nunique()),
                "skipped_offered_card_count": int(skipped["offer_size"].sum()),
                "avg_skipped_global_node": safe_mean(skipped["global_node"]) if not skipped.empty else None,
            }
        )
    return pd.DataFrame(rows).sort_values(
        ["skipped_menu_count", "menu_count", "source_type", "act"],
        ascending=[False, False, True, True],
        ignore_index=True,
    )


def build_card_skip_summary(card_offers: pd.DataFrame) -> pd.DataFrame:
    if card_offers.empty:
        return pd.DataFrame(
            columns=[
                "card_id",
                "non_shop_offer_count",
                "skipped_offer_count",
                "skip_exposure_rate",
                "act1_skipped_offer_count",
                "act2_skipped_offer_count",
                "act3_skipped_offer_count",
                "skipped_run_count",
                "avg_skipped_floor",
            ]
        )

    non_shop = card_offers[~card_offers["source_type"].isin(SHOP_SOURCES)].copy()
    rows: list[dict[str, Any]] = []
    for card_id, group in non_shop.groupby("card_id"):
        skipped = group[group["was_skipped_menu"]]
        rows.append(
            {
                "card_id": card_id,
                "non_shop_offer_count": int(len(group)),
                "skipped_offer_count": int(len(skipped)),
                "skip_exposure_rate": safe_rate(len(skipped), len(group)),
                "act1_skipped_offer_count": int((skipped["act"] == 1).sum()),
                "act2_skipped_offer_count": int((skipped["act"] == 2).sum()),
                "act3_skipped_offer_count": int((skipped["act"] == 3).sum()),
                "skipped_run_count": int(skipped["run_id"].nunique()),
                "avg_skipped_floor": safe_mean(skipped["global_node"]) if not skipped.empty else None,
            }
        )
    return pd.DataFrame(rows).sort_values(
        ["skipped_offer_count", "non_shop_offer_count", "card_id"],
        ascending=[False, False, True],
        ignore_index=True,
    )


def build_early_3_offer_detail(card_offers: pd.DataFrame) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    if card_offers.empty:
        return pd.DataFrame(rows)
    menu_rows = menu_dataframe(card_offers)
    menu_rows = menu_rows[~menu_rows["source_type"].isin(SHOP_SOURCES)]
    menu_rows = menu_rows.sort_values(["run_id", "global_node", "menu_id"])
    for run_id, group in menu_rows.groupby("run_id"):
        for index, (_, menu) in enumerate(group.head(3).iterrows(), start=1):
            rows.append(
                {
                    "run_id": run_id,
                    "early_menu_index": index,
                    "global_node": int(menu["global_node"]),
                    "source_type": menu["source_type"],
                    "offer_size": int(menu["offer_size"]),
                    "picked_count": int(menu["picked_count"]),
                    "offered_cards": menu["offered_cards"],
                    "picked_cards": menu["picked_cards"],
                    "skipped": int(menu["picked_count"]) == 0,
                    "menu_id": menu["menu_id"],
                }
            )
    return pd.DataFrame(rows)


def build_early_3_card_stats(card_offers: pd.DataFrame, early_detail: pd.DataFrame) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    if early_detail.empty:
        return pd.DataFrame(rows)
    indexed_detail = early_detail.set_index("menu_id")
    early_offers = card_offers[card_offers["menu_id"].isin(indexed_detail.index)].copy()
    early_offers["early_menu_index"] = early_offers["menu_id"].map(indexed_detail["early_menu_index"])
    for card_id, group in early_offers.groupby("card_id"):
        row = {
            "card_id": card_id,
            "early_offer_count": int(len(group)),
            "early_pick_count": int(group["was_picked"].sum()),
            "early_pick_rate": safe_rate(int(group["was_picked"].sum()), len(group)),
        }
        for index in (1, 2, 3):
            index_group = group[group["early_menu_index"] == index]
            row[f"early_menu{index}_offer_count"] = int(len(index_group))
            row[f"early_menu{index}_pick_count"] = int(index_group["was_picked"].sum())
        rows.append(row)
    return pd.DataFrame(rows).sort_values(
        ["early_pick_count", "early_offer_count", "card_id"],
        ascending=[False, False, True],
        ignore_index=True,
    )


def build_pairwise_choices(card_offers: pd.DataFrame, standard_only: bool) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    if card_offers.empty:
        return pd.DataFrame(rows)
    menus = menu_dataframe(card_offers)
    if standard_only:
        menus = menus[
            menus["source_type"].isin(MAIN_PAIRWISE_SOURCES)
            & menus["offer_size"].isin([3, 4])
        ]
    else:
        menus = menus[~menus["source_type"].isin(SHOP_SOURCES)]

    details = card_offers[card_offers["menu_id"].isin(set(menus["menu_id"]))].copy()
    for menu_id, group in details.groupby("menu_id"):
        first = group.iloc[0]
        picked_cards = list(group[group["was_picked"]]["card_id"])
        unpicked_cards = list(group[~group["was_picked"]]["card_id"])
        if not picked_cards:
            continue
        else:
            winners = picked_cards
            losers = unpicked_cards
        for winner in winners:
            for loser in losers:
                if winner == loser:
                    continue
                rows.append(
                    {
                        "run_id": first["run_id"],
                        "menu_id": menu_id,
                        "act": int(first["act"]),
                        "global_node": int(first["global_node"]),
                        "source_type": first["source_type"],
                        "winner_card": winner,
                        "loser_card": loser,
                        "offer_size": int(first["offer_size"]),
                        "picked_count": int(first["picked_count"]),
                    }
                )
    return pd.DataFrame(rows)


def build_pairwise_summary(pairwise_choices: pd.DataFrame) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    if pairwise_choices.empty:
        return pd.DataFrame(rows)

    buckets: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for row in pairwise_choices.to_dict("records"):
        card_a, card_b = sorted([row["winner_card"], row["loser_card"]])
        buckets[(card_a, card_b)].append(row)

    for (card_a, card_b), items in buckets.items():
        a_beats_b = sum(1 for item in items if item["winner_card"] == card_a and item["loser_card"] == card_b)
        b_beats_a = sum(1 for item in items if item["winner_card"] == card_b and item["loser_card"] == card_a)
        total = a_beats_b + b_beats_a
        row = {
            "card_a": card_a,
            "card_b": card_b,
            "a_beats_b": a_beats_b,
            "b_beats_a": b_beats_a,
            "total_meetings": total,
            "a_pick_rate_vs_b": safe_rate(a_beats_b, total),
            "smooth_a_pick_rate_vs_b": (a_beats_b + 0.5) / (total + 1),
        }
        for act in (1, 2, 3):
            act_items = [item for item in items if int(item["act"]) == act]
            row[f"act{act}_a_beats_b"] = sum(
                1 for item in act_items if item["winner_card"] == card_a and item["loser_card"] == card_b
            )
            row[f"act{act}_b_beats_a"] = sum(
                1 for item in act_items if item["winner_card"] == card_b and item["loser_card"] == card_a
            )
        rows.append(row)

    return pd.DataFrame(rows).sort_values(
        ["total_meetings", "card_a", "card_b"],
        ascending=[False, True, True],
        ignore_index=True,
    )


def build_final_deck_cards(records: list[RunRecord]) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for record in records:
        for card in as_list(first_player(record.data).get("deck")):
            card_id = card_id_from(card)
            if not card_id:
                continue
            enchantment = card.get("enchantment") if isinstance(card, dict) else {}
            if not isinstance(enchantment, dict):
                enchantment = {}
            floor_added = int(card.get("floor_added_to_deck") or 0)
            upgrade_level = int(card.get("current_upgrade_level") or 0)
            rows.append(
                {
                    "run_id": record.run_id,
                    "card_id": card_id,
                    "floor_added_to_deck": floor_added,
                    "current_upgrade_level": upgrade_level,
                    "has_enchantment": bool(enchantment),
                    "enchantment_id": enchantment.get("id", ""),
                    "enchantment_amount": enchantment.get("amount", ""),
                    "is_starting_card": card_id in STARTING_CARDS and floor_added <= 1,
                }
            )
    return pd.DataFrame(rows)


def build_final_deck_card_summary(final_deck_cards: pd.DataFrame) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for card_id, group in final_deck_cards.groupby("card_id"):
        run_counts = group.groupby("run_id").size()
        floors = [int(value) for value in group["floor_added_to_deck"] if int(value) > 0]
        rows.append(
            {
                "card_id": card_id,
                "final_run_count": int(group["run_id"].nunique()),
                "final_copy_count": int(len(group)),
                "avg_copies_when_present": safe_mean(run_counts),
                "avg_floor_added": safe_mean(floors),
                "median_floor_added": median(floors) if floors else None,
                "upgraded_copy_count": int((group["current_upgrade_level"] > 0).sum()),
                "upgrade_rate_in_final": safe_rate(int((group["current_upgrade_level"] > 0).sum()), len(group)),
                "enchanted_copy_count": int(group["has_enchantment"].sum()),
            }
        )
    return pd.DataFrame(rows).sort_values(
        ["final_run_count", "final_copy_count", "card_id"],
        ascending=[False, False, True],
        ignore_index=True,
    )


def build_card_upgrade_summary(events: list[dict[str, Any]]) -> pd.DataFrame:
    first_counts: Counter[str] = Counter()
    for run_id, run_events in group_events_by_run(events).items():
        _ = run_id
        first_floor = min(event["global_node"] for event in run_events)
        first_cards = {event["card_id"] for event in run_events if event["global_node"] == first_floor}
        first_counts.update(first_cards)
    return summarize_single_card_events(
        events,
        card_column="card_id",
        count_column="upgrade_count",
        avg_floor_column="avg_upgrade_floor",
        act_count_prefix="upgrade",
        extra_counter_name="first_upgrade_count",
        extra_counter=first_counts,
    )


def build_non_campfire_card_upgrade_summary(events: list[dict[str, Any]]) -> pd.DataFrame:
    summary = build_card_upgrade_summary(events)
    if summary.empty:
        for column in non_campfire_upgrade_source_columns(events):
            summary[column] = []
        return summary

    frame = pd.DataFrame(events)
    for source in sorted(frame["upgrade_source_detail"].unique()):
        counts = Counter(frame.loc[frame["upgrade_source_detail"] == source, "card_id"])
        summary[f"{source}_upgrade_count"] = summary["card_id"].map(lambda card_id: int(counts.get(card_id, 0)))
    return summary


def non_campfire_upgrade_source_columns(events: list[dict[str, Any]]) -> list[str]:
    if not events:
        return []
    return [f"{source}_upgrade_count" for source in sorted({event["upgrade_source_detail"] for event in events})]


def collect_card_upgrade_events(records: list[RunRecord]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for context in iter_node_contexts(records):
        for stats in context.stats:
            source_group, source_detail = classify_upgrade_source(context, stats)
            for card in as_list(stats.get("upgraded_cards")):
                card_id = card_id_from(card)
                if not card_id:
                    continue
                rows.append(
                    {
                        "run_id": context.record.run_id,
                        "card_id": card_id,
                        "global_node": context.global_node,
                        "act": context.act,
                        "upgrade_source_group": source_group,
                        "upgrade_source_detail": source_detail,
                    }
                )
    return rows


def classify_upgrade_source(context: NodeContext, stats: dict[str, Any]) -> tuple[str, str]:
    if is_campfire_smith_stats(stats):
        return ("campfire", "campfire")
    source = context.canonical_room_type or context.map_point_type or "unknown"
    return ("non_campfire", source)


def is_campfire_smith_stats(stats: dict[str, Any]) -> bool:
    return "SMITH" in {str(value) for value in as_list(stats.get("rest_site_choices"))}


def build_card_remove_summary(records: list[RunRecord]) -> pd.DataFrame:
    events = collect_card_remove_events(records)
    return summarize_single_card_events(
        events,
        card_column="card_id",
        count_column="remove_count",
        avg_floor_column="avg_remove_floor",
        act_count_prefix="remove",
    )


def collect_card_remove_events(records: list[RunRecord]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for context in iter_node_contexts(records):
        for stats in context.stats:
            for card in effective_removed_cards(stats):
                card_id = card_id_from(card)
                if not card_id:
                    continue
                rows.append(
                    {
                        "run_id": context.record.run_id,
                        "card_id": card_id,
                        "global_node": context.global_node,
                        "act": context.act,
                    }
                )
    return rows


def build_special_event_detail(records: list[RunRecord]) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for context in iter_node_contexts(records):
        for stats in context.stats:
            choice_keys = event_choice_keys(stats)
            completed_quests = {str(value) for value in as_list(stats.get("completed_quests"))}
            gained_cards = {card_id_from(card) for card in as_list(stats.get("cards_gained"))}
            removed_cards = {card_id_from(card) for card in as_list(stats.get("cards_removed"))}
            reward_relic_ids = picked_relic_ids(stats)
            for card_id, info in SPECIAL_QUEST_CARDS.items():
                relevant_keys = [
                    key
                    for key in choice_keys
                    if key.startswith(str(info["event_key_prefix"]))
                    or key.startswith("WAR_HISTORIAN_REPY")
                ]
                action = special_event_action(card_id, info, relevant_keys, gained_cards, removed_cards, completed_quests)
                if not action:
                    continue
                rows.append(
                    {
                        "run_id": context.record.run_id,
                        "special_card_id": card_id,
                        "event_label": info["event_label"],
                        "action": action,
                        "global_node": context.global_node,
                        "act": context.act,
                        "map_point_type": context.map_point_type,
                        "room_type": context.room_type,
                        "monster_ids": context.monster_ids,
                        "event_choice_keys": ";".join(relevant_keys),
                        "gained_card": card_id if card_id in gained_cards else "",
                        "completed_quest": card_id if card_id in completed_quests else "",
                        "removed_card": card_id if card_id in removed_cards else "",
                        "reward_relic_id": ";".join(reward_relic_ids),
                    }
                )
    return pd.DataFrame(
        rows,
        columns=[
            "run_id",
            "special_card_id",
            "event_label",
            "action",
            "global_node",
            "act",
            "map_point_type",
            "room_type",
            "monster_ids",
            "event_choice_keys",
            "gained_card",
            "completed_quest",
            "removed_card",
            "reward_relic_id",
        ],
    )


def build_special_event_summary(detail: pd.DataFrame) -> pd.DataFrame:
    columns = [
        "special_card_id",
        "event_label",
        "encounter_count",
        "take_count",
        "return_count",
        "completion_count",
        "encounter_run_count",
        "take_run_count",
        "completion_run_count",
        "avg_take_floor",
        "avg_completion_floor",
        "completion_reward_relic_ids",
    ]
    if detail.empty:
        return pd.DataFrame(columns=columns)

    rows: list[dict[str, Any]] = []
    for card_id, group in detail.groupby("special_card_id"):
        encounters = group[group["action"].isin(["take", "return"])]
        takes = group[group["action"] == "take"]
        returns = group[group["action"] == "return"]
        completions = group[group["action"] == "complete"]
        reward_relic_ids = sorted(
            {
                relic
                for value in completions["reward_relic_id"]
                for relic in str(value).split(";")
                if relic
            }
        )
        rows.append(
            {
                "special_card_id": card_id,
                "event_label": first_non_empty(group["event_label"]),
                "encounter_count": int(len(encounters)),
                "take_count": int(len(takes)),
                "return_count": int(len(returns)),
                "completion_count": int(len(completions)),
                "encounter_run_count": int(encounters["run_id"].nunique()),
                "take_run_count": int(takes["run_id"].nunique()),
                "completion_run_count": int(completions["run_id"].nunique()),
                "avg_take_floor": safe_mean(takes["global_node"]) if not takes.empty else None,
                "avg_completion_floor": safe_mean(completions["global_node"]) if not completions.empty else None,
                "completion_reward_relic_ids": ";".join(reward_relic_ids),
            }
        )
    return pd.DataFrame(rows, columns=columns).sort_values(
        ["encounter_count", "special_card_id"],
        ascending=[False, True],
        ignore_index=True,
    )


def event_choice_keys(stats: dict[str, Any]) -> list[str]:
    keys = []
    for choice in as_list(stats.get("event_choices")):
        if not isinstance(choice, dict):
            continue
        title = choice.get("title")
        if isinstance(title, dict) and title.get("key"):
            keys.append(str(title["key"]))
    return keys


def special_event_action(
    card_id: str,
    info: dict[str, str],
    relevant_keys: list[str],
    gained_cards: set[str],
    removed_cards: set[str],
    completed_quests: set[str],
) -> str:
    if card_id in completed_quests and card_id in removed_cards:
        return "complete"
    if card_id in gained_cards:
        return "take"
    return_fragment = info.get("return_key_fragment", "")
    if return_fragment and any(return_fragment in key for key in relevant_keys):
        return "return"
    take_fragment = info.get("take_key_fragment", "")
    if take_fragment and any(take_fragment in key for key in relevant_keys):
        return "take"
    return ""


def picked_relic_ids(stats: dict[str, Any]) -> list[str]:
    relic_ids = []
    for choice in as_list(stats.get("relic_choices")):
        if not isinstance(choice, dict) or not choice.get("was_picked"):
            continue
        relic_id = choice.get("choice")
        if relic_id:
            relic_ids.append(str(relic_id))
    return relic_ids


def build_card_transform_summary(records: list[RunRecord]) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for context in iter_node_contexts(records):
        for stats in context.stats:
            for transform in as_list(stats.get("cards_transformed")):
                if not isinstance(transform, dict):
                    continue
                original = card_id_from(transform.get("original_card"))
                final = card_id_from(transform.get("final_card"))
                if not original and not final:
                    continue
                rows.append(
                    {
                        "original_card": original or "",
                        "final_card": final or "",
                        "run_id": context.record.run_id,
                        "global_node": context.global_node,
                        "act": context.act,
                    }
                )
    if not rows:
        return pd.DataFrame(columns=["original_card", "final_card", "transform_count", "avg_transform_floor"])
    events = pd.DataFrame(rows)
    summary_rows = []
    for (original, final), group in events.groupby(["original_card", "final_card"]):
        summary_rows.append(
            {
                "original_card": original,
                "final_card": final,
                "transform_count": int(len(group)),
                "avg_transform_floor": safe_mean(group["global_node"]),
            }
        )
    return pd.DataFrame(summary_rows).sort_values(
        ["transform_count", "original_card", "final_card"],
        ascending=[False, True, True],
        ignore_index=True,
    )


def build_relic_summary(records: list[RunRecord]) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for record in records:
        for relic in as_list(first_player(record.data).get("relics")):
            if not isinstance(relic, dict):
                continue
            relic_id = relic.get("id")
            if not relic_id:
                continue
            rows.append(
                {
                    "run_id": record.run_id,
                    "relic_id": relic_id,
                    "floor_added_to_deck": int(relic.get("floor_added_to_deck") or 0),
                }
            )
    if not rows:
        return pd.DataFrame(
            columns=[
                "relic_id",
                "final_run_count",
                "final_copy_count",
                "avg_floor_added",
                "median_floor_added",
            ]
        )
    relics = pd.DataFrame(rows)
    summary_rows = []
    for relic_id, group in relics.groupby("relic_id"):
        floors = [int(value) for value in group["floor_added_to_deck"] if int(value) > 0]
        summary_rows.append(
            {
                "relic_id": relic_id,
                "final_run_count": int(group["run_id"].nunique()),
                "final_copy_count": int(len(group)),
                "avg_floor_added": safe_mean(floors),
                "median_floor_added": median(floors) if floors else None,
            }
        )
    return pd.DataFrame(summary_rows).sort_values(
        ["final_run_count", "final_copy_count", "relic_id"],
        ascending=[False, False, True],
        ignore_index=True,
    )


def iter_node_contexts(records: Iterable[RunRecord]) -> Iterable[NodeContext]:
    for record in records:
        global_node = 0
        for act_index, act_nodes in enumerate(as_list(record.data.get("map_point_history")), start=1):
            for act_node, node in enumerate(as_list(act_nodes), start=1):
                if not isinstance(node, dict):
                    continue
                global_node += 1
                room_types = sorted(
                    {
                        str(room.get("room_type"))
                        for room in as_list(node.get("rooms"))
                        if isinstance(room, dict) and room.get("room_type")
                    }
                )
                monster_ids = []
                for room in as_list(node.get("rooms")):
                    if not isinstance(room, dict):
                        continue
                    monster_ids.extend(str(item) for item in as_list(room.get("monster_ids")))
                stats = [item for item in as_list(node.get("player_stats")) if isinstance(item, dict)]
                yield NodeContext(
                    record=record,
                    node=node,
                    act=act_index,
                    act_node=act_node,
                    global_node=global_node,
                    map_point_type=str(node.get("map_point_type", "")),
                    room_type=join_values(room_types),
                    canonical_room_type=canonical_room_type(node, room_types),
                    monster_ids=join_values(monster_ids),
                    stats=stats,
                )


def collect_card_events(records: list[RunRecord], stat_key: str, card_column: str) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for context in iter_node_contexts(records):
        for stats in context.stats:
            for card in as_list(stats.get(stat_key)):
                card_id = card_id_from(card)
                if not card_id:
                    continue
                rows.append(
                    {
                        "run_id": context.record.run_id,
                        card_column: card_id,
                        "global_node": context.global_node,
                        "act": context.act,
                    }
                )
    return rows


def count_effective_card_removals(stats: dict[str, Any]) -> int:
    return len(effective_removed_cards(stats))


def effective_removed_cards(stats: dict[str, Any]) -> list[Any]:
    gained_identities = Counter(card_identity(card) for card in as_list(stats.get("cards_gained")))
    completed_quests = {str(value) for value in as_list(stats.get("completed_quests"))}
    effective: list[Any] = []
    for card in as_list(stats.get("cards_removed")):
        card_id = card_id_from(card)
        if card_id in SPECIAL_QUEST_CARDS and card_id in completed_quests:
            continue
        identity = card_identity(card)
        if gained_identities[identity] > 0:
            gained_identities[identity] -= 1
            continue
        effective.append(card)
    return effective


def card_identity(card: Any) -> tuple[str, int | None, int]:
    if isinstance(card, str):
        return (card, None, 0)
    if isinstance(card, dict):
        return (
            card_id_from(card),
            int(card["floor_added_to_deck"]) if card.get("floor_added_to_deck") is not None else None,
            int(card.get("current_upgrade_level") or card.get("upgrade") or 0),
        )
    return ("", None, 0)


def summarize_single_card_events(
    events: list[dict[str, Any]],
    card_column: str,
    count_column: str,
    avg_floor_column: str,
    act_count_prefix: str,
    extra_counter_name: str | None = None,
    extra_counter: Counter[str] | None = None,
) -> pd.DataFrame:
    columns = [
        card_column,
        count_column,
        avg_floor_column,
        f"act1_{act_count_prefix}_count",
        f"act2_{act_count_prefix}_count",
        f"act3_{act_count_prefix}_count",
    ]
    if extra_counter_name:
        columns.append(extra_counter_name)
    if not events:
        return pd.DataFrame(columns=columns)

    frame = pd.DataFrame(events)
    rows: list[dict[str, Any]] = []
    for card_id, group in frame.groupby(card_column):
        row = {
            card_column: card_id,
            count_column: int(len(group)),
            avg_floor_column: safe_mean(group["global_node"]),
            f"act1_{act_count_prefix}_count": int((group["act"] == 1).sum()),
            f"act2_{act_count_prefix}_count": int((group["act"] == 2).sum()),
            f"act3_{act_count_prefix}_count": int((group["act"] == 3).sum()),
        }
        if extra_counter_name:
            row[extra_counter_name] = int((extra_counter or Counter()).get(card_id, 0))
        rows.append(row)
    return pd.DataFrame(rows).sort_values([count_column, card_column], ascending=[False, True], ignore_index=True)


def menu_dataframe(card_offers: pd.DataFrame) -> pd.DataFrame:
    rows: list[dict[str, Any]] = []
    for menu_id, group in card_offers.groupby("menu_id", sort=False):
        first = group.iloc[0]
        offered = list(group["card_id"])
        picked = list(group[group["was_picked"]]["card_id"])
        rows.append(
            {
                "menu_id": menu_id,
                "run_id": first["run_id"],
                "global_node": int(first["global_node"]),
                "act": int(first["act"]),
                "act_node": int(first["act_node"]),
                "source_type": first["source_type"],
                "offer_size": int(first["offer_size"]),
                "picked_count": int(first["picked_count"]),
                "offered_cards": ";".join(offered),
                "picked_cards": ";".join(picked),
            }
        )
    return pd.DataFrame(rows)


def classify_source_type(context: NodeContext, offer_size: int, picked_count: int) -> str:
    room_types = set(context.room_type.split(";")) if context.room_type else set()
    if "shop" in room_types:
        base = "unknown_shop_offer" if context.map_point_type == "unknown" else "shop_offer"
    elif "monster" in room_types:
        base = "unknown_monster_reward" if context.map_point_type == "unknown" else "combat_reward"
    elif "elite" in room_types:
        base = "elite_reward"
    elif "boss" in room_types:
        base = "boss_reward"
    elif "event" in room_types or context.map_point_type in {"unknown", "ancient"}:
        base = "event_reward"
    else:
        base = "special_multi_choice"

    if base not in SHOP_SOURCES and (offer_size not in {3, 4} or picked_count > 1):
        return "special_multi_choice"
    return base


def canonical_room_type(node: dict[str, Any], room_types: list[str]) -> str:
    priorities = ["boss", "elite", "monster", "shop", "rest_site", "treasure", "event"]
    for value in priorities:
        if value in room_types:
            return value
    return str(node.get("map_point_type", ""))


def first_player(data: dict[str, Any]) -> dict[str, Any]:
    players = as_list(data.get("players"))
    return players[0] if players and isinstance(players[0], dict) else {}


def last_player_stats(data: dict[str, Any]) -> dict[str, Any]:
    last: dict[str, Any] = {}
    for act in as_list(data.get("map_point_history")):
        for node in as_list(act):
            if not isinstance(node, dict):
                continue
            for stats in as_list(node.get("player_stats")):
                if isinstance(stats, dict):
                    last = stats
    return last


def as_list(value: Any) -> list[Any]:
    if value is None:
        return []
    if isinstance(value, list):
        return value
    return [value]


def card_id_from(value: Any) -> str:
    if isinstance(value, str):
        return value
    if isinstance(value, dict):
        if value.get("id"):
            return str(value["id"])
        if isinstance(value.get("card"), dict) and value["card"].get("id"):
            return str(value["card"]["id"])
    return ""


def card_id_from_choice(choice: Any) -> str:
    if not isinstance(choice, dict):
        return ""
    return card_id_from(choice.get("card")) or card_id_from(choice)


def sum_int(stats: list[dict[str, Any]], key: str) -> int:
    return sum(int(item.get(key) or 0) for item in stats)


def last_stat_value(stats: list[dict[str, Any]], key: str) -> Any:
    for item in reversed(stats):
        if key in item:
            return item.get(key)
    return ""


def has_any(stats: list[dict[str, Any]], key: str) -> bool:
    return any(bool(as_list(item.get(key))) for item in stats)


def flatten_stat_values(stats: list[dict[str, Any]], key: str) -> list[Any]:
    values: list[Any] = []
    for item in stats:
        values.extend(as_list(item.get(key)))
    return values


def join_values(values: Iterable[Any]) -> str:
    return ";".join(str(value) for value in values if value not in (None, ""))


def count_nodes(nodes: pd.DataFrame, canonical_room: str) -> int:
    if nodes.empty:
        return 0
    return int((nodes["canonical_room_type"] == canonical_room).sum())


def sum_by_act(nodes: pd.DataFrame, act: int, column: str) -> int:
    if nodes.empty:
        return 0
    return int(nodes.loc[nodes["act"] == act, column].sum())


def count_rest_choice(nodes: pd.DataFrame, choice: str) -> int:
    if nodes.empty:
        return 0
    return int(nodes["rest_choice"].fillna("").str.split(";").apply(lambda values: choice in values).sum())


def group_events_by_run(events: list[dict[str, Any]]) -> dict[str, list[dict[str, Any]]]:
    grouped: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for event in events:
        grouped[event["run_id"]].append(event)
    return grouped


def safe_rate(numerator: int | float, denominator: int | float) -> float | None:
    return float(numerator) / float(denominator) if denominator else None


def safe_mean(values: Iterable[Any]) -> float | None:
    materialized = [float(value) for value in values if value not in (None, "")]
    if not materialized:
        return None
    return sum(materialized) / len(materialized)


def unix_to_utc(timestamp: int) -> str:
    return datetime.fromtimestamp(timestamp, tz=timezone.utc).strftime("%Y-%m-%d %H:%M:%S")


def excel_sheet_name(table_name: str) -> str:
    replacements = {
        "card_pairwise_all_non_shop_choices": "pairwise_all_choices",
        "card_pairwise_all_non_shop_summary": "pairwise_all_summary",
    }
    return replacements.get(table_name, table_name)[:31]


def build_validation(
    all_records: list[RunRecord],
    selected: list[RunRecord],
    tables: dict[str, pd.DataFrame],
    expected_streak_length: int,
) -> dict[str, Any]:
    builds = sorted({record.data.get("build_id", "") for record in selected})
    runs_check = tables["runs_check"]
    return {
        "all_run_file_count": len(all_records),
        "selected_run_count": len(selected),
        "expected_streak_length": expected_streak_length,
        "streak_start_run_id": selected[0].run_id,
        "streak_end_run_id": selected[-1].run_id,
        "streak_start_time": selected[0].start_time,
        "streak_end_time": selected[-1].start_time,
        "streak_start_time_utc": unix_to_utc(selected[0].start_time),
        "streak_end_time_utc": unix_to_utc(selected[-1].start_time),
        "all_selected_regent": bool((runs_check["character"] == CHARACTER).all()),
        "all_selected_a10": bool((runs_check["ascension"] == ASCENSION).all()),
        "all_selected_wins": bool(runs_check["win"].all()),
        "any_abandoned": bool(runs_check["was_abandoned"].any()),
        "build_ids": builds,
        "build_id_count": len(builds),
        "table_row_counts": {name: int(len(frame)) for name, frame in tables.items()},
    }


def validate_tables(tables: dict[str, pd.DataFrame], validation: dict[str, Any]) -> None:
    required = set(TABLE_ORDER)
    missing = required.difference(tables)
    if missing:
        raise ValueError(f"Missing tables: {sorted(missing)}")
    if validation["selected_run_count"] != validation["expected_streak_length"]:
        raise ValueError("Selected run count does not match expected streak length.")
    if not validation["all_selected_regent"]:
        raise ValueError("Selected runs include non-Regent records.")
    if not validation["all_selected_a10"]:
        raise ValueError("Selected runs include non-A10 records.")
    if not validation["all_selected_wins"]:
        raise ValueError("Selected runs include non-win records.")
    if validation["any_abandoned"]:
        raise ValueError("Selected runs include abandoned records.")

    assert_columns(
        tables["runs_check"],
        [
            "run_id",
            "seed",
            "start_time",
            "build_id",
            "character",
            "ascension",
            "win",
            "was_abandoned",
            "run_time",
            "act_1",
            "act_2",
            "act_3",
            "final_hp",
            "final_max_hp",
            "final_deck_size",
            "final_relic_count",
        ],
    )
    assert_columns(
        tables["card_pairwise_summary"],
        [
            "card_a",
            "card_b",
            "a_beats_b",
            "b_beats_a",
            "total_meetings",
            "a_pick_rate_vs_b",
            "smooth_a_pick_rate_vs_b",
        ],
    )
    assert_columns(
        tables["skip_menu_summary"],
        ["source_type", "act", "menu_count", "skipped_menu_count", "skip_rate"],
    )
    assert_columns(
        tables["card_skip_summary"],
        ["card_id", "non_shop_offer_count", "skipped_offer_count", "skip_exposure_rate"],
    )
    assert_columns(
        tables["campfire_card_upgrade_summary"],
        ["card_id", "upgrade_count", "avg_upgrade_floor", "first_upgrade_count"],
    )
    assert_columns(
        tables["non_campfire_card_upgrade_summary"],
        ["card_id", "upgrade_count", "avg_upgrade_floor", "first_upgrade_count"],
    )
    assert_columns(
        tables["special_event_detail"],
        [
            "run_id",
            "special_card_id",
            "event_label",
            "action",
            "global_node",
            "act",
            "completed_quest",
            "reward_relic_id",
        ],
    )
    assert_columns(
        tables["special_event_summary"],
        [
            "special_card_id",
            "event_label",
            "encounter_count",
            "take_count",
            "return_count",
            "completion_count",
        ],
    )


def assert_columns(frame: pd.DataFrame, columns: list[str]) -> None:
    missing = [column for column in columns if column not in frame.columns]
    if missing:
        raise ValueError(f"Missing columns {missing}")


def build_summary_markdown(tables: dict[str, pd.DataFrame], validation: dict[str, Any]) -> str:
    run_summary = tables["run_summary"]
    card_stats = tables["card_stats_overall"]
    skip_menu_summary = tables["skip_menu_summary"]
    card_skip_summary = tables["card_skip_summary"]
    final_summary = tables["final_deck_card_summary"]
    pairwise = tables["card_pairwise_summary"]
    offers = tables["card_offers"]
    early_stats = tables["early_3_card_stats"]
    campfire_upgrade_summary = tables["campfire_card_upgrade_summary"]
    non_campfire_upgrade_summary = tables["non_campfire_card_upgrade_summary"]
    remove_summary = tables["card_remove_summary"]
    special_event_summary = tables["special_event_summary"]
    relic_summary = tables["relic_summary"]
    boss_stats = summarize_source_pick_rates(offers, "boss_reward", min_offer_count=3)

    lines = [
        "# Dashen 储君 77 连胜统计报告",
        "",
        "本报告由 `history-analysis` Python 代码从 `.run` 原始历史自动生成；CSV、Excel 和本摘要使用同一批派生表，不包含手工拼表。",
        "",
        "## 数据校验",
        "",
        f"- 输入 `.run` 正本数量：{validation['all_run_file_count']}",
        f"- 选中的连胜样本数量：{validation['selected_run_count']}",
        f"- 连胜范围：`{validation['streak_start_run_id']}` 到 `{validation['streak_end_run_id']}`",
        f"- UTC 时间范围：{validation['streak_start_time_utc']} 到 {validation['streak_end_time_utc']}",
        f"- 游戏版本：{', '.join(validation['build_ids'])}",
        f"- 全部为储君 A10 胜利：{validation['all_selected_regent'] and validation['all_selected_a10'] and validation['all_selected_wins']}",
        f"- 是否包含放弃局：{validation['any_abandoned']}",
        "",
        "## 表格模块说明",
        "",
        build_table_description_markdown(validation),
        "",
        "## 宏观打法均值",
        "",
        "这部分来自 `run_summary`，用于快速看 77 连胜的路线强度、血量压力和资源节奏。",
        "",
        small_table(
            pd.DataFrame(
                [
                    {
                        "平均精英数": run_summary["elite_count"].mean(),
                        "平均商店数": run_summary["shop_count"].mean(),
                        "平均火堆数": run_summary["rest_count"].mean(),
                        "平均火堆升级张数": run_summary["campfire_card_upgrade_count"].mean(),
                        "平均非火堆升级张数": run_summary["non_campfire_card_upgrade_count"].mean(),
                        "平均休息次数": run_summary["heal_count"].mean(),
                        "第一幕平均掉血": run_summary["act1_damage"].mean(),
                        "全局平均掉血": run_summary["total_damage"].mean(),
                        "平均删牌次数": run_summary["card_remove_count"].mean(),
                    }
                ]
            )
        ),
        "",
        "## 卡牌选择榜单",
        "",
        "这些榜单来自 `card_offers` 和 `card_stats_overall`。注意这里描述的是 77 连胜样本里的采用偏好，不是独立卡牌胜率。",
        "",
        "### 出现次数 Top 20",
        "",
        small_table(card_summary_view(card_stats.sort_values(["offer_count", "card_id"], ascending=[False, True]).head(20))),
        "",
        "### 抓取次数 Top 20",
        "",
        small_table(card_summary_view(card_stats.sort_values(["pick_count", "card_id"], ascending=[False, True]).head(20))),
        "",
        "### 见到后抓取率 Top 20（出现次数 >= 5）",
        "",
        small_table(card_summary_view(
            card_stats[card_stats["offer_count"] >= 5]
            .sort_values(["pick_rate_when_offered", "offer_count", "card_id"], ascending=[False, False, True])
            .head(20)
        )),
        "",
        "### 第一幕抓取率 Top 20（第一幕出现次数 >= 3）",
        "",
        small_table(card_summary_view(
            card_stats[card_stats["act1_offer_count"] >= 3]
            .sort_values(["act1_pick_rate", "act1_offer_count", "card_id"], ascending=[False, False, True])
            .head(20)
        )),
        "",
        "### Boss 奖励抓取率 Top 20（Boss 出现次数 >= 3）",
        "",
        small_table(boss_summary_view(boss_stats.head(20))),
        "",
        "## 开局前三次奖励",
        "",
        "这部分来自 `early_3_offer_detail` 和 `early_3_card_stats`，只看每局前 3 次非商店卡牌奖励。",
        "",
        small_table(early_summary_view(
            early_stats.sort_values(["early_pick_count", "early_offer_count", "card_id"], ascending=[False, False, True]).head(20)
        )),
        "",
        "## 最终卡组 Top 20",
        "",
        "这部分来自 `final_deck_cards` 和 `final_deck_card_summary`，回答最终卡组里真正留下了什么。",
        "",
        small_table(final_summary_view(final_summary.head(20))),
        "",
        "## 跳过相关统计",
        "",
        "原来的'被整菜单跳过次数'指的是：某张牌出现在一次卡牌菜单中，但该菜单一张牌都没有拿。这个口径不是卡牌对位，也不表示它输给某张具体卡。现在跳过单独统计，且排除商店菜单。",
        "",
        "### 非商店整跳菜单来源统计",
        "",
        small_table(skip_menu_summary_view(skip_menu_summary.head(20))),
        "",
        "### 出现在整跳菜单中的卡牌 Top 20",
        "",
        small_table(card_skip_summary_view(card_skip_summary.head(20))),
        "",
        "## 真实卡牌同屏对位相遇次数 Top 20（不含 SKIP）",
        "",
        "这部分来自标准奖励对位表：只统计非商店、3/4 选 1 的奖励菜单。整跳菜单已移到上面的跳过相关统计，不再把 SKIP 当成一张牌参与两两对位。",
        "",
        small_table(pairwise_summary_view(pairwise.head(20))),
        "",
        "## 升级、删除和遗物 Top 20",
        "",
        "### 火堆升级次数 Top 20",
        "",
        "这部分只看休息点 `SMITH`，用于判断主动敲牌优先级；事件、遗物、宝箱等奖励升级放在下一张表里单独看。",
        "",
        small_table(upgrade_summary_view(campfire_upgrade_summary.head(20))),
        "",
        "### 非火堆升级次数 Top 20",
        "",
        "这部分用于解释自动升级噪声，例如打击/防御被事件或遗物批量升级，不建议当作火堆优先级。",
        "",
        small_table(upgrade_summary_view(non_campfire_upgrade_summary.head(20))),
        "",
        "### 删除次数 Top 20",
        "",
        small_table(remove_summary_view(remove_summary.head(20))),
        "",
        "### 特殊事件任务牌",
        "",
        "这部分单独统计藏宝图和灯火钥匙这类事件给牌/任务回收；任务完成时的回收不计入主动删牌。",
        "",
        small_table(special_event_summary_view(special_event_summary)),
        "",
        "### 最终遗物出现局数 Top 20",
        "",
        small_table(relic_summary_view(relic_summary.head(20))),
    ]
    return "\n".join(lines) + "\n"


def build_table_description_markdown(validation: dict[str, Any]) -> str:
    rows = []
    row_counts = validation["table_row_counts"]
    for table_name in TABLE_ORDER:
        info = TABLE_DESCRIPTIONS[table_name]
        rows.append(
            {
                "序号": CSV_ORDER[table_name].split("_", 1)[0],
                "表名": table_name,
                "中文名称": info["label"],
                "行数": row_counts.get(table_name, 0),
                "粒度": info["grain"],
                "主要内容": info["content"],
                "用途": info["purpose"],
                "CSV": CSV_ORDER[table_name],
            }
        )
    return small_table(pd.DataFrame(rows))


def card_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame[
        [
            "card_id",
            "card_name_zh",
            "offer_count",
            "pick_count",
            "pick_rate_when_offered",
            "skipped_loss_count",
            "act1_offer_count",
            "act1_pick_rate",
            "avg_pick_floor",
            "final_deck_run_count",
            "final_deck_copy_count",
        ]
    ].rename(
        columns={
            "card_id": "卡牌",
            "card_name_zh": "中文名",
            "offer_count": "出现次数",
            "pick_count": "抓取次数",
            "pick_rate_when_offered": "见到后抓取率",
            "skipped_loss_count": "整菜单跳过次数",
            "act1_offer_count": "第一幕出现",
            "act1_pick_rate": "第一幕抓取率",
            "avg_pick_floor": "平均抓取楼层",
            "final_deck_run_count": "最终出现局数",
            "final_deck_copy_count": "最终总拷贝",
        }
    )


def boss_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame.rename(
        columns={
            "card_id": "卡牌",
            "card_name_zh": "中文名",
            "offer_count": "Boss 出现次数",
            "pick_count": "Boss 抓取次数",
            "pick_rate": "Boss 抓取率",
        }
    )


def skip_menu_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame.rename(
        columns={
            "source_type": "来源",
            "act": "幕",
            "menu_count": "菜单数",
            "skipped_menu_count": "整跳菜单数",
            "skip_rate": "整跳率",
            "skipped_run_count": "涉及局数",
            "skipped_offered_card_count": "整跳展示牌数",
            "avg_skipped_global_node": "平均整跳楼层",
        }
    )


def card_skip_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame[
        [
            "card_id",
            "card_name_zh",
            "non_shop_offer_count",
            "skipped_offer_count",
            "skip_exposure_rate",
            "act1_skipped_offer_count",
            "act2_skipped_offer_count",
            "act3_skipped_offer_count",
            "skipped_run_count",
            "avg_skipped_floor",
        ]
    ].rename(
        columns={
            "card_id": "卡牌",
            "card_name_zh": "中文名",
            "non_shop_offer_count": "非商店出现",
            "skipped_offer_count": "整跳菜单出现",
            "skip_exposure_rate": "整跳暴露率",
            "act1_skipped_offer_count": "第一幕整跳",
            "act2_skipped_offer_count": "第二幕整跳",
            "act3_skipped_offer_count": "第三幕整跳",
            "skipped_run_count": "涉及局数",
            "avg_skipped_floor": "平均整跳楼层",
        }
    )


def early_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame[
        [
            "card_id",
            "card_name_zh",
            "early_offer_count",
            "early_pick_count",
            "early_pick_rate",
            "early_menu1_offer_count",
            "early_menu1_pick_count",
            "early_menu2_offer_count",
            "early_menu2_pick_count",
            "early_menu3_offer_count",
            "early_menu3_pick_count",
        ]
    ].rename(
        columns={
            "card_id": "卡牌",
            "card_name_zh": "中文名",
            "early_offer_count": "前三奖励出现",
            "early_pick_count": "前三奖励抓取",
            "early_pick_rate": "前三奖励抓取率",
            "early_menu1_offer_count": "第1次出现",
            "early_menu1_pick_count": "第1次抓取",
            "early_menu2_offer_count": "第2次出现",
            "early_menu2_pick_count": "第2次抓取",
            "early_menu3_offer_count": "第3次出现",
            "early_menu3_pick_count": "第3次抓取",
        }
    )


def final_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame[
        [
            "card_id",
            "card_name_zh",
            "final_run_count",
            "final_copy_count",
            "avg_copies_when_present",
            "avg_floor_added",
            "median_floor_added",
            "upgraded_copy_count",
            "upgrade_rate_in_final",
            "enchanted_copy_count",
        ]
    ].rename(
        columns={
            "card_id": "卡牌",
            "card_name_zh": "中文名",
            "final_run_count": "最终出现局数",
            "final_copy_count": "最终总拷贝",
            "avg_copies_when_present": "出现时平均拷贝",
            "avg_floor_added": "平均加入楼层",
            "median_floor_added": "中位加入楼层",
            "upgraded_copy_count": "升级拷贝数",
            "upgrade_rate_in_final": "最终升级率",
            "enchanted_copy_count": "附魔拷贝数",
        }
    )


def pairwise_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame[
        [
            "card_a",
            "card_a_name_zh",
            "card_b",
            "card_b_name_zh",
            "a_beats_b",
            "b_beats_a",
            "total_meetings",
            "a_pick_rate_vs_b",
            "smooth_a_pick_rate_vs_b",
        ]
    ].rename(
        columns={
            "card_a": "卡 A",
            "card_a_name_zh": "卡 A 中文名",
            "card_b": "卡 B",
            "card_b_name_zh": "卡 B 中文名",
            "a_beats_b": "A 胜 B",
            "b_beats_a": "B 胜 A",
            "total_meetings": "有效相遇",
            "a_pick_rate_vs_b": "A 对 B 选择率",
            "smooth_a_pick_rate_vs_b": "平滑选择率",
        }
    )


def upgrade_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame.rename(
        columns={
            "card_id": "卡牌",
            "card_name_zh": "中文名",
            "upgrade_count": "升级次数",
            "avg_upgrade_floor": "平均升级楼层",
            "act1_upgrade_count": "第一幕升级",
            "act2_upgrade_count": "第二幕升级",
            "act3_upgrade_count": "第三幕升级",
            "first_upgrade_count": "每局首升次数",
            "event_upgrade_count": "事件升级",
            "elite_upgrade_count": "精英节点升级",
            "monster_upgrade_count": "战斗节点升级",
            "treasure_upgrade_count": "宝箱升级",
            "shop_upgrade_count": "商店升级",
            "boss_upgrade_count": "Boss 节点升级",
            "unknown_upgrade_count": "未知来源升级",
        }
    )


def remove_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame.rename(
        columns={
            "card_id": "卡牌",
            "card_name_zh": "中文名",
            "remove_count": "删除次数",
            "avg_remove_floor": "平均删除楼层",
            "act1_remove_count": "第一幕删除",
            "act2_remove_count": "第二幕删除",
            "act3_remove_count": "第三幕删除",
        }
    )


def special_event_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    if frame.empty:
        return frame
    return frame[
        [
            "special_card_id",
            "special_card_name_zh",
            "event_label",
            "encounter_count",
            "take_count",
            "return_count",
            "completion_count",
            "avg_take_floor",
            "avg_completion_floor",
            "completion_reward_relic_ids",
            "completion_reward_relic_names_zh",
        ]
    ].rename(
        columns={
            "special_card_id": "特殊牌",
            "special_card_name_zh": "中文名",
            "event_label": "事件区域",
            "encounter_count": "遭遇次数",
            "take_count": "拿取次数",
            "return_count": "归还次数",
            "completion_count": "任务完成次数",
            "avg_take_floor": "平均获得楼层",
            "avg_completion_floor": "平均完成楼层",
            "completion_reward_relic_ids": "完成奖励遗物",
            "completion_reward_relic_names_zh": "完成奖励遗物中文名",
        }
    )


def relic_summary_view(frame: pd.DataFrame) -> pd.DataFrame:
    return frame.rename(
        columns={
            "relic_id": "遗物",
            "relic_name_zh": "中文名",
            "final_run_count": "最终出现局数",
            "final_copy_count": "最终总拷贝",
            "avg_floor_added": "平均获得楼层",
            "median_floor_added": "中位获得楼层",
        }
    )


def summarize_source_pick_rates(card_offers: pd.DataFrame, source_type: str, min_offer_count: int) -> pd.DataFrame:
    source = card_offers[card_offers["source_type"] == source_type]
    rows = []
    for card_id, group in source.groupby("card_id"):
        offer_count = len(group)
        if offer_count < min_offer_count:
            continue
        pick_count = int(group["was_picked"].sum())
        rows.append(
            {
                "card_id": card_id,
                "card_name_zh": first_non_empty(group.get("card_name_zh", [])),
                "offer_count": offer_count,
                "pick_count": pick_count,
                "pick_rate": safe_rate(pick_count, offer_count),
            }
        )
    if not rows:
        return pd.DataFrame(columns=["card_id", "card_name_zh", "offer_count", "pick_count", "pick_rate"])
    return pd.DataFrame(rows).sort_values(
        ["pick_rate", "offer_count", "card_id"],
        ascending=[False, False, True],
        ignore_index=True,
    )


def small_table(frame: pd.DataFrame) -> str:
    if frame.empty:
        return "_No rows._"
    view = frame.copy()
    for column in view.columns:
        if pd.api.types.is_float_dtype(view[column]):
            view[column] = view[column].round(3)
    return view.to_markdown(index=False)


def first_non_empty(values: Iterable[Any]) -> str:
    for value in values:
        if value not in (None, ""):
            return str(value)
    return ""
