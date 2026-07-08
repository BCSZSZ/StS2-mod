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
        state = moves_by_state.get(state_id)
        if not state:
            errors.append(
                f"slot {plan['position']} {plan['monsterTypeName']} references missing state {state_id!r} on turn {turn}."
            )
            break
        state_log.append(state_id)
        if turn == turn_count:
            break
        followups = [
            followup
            for followup in state.get("followUpStateIds", [])
            if followup in moves_by_state
        ]
        if len(followups) > 1:
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
    vulnerable = 0.0
    rows: list[dict[str, Any]] = []
    warnings: list[str] = []
    used_random_branch = False

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
                raise MatrixGenerationError(
                    [
                        f"{encounter.get('typeName')}/{table_id}: missing state {state_id or '<unknown>'} for {plan['monsterTypeName']} slot {position} on turn {turn}."
                    ]
                )

            state_logs[position].append(state_id)
            damage, state_unknown = state_adjusted_damage(state, strengths[position], vulnerable)
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
            if turn < turn_count:
                followups = [
                    followup
                    for followup in state.get("followUpStateIds", [])
                    if followup in moves_by_state
                ]
                if len(followups) > 1:
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
    compositions = composition_scenarios(slots)
    encounter_overrides = overrides.get("encounters", {}).get(action_encounter.get("typeName"), {})
    override_tables = encounter_overrides.get("tables", [])
    encounter_type = action_encounter.get("typeName", "<unknown>")

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
                "tables": tables,
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
