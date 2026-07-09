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
import sys
from fractions import Fraction
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

WATERFALL_GIANT_BASE_PRESSURE_GUN_DAMAGE = 23
WATERFALL_GIANT_PRESSURE_GUN_INCREASE = 5
WATERFALL_GIANT_INITIAL_STEAM_ERUPTION = 20
WATERFALL_GIANT_STEAM_ERUPTION_PER_MOVE = 3
KNOWLEDGE_DEMON_CURSE_TURNS = [1, 5, 9]
KNOWLEDGE_DEMON_DISINTEGRATION_DAMAGE = [6, 7, 8]
SLUMBERING_BEETLE_SLEEP_TURNS = 3
FABRICATOR_FORCED_STATES = ("FABRICATE_MOVE", "FABRICATING_STRIKE_MOVE")
FABRICATOR_SUMMONED_ATTACK_BOTS = [
    (2, "bot2", "Zapbot", 2),
    (4, "bot3", "Stabbot", 3),
]

NON_PRESSURE_MONSTERS = {
    "Architect",
    "BattleFriendV1",
    "BattleFriendV2",
    "BattleFriendV3",
}


class MatrixGenerationError(RuntimeError):
    def __init__(self, errors: list[str]):
        self.errors = list(dict.fromkeys(errors))
        super().__init__(
            "Monster damage matrix generation failed:\n"
            + "\n".join(f"- {error}" for error in self.errors)
        )


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


def act_label(encounter: dict[str, Any]) -> str:
    acts = encounter.get("acts", [])
    if not acts:
        return ""
    return ",".join(f"{act.get('actTypeName')}({act.get('actNumber')})" for act in acts)


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


def forced_state_for_turn(plan: dict[str, Any], turn: int) -> str | None:
    forced_states = plan.get("forcedStateByTurn") or {}
    return forced_states.get(turn) or forced_states.get(str(turn))


def plan_is_active_on_turn(plan: dict[str, Any], turn: int) -> bool:
    active_from = int(plan.get("activeFromTurn") or 1)
    active_through = plan.get("activeThroughTurn")
    if turn < active_from:
        return False
    if active_through is not None and turn > int(active_through):
        return False
    return True


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


def source_start_state_from_properties(
    monster_type: str,
    properties: dict[str, bool],
) -> tuple[str | None, str | None]:
    if monster_type == "KinFollower":
        if properties.get("StartsWithDance", False):
            return "POWER_DANCE_MOVE", "StartsWithDance=true"
        return "QUICK_SLASH_MOVE", "StartsWithDance=false"
    if monster_type == "Inklet":
        if properties.get("MiddleInklet", False):
            return "WHIRLWIND_MOVE", "MiddleInklet=true"
        return "JAB_MOVE", "MiddleInklet=false"
    if monster_type == "Nibbit":
        if properties.get("IsAlone", False):
            return "BUTT_MOVE", "IsAlone=true"
        if properties.get("IsFront", False):
            return "SLICE_MOVE", "IsFront=true"
        return "HISS_MOVE", "IsFront=false"
    if monster_type == "PunchConstruct":
        if properties.get("StartsWithFastPunch", False):
            return "FAST_PUNCH_MOVE", "StartsWithFastPunch=true"
        return "READY_MOVE", "StartsWithFastPunch=false"
    if monster_type == "Toadpole":
        if properties.get("IsFront", False):
            return "SPIKEN_MOVE", "IsFront=true"
        return "WHIRL_MOVE", "IsFront=false"
    if monster_type == "Chomper":
        if properties.get("ScreamFirst", False):
            return "SCREECH_MOVE", "ScreamFirst=true"
        return "CLAMP_MOVE", "ScreamFirst=false"
    if monster_type == "Axebot":
        return "HAMMER_UPPERCUT_MOVE", "StockAmount uses the default value with no override"
    return None, None


def infer_encounter_slot_start_states(source: str) -> list[dict[str, Any]]:
    variable_types: dict[str, str] = {}
    variable_properties: dict[str, dict[str, bool]] = {}
    for match in re.finditer(
        r"(?P<type>[A-Za-z_][A-Za-z0-9_]*)\s+"
        r"(?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*"
        r"\((?P=type)\)ModelDb\.Monster<(?P=type)>\(\)\.ToMutable\(\);",
        source,
    ):
        variable_types[match.group("var")] = match.group("type")
        variable_properties.setdefault(match.group("var"), {})

    for match in re.finditer(
        r"(?P<var>[A-Za-z_][A-Za-z0-9_]*)\.(?P<property>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?P<value>true|false)\s*;",
        source,
    ):
        if match.group("var") not in variable_types:
            continue
        variable_properties.setdefault(match.group("var"), {})[match.group("property")] = (
            match.group("value") == "true"
        )

    entries: list[dict[str, Any]] = []
    for match in re.finditer(
        r"\((?P<expr>(?:[A-Za-z_][A-Za-z0-9_]*|ModelDb\.Monster<(?P<direct>[A-Za-z_][A-Za-z0-9_]*)>\(\)\.ToMutable\(\))),\s*(?P<slot>null|\"[^\"]+\")\)",
        source,
    ):
        expr = match.group("expr")
        if match.group("direct"):
            monster_type = match.group("direct")
            properties: dict[str, bool] = {}
            source_label = "default ModelDb monster"
        else:
            monster_type = variable_types.get(expr)
            if not monster_type:
                continue
            properties = variable_properties.get(expr, {})
            source_label = expr

        start_state, reason = source_start_state_from_properties(monster_type, properties)
        slot_text = match.group("slot")
        entries.append(
            {
                "monsterTypeName": monster_type,
                "slotName": None if slot_text == "null" else slot_text.strip('"'),
                "sourceStartStateId": start_state,
                "sourceStartReason": (
                    f"{source_label}: {reason}" if reason is not None else None
                ),
            }
        )
    return entries


def apply_source_slot_start_states(
    encounter: dict[str, Any],
    source: str,
) -> dict[str, Any]:
    entries = infer_encounter_slot_start_states(source)
    if not entries:
        return encounter

    enhanced = dict(encounter)
    slots = [dict(slot) for slot in enhanced.get("monsterSlots", [])]
    applied: list[dict[str, Any]] = []
    for index, entry in enumerate(entries):
        if index >= len(slots):
            continue
        slot = slots[index]
        if entry["monsterTypeName"] not in slot_possible_types(slot):
            continue
        if not entry.get("sourceStartStateId"):
            continue
        slot["sourceStartStateId"] = entry["sourceStartStateId"]
        slot["sourceStartReason"] = entry["sourceStartReason"]
        slots[index] = slot
        applied.append(
            {
                "position": slot.get("position", index + 1),
                "monsterTypeName": entry["monsterTypeName"],
                "startStateId": entry["sourceStartStateId"],
                "reason": entry["sourceStartReason"],
            }
        )

    if applied:
        enhanced["monsterSlots"] = slots
        enhanced["sourceSlotStartCorrection"] = {
            "reason": "Slot start states were inferred from encounter-specific mutable monster property assignments.",
            "slots": applied,
        }
    return enhanced


def enhanced_encounter_from_source(
    encounter: dict[str, Any],
    decompile_root: Path,
) -> dict[str, Any]:
    source = read_source_for_full_type(decompile_root, encounter.get("fullTypeName"))
    slots = parse_declared_slots(source)
    enhanced = encounter
    if slots and "foreach (string slot in Slots)" in source and "list.Add((" in source:
        monster_types = sorted(set(re.findall(r"ModelDb\.Monster<(?P<type>[A-Za-z0-9_]+)>\(\)", source)))
        if len(monster_types) == 1:
            monster_type = monster_types[0]
            enhanced = dict(enhanced)
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
    return apply_source_slot_start_states(enhanced, source)


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
        match = re.search(
            rf"{index}\s*=>\s*new MonsterMoveStateMachine\([^,]+,\s*(?P<var>[A-Za-z_][A-Za-z0-9_]*)",
            source,
        )
        if not match:
            match = re.search(rf"{index}\s*=>\s*(?P<var>[A-Za-z_][A-Za-z0-9_]*)", source)
        if match and state_vars.get(match.group("var")):
            order.append(state_vars[match.group("var")])
    match = re.search(
        r"_\s*=>\s*new MonsterMoveStateMachine\([^,]+,\s*(?P<var>[A-Za-z_][A-Za-z0-9_]*)",
        source,
    )
    if not match:
        match = re.search(r"_\s*=>\s*(?P<var>[A-Za-z_][A-Za-z0-9_]*)", source)
    if match and state_vars.get(match.group("var")):
        order.append(state_vars[match.group("var")])
    return order


def split_csharp_args(args: str) -> list[str]:
    result: list[str] = []
    start = 0
    depth = 0
    in_string = False
    escape = False
    for index, char in enumerate(args):
        if in_string:
            if escape:
                escape = False
            elif char == "\\":
                escape = True
            elif char == '"':
                in_string = False
            continue
        if char == '"':
            in_string = True
            continue
        if char in "([{":
            depth += 1
            continue
        if char in ")]}":
            depth = max(0, depth - 1)
            continue
        if char == "," and depth == 0:
            result.append(args[start:index].strip())
            start = index + 1
    result.append(args[start:].strip())
    return [item for item in result if item]


def parse_state_variables(source: str) -> dict[str, str]:
    state_vars: dict[str, str] = {}
    for match in re.finditer(
        r"(?:(?:MoveState|MonsterState)\s+)?(?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new MoveState\(\"(?P<state>[^\"]+)\"",
        source,
    ):
        state_vars[match.group("var")] = match.group("state")
    return state_vars


def parse_weight_fraction(expression: str | None) -> Fraction:
    if not expression:
        return Fraction(1, 1)
    text = expression.strip()
    if "=>" in text:
        text = text.split("=>", 1)[1].strip()
    ternary = re.search(
        r"(?P<condition>[^?]+)\?\s*(?P<true>[^:]+)\s*:\s*(?P<false>.+)",
        text,
    )
    if ternary:
        condition = ternary.group("condition")
        # TwoTailedRat uses CanSummon-dependent weights. For matrix planning,
        # assume the branch is evaluated before the summon has happened.
        text = ternary.group("false" if "!CanSummon()" in condition else "true")

    cleaned = (
        text.replace("f", "")
        .replace("F", "")
        .replace("m", "")
        .replace("M", "")
        .replace("(", "")
        .replace(")", "")
        .replace(" ", "")
    )
    if "/" in cleaned:
        numerator, denominator = cleaned.split("/", 1)
        return Fraction(numerator) / Fraction(denominator)
    try:
        return Fraction(cleaned)
    except ValueError:
        return Fraction(1, 1)


def parse_branch_add_args(args: list[str], state_vars: dict[str, str]) -> dict[str, Any] | None:
    if not args:
        return None
    state_id = state_vars.get(args[0])
    if not state_id:
        return None

    repeat_type = "CanRepeatForever"
    max_times: int | None = None
    cooldown = 0
    weight_expr: str | None = None
    numeric_args: list[int] = []
    for arg in args[1:]:
        repeat_match = re.search(r"MoveRepeatType\.(?P<type>[A-Za-z0-9_]+)", arg)
        if repeat_match:
            repeat_type = repeat_match.group("type")
            continue
        if "=>" in arg or re.search(r"\d+(?:\.\d+)?f", arg):
            weight_expr = arg
            continue
        int_match = re.fullmatch(r"\d+", arg.strip())
        if int_match:
            numeric_args.append(int(arg.strip()))

    if repeat_type == "CanRepeatForever":
        if len(numeric_args) >= 1:
            repeat_type = "CanRepeatXTimes"
            max_times = numeric_args[0]
    else:
        if numeric_args:
            cooldown = numeric_args[0]

    return {
        "stateId": state_id,
        "repeatType": repeat_type,
        "maxTimes": max_times,
        "cooldown": cooldown,
        "weight": parse_weight_fraction(weight_expr),
    }


def weighted_sequence(branches: list[dict[str, Any]]) -> list[str]:
    weights = [branch["weight"] for branch in branches]
    positive = [weight for weight in weights if weight > 0]
    if not positive:
        return [branch["stateId"] for branch in branches]
    denominator_lcm = 1
    for weight in positive:
        denominator_lcm = math.lcm(denominator_lcm, weight.denominator)
    counts = [
        int(weight * denominator_lcm) if weight > 0 else 0
        for weight in weights
    ]
    count_gcd = 0
    for count in counts:
        if count > 0:
            count_gcd = count if count_gcd == 0 else math.gcd(count_gcd, count)
    if count_gcd > 1:
        counts = [count // count_gcd for count in counts]

    sequence: list[str] = []
    for branch, count in zip(branches, counts):
        sequence.extend([branch["stateId"]] * count)
    return sequence or [branch["stateId"] for branch in branches]


def parse_random_branch_profiles(source: str) -> dict[str, Any]:
    state_vars = parse_state_variables(source)
    branch_vars: dict[str, dict[str, Any]] = {}
    constructor_calls: list[tuple[str, int]] = []
    for match in re.finditer(
        r"RandomBranchState\s+(?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*=.*?new RandomBranchState\(\"(?P<id>[^\"]+)\"\)",
        source,
    ):
        branch_vars[match.group("var")] = {
            "id": match.group("id"),
            "variableName": match.group("var"),
            "branches": [],
        }
        constructor_calls.append(
            (
                match.group("var"),
                match.start() + match.group(0).find("new RandomBranchState"),
            )
        )

    for match in re.finditer(
        r"(?P<branch>[A-Za-z_][A-Za-z0-9_]*)\.AddBranch\((?P<args>[^;\r\n]+)\);",
        source,
    ):
        branch = branch_vars.get(match.group("branch"))
        if not branch:
            continue
        parsed = parse_branch_add_args(split_csharp_args(match.group("args")), state_vars)
        if parsed:
            branch["branches"].append(parsed)

    by_state: dict[str, dict[str, Any]] = {}
    for match in re.finditer(
        r"(?P<state>[A-Za-z_][A-Za-z0-9_]*)\.FollowUpState\s*=\s*(?P<branch>[A-Za-z_][A-Za-z0-9_]*)",
        source,
    ):
        state_id = state_vars.get(match.group("state"))
        branch = branch_vars.get(match.group("branch"))
        if state_id and branch:
            by_state[state_id] = branch

    for branch_var, start_index in constructor_calls:
        line_start = source.rfind("\n", 0, start_index)
        prefix = source[line_start + 1 : start_index]
        branch = branch_vars.get(branch_var)
        if not branch:
            continue
        for match in re.finditer(r"(?P<state>[A-Za-z_][A-Za-z0-9_]*)\.FollowUpState\s*=", prefix):
            state_id = state_vars.get(match.group("state"))
            if state_id:
                by_state[state_id] = branch

    initial: dict[str, Any] | None = None
    match = re.search(
        r"return\s+new\s+MonsterMoveStateMachine\([^,]+,\s*(?P<branch>[A-Za-z_][A-Za-z0-9_]*)\)",
        source,
    )
    if match:
        initial = branch_vars.get(match.group("branch"))

    for branch in branch_vars.values():
        branch["sequence"] = weighted_sequence(branch["branches"])

    return {
        "branches": branch_vars,
        "byState": by_state,
        "initial": initial,
    }


def branch_allowed(branch: dict[str, Any], state_log: list[str]) -> bool:
    state_id = branch["stateId"]
    repeat_type = branch.get("repeatType")
    allowed = True
    if repeat_type == "UseOnlyOnce":
        allowed = state_id not in state_log
    elif repeat_type == "CannotRepeat":
        allowed = not state_log or state_log[-1] != state_id
    elif repeat_type == "CanRepeatXTimes":
        max_times = int(branch.get("maxTimes") or 1)
        allowed = len(state_log) < max_times or state_log[-max_times:] != [state_id] * max_times
    cooldown = int(branch.get("cooldown") or 0)
    if cooldown > 0 and state_id in state_log[-cooldown:]:
        return False
    return allowed


def choose_representative_branch_target(
    branch: dict[str, Any],
    state_log: list[str],
    slot_position: int,
    branch_counts: dict[str, int],
) -> str | None:
    sequence = branch.get("sequence") or [item["stateId"] for item in branch.get("branches", [])]
    branches_by_state = {item["stateId"]: item for item in branch.get("branches", [])}
    if not sequence:
        return None
    branch_key = branch.get("variableName") or branch.get("id") or "branch"
    count = branch_counts.get(branch_key, 0)
    offset = max(0, slot_position - 1)
    for attempt in range(len(sequence)):
        state_id = sequence[(count + offset + attempt) % len(sequence)]
        branch_item = branches_by_state.get(state_id)
        if branch_item and branch_allowed(branch_item, state_log):
            branch_counts[branch_key] = count + 1
            return state_id
    for branch_item in branch.get("branches", []):
        if branch_allowed(branch_item, state_log):
            branch_counts[branch_key] = count + 1
            return branch_item["stateId"]
    branch_counts[branch_key] = count + 1
    return sequence[(count + offset) % len(sequence)]


def scan_special_mechanics(source: str) -> list[str]:
    found: list[str] = []
    for key, tokens in SPECIAL_TOKENS.items():
        if any(token in source for token in tokens):
            found.append(key)
    return found


def composition_scenarios(slots: list[dict[str, Any]]) -> list[tuple[str, ...]]:
    choices = [slot_possible_types(slot) or ["<missing>"] for slot in slots]
    return [tuple(types) for types in itertools.product(*choices)]


def ruby_raiders_composition_scenarios(slots: list[dict[str, Any]]) -> list[tuple[str, ...]]:
    if len(slots) != 3:
        return composition_scenarios(slots)
    choices = slot_possible_types(slots[0])
    if not choices or any(slot_possible_types(slot) != choices for slot in slots):
        return composition_scenarios(slots)
    return [tuple(types) for types in itertools.combinations(choices, 3)]


def excluded_encounter_reason(encounter: dict[str, Any]) -> str | None:
    slots = encounter.get("monsterSlots", [])
    if not slots:
        return "No monster slots were parsed for this encounter."

    possible_types = [
        monster_type
        for slot in slots
        for monster_type in slot_possible_types(slot)
    ]
    if possible_types and all(monster_type in NON_PRESSURE_MONSTERS for monster_type in possible_types):
        return "Encounter uses non-pressure event/helper monsters with no combat damage matrix."

    return None


def build_slot_plans(
    encounter: dict[str, Any],
    composition: tuple[str, ...],
    monster_catalog: dict[str, dict[str, Any]],
    slot_initials_by_monster: dict[str, dict[str, str]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    override: dict[str, Any] | None = None,
    corpse_slug_offset: int | None = None,
    corpse_slug_order: list[str] | None = None,
) -> tuple[list[dict[str, Any]], list[str]]:
    override = override or {}
    warnings: list[str] = []
    start_overrides = override.get("slotStartStateIds", {})
    active_from_overrides = override.get("slotActiveFromTurn", {})
    active_overrides = override.get("slotActiveThroughTurn", {})
    forced_state_overrides = override.get("slotForcedStateByTurn", {})
    plans: list[dict[str, Any]] = []

    for index, slot in enumerate(encounter.get("monsterSlots", [])):
        monster_type = composition[index] if index < len(composition) else "<missing>"
        position = int(slot.get("position", index + 1))
        slot_name = slot.get("slotName") or f"slot{position}"
        monster = monster_catalog.get(monster_type, {})
        moves = monster.get("moves", [])
        source_start_reason = slot.get("sourceStartReason")
        start_state = (
            start_overrides.get(str(position))
            or start_overrides.get(slot_name)
        )
        if not start_state and corpse_slug_offset is not None and monster_type == "CorpseSlug" and corpse_slug_order:
            start_state = corpse_slug_order[(corpse_slug_offset + index) % len(corpse_slug_order)]
        if not start_state:
            start_state = slot.get("sourceStartStateId")
        if not start_state:
            start_state = slot_initials_by_monster.get(monster_type, {}).get(slot_name)
        if not start_state and monster_type == "Exoskeleton" and slot_name == "fourth":
            start_state = "SKITTER_MOVE"
            source_start_reason = "manual: fourth Exoskeleton defaults to first random branch SKITTER_MOVE"
        if not start_state:
            start_state = monster.get("initialStateId")
        if not start_state:
            initial_branch = random_branches_by_monster.get(monster_type, {}).get("initial")
            if initial_branch:
                start_state = choose_representative_branch_target(
                    initial_branch,
                    [],
                    position,
                    {},
                )
        if not start_state and len(moves) == 1:
            start_state = moves[0].get("stateId")
        if not start_state:
            warnings.append(f"No deterministic start state for {monster_type} in slot {position}.")

        active_from = active_from_overrides.get(str(position), active_from_overrides.get(slot_name))
        active_through = active_overrides.get(str(position), active_overrides.get(slot_name))
        forced_states = (
            forced_state_overrides.get(str(position))
            or forced_state_overrides.get(slot_name)
            or {}
        )
        plans.append(
            {
                "position": position,
                "slotName": slot_name,
                "monsterTypeName": monster_type,
                "monsterName": monster.get("name"),
                "startStateId": start_state,
                "sourceStartReason": source_start_reason,
                "activeFromTurn": int(active_from) if active_from is not None else 1,
                "activeThroughTurn": int(active_through) if active_through is not None else None,
                "forcedStateByTurn": forced_states,
            }
        )
    return plans, warnings


def choose_scripted_conditional_followup(
    monster_type: str,
    state_id: str,
    followups: list[str],
    state_log: list[str],
) -> tuple[str | None, str | None]:
    if monster_type == "LagavulinMatriarch" and state_id == "SLEEP_MOVE":
        sleep_count = state_log.count("SLEEP_MOVE")
        target = "SLEEP_MOVE" if sleep_count < 3 else "SLASH_MOVE"
        if target in followups:
            return target, "Lagavulin Matriarch assumes no player wake-up damage and sleeps through AsleepPower(3)."
    if monster_type == "KnowledgeDemon" and state_id == "PONDER_MOVE":
        curse_count = state_log.count("CURSE_OF_KNOWLEDGE_MOVE")
        target = "CURSE_OF_KNOWLEDGE_MOVE" if curse_count < 3 else "SLAP_MOVE"
        if target in followups:
            return target, "Knowledge Demon follows CurseOfKnowledgeCounter: first two Ponder follow-ups return to Curse, the third and later go to Slap."
    if monster_type == "BowlbugRock" and state_id == "HEADBUTT_MOVE":
        if "DIZZY_MOVE" in followups:
            return "DIZZY_MOVE", "BowlbugRock assumes the player blocks enough Headbutt damage to trigger OffBalance, so Headbutt is followed by Dizzy."
    if monster_type == "SlumberingBeetle" and state_id == "SNORE_MOVE":
        snore_count = state_log.count("SNORE_MOVE")
        target = "SNORE_MOVE" if snore_count < SLUMBERING_BEETLE_SLEEP_TURNS else "ROLL_OUT_MOVE"
        if target in followups:
            return target, "Slumbering Beetle assumes the player does not wake it early and lets SlumberPower(3) expire."
    if monster_type == "Ovicopter" and state_id == "TENDERIZER_MOVE":
        tenderizer_count = state_log.count("TENDERIZER_MOVE")
        target = "LAY_EGGS_MOVE" if tenderizer_count == 1 else "NUTRITIONAL_PASTE_MOVE"
        if target in followups:
            return target, "Ovicopter assumes two eggs are cleared on player turn 2, allowing the first post-Tenderizer Lay Eggs; later checks use Nutritional Paste."
    return None, None


def exact_state_path_errors(
    plan: dict[str, Any],
    monster_catalog: dict[str, dict[str, Any]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    turn_count: int,
) -> list[str]:
    errors: list[str] = []
    start_state = plan.get("startStateId")
    if not start_state:
        return [
            f"slot {plan['position']} {plan['monsterTypeName']} has no deterministic start state."
        ]

    monster = monster_catalog.get(plan["monsterTypeName"], {})
    moves_by_state = {move["stateId"]: move for move in monster.get("moves", [])}
    if not moves_by_state:
        return [
            f"slot {plan['position']} {plan['monsterTypeName']} has no parsed moves."
        ]

    state_id = start_state
    state_log: list[str] = []
    branch_counts: dict[str, int] = {}
    random_branches = random_branches_by_monster.get(plan["monsterTypeName"], {})
    for turn in range(1, turn_count + 1):
        if not plan_is_active_on_turn(plan, turn):
            continue
        forced_state = forced_state_for_turn(plan, turn)
        if forced_state:
            state_id = forced_state
        state = moves_by_state.get(state_id)
        if not state:
            errors.append(
                f"slot {plan['position']} {plan['monsterTypeName']} references missing state {state_id!r} on turn {turn}."
            )
            break
        state_log.append(state_id)
        if turn == turn_count:
            break
        if not plan_is_active_on_turn(plan, turn + 1):
            continue
        next_forced_state = forced_state_for_turn(plan, turn + 1)
        if next_forced_state:
            state_id = next_forced_state
            continue
        followups = [
            followup
            for followup in state.get("followUpStateIds", [])
            if followup in moves_by_state
        ]
        if len(followups) > 1:
            next_state, _ = choose_scripted_conditional_followup(
                plan["monsterTypeName"],
                state_id,
                followups,
                state_log,
            )
            if next_state:
                state_id = next_state
                continue
            branch = random_branches.get("byState", {}).get(state_id)
            next_state = choose_representative_branch_target(
                branch,
                state_log,
                int(plan["position"]),
                branch_counts,
            ) if branch else None
            if next_state:
                state_id = next_state
                continue
            errors.append(
                f"slot {plan['position']} {plan['monsterTypeName']} state {state_id!r} has non-deterministic follow-ups {followups} after turn {turn}."
            )
            break
        if not followups:
            if len(moves_by_state) > 1:
                errors.append(
                    f"slot {plan['position']} {plan['monsterTypeName']} state {state_id!r} has no follow-up after turn {turn}."
                )
            break
        state_id = followups[0]

    return errors


def assert_exact_slot_plans(
    encounter_type: str,
    table_id: str,
    slot_plans: list[dict[str, Any]],
    plan_warnings: list[str],
    monster_catalog: dict[str, dict[str, Any]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    turn_count: int,
) -> None:
    errors = [
        f"{encounter_type}/{table_id}: {warning}"
        for warning in plan_warnings
        if not warning.startswith("No deterministic start state ")
    ]
    for plan in slot_plans:
        errors.extend(
            f"{encounter_type}/{table_id}: {error}"
            for error in exact_state_path_errors(plan, monster_catalog, random_branches_by_monster, turn_count)
        )
    if errors:
        raise MatrixGenerationError(errors)


def simulate_exact_table(
    table_id: str,
    title: str,
    encounter: dict[str, Any],
    slot_plans: list[dict[str, Any]],
    monster_catalog: dict[str, dict[str, Any]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    turn_count: int,
    note: str | None = None,
) -> dict[str, Any]:
    state_ids = {plan["position"]: plan.get("startStateId") for plan in slot_plans}
    strengths = {plan["position"]: 0.0 for plan in slot_plans}
    state_logs: dict[int, list[str]] = {plan["position"]: [] for plan in slot_plans}
    branch_counts: dict[int, dict[str, int]] = {plan["position"]: {} for plan in slot_plans}
    pressure_gun_damage = {
        plan["position"]: WATERFALL_GIANT_BASE_PRESSURE_GUN_DAMAGE
        for plan in slot_plans
        if plan["monsterTypeName"] == "WaterfallGiant"
    }
    vulnerable = 0.0
    rows: list[dict[str, Any]] = []
    warnings: list[str] = []
    used_random_branch = False
    used_scripted_conditional = False
    resolved_dynamic_damage = False

    for turn in range(1, turn_count + 1):
        cells: list[dict[str, Any]] = []
        total = 0.0
        unknown: list[str] = []
        for plan in slot_plans:
            position = plan["position"]
            if not plan_is_active_on_turn(plan, turn):
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
            forced_state = forced_state_for_turn(plan, turn)
            if forced_state:
                state_ids[position] = forced_state
            state_id = state_ids.get(position)
            state = moves_by_state.get(state_id)
            if not state:
                raise MatrixGenerationError(
                    [
                        f"{encounter.get('typeName')}/{table_id}: missing state {state_id or '<unknown>'} for {plan['monsterTypeName']} slot {position} on turn {turn}."
                    ]
                )

            state_logs[position].append(state_id)
            damage, state_unknown = state_adjusted_damage(state, strengths[position], vulnerable)
            if (
                state_unknown == ["CurrentPressureGunDamage"]
                and plan["monsterTypeName"] == "WaterfallGiant"
                and state_id == "PRESSURE_GUN_MOVE"
            ):
                damage = pressure_gun_damage[position] + strengths[position]
                if vulnerable > 0:
                    damage *= VULNERABLE_MULTIPLIER
                state_unknown = []
                resolved_dynamic_damage = True
            if state_unknown:
                raise MatrixGenerationError(
                    [
                        f"{encounter.get('typeName')}/{table_id}: unresolved damage expressions {sorted(set(state_unknown))} for {plan['monsterTypeName']} slot {position} state {state_id!r} on turn {turn}."
                    ]
                )
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
            if plan["monsterTypeName"] == "WaterfallGiant" and state_id == "PRESSURE_GUN_MOVE":
                pressure_gun_damage[position] += WATERFALL_GIANT_PRESSURE_GUN_INCREASE
            if turn < turn_count:
                if not plan_is_active_on_turn(plan, turn + 1):
                    continue
                next_forced_state = forced_state_for_turn(plan, turn + 1)
                if next_forced_state:
                    state_ids[position] = next_forced_state
                    continue
                followups = [
                    followup
                    for followup in state.get("followUpStateIds", [])
                    if followup in moves_by_state
                ]
                if len(followups) > 1:
                    next_state, scripted_reason = choose_scripted_conditional_followup(
                        plan["monsterTypeName"],
                        state_id,
                        followups,
                        state_logs[position],
                    )
                    if next_state:
                        state_ids[position] = next_state
                        if scripted_reason:
                            warnings.append(scripted_reason)
                        used_scripted_conditional = True
                        continue
                    branch = random_branches_by_monster.get(plan["monsterTypeName"], {}).get("byState", {}).get(state_id)
                    next_state = choose_representative_branch_target(
                        branch,
                        state_logs[position],
                        position,
                        branch_counts[position],
                    ) if branch else None
                    if not next_state:
                        raise MatrixGenerationError(
                            [
                                f"{encounter.get('typeName')}/{table_id}: non-deterministic follow-ups {followups} for {plan['monsterTypeName']} slot {position} state {state_id!r} after turn {turn}."
                            ]
                        )
                    state_ids[position] = next_state
                    used_random_branch = True
                    continue
                if not followups and len(moves_by_state) > 1:
                    raise MatrixGenerationError(
                        [
                            f"{encounter.get('typeName')}/{table_id}: no follow-up for {plan['monsterTypeName']} slot {position} state {state_id!r} after turn {turn}."
                        ]
                    )
                if followups:
                    state_ids[position] = followups[0]

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
        "warnings": sorted(
            set(
                warnings
                + (
                    ["RandomBranchState resolved by deterministic weighted representative sequence with slot-position offset."]
                    if used_random_branch
                    else []
                )
                + (
                    ["Scripted conditional branch assumptions resolved source ConditionalBranchState paths."]
                    if used_scripted_conditional
                    else []
                )
                + (
                    ["Waterfall Giant Pressure Gun damage resolved as BasePressureGunDamage 23 plus 5 after each use."]
                    if resolved_dynamic_damage
                    else []
                )
            )
        ),
    }


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
            if reference_row.get("extraDamageCells", []) != candidate_row.get("extraDamageCells", []):
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


def knowledge_demon_curse_choices(first_choice_left: bool, turn_count: int) -> tuple[list[str], list[int]]:
    disintegration_amount = 0
    choices_by_turn: list[str] = []
    damage_by_turn: list[int] = []
    for turn in range(1, turn_count + 1):
        if turn in KNOWLEDGE_DEMON_CURSE_TURNS:
            choice_index = KNOWLEDGE_DEMON_CURSE_TURNS.index(turn)
            if choice_index == 0 and not first_choice_left:
                choices_by_turn.append("右: MindRot")
            else:
                disintegration_amount += KNOWLEDGE_DEMON_DISINTEGRATION_DAMAGE[choice_index]
                choices_by_turn.append(
                    f"左: Disintegration +{KNOWLEDGE_DEMON_DISINTEGRATION_DAMAGE[choice_index]}"
                )
        else:
            choices_by_turn.append("")
        damage_by_turn.append(disintegration_amount)
    return choices_by_turn, damage_by_turn


def apply_knowledge_demon_curse_damage(
    table: dict[str, Any],
    first_choice_left: bool,
    turn_count: int,
) -> dict[str, Any]:
    choices_by_turn, damage_by_turn = knowledge_demon_curse_choices(first_choice_left, turn_count)
    choice_events = [
        {
            "turn": turn,
            "choice": choice,
            "disintegrationEndTurnDamage": damage_by_turn[turn - 1],
        }
        for turn, choice in enumerate(choices_by_turn, start=1)
        if choice
    ]
    table = dict(table)
    table["extraDamageColumns"] = [
        {
            "id": "knowledgeDemonDisintegrationEndTurn",
            "label": "Disintegration回合结束伤害",
        }
    ]
    table["choiceEvents"] = choice_events
    note = (
        "CURSE_OF_KNOWLEDGE_MOVE 的二选一会创建临时选择牌并立即执行 OnChosen，不会把牌加入卡组。"
        "本表将左侧 DisintegrationPower 的玩家回合结束扣血并入总计；右侧 debuff 不直接造成伤害。"
    )
    table["note"] = (table.get("note") + " " if table.get("note") else "") + note
    rows: list[dict[str, Any]] = []
    for row in table.get("rows", []):
        turn = int(row.get("turn", 0))
        extra_damage = damage_by_turn[turn - 1] if 1 <= turn <= len(damage_by_turn) else 0
        monster_total = float(row.get("totalDamage") or 0.0)
        updated = dict(row)
        updated["monsterActionDamage"] = row.get("totalDamage")
        updated["displayMonsterActionDamage"] = row.get("displayTotal")
        updated["extraDamageCells"] = [
            {
                "id": "knowledgeDemonDisintegrationEndTurn",
                "label": "Disintegration回合结束伤害",
                "damage": extra_damage,
                "display": fmt(extra_damage),
                "choice": choices_by_turn[turn - 1] if 1 <= turn <= len(choices_by_turn) else "",
            }
        ]
        total = round3(monster_total + extra_damage)
        updated["totalDamage"] = total
        updated["displayTotal"] = fmt(total)
        rows.append(updated)
    table["rows"] = rows
    table["warnings"] = sorted(
        set(
            table.get("warnings", [])
            + [
                "Knowledge Demon Disintegration end-turn damage is included as an extra damage column and in total damage."
            ]
        )
    )
    return table


def waterfall_giant_steam_eruption_table(turn_count: int) -> dict[str, Any]:
    turns = list(range(0, turn_count + 1))
    damages = [
        None if turn == 0 else WATERFALL_GIANT_INITIAL_STEAM_ERUPTION
        + WATERFALL_GIANT_STEAM_ERUPTION_PER_MOVE * (turn - 1)
        for turn in turns
    ]
    return {
        "id": "steam-eruption-on-death",
        "title": "死亡后自爆伤害",
        "mode": "supplemental",
        "note": (
            "瀑布巨兽死亡时若仍有 SteamEruptionPower，会先进入 ABOUT_TO_BLOW_MOVE，"
            "下一次行动使用 EXPLODE_MOVE；自爆伤害等于死亡时已有的 SteamEruptionPower。"
            "第0回合表示尚未使用 PRESSURIZE_MOVE，通常没有该自爆计数。"
        ),
        "turns": turns,
        "rows": [
            {
                "label": "自爆伤害",
                "values": damages,
                "displayValues": ["-" if value is None else fmt(value) for value in damages],
            }
        ],
    }


def supplemental_tables_for_encounter(encounter_type: str, turn_count: int) -> list[dict[str, Any]]:
    if encounter_type == "WaterfallGiantBoss":
        return [waterfall_giant_steam_eruption_table(turn_count)]
    return []


def build_exact_table_or_collect_errors(
    errors: list[str],
    encounter_type: str,
    table_id: str,
    title: str,
    action_encounter: dict[str, Any],
    composition: tuple[str, ...],
    monster_catalog: dict[str, dict[str, Any]],
    slot_initials_by_monster: dict[str, dict[str, str]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    turn_count: int,
    override: dict[str, Any] | None = None,
    note: str | None = None,
) -> dict[str, Any] | None:
    slot_plans, plan_warnings = build_slot_plans(
        action_encounter,
        composition,
        monster_catalog,
        slot_initials_by_monster,
        random_branches_by_monster,
        override,
    )
    try:
        assert_exact_slot_plans(
            encounter_type,
            table_id,
            slot_plans,
            plan_warnings,
            monster_catalog,
            random_branches_by_monster,
            turn_count,
        )
        table = simulate_exact_table(
            table_id,
            title,
            action_encounter,
            slot_plans,
            monster_catalog,
            random_branches_by_monster,
            turn_count,
            note,
        )
        table["warnings"].extend(plan_warnings)
        return table
    except MatrixGenerationError as error:
        errors.extend(error.errors)
        return None


def start_state_for_monster(monster: dict[str, Any], start_state: str | None = None) -> str | None:
    if start_state:
        return start_state
    if monster.get("initialStateId"):
        return monster["initialStateId"]
    moves = monster.get("moves", [])
    if len(moves) == 1:
        return moves[0].get("stateId")
    return None


def manual_slot_plan(
    monster_catalog: dict[str, dict[str, Any]],
    position: int,
    slot_name: str,
    monster_type: str,
    start_state: str | None = None,
    active_from: int = 1,
    active_through: int | None = None,
    forced_states: dict[int, str] | None = None,
    source_reason: str | None = None,
) -> dict[str, Any]:
    monster = monster_catalog.get(monster_type, {})
    return {
        "position": position,
        "slotName": slot_name,
        "monsterTypeName": monster_type,
        "monsterName": monster.get("name"),
        "startStateId": start_state_for_monster(monster, start_state),
        "sourceStartReason": source_reason,
        "activeFromTurn": active_from,
        "activeThroughTurn": active_through,
        "forcedStateByTurn": forced_states or {},
    }


def build_manual_table_or_collect_errors(
    errors: list[str],
    encounter_type: str,
    table_id: str,
    title: str,
    action_encounter: dict[str, Any],
    slot_plans: list[dict[str, Any]],
    monster_catalog: dict[str, dict[str, Any]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    turn_count: int,
    note: str,
) -> dict[str, Any] | None:
    try:
        assert_exact_slot_plans(
            encounter_type,
            table_id,
            slot_plans,
            [],
            monster_catalog,
            random_branches_by_monster,
            turn_count,
        )
        return simulate_exact_table(
            table_id,
            title,
            action_encounter,
            slot_plans,
            monster_catalog,
            random_branches_by_monster,
            turn_count,
            note,
        )
    except MatrixGenerationError as error:
        errors.extend(error.errors)
        return None


def queen_forced_states(kill_turn: int, turn_count: int) -> dict[int, str]:
    states: dict[int, str] = {}
    for turn in range(1, turn_count + 1):
        if turn == 1:
            states[turn] = "PUPPET_STRINGS_MOVE"
        elif turn == 2:
            states[turn] = "YOU_ARE_MINE_MOVE"
        elif turn < kill_turn:
            states[turn] = "BURN_BRIGHT_FOR_ME_MOVE"
        elif turn == kill_turn:
            states[turn] = "ENRAGE_MOVE"
        else:
            cycle = ["OFF_WITH_YOUR_HEAD_MOVE", "EXECUTION_MOVE", "ENRAGE_MOVE"]
            states[turn] = cycle[(turn - kill_turn - 1) % len(cycle)]
    return states


def frog_knight_forced_states(half_turn: int, turn_count: int) -> dict[int, str]:
    states: dict[int, str] = {}
    state = "TONGUE_LASH"
    has_beetle_charged = False
    for turn in range(1, turn_count + 1):
        states[turn] = state
        if state == "TONGUE_LASH":
            state = "STRIKE_DOWN_EVIL"
        elif state == "STRIKE_DOWN_EVIL":
            state = "FOR_THE_QUEEN"
        elif state == "FOR_THE_QUEEN":
            if not has_beetle_charged and half_turn <= turn:
                state = "BEETLE_CHARGE"
                has_beetle_charged = True
            else:
                state = "TONGUE_LASH"
        elif state == "BEETLE_CHARGE":
            state = "TONGUE_LASH"
    return states


def fabricator_forced_states(turn_count: int) -> dict[int, str]:
    return {
        turn: FABRICATOR_FORCED_STATES[(turn - 1) % len(FABRICATOR_FORCED_STATES)]
        for turn in range(1, turn_count + 1)
    }


def build_queen_tables(
    action_encounter: dict[str, Any],
    compositions: list[tuple[str, ...]],
    monster_catalog: dict[str, dict[str, Any]],
    slot_initials_by_monster: dict[str, dict[str, str]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    turn_count: int,
) -> list[dict[str, Any]]:
    errors: list[str] = []
    tables: list[dict[str, Any]] = []
    composition = compositions[0] if compositions else tuple()
    for kill_turn in (4, 5, 6, 7):
        slot_plans, plan_warnings = build_slot_plans(
            action_encounter,
            composition,
            monster_catalog,
            slot_initials_by_monster,
            random_branches_by_monster,
        )
        for plan in slot_plans:
            if plan["monsterTypeName"] == "TorchHeadAmalgam":
                plan["activeThroughTurn"] = kill_turn - 1
            elif plan["monsterTypeName"] == "Queen":
                plan["forcedStateByTurn"] = queen_forced_states(kill_turn, turn_count)
        table_id = f"amalgam-killed-turn-{kill_turn}"
        note = (
            f"假设玩家第{kill_turn}回合行动中击杀 TorchHeadAmalgam；"
            f"因此 Amalgam 从第{kill_turn}回合敌方行动开始不再行动。"
            "Queen 源码在 Amalgam 死亡且下一招为 Burn Bright 时会 SetMoveImmediate(ENRAGE_MOVE)，"
            "之后进入 Off With Your Head -> Execution -> Enrage 循环。"
        )
        try:
            assert_exact_slot_plans(
                action_encounter.get("typeName", "<unknown>"),
                table_id,
                slot_plans,
                plan_warnings,
                monster_catalog,
                random_branches_by_monster,
                turn_count,
            )
            table = simulate_exact_table(
                table_id,
                f"第{kill_turn}回合击杀 TorchHeadAmalgam",
                action_encounter,
                slot_plans,
                monster_catalog,
                random_branches_by_monster,
                turn_count,
                note,
            )
            table["warnings"].extend(plan_warnings)
            tables.append(table)
        except MatrixGenerationError as error:
            errors.extend(error.errors)
    if errors:
        raise MatrixGenerationError(errors)
    return tables


def build_frog_knight_tables(
    action_encounter: dict[str, Any],
    compositions: list[tuple[str, ...]],
    monster_catalog: dict[str, dict[str, Any]],
    slot_initials_by_monster: dict[str, dict[str, str]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    turn_count: int,
) -> list[dict[str, Any]]:
    errors: list[str] = []
    tables: list[dict[str, Any]] = []
    composition = compositions[0] if compositions else tuple()
    for half_turn in (3, 4, 5):
        slot_plans, plan_warnings = build_slot_plans(
            action_encounter,
            composition,
            monster_catalog,
            slot_initials_by_monster,
            random_branches_by_monster,
        )
        for plan in slot_plans:
            if plan["monsterTypeName"] == "FrogKnight":
                plan["forcedStateByTurn"] = frog_knight_forced_states(half_turn, turn_count)
        table_id = f"half-hp-turn-{half_turn}"
        note = (
            f"假设玩家第{half_turn}回合行动中将 FrogKnight 打到半血以下。"
            "源码只在 FOR_THE_QUEEN 后选择下一招；如果半血发生在该次 FOR_THE_QUEEN 之后，"
            "会等下一轮 FOR_THE_QUEEN 才转入 BEETLE_CHARGE。"
        )
        try:
            assert_exact_slot_plans(
                action_encounter.get("typeName", "<unknown>"),
                table_id,
                slot_plans,
                plan_warnings,
                monster_catalog,
                random_branches_by_monster,
                turn_count,
            )
            table = simulate_exact_table(
                table_id,
                f"第{half_turn}回合打到半血以下",
                action_encounter,
                slot_plans,
                monster_catalog,
                random_branches_by_monster,
                turn_count,
                note,
            )
            table["warnings"].extend(plan_warnings)
            tables.append(table)
        except MatrixGenerationError as error:
            errors.extend(error.errors)
    if errors:
        raise MatrixGenerationError(errors)
    return tables


def build_fabricator_tables(
    action_encounter: dict[str, Any],
    monster_catalog: dict[str, dict[str, Any]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    turn_count: int,
) -> list[dict[str, Any]]:
    errors: list[str] = []
    slot_plans = [
        manual_slot_plan(
            monster_catalog,
            3,
            "fabricator",
            "Fabricator",
            "FABRICATE_MOVE",
            forced_states=fabricator_forced_states(turn_count),
            source_reason="manual: Fabricate/Fabricating Strike alternating sequence",
        )
    ]
    for position, slot_name, monster_type, active_from in FABRICATOR_SUMMONED_ATTACK_BOTS:
        slot_plans.append(
            manual_slot_plan(
                monster_catalog,
                position,
                slot_name,
                monster_type,
                active_from=active_from,
                source_reason="manual: alternating Fabricator summon slot",
            )
        )
    slot_plans.sort(key=lambda plan: plan["position"])
    note = (
        "按用户假设固定 Fabricator 第一回合 Fabricate、第二回合 Fabricating Strike，并持续交替。"
        "召唤物从下一回合开始行动；攻击型 bot 按 Zapbot/Stabbot 交替并加入伤害列。"
        "防御池按 Guardbot/Noisebot 交替，但两者无攻击伤害，因此仅在本注释记录，"
        "不输出永久全零伤害列。槽位填满后，后续召唤动作不再新增矩阵列。"
    )
    table = build_manual_table_or_collect_errors(
        errors,
        action_encounter.get("typeName", "<unknown>"),
        "fabricate-strike-alternating",
        "Fabricate / Fabricating Strike 交替",
        action_encounter,
        slot_plans,
        monster_catalog,
        random_branches_by_monster,
        turn_count,
        note,
    )
    if errors:
        raise MatrixGenerationError(errors)
    return [table] if table is not None else []


def build_turret_operator_weak_tables(
    action_encounter: dict[str, Any],
    compositions: list[tuple[str, ...]],
    monster_catalog: dict[str, dict[str, Any]],
    slot_initials_by_monster: dict[str, dict[str, str]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    turn_count: int,
) -> list[dict[str, Any]]:
    errors: list[str] = []
    tables: list[dict[str, Any]] = []
    composition = compositions[0] if compositions else tuple()
    for kill_turn in (3, 4, 5):
        slot_plans, plan_warnings = build_slot_plans(
            action_encounter,
            composition,
            monster_catalog,
            slot_initials_by_monster,
            random_branches_by_monster,
        )
        for plan in slot_plans:
            if plan["monsterTypeName"] == "LivingShield":
                plan["activeThroughTurn"] = kill_turn - 1
                plan["forcedStateByTurn"] = {
                    turn: "SHIELD_SLAM_MOVE"
                    for turn in range(1, kill_turn)
                }
        table_id = f"living-shield-killed-turn-{kill_turn}"
        note = (
            f"假设玩家第{kill_turn}回合行动中击杀 LivingShield；"
            f"因此 LivingShield 从第{kill_turn}回合敌方行动开始不再行动。"
            "击杀前 TurretOperator 仍存活，所以 LivingShield 持续使用 SHIELD_SLAM_MOVE。"
        )
        try:
            assert_exact_slot_plans(
                action_encounter.get("typeName", "<unknown>"),
                table_id,
                slot_plans,
                plan_warnings,
                monster_catalog,
                random_branches_by_monster,
                turn_count,
            )
            table = simulate_exact_table(
                table_id,
                f"第{kill_turn}回合击杀 LivingShield",
                action_encounter,
                slot_plans,
                monster_catalog,
                random_branches_by_monster,
                turn_count,
                note,
            )
            table["warnings"].extend(plan_warnings)
            tables.append(table)
        except MatrixGenerationError as error:
            errors.extend(error.errors)
    if errors:
        raise MatrixGenerationError(errors)
    return tables


def starter_offset_override(
    composition: tuple[str, ...],
    starter_order: list[str],
    offset: int,
    fixed_slot_starts: dict[int, str] | None = None,
) -> dict[str, Any]:
    fixed_slot_starts = fixed_slot_starts or {}
    return {
        "slotStartStateIds": {
            str(index + 1): fixed_slot_starts.get(
                index + 1,
                starter_order[(offset + index) % len(starter_order)],
            )
            for index, _ in enumerate(composition)
        }
    }


def build_tables_for_encounter(
    action_encounter: dict[str, Any],
    damage_encounter: dict[str, Any],
    monster_catalog: dict[str, dict[str, Any]],
    slot_initials_by_monster: dict[str, dict[str, str]],
    random_branches_by_monster: dict[str, dict[str, Any]],
    starter_orders: dict[str, list[str]],
    overrides: dict[str, Any],
    turn_count: int,
    composition_limit: int,
) -> list[dict[str, Any]]:
    tables: list[dict[str, Any]] = []
    errors: list[str] = []
    slots = action_encounter.get("monsterSlots", [])
    encounter_overrides = overrides.get("encounters", {}).get(action_encounter.get("typeName"), {})
    override_tables = encounter_overrides.get("tables", [])
    encounter_type = action_encounter.get("typeName", "<unknown>")
    compositions = (
        ruby_raiders_composition_scenarios(slots)
        if encounter_type == "RubyRaidersNormal"
        else composition_scenarios(slots)
    )

    # Manual phase/survival tables are shown first because they encode the user's
    # preferred practical assumptions.
    for override in override_tables:
        composition = compositions[0] if compositions else tuple()
        slot_plans, plan_warnings = build_slot_plans(
            action_encounter,
            composition,
            monster_catalog,
            slot_initials_by_monster,
            random_branches_by_monster,
            override,
        )
        table_id = override.get("id", "manual")
        table_turn_count = int(override.get("turnCount", turn_count))
        try:
            assert_exact_slot_plans(
                encounter_type,
                table_id,
                slot_plans,
                plan_warnings,
                monster_catalog,
                random_branches_by_monster,
                table_turn_count,
            )
            table = simulate_exact_table(
                table_id,
                override.get("title", "手动表"),
                action_encounter,
                slot_plans,
                monster_catalog,
                random_branches_by_monster,
                table_turn_count,
                override.get("note"),
            )
            table["warnings"].extend(plan_warnings)
            tables.append(table)
        except MatrixGenerationError as error:
            errors.extend(error.errors)

    fixed_monster_types = sorted({monster for composition in compositions for monster in composition})
    if action_encounter.get("typeName") == "DecimillipedeElite":
        composition = compositions[0] if compositions else tuple()
        starter_order = next(
            (
                starter_orders[monster_type]
                for monster_type in composition
                if monster_type in starter_orders and len(starter_orders[monster_type]) == 3
            ),
            [],
        )
        if starter_order:
            for offset in range(3):
                override = {
                    "slotStartStateIds": {
                        str(index + 1): starter_order[(offset + index) % len(starter_order)]
                        for index, _ in enumerate(composition)
                    }
                }
                slot_plans, plan_warnings = build_slot_plans(
                    action_encounter,
                    composition,
                    monster_catalog,
                    slot_initials_by_monster,
                    random_branches_by_monster,
                    override,
                )
                table_id = f"decimillipede-starter-offset-{offset}"
                try:
                    assert_exact_slot_plans(
                        encounter_type,
                        table_id,
                        slot_plans,
                        plan_warnings,
                        monster_catalog,
                        random_branches_by_monster,
                        turn_count,
                    )
                    table = simulate_exact_table(
                        table_id,
                        f"残杀千足虫随机起手 offset {offset}",
                        action_encounter,
                        slot_plans,
                        monster_catalog,
                        random_branches_by_monster,
                        turn_count,
                        "游戏随机选择一个 StarterMoveIdx，并让三个体节依次错开起手。",
                    )
                    table["warnings"].extend(plan_warnings)
                    tables.append(table)
                except MatrixGenerationError as error:
                    errors.extend(error.errors)
            if errors:
                raise MatrixGenerationError(errors)
            return collapse_cyclically_symmetric_tables(tables)

    if (
        fixed_monster_types == ["TwoTailedRat"]
        and "TwoTailedRat" in starter_orders
        and len(starter_orders["TwoTailedRat"]) == 3
        and len(compositions) == 1
    ):
        composition = compositions[0]
        for offset in range(3):
            table = build_exact_table_or_collect_errors(
                errors,
                encounter_type,
                f"two-tailed-rat-starter-offset-{offset}",
                f"双尾鼠随机起手 offset {offset}",
                action_encounter,
                composition,
                monster_catalog,
                slot_initials_by_monster,
                random_branches_by_monster,
                turn_count,
                starter_offset_override(composition, starter_orders["TwoTailedRat"], offset),
                "游戏随机选择一个 StarterMoveIndex，并让三只双尾鼠依次错开起手。",
            )
            if table is not None:
                tables.append(table)
        if errors:
            raise MatrixGenerationError(errors)
        return collapse_cyclically_symmetric_tables(tables)

    if (
        fixed_monster_types == ["ScrollOfBiting"]
        and "ScrollOfBiting" in starter_orders
        and len(starter_orders["ScrollOfBiting"]) == 3
        and len(compositions) == 1
    ):
        composition = compositions[0]
        starter_order = starter_orders["ScrollOfBiting"]
        if len(composition) == 4:
            offset_range = range(1)
            fixed_starts = {4: starter_order[2]}
            note = (
                "前三张咬人卷轴按随机 StarterMoveIdx 依次错开，4号固定为 index 2。"
                "前三个槽位是同类对称槽位，因此选 StarterMoveIdx=0 作为代表表。"
            )
        else:
            offset_range = range(3)
            fixed_starts = {}
            note = "游戏随机选择一个 StarterMoveIdx，并让咬人卷轴依次错开起手。"

        for offset in offset_range:
            table = build_exact_table_or_collect_errors(
                errors,
                encounter_type,
                f"scroll-of-biting-starter-offset-{offset}",
                f"咬人卷轴随机起手 offset {offset}",
                action_encounter,
                composition,
                monster_catalog,
                slot_initials_by_monster,
                random_branches_by_monster,
                turn_count,
                starter_offset_override(composition, starter_order, offset, fixed_starts),
                note,
            )
            if table is not None:
                tables.append(table)
        if errors:
            raise MatrixGenerationError(errors)
        return collapse_cyclically_symmetric_tables(tables)

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
                random_branches_by_monster,
                corpse_slug_offset=offset,
                corpse_slug_order=starter_orders["CorpseSlug"],
            )
            table_id = f"corpse-slug-starter-offset-{offset}"
            try:
                assert_exact_slot_plans(
                    encounter_type,
                    table_id,
                    slot_plans,
                    plan_warnings,
                    monster_catalog,
                    random_branches_by_monster,
                    turn_count,
                )
                table = simulate_exact_table(
                    table_id,
                    f"噬尸蛞蝓随机起手 offset {offset}",
                    action_encounter,
                    slot_plans,
                    monster_catalog,
                    random_branches_by_monster,
                    turn_count,
                    "游戏会随机选择一个起手 offset，并让多只噬尸蛞蝓依次错开起手。",
                )
                table["warnings"].extend(plan_warnings)
                tables.append(table)
            except MatrixGenerationError as error:
                errors.extend(error.errors)
        if errors:
            raise MatrixGenerationError(errors)
        return collapse_cyclically_symmetric_tables(tables)

    if encounter_type == "KnowledgeDemonBoss":
        composition = compositions[0] if compositions else tuple()
        variants = [
            (
                "curse-choice-first-left",
                "二选一：第一次选左，之后选左",
                True,
            ),
            (
                "curse-choice-first-right",
                "二选一：第一次选右，之后选左",
                False,
            ),
        ]
        for table_id, title, first_choice_left in variants:
            table = build_exact_table_or_collect_errors(
                errors,
                encounter_type,
                table_id,
                title,
                action_encounter,
                composition,
                monster_catalog,
                slot_initials_by_monster,
                random_branches_by_monster,
                turn_count,
            )
            if table is not None:
                tables.append(
                    apply_knowledge_demon_curse_damage(
                        table,
                        first_choice_left,
                        turn_count,
                    )
                )
        if errors:
            raise MatrixGenerationError(errors)
        return tables

    if encounter_type == "QueenBoss":
        return build_queen_tables(
            action_encounter,
            compositions,
            monster_catalog,
            slot_initials_by_monster,
            random_branches_by_monster,
            turn_count,
        )

    if encounter_type == "FrogKnightNormal":
        return build_frog_knight_tables(
            action_encounter,
            compositions,
            monster_catalog,
            slot_initials_by_monster,
            random_branches_by_monster,
            turn_count,
        )

    if encounter_type == "FabricatorNormal":
        return build_fabricator_tables(
            action_encounter,
            monster_catalog,
            random_branches_by_monster,
            turn_count,
        )

    if encounter_type == "TurretOperatorWeak":
        return build_turret_operator_weak_tables(
            action_encounter,
            compositions,
            monster_catalog,
            slot_initials_by_monster,
            random_branches_by_monster,
            turn_count,
        )

    if len(compositions) > composition_limit:
        raise MatrixGenerationError(
            [
                f"{encounter_type}: composition count {len(compositions)} exceeds expansion limit {composition_limit}."
            ]
        )

    for index, composition in enumerate(compositions):
        slot_plans, plan_warnings = build_slot_plans(
            action_encounter,
            composition,
            monster_catalog,
            slot_initials_by_monster,
            random_branches_by_monster,
        )
        suffix = "" if len(compositions) == 1 else f" 组合{index + 1}"
        table_id = "full-sequence" if len(compositions) == 1 else f"composition-{index + 1}"
        try:
            assert_exact_slot_plans(
                encounter_type,
                table_id,
                slot_plans,
                plan_warnings,
                monster_catalog,
                random_branches_by_monster,
                turn_count,
            )
        except MatrixGenerationError as error:
            errors.extend(error.errors)
            continue

        try:
            tables.append(
                simulate_exact_table(
                    table_id,
                    f"完整序列{suffix}",
                    action_encounter,
                    slot_plans,
                    monster_catalog,
                    random_branches_by_monster,
                    turn_count,
                )
            )
        except MatrixGenerationError as error:
            errors.extend(error.errors)

    if errors:
        raise MatrixGenerationError(errors)
    if not tables:
        raise MatrixGenerationError([f"{encounter_type}: no exact matrix tables were generated."])
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
    random_branches_by_monster = {
        monster_type: parse_random_branch_profiles(source)
        for monster_type, source in source_by_monster.items()
    }
    mechanics_by_monster = {
        monster_type: scan_special_mechanics(source)
        for monster_type, source in source_by_monster.items()
    }

    damage_by_type = {
        encounter["typeName"]: encounter
        for encounter in damage_report.get("encounters", [])
    }
    encounters: list[dict[str, Any]] = []
    excluded_encounters: list[dict[str, Any]] = []
    matrix_errors: list[str] = []
    for raw_action_encounter in action_report.get("encounters", []):
        action_encounter = enhanced_encounter_from_source(raw_action_encounter, decompile_root)
        encounter_type = action_encounter["typeName"]
        reason = excluded_encounter_reason(action_encounter)
        if reason is not None:
            excluded_encounters.append(
                {
                    "modelId": action_encounter.get("modelId"),
                    "typeName": encounter_type,
                    "name": localized_title(encounter_type, localized_entries),
                    "acts": action_encounter.get("acts", []),
                    "actLabel": act_label(action_encounter),
                    "category": action_encounter.get("category"),
                    "reason": reason,
                }
            )
            continue

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
        try:
            tables = build_tables_for_encounter(
                action_encounter,
                damage_encounter,
                monster_catalog,
                slot_initials_by_monster,
                random_branches_by_monster,
                starter_orders,
                overrides,
                turn_count,
                composition_limit,
            )
        except MatrixGenerationError as error:
            matrix_errors.extend(error.errors)
            continue
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
                "sourceSlotStartCorrection": action_encounter.get("sourceSlotStartCorrection"),
                "tables": tables,
                "supplementalTables": supplemental_tables_for_encounter(encounter_type, turn_count),
            }
        )

    if matrix_errors:
        raise MatrixGenerationError(matrix_errors)

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
            "Generation fails instead of emitting a matrix when slot starts, follow-ups, compositions, or damage expressions cannot be resolved exactly.",
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
            "manualOverrideEncounterCount": len(overrides.get("encounters", {})),
            "excludedEncounterCount": len(excluded_encounters),
            "cyclicSymmetryOmittedTableCount": sum(
                len(table.get("omittedSymmetricTables", []))
                for encounter in encounters
                for table in encounter["tables"]
            ),
            "supplementalTableCount": sum(
                len(encounter.get("supplementalTables", []))
                for encounter in encounters
            ),
        },
        "encounters": encounters,
        "excludedEncounters": excluded_encounters,
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
    return []


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

    if report.get("excludedEncounters"):
        lines.append("## Excluded Encounters")
        lines.append("")
        lines.append("| Act | Category | Encounter | Reason |")
        lines.append("| --- | --- | --- | --- |")
        for encounter in report["excludedEncounters"]:
            lines.append(
                "| "
                + " | ".join(
                    [
                        escape_md(encounter.get("actLabel") or ""),
                        escape_md(encounter.get("category") or ""),
                        escape_md(encounter.get("typeName") or ""),
                        escape_md(encounter.get("reason") or ""),
                    ]
                )
                + " |"
            )
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
        if encounter.get("sourceSlotStartCorrection"):
            correction = encounter["sourceSlotStartCorrection"]
            starts = [
                f"{item.get('position')}号 {item.get('monsterTypeName')} -> {item.get('startStateId')} ({item.get('reason')})"
                for item in correction.get("slots", [])
            ]
            if starts:
                lines.append("- Source start correction: " + escape_md("; ".join(starts)))
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
            extra_columns = table.get("extraDamageColumns", [])
            extra_headers = [column.get("label", column.get("id", "")) for column in extra_columns]
            header = ["回合"] + slot_header(max_slots) + extra_headers + ["总计"]
            lines.append("| " + " | ".join(header) + " |")
            lines.append(
                "| "
                + " | ".join(["---"] + ["---:"] * (max_slots + len(extra_headers)) + ["---:"])
                + " |"
            )
            for row in table.get("rows", []):
                cells = row.get("cells", [])
                values = [cell.get("display", fmt(cell.get("damage"))) for cell in cells]
                values.extend(["-"] * (max_slots - len(values)))
                extra_values_by_id = {
                    cell.get("id"): cell.get("display", fmt(cell.get("damage")))
                    for cell in row.get("extraDamageCells", [])
                }
                values.extend(
                    extra_values_by_id.get(column.get("id"), "-")
                    for column in extra_columns
                )
                md_row = [f"第{row['turn']}回合"] + values + [row.get("displayTotal", fmt(row.get("totalDamage")))]
                lines.append("| " + " | ".join(escape_md(item) for item in md_row) + " |")
            lines.append("")

        for table in encounter.get("supplementalTables", []):
            lines.append(f"### {escape_md(table.get('title') or table.get('id'))}")
            lines.append("")
            lines.append(f"- Mode: `{table.get('mode')}`")
            if table.get("note"):
                lines.append(f"- Note: {escape_md(table['note'])}")
            lines.append("")
            turns = table.get("turns", [])
            header = ["项目"] + [f"第{turn}回合" for turn in turns]
            lines.append("| " + " | ".join(header) + " |")
            lines.append("| " + " | ".join(["---"] + ["---:"] * len(turns)) + " |")
            for row in table.get("rows", []):
                values = row.get("displayValues", row.get("values", []))
                lines.append(
                    "| "
                    + " | ".join(
                        escape_md(item)
                        for item in [row.get("label", "")] + [str(value) for value in values]
                    )
                    + " |"
                )
            lines.append("")

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    action_report = load_json(Path(args.actions))
    damage_report = load_json(Path(args.damage_details))
    localized_entries = load_localized_entries(Path(args.localized_names))
    overrides = load_json_or_empty(Path(args.overrides))
    output_json = Path(args.output_json)
    output_md = Path(args.output_md)
    output_json.parent.mkdir(parents=True, exist_ok=True)
    output_md.parent.mkdir(parents=True, exist_ok=True)
    try:
        report = build_report(
            action_report,
            damage_report,
            localized_entries,
            overrides,
            Path(args.decompile_root),
            args.turns,
            args.composition_limit,
        )
    except MatrixGenerationError as error:
        failure_report = {
            "schemaVersion": 1,
            "status": "failed",
            "generatedAt": dt.datetime.now(dt.timezone.utc).isoformat(),
            "turnCount": args.turns,
            "errors": error.errors,
            "sourceFiles": {
                "turnActions": args.actions,
                "damageDetails": args.damage_details,
                "localizedNames": args.localized_names,
                "overrides": args.overrides,
                "decompileRoot": args.decompile_root,
            },
        }
        output_json.write_text(json.dumps(failure_report, ensure_ascii=False, indent=2), encoding="utf-8")
        output_md.write_text(
            "# Monster Encounter Damage Matrices\n\n"
            "Generation failed. No matrix table was emitted because exact damage "
            "resolution found unresolved states, branches, compositions, or damage expressions.\n\n"
            "## Errors\n\n"
            + "\n".join(f"- {escape_md(item)}" for item in error.errors)
            + "\n",
            encoding="utf-8",
        )
        print("monster encounter damage matrices failed", file=sys.stderr)
        for item in error.errors:
            print(f"- {item}", file=sys.stderr)
        print(f"failureOutput: {output_json}", file=sys.stderr)
        print(f"failureReport: {output_md}", file=sys.stderr)
        return 1

    output_json.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    write_markdown(report, output_md)

    print("monster encounter damage matrices generated")
    print(f"encounters: {report['summary']['encounterCount']}")
    print(f"tables: {report['summary']['tableCount']}")
    print(f"exactTables: {report['summary']['exactTableCount']}")
    print(f"output: {output_json}")
    print(f"report: {output_md}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
