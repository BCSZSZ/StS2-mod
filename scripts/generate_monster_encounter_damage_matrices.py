"""Generate encounter damage matrix tables.

This report is optimized for reading one encounter at a time: rows are turns,
columns are monster slots, and the final column is the turn total. It builds on
the richer action/damage reports and adds a deterministic slot-state pass for
encounters whose initial move can be inferred from decompiled source.
"""

from __future__ import annotations

import argparse
import datetime as dt
import itertools
import json
import math
import re
from pathlib import Path
from typing import Any


VULNERABLE_MULTIPLIER = 1.5
SPECIAL_TOKENS = {
    "phase": ["IsInSecondPhase", "SetStunned", "PlowPower", "Respawns", "Revive("],
    "deathTrigger": ["AfterDeath", ".Died +=", "BeforeDeath"],
    "summon": ["CreatureCmd.Add", "SummonIntent", "SpawnBot", "AddMinion"],
    "explode": ["DeathBlowIntent", "ExplodeMove", "explode", "Explosion"],
    "slotStart": ["Creature.SlotName", "SlotName =="],
    "randomStart": ["EnsureCorpseSlugsStartWithDifferentMoves", "StarterMoveIdx"],
}

CATEGORY_LABELS = {
    "attack": "攻击",
    "defense": "防御",
    "selfBuff": "自buff",
    "playerDebuff": "debuff",
    "addCard": "塞牌",
    "special": "特殊",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate per-encounter turn x monster-slot damage matrices."
    )
    parser.add_argument(
        "--actions",
        default="data/generated/monster_encounter_turn_actions.generated.json",
        help="Action report from generate_monster_encounter_turn_actions.py.",
    )
    parser.add_argument(
        "--damage-details",
        default="data/generated/monster_encounter_damage_details.generated.json",
        help="Damage detail report from generate_monster_encounter_damage_details.py.",
    )
    parser.add_argument(
        "--localized-names",
        default="history-analysis/data/localized_names_en_zhs.json",
        help="Extracted en/zhs localization names used for encounter titles.",
    )
    parser.add_argument(
        "--decompile-root",
        default="data/generated/decompiled/sts2",
        help="ILSpy decompiled source root used to verify encounter slot generation.",
    )
    parser.add_argument(
        "--overrides",
        default="data/manual-tags/encounter_damage_matrix_overrides.json",
        help="Manual phase/survival overrides for matrix tables.",
    )
    parser.add_argument("--turns", type=int, default=14, help="Default number of turns.")
    parser.add_argument(
        "--composition-limit",
        type=int,
        default=12,
        help="Maximum conditional compositions to expand as separate exact tables.",
    )
    parser.add_argument(
        "--output-json",
        default="data/generated/monster_encounter_damage_matrices.generated.json",
        help="Output JSON path.",
    )
    parser.add_argument(
        "--output-md",
        default="data/generated/monster_encounter_damage_matrices.md",
        help="Output Markdown path.",
    )
    return parser.parse_args()


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def load_json_or_empty(path: Path) -> dict[str, Any]:
    return load_json(path) if path.exists() else {}


def load_localized_entries(path: Path) -> dict[str, dict[str, str]]:
    if not path.exists():
        return {}
    data = load_json(path)
    entries = data.get("entries", data) if isinstance(data, dict) else {}
    return entries if isinstance(entries, dict) else {}


def round3(value: float) -> float:
    if math.isclose(value, 0.0, abs_tol=0.0005):
        return 0.0
    return round(value, 3)


def fmt(value: Any) -> str:
    if value is None:
        return "-"
    if isinstance(value, float):
        value = round3(value)
        if value.is_integer():
            return str(int(value))
        return f"{value:g}"
    return str(value)


def escape_md(value: Any) -> str:
    return str(value).replace("|", "\\|").replace("\n", "<br>")


def humanize_identifier(identifier: str | None) -> str:
    if not identifier:
        return ""
    value = identifier.replace("_", " ")
    value = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", " ", value)
    value = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", value)
    value = re.sub(r"(?<=[A-Za-z])(?=\d)", " ", value)
    value = re.sub(r"(?<=\d)(?=[A-Za-z])", " ", value)
    value = re.sub(r"\s+", " ", value).strip()
    value = re.sub(r"\bV\s+(\d+)\b", r"V\1", value)
    return value


def localization_key(type_name: str | None) -> str:
    if not type_name:
        return ""
    value = re.sub(r"(?<=[A-Z])(?=[A-Z][a-z])", "_", type_name)
    value = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", value)
    value = re.sub(r"(?<=[A-Za-z])(?=\d)", "_", value)
    value = re.sub(r"(?<=\d)(?=[A-Za-z])", "_", value)
    return value.upper()


def localized_title(type_name: str | None, entries: dict[str, dict[str, str]]) -> dict[str, str]:
    key = localization_key(type_name)
    entry = entries.get(key)
    if entry:
        return {
            "en": entry.get("en") or humanize_identifier(type_name),
            "zh": entry.get("zhs") or entry.get("zh") or entry.get("en") or humanize_identifier(type_name),
            "source": f"history-analysis/data/localized_names_en_zhs.json:{key}",
        }
    fallback = humanize_identifier(type_name)
    return {"en": fallback, "zh": fallback, "source": "fallback:typeName"}


def format_bilingual_name(name: dict[str, Any] | None, fallback: str = "") -> str:
    if not isinstance(name, dict):
        return fallback
    english = name.get("en") or fallback
    chinese = name.get("zh") or fallback
    if english and chinese:
        return f"{chinese} / {english}"
    return chinese or english or fallback


def slot_possible_types(slot: dict[str, Any]) -> list[str]:
    if slot.get("monsterTypeName"):
        return [slot["monsterTypeName"]]
    return slot.get("possibleMonsterTypeNames", [])


def state_attacks(state: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        action
        for action in state.get("categories", {}).get("attack", [])
        if action.get("kind") == "attack"
    ]


def adjusted_attack_damage(
    attack: dict[str, Any],
    strength: float,
    vulnerable_active: bool,
) -> tuple[float | None, str | None]:
    amount = attack.get("ascensionAmount")
    hits = attack.get("ascensionHitCount")
    if amount is None:
        return None, attack.get("amountExpression") or attack.get("kind")
    if hits is None:
        hits = 1
    damage = (float(amount) + strength) * float(hits)
    if vulnerable_active:
        damage *= VULNERABLE_MULTIPLIER
    return damage, None


def state_adjusted_damage(
    state: dict[str, Any],
    strength: float,
    vulnerable_duration: float,
) -> tuple[float, list[str]]:
    total = 0.0
    unknown: list[str] = []
    vulnerable_active = vulnerable_duration > 0
    for attack in state_attacks(state):
        damage, expression = adjusted_attack_damage(attack, strength, vulnerable_active)
        if damage is None:
            unknown.append(expression or "unknown")
        else:
            total += damage
    return total, unknown


def strength_gain_from_state(state: dict[str, Any]) -> float:
    total = 0.0
    for action in state.get("categories", {}).get("selfBuff", []):
        parameter = action.get("parameter")
        kind = action.get("kind")
        if kind == "buffStrength" or parameter == "power:Strength":
            amount = action.get("ascensionAmount")
            if amount is None:
                amount = action.get("amount")
            if action.get("target") == "self" and amount is not None:
                total += float(amount)
    return total


def vulnerable_gain_from_state(state: dict[str, Any]) -> float:
    total = 0.0
    for action in state.get("categories", {}).get("playerDebuff", []):
        parameter = action.get("parameter")
        kind = action.get("kind")
        if kind == "debuffVulnerable" or parameter == "power:Vulnerable":
            amount = action.get("ascensionAmount")
            if amount is None:
                amount = action.get("amount")
            if amount is not None:
                total += float(amount)
    return total


def next_state_id(state: dict[str, Any]) -> str | None:
    followups = state.get("followUpStateIds", [])
    if len(followups) == 1:
        return followups[0]
    return None


def read_source(path: str | None) -> str:
    if not path:
        return ""
    source_path = Path(path)
    if not source_path.exists():
        return ""
    return source_path.read_text(encoding="utf-8", errors="replace")


def source_path_for_full_type(decompile_root: Path, full_type: str | None) -> Path | None:
    if not full_type:
        return None
    path = decompile_root / Path(*full_type.split(".")).with_suffix(".cs")
    return path if path.exists() else None


def read_source_for_full_type(decompile_root: Path, full_type: str | None) -> str:
    path = source_path_for_full_type(decompile_root, full_type)
    if not path:
        return ""
    return path.read_text(encoding="utf-8", errors="replace")


def parse_declared_slots(source: str) -> list[str]:
    match = re.search(r"new string\[\d+\]\s*\{(?P<body>[^}]+)\}", source)
    if not match:
        return []
    return re.findall(r'"([^"]+)"', match.group("body"))


def enhanced_encounter_from_source(
    encounter: dict[str, Any],
    decompile_root: Path,
) -> dict[str, Any]:
    source = read_source_for_full_type(decompile_root, encounter.get("fullTypeName"))
    slots = parse_declared_slots(source)
    if not slots:
        return encounter
    if "foreach (string slot in Slots)" not in source or "list.Add((" not in source:
        return encounter
    monster_types = sorted(set(re.findall(r"ModelDb\.Monster<(?P<type>[A-Za-z0-9_]+)>\(\)", source)))
    if len(monster_types) != 1:
        return encounter

    monster_type = monster_types[0]
    enhanced = dict(encounter)
    enhanced["monsterSlots"] = [
        {
            "position": index + 1,
            "slotName": slot_name,
            "monsterTypeName": monster_type,
            "possibleMonsterTypeNames": [monster_type],
            "source": "GenerateMonsters foreach Slots verified from encounter source",
            "confidence": 0.95,
        }
        for index, slot_name in enumerate(slots)
    ]
    enhanced["sourceSlotCorrection"] = {
        "reason": "Generated encounter pattern collapsed a foreach Slots loop; matrix report expanded it from source.",
        "slotCount": len(slots),
        "monsterTypeName": monster_type,
    }
    return enhanced


def infer_slot_initial_states(source: str) -> dict[str, str]:
    state_vars: dict[str, str] = {}
    for match in re.finditer(
        r"(?:(?:MoveState|MonsterState)\s+)?(?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new MoveState\(\"(?P<state>[^\"]+)\"",
        source,
    ):
        state_vars[match.group("var")] = match.group("state")

    slot_states: dict[str, str] = {}
    for match in re.finditer(
        r"\.AddState\((?P<var>[A-Za-z_][A-Za-z0-9_]*),\s*\(\)\s*=>\s*(?:base\.)?Creature\.SlotName\s*==\s*\"(?P<slot>[^\"]+)\"",
        source,
    ):
        state_id = state_vars.get(match.group("var"))
        if state_id:
            slot_states[match.group("slot")] = state_id
    return slot_states


def infer_corpse_slug_starter_order(source: str) -> list[str]:
    state_vars: dict[str, str] = {}
    for match in re.finditer(
        r"MoveState\s+(?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new MoveState\(\"(?P<state>[^\"]+)\"",
        source,
    ):
        state_vars[match.group("var")] = match.group("state")

    order: list[str] = []
    for index in (0, 1):
        match = re.search(rf"{index}\s*=>\s*(?P<var>[A-Za-z_][A-Za-z0-9_]*)", source)
        if match and state_vars.get(match.group("var")):
            order.append(state_vars[match.group("var")])
    match = re.search(r"_\s*=>\s*(?P<var>[A-Za-z_][A-Za-z0-9_]*)", source)
    if match and state_vars.get(match.group("var")):
        order.append(state_vars[match.group("var")])
    return order


def scan_special_mechanics(source: str) -> list[str]:
    found: list[str] = []
    for key, tokens in SPECIAL_TOKENS.items():
        if any(token in source for token in tokens):
            found.append(key)
    return found


def composition_scenarios(slots: list[dict[str, Any]]) -> list[tuple[str, ...]]:
    choices = [slot_possible_types(slot) or ["<missing>"] for slot in slots]
    return [tuple(types) for types in itertools.product(*choices)]


def build_slot_plans(
    encounter: dict[str, Any],
    composition: tuple[str, ...],
    monster_catalog: dict[str, dict[str, Any]],
    slot_initials_by_monster: dict[str, dict[str, str]],
    override: dict[str, Any] | None = None,
    corpse_slug_offset: int | None = None,
    corpse_slug_order: list[str] | None = None,
) -> tuple[list[dict[str, Any]], list[str]]:
    override = override or {}
    warnings: list[str] = []
    start_overrides = override.get("slotStartStateIds", {})
    active_overrides = override.get("slotActiveThroughTurn", {})
    plans: list[dict[str, Any]] = []

    for index, slot in enumerate(encounter.get("monsterSlots", [])):
        monster_type = composition[index] if index < len(composition) else "<missing>"
        position = int(slot.get("position", index + 1))
        slot_name = slot.get("slotName") or f"slot{position}"
        monster = monster_catalog.get(monster_type, {})
        moves = monster.get("moves", [])
        start_state = (
            start_overrides.get(str(position))
            or start_overrides.get(slot_name)
        )
        if not start_state and corpse_slug_offset is not None and monster_type == "CorpseSlug" and corpse_slug_order:
            start_state = corpse_slug_order[(corpse_slug_offset + index) % len(corpse_slug_order)]
        if not start_state:
            start_state = slot_initials_by_monster.get(monster_type, {}).get(slot_name)
        if not start_state:
            start_state = monster.get("initialStateId")
        if not start_state and len(moves) == 1:
            start_state = moves[0].get("stateId")
        if not start_state:
            warnings.append(f"No deterministic start state for {monster_type} in slot {position}.")

        active_through = active_overrides.get(str(position), active_overrides.get(slot_name))
        plans.append(
            {
                "position": position,
                "slotName": slot_name,
                "monsterTypeName": monster_type,
                "monsterName": monster.get("name"),
                "startStateId": start_state,
                "activeThroughTurn": int(active_through) if active_through is not None else None,
            }
        )
    return plans, warnings


def simulate_exact_table(
    table_id: str,
    title: str,
    encounter: dict[str, Any],
    slot_plans: list[dict[str, Any]],
    monster_catalog: dict[str, dict[str, Any]],
    turn_count: int,
    note: str | None = None,
) -> dict[str, Any]:
    state_ids = {plan["position"]: plan.get("startStateId") for plan in slot_plans}
    strengths = {plan["position"]: 0.0 for plan in slot_plans}
    vulnerable = 0.0
    rows: list[dict[str, Any]] = []
    warnings: list[str] = []

    for turn in range(1, turn_count + 1):
        cells: list[dict[str, Any]] = []
        total = 0.0
        unknown: list[str] = []
        for plan in slot_plans:
            position = plan["position"]
            if plan.get("activeThroughTurn") is not None and turn > plan["activeThroughTurn"]:
                cells.append(
                    {
                        "position": position,
                        "slotName": plan["slotName"],
                        "monsterTypeName": plan["monsterTypeName"],
                        "monsterName": plan["monsterName"],
                        "stateId": None,
                        "damage": None,
                        "display": "-",
                        "omittedByAssumption": True,
                    }
                )
                continue

            monster = monster_catalog.get(plan["monsterTypeName"], {})
            moves_by_state = {move["stateId"]: move for move in monster.get("moves", [])}
            state_id = state_ids.get(position)
            state = moves_by_state.get(state_id)
            if not state:
                warnings.append(
                    f"Missing state {state_id or '<unknown>'} for {plan['monsterTypeName']} slot {position}."
                )
                cells.append(
                    {
                        "position": position,
                        "slotName": plan["slotName"],
                        "monsterTypeName": plan["monsterTypeName"],
                        "monsterName": plan["monsterName"],
                        "stateId": state_id,
                        "damage": None,
                        "display": "?",
                        "unknownDamageExpressions": ["missingState"],
                    }
                )
                continue

            damage, state_unknown = state_adjusted_damage(state, strengths[position], vulnerable)
            damage = round3(damage)
            total += damage
            unknown.extend(state_unknown)
            cells.append(
                {
                    "position": position,
                    "slotName": plan["slotName"],
                    "monsterTypeName": plan["monsterTypeName"],
                    "monsterName": plan["monsterName"],
                    "stateId": state_id,
                    "intents": state.get("intents", []),
                    "intentDetails": state.get("intentDetails", []),
                    "categoryIds": state.get("categoryIds", []),
                    "strengthBefore": round3(strengths[position]),
                    "vulnerableDurationBefore": round3(vulnerable),
                    "damage": damage,
                    "display": fmt(damage),
                    "unknownDamageExpressions": state_unknown,
                }
            )

            strengths[position] += strength_gain_from_state(state)
            vulnerable += vulnerable_gain_from_state(state)
            next_state = next_state_id(state)
            if next_state:
                state_ids[position] = next_state

        rows.append(
            {
                "turn": turn,
                "cells": cells,
                "totalDamage": round3(total),
                "displayTotal": fmt(total),
                "unknownDamageExpressions": sorted(set(unknown)),
            }
        )
        vulnerable = max(0.0, vulnerable - 1.0)

    return {
        "id": table_id,
        "title": title,
        "mode": "exact",
        "damageBasis": "Adjusted Ascension 10 damage; includes parsed Strength and player Vulnerable.",
        "note": note,
        "slotPlans": slot_plans,
        "rows": rows,
        "warnings": sorted(set(warnings)),
    }


def expected_table_from_damage_details(
    table_id: str,
    title: str,
    damage_encounter: dict[str, Any],
    turn_count: int,
    note: str | None = None,
) -> dict[str, Any]:
    rows: list[dict[str, Any]] = []
    for turn in damage_encounter.get("turns", [])[:turn_count]:
        cells = []
        for slot in turn.get("slots", []):
            names = slot.get("possibleMonsterNames") or [
                {"typeName": monster_type, "name": None}
                for monster_type in slot.get("possibleMonsterTypeNames", [])
            ]
            cells.append(
                {
                    "position": slot.get("position"),
                    "slotName": slot.get("slotName"),
                    "possibleMonsterNames": names,
                    "damage": slot.get("expectedAdjustedDamage"),
                    "display": fmt(slot.get("expectedAdjustedDamage")),
                    "unknownDamageExpressions": slot.get("unknownDamageExpressions", []),
                }
            )
        rows.append(
            {
                "turn": turn.get("turn"),
                "cells": cells,
                "totalDamage": turn.get("totalAdjustedDamage"),
                "displayTotal": fmt(turn.get("totalAdjustedDamage")),
                "unknownDamageExpressions": turn.get("unknownDamageExpressions", []),
            }
        )
    return {
        "id": table_id,
        "title": title,
        "mode": "expected",
        "damageBasis": "Expected adjusted damage from monster_encounter_damage_details; used when exact slot starts/compositions are not fully deterministic.",
        "note": note,
        "rows": rows,
        "warnings": [],
    }


def needs_expected_fallback(slot_plans: list[dict[str, Any]]) -> bool:
    return any(not plan.get("startStateId") for plan in slot_plans)


def comparable_cell_signature(cell: dict[str, Any]) -> tuple[Any, ...]:
    return (
        cell.get("monsterTypeName"),
        cell.get("stateId"),
        tuple(cell.get("intents", [])),
        tuple(cell.get("categoryIds", [])),
        cell.get("strengthBefore"),
        cell.get("vulnerableDurationBefore"),
        cell.get("damage"),
        cell.get("display"),
        tuple(sorted(cell.get("unknownDamageExpressions", []))),
        bool(cell.get("omittedByAssumption")),
    )


def rotate(items: list[Any], offset: int) -> list[Any]:
    if not items:
        return items
    offset %= len(items)
    return items[offset:] + items[:offset]


def cyclic_slot_rotation_offset(reference: dict[str, Any], candidate: dict[str, Any]) -> int | None:
    if reference.get("mode") != "exact" or candidate.get("mode") != "exact":
        return None

    reference_rows = reference.get("rows", [])
    candidate_rows = candidate.get("rows", [])
    if len(reference_rows) != len(candidate_rows) or not reference_rows:
        return None

    slot_count = len(reference_rows[0].get("cells", []))
    if slot_count <= 1:
        return None

    for offset in range(1, slot_count):
        matches = True
        for reference_row, candidate_row in zip(reference_rows, candidate_rows):
            reference_cells = reference_row.get("cells", [])
            candidate_cells = candidate_row.get("cells", [])
            if len(reference_cells) != slot_count or len(candidate_cells) != slot_count:
                matches = False
                break
            if reference_row.get("totalDamage") != candidate_row.get("totalDamage"):
                matches = False
                break
            if reference_row.get("displayTotal") != candidate_row.get("displayTotal"):
                matches = False
                break
            if sorted(reference_row.get("unknownDamageExpressions", [])) != sorted(
                candidate_row.get("unknownDamageExpressions", [])
            ):
                matches = False
                break

            rotated_reference = rotate(
                [comparable_cell_signature(cell) for cell in reference_cells],
                offset,
            )
            candidate_signature = [
                comparable_cell_signature(cell)
                for cell in candidate_cells
            ]
            if rotated_reference != candidate_signature:
                matches = False
                break
        if matches:
            return offset

    return None


def collapse_cyclically_symmetric_tables(tables: list[dict[str, Any]]) -> list[dict[str, Any]]:
    collapsed: list[dict[str, Any]] = []
    for table in tables:
        matched = False
        for kept in collapsed:
            rotation = cyclic_slot_rotation_offset(kept, table)
            if rotation is None:
                continue
            kept.setdefault("omittedSymmetricTables", []).append(
                {
                    "id": table.get("id"),
                    "title": table.get("title"),
                    "slotRotation": rotation,
                    "reason": "cyclicSlotRotation",
                }
            )
            matched = True
            break
        if not matched:
            collapsed.append(table)
    return collapsed


def build_tables_for_encounter(
    action_encounter: dict[str, Any],
    damage_encounter: dict[str, Any],
    monster_catalog: dict[str, dict[str, Any]],
    slot_initials_by_monster: dict[str, dict[str, str]],
    starter_orders: dict[str, list[str]],
    overrides: dict[str, Any],
    turn_count: int,
    composition_limit: int,
) -> list[dict[str, Any]]:
    tables: list[dict[str, Any]] = []
    slots = action_encounter.get("monsterSlots", [])
    compositions = composition_scenarios(slots)
    encounter_overrides = overrides.get("encounters", {}).get(action_encounter.get("typeName"), {})
    override_tables = encounter_overrides.get("tables", [])

    # Manual phase/survival tables are shown first because they encode the user's
    # preferred practical assumptions.
    for override in override_tables:
        composition = compositions[0] if compositions else tuple()
        slot_plans, plan_warnings = build_slot_plans(
            action_encounter,
            composition,
            monster_catalog,
            slot_initials_by_monster,
            override,
        )
        if plan_warnings or needs_expected_fallback(slot_plans):
            table = expected_table_from_damage_details(
                override.get("id", "manual-expected"),
                override.get("title", "手动表"),
                damage_encounter,
                int(override.get("turnCount", turn_count)),
                override.get("note"),
            )
            table["warnings"].extend(plan_warnings)
        else:
            table = simulate_exact_table(
                override.get("id", "manual"),
                override.get("title", "手动表"),
                action_encounter,
                slot_plans,
                monster_catalog,
                int(override.get("turnCount", turn_count)),
                override.get("note"),
            )
            table["warnings"].extend(plan_warnings)
        tables.append(table)

    fixed_monster_types = sorted({monster for composition in compositions for monster in composition})
    if (
        fixed_monster_types == ["CorpseSlug"]
        and "CorpseSlug" in starter_orders
        and len(starter_orders["CorpseSlug"]) == 3
    ):
        for offset in range(3):
            composition = compositions[0] if compositions else tuple()
            slot_plans, plan_warnings = build_slot_plans(
                action_encounter,
                composition,
                monster_catalog,
                slot_initials_by_monster,
                corpse_slug_offset=offset,
                corpse_slug_order=starter_orders["CorpseSlug"],
            )
            table = simulate_exact_table(
                f"corpse-slug-starter-offset-{offset}",
                f"噬尸蛞蝓随机起手 offset {offset}",
                action_encounter,
                slot_plans,
                monster_catalog,
                turn_count,
                "游戏会随机选择一个起手 offset，并让多只噬尸蛞蝓依次错开起手。",
            )
            table["warnings"].extend(plan_warnings)
            tables.append(table)
        return collapse_cyclically_symmetric_tables(tables)

    if len(compositions) > composition_limit:
        tables.append(
            expected_table_from_damage_details(
                "expected",
                "期望伤害矩阵",
                damage_encounter,
                turn_count,
                f"条件组合数 {len(compositions)} 超过展开上限 {composition_limit}，使用期望表。",
            )
        )
        return tables

    for index, composition in enumerate(compositions):
        slot_plans, plan_warnings = build_slot_plans(
            action_encounter,
            composition,
            monster_catalog,
            slot_initials_by_monster,
        )
        if plan_warnings or needs_expected_fallback(slot_plans):
            if index == 0:
                table = expected_table_from_damage_details(
                    "expected",
                    "期望伤害矩阵",
                    damage_encounter,
                    turn_count,
                    "有槽位起手状态无法唯一确定，使用现有伤害明细的期望值。",
                )
                table["warnings"].extend(plan_warnings)
                tables.append(table)
            continue

        suffix = "" if len(compositions) == 1 else f" 组合{index + 1}"
        table_id = "full-sequence" if len(compositions) == 1 else f"composition-{index + 1}"
        tables.append(
            simulate_exact_table(
                table_id,
                f"完整序列{suffix}",
                action_encounter,
                slot_plans,
                monster_catalog,
                turn_count,
            )
        )

    if not tables:
        tables.append(
            expected_table_from_damage_details(
                "expected",
                "期望伤害矩阵",
                damage_encounter,
                turn_count,
            )
        )
    return tables


def build_report(
    action_report: dict[str, Any],
    damage_report: dict[str, Any],
    localized_entries: dict[str, dict[str, str]],
    overrides: dict[str, Any],
    decompile_root: Path,
    turn_count: int,
    composition_limit: int,
) -> dict[str, Any]:
    monster_catalog = {
        monster["typeName"]: monster
        for monster in action_report.get("monsterCatalog", [])
    }
    source_by_monster = {
        monster_type: read_source((monster.get("moves") or [{}])[0].get("sourcePath"))
        for monster_type, monster in monster_catalog.items()
    }
    slot_initials_by_monster = {
        monster_type: infer_slot_initial_states(source)
        for monster_type, source in source_by_monster.items()
    }
    starter_orders = {
        monster_type: infer_corpse_slug_starter_order(source)
        for monster_type, source in source_by_monster.items()
    }
    starter_orders = {key: value for key, value in starter_orders.items() if value}
    mechanics_by_monster = {
        monster_type: scan_special_mechanics(source)
        for monster_type, source in source_by_monster.items()
    }

    damage_by_type = {
        encounter["typeName"]: encounter
        for encounter in damage_report.get("encounters", [])
    }
    encounters: list[dict[str, Any]] = []
    for raw_action_encounter in action_report.get("encounters", []):
        action_encounter = enhanced_encounter_from_source(raw_action_encounter, decompile_root)
        encounter_type = action_encounter["typeName"]
        damage_encounter = damage_by_type.get(encounter_type)
        if not damage_encounter:
            continue
        monster_types = sorted(
            {
                monster_type
                for slot in action_encounter.get("monsterSlots", [])
                for monster_type in slot_possible_types(slot)
            }
        )
        mechanics = sorted(
            {
                mechanic
                for monster_type in monster_types
                for mechanic in mechanics_by_monster.get(monster_type, [])
            }
        )
        tables = build_tables_for_encounter(
            action_encounter,
            damage_encounter,
            monster_catalog,
            slot_initials_by_monster,
            starter_orders,
            overrides,
            turn_count,
            composition_limit,
        )
        encounters.append(
            {
                "modelId": action_encounter.get("modelId"),
                "typeName": encounter_type,
                "name": localized_title(encounter_type, localized_entries),
                "acts": action_encounter.get("acts", []),
                "actLabel": damage_encounter.get("actLabel"),
                "category": action_encounter.get("category"),
                "hasConditionalMonsterSelection": action_encounter.get("hasConditionalMonsterSelection"),
                "mechanics": mechanics,
                "sourceSlotCorrection": action_encounter.get("sourceSlotCorrection"),
                "tables": tables,
            }
        )

    return {
        "schemaVersion": 1,
        "generatedAt": dt.datetime.now(dt.timezone.utc).isoformat(),
        "turnCount": turn_count,
        "sourceFiles": {
            "turnActions": "data/generated/monster_encounter_turn_actions.generated.json",
            "damageDetails": "data/generated/monster_encounter_damage_details.generated.json",
            "localizedNames": "history-analysis/data/localized_names_en_zhs.json",
            "overrides": "data/manual-tags/encounter_damage_matrix_overrides.json",
            "decompileRoot": str(decompile_root),
        },
        "rules": [
            "Exact tables simulate deterministic slot start states from source when possible.",
            "Adjusted damage includes parsed monster Strength and player Vulnerable.",
            "Expected tables fall back to the existing damage detail report when a slot start or composition cannot be uniquely determined.",
            "Manual override tables encode practical survival or phase-start assumptions; '-' means the slot is omitted by that assumption.",
            "Exact tables that differ only by cyclic slot rotation are collapsed into one representative table.",
        ],
        "summary": {
            "encounterCount": len(encounters),
            "tableCount": sum(len(encounter["tables"]) for encounter in encounters),
            "exactTableCount": sum(
                1
                for encounter in encounters
                for table in encounter["tables"]
                if table.get("mode") == "exact"
            ),
            "expectedTableCount": sum(
                1
                for encounter in encounters
                for table in encounter["tables"]
                if table.get("mode") == "expected"
            ),
            "manualOverrideEncounterCount": len(overrides.get("encounters", {})),
            "cyclicSymmetryOmittedTableCount": sum(
                len(table.get("omittedSymmetricTables", []))
                for encounter in encounters
                for table in encounter["tables"]
            ),
        },
        "encounters": encounters,
    }


def slot_header(slot_count: int) -> list[str]:
    return [f"{index}号" for index in range(1, slot_count + 1)]


def slot_names_from_table(table: dict[str, Any]) -> list[str]:
    plans = table.get("slotPlans")
    if plans:
        return [
            f"{plan['position']}号={format_bilingual_name(plan.get('monsterName'), plan.get('monsterTypeName', ''))}"
            for plan in plans
        ]
    first_row = (table.get("rows") or [{}])[0]
    names = []
    for cell in first_row.get("cells", []):
        possible = cell.get("possibleMonsterNames", [])
        text = "/".join(
            format_bilingual_name(item.get("name"), item.get("typeName", ""))
            for item in possible
        )
        names.append(f"{cell.get('position')}号={text}")
    return names


def state_cycle_preview(table: dict[str, Any], max_steps: int = 4) -> list[str]:
    if table.get("mode") != "exact":
        return []
    by_position: dict[int, list[str]] = {}
    for row in table.get("rows", [])[:max_steps]:
        for cell in row.get("cells", []):
            position = cell.get("position")
            state_id = cell.get("stateId")
            if position is None or not state_id:
                continue
            categories = "+".join(
                CATEGORY_LABELS.get(category, category)
                for category in cell.get("categoryIds", [])
            )
            state_text = f"{state_id}" + (f"({categories})" if categories else "")
            by_position.setdefault(int(position), []).append(state_text)
    result = []
    for position in sorted(by_position):
        states = by_position[position]
        compact: list[str] = []
        for state in states:
            if not compact or compact[-1] != state:
                compact.append(state)
        if compact:
            result.append(f"{position}号: " + " -> ".join(compact))
    return result


def write_markdown(report: dict[str, Any], path: Path) -> None:
    lines: list[str] = []
    lines.append("# Monster Encounter Damage Matrices")
    lines.append("")
    lines.append(f"Generated at: {report['generatedAt']}")
    lines.append(f"Default turns: 1-{report['turnCount']}")
    lines.append("")
    lines.append("## Rules")
    lines.append("")
    for rule in report["rules"]:
        lines.append(f"- {rule}")
    lines.append("")
    lines.append("## Summary")
    lines.append("")
    lines.append("| Metric | Value |")
    lines.append("| --- | ---: |")
    for key, value in report["summary"].items():
        lines.append(f"| {key} | {value} |")
    lines.append("")

    for encounter in report["encounters"]:
        encounter_name = format_bilingual_name(encounter.get("name"), encounter.get("typeName", ""))
        lines.append(f"## {escape_md(encounter_name)}")
        lines.append("")
        lines.append(
            f"- Type: `{encounter['typeName']}`; Act: {escape_md(encounter.get('actLabel') or '')}; Category: {escape_md(encounter.get('category') or '')}"
        )
        if encounter.get("mechanics"):
            lines.append(f"- Special mechanics detected: {', '.join(encounter['mechanics'])}")
        if encounter.get("sourceSlotCorrection"):
            correction = encounter["sourceSlotCorrection"]
            lines.append(
                "- Source slot correction: "
                + escape_md(
                    f"{correction.get('slotCount')} slots of {correction.get('monsterTypeName')} expanded from encounter source."
                )
            )
        lines.append("")

        for table in encounter.get("tables", []):
            lines.append(f"### {escape_md(table.get('title') or table.get('id'))}")
            lines.append("")
            lines.append(f"- Mode: `{table.get('mode')}`")
            if table.get("note"):
                lines.append(f"- Note: {escape_md(table['note'])}")
            slot_names = slot_names_from_table(table)
            if slot_names:
                lines.append(f"- Slots: {escape_md('; '.join(slot_names))}")
            preview = state_cycle_preview(table)
            if preview:
                lines.append(f"- State cycle: {escape_md('; '.join(preview))}")
            if table.get("omittedSymmetricTables"):
                omitted = [
                    f"{item.get('title') or item.get('id')} (slot rotation {item.get('slotRotation')})"
                    for item in table["omittedSymmetricTables"]
                ]
                lines.append(f"- Omitted symmetric variants: {escape_md('; '.join(omitted))}")
            if table.get("warnings"):
                lines.append(f"- Warnings: {escape_md('; '.join(table['warnings']))}")
            lines.append("")

            max_slots = max((len(row.get("cells", [])) for row in table.get("rows", [])), default=0)
            header = ["回合"] + slot_header(max_slots) + ["总计"]
            lines.append("| " + " | ".join(header) + " |")
            lines.append("| " + " | ".join(["---"] + ["---:"] * max_slots + ["---:"]) + " |")
            for row in table.get("rows", []):
                cells = row.get("cells", [])
                values = [cell.get("display", fmt(cell.get("damage"))) for cell in cells]
                values.extend(["-"] * (max_slots - len(values)))
                md_row = [f"第{row['turn']}回合"] + values + [row.get("displayTotal", fmt(row.get("totalDamage")))]
                lines.append("| " + " | ".join(escape_md(item) for item in md_row) + " |")
            lines.append("")

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    action_report = load_json(Path(args.actions))
    damage_report = load_json(Path(args.damage_details))
    localized_entries = load_localized_entries(Path(args.localized_names))
    overrides = load_json_or_empty(Path(args.overrides))
    report = build_report(
        action_report,
        damage_report,
        localized_entries,
        overrides,
        Path(args.decompile_root),
        args.turns,
        args.composition_limit,
    )
    output_json = Path(args.output_json)
    output_md = Path(args.output_md)
    output_json.parent.mkdir(parents=True, exist_ok=True)
    output_md.parent.mkdir(parents=True, exist_ok=True)
    output_json.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    write_markdown(report, output_md)

    print("monster encounter damage matrices generated")
    print(f"encounters: {report['summary']['encounterCount']}")
    print(f"tables: {report['summary']['tableCount']}")
    print(f"exactTables: {report['summary']['exactTableCount']}")
    print(f"expectedTables: {report['summary']['expectedTableCount']}")
    print(f"output: {output_json}")
    print(f"report: {output_md}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
