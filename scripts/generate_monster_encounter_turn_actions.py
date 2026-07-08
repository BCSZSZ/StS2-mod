"""Generate monster move and encounter turn-action reports.

The report combines the decompiled monster move profiles with parsed encounter
patterns and expands each monster through turns 1..N using the same follow-up
state approximation as the enemy pressure estimator.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
from pathlib import Path
from typing import Any


CATEGORY_LABELS = {
    "attack": "攻击",
    "defense": "防御",
    "selfBuff": "给自己buff",
    "playerDebuff": "给我们debuff",
    "addCard": "给我们塞牌",
    "special": "其他特殊",
}

CATEGORY_CODES = {
    "attack": "A",
    "defense": "D",
    "selfBuff": "B",
    "playerDebuff": "X",
    "addCard": "C",
    "special": "S",
}

SPECIAL_INTENTS = {
    "DeathBlowIntent",
    "EscapeIntent",
    "HealIntent",
    "HiddenIntent",
    "SleepIntent",
    "StunIntent",
    "SummonIntent",
}

INTENT_NAMES = {
    "AbstractIntent": ("Abstract Intent", "抽象意图"),
    "AttackIntent": ("Attack", "攻击"),
    "BuffIntent": ("Buff", "强化"),
    "CardDebuffIntent": ("Card Debuff", "卡牌减益"),
    "DeathBlowIntent": ("Death Blow", "致命一击"),
    "DebuffIntent": ("Debuff", "减益"),
    "DefendIntent": ("Defend", "防御"),
    "EscapeIntent": ("Escape", "逃跑"),
    "HealIntent": ("Heal", "治疗"),
    "HiddenIntent": ("Hidden", "隐藏意图"),
    "MultiAttackIntent": ("Multi Attack", "多段攻击"),
    "SingleAttackIntent": ("Single Attack", "单段攻击"),
    "SleepIntent": ("Sleep", "沉睡"),
    "StatusIntent": ("Status", "塞状态牌"),
    "StunIntent": ("Stun", "眩晕"),
    "SummonIntent": ("Summon", "召唤"),
    "UnknownIntent": ("Unknown Intent", "未知意图"),
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate all-encounter monster turn actions from extracted StS2 data."
    )
    parser.add_argument(
        "--profiles",
        default="data/extracted/monster_move_profiles.generated.json",
        help="Monster move profile JSON produced by parse-monster-moves.",
    )
    parser.add_argument(
        "--patterns",
        default="data/extracted/encounter_patterns.generated.json",
        help="Encounter pattern JSON produced by parse-encounter-patterns.",
    )
    parser.add_argument(
        "--decompile-root",
        default="data/generated/decompiled/sts2",
        help="ILSpy decompiled source root used to infer status-card names and piles.",
    )
    parser.add_argument(
        "--localized-names",
        default="history-analysis/data/localized_names_en_zhs.json",
        help="Extracted en/zhs localization names used for monster display names.",
    )
    parser.add_argument("--turns", type=int, default=14, help="Number of turns to expand.")
    parser.add_argument(
        "--output-json",
        default="data/generated/monster_encounter_turn_actions.generated.json",
        help="Output JSON path.",
    )
    parser.add_argument(
        "--output-md",
        default="data/generated/monster_encounter_turn_actions.md",
        help="Output Markdown path.",
    )
    return parser.parse_args()


def load_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def load_localized_entries(path: Path) -> dict[str, dict[str, str]]:
    if not path.exists():
        return {}
    data = load_json(path)
    entries = data.get("entries", data) if isinstance(data, dict) else {}
    return entries if isinstance(entries, dict) else {}


def numeric_value(number: dict[str, Any] | None, ascension: bool) -> float | None:
    if not number:
        return None
    if ascension and number.get("ascensionValue") is not None:
        return number.get("ascensionValue")
    return number.get("value")


def numeric_expression(number: dict[str, Any] | None) -> str | None:
    if not number:
        return None
    return number.get("expression")


def fmt_num(value: Any) -> str:
    if value is None:
        return "?"
    if isinstance(value, float) and value.is_integer():
        return str(int(value))
    return f"{value:g}" if isinstance(value, float) else str(value)


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


def localization_key_from_model_id(model_id: str | None) -> str | None:
    if not model_id:
        return None
    return model_id.split(".", 1)[1] if model_id.startswith("MONSTER.") else model_id


def monster_name(
    profile: dict[str, Any],
    localized_entries: dict[str, dict[str, str]],
) -> dict[str, Any]:
    key = localization_key_from_model_id(profile.get("modelId"))
    if key and key in localized_entries:
        entry = localized_entries[key]
        return {
            "en": entry.get("en") or humanize_identifier(profile.get("typeName")),
            "zh": entry.get("zhs") or entry.get("zh") or entry.get("en") or humanize_identifier(profile.get("typeName")),
            "source": f"history-analysis/data/localized_names_en_zhs.json:{key}",
            "translationStatus": "localized",
        }
    if key and key.startswith("DECIMILLIPEDE_SEGMENT_") and "DECIMILLIPEDE_SEGMENT" in localized_entries:
        entry = localized_entries["DECIMILLIPEDE_SEGMENT"]
        suffix_id = key.removeprefix("DECIMILLIPEDE_SEGMENT_")
        suffix_en = humanize_identifier(suffix_id.title().replace("_", ""))
        suffix_zh = {"BACK": "后段", "FRONT": "前段", "MIDDLE": "中段"}.get(suffix_id, suffix_en)
        return {
            "en": f"{entry.get('en', 'Decimillipede')} ({suffix_en})",
            "zh": f"{entry.get('zhs') or entry.get('zh') or entry.get('en', 'Decimillipede')}（{suffix_zh}）",
            "source": "history-analysis/data/localized_names_en_zhs.json:DECIMILLIPEDE_SEGMENT+segmentSuffix",
            "translationStatus": "localizedBaseWithSegmentSuffix",
        }
    english = humanize_identifier(profile.get("typeName") or profile.get("modelId"))
    return {
        "en": english,
        "zh": english,
        "source": "fallback:typeName",
        "translationStatus": "zhFallbackOfficialLocalizationPending",
    }


def intent_name(intent_id: str | None) -> dict[str, Any]:
    intent_id = intent_id or "UnknownIntent"
    if intent_id in INTENT_NAMES:
        english, chinese = INTENT_NAMES[intent_id]
        source = "analysis-glossary:intentTypeName"
        status = "translated"
    else:
        base = intent_id.removesuffix("Intent")
        english = humanize_identifier(base)
        chinese = english
        source = "fallback:typeName"
        status = "zhFallbackOfficialLocalizationPending"
    return {
        "id": intent_id,
        "en": english,
        "zh": chinese,
        "source": source,
        "translationStatus": status,
    }


def format_bilingual_name(name: dict[str, Any] | None, fallback: str = "") -> str:
    if not isinstance(name, dict):
        return fallback
    english = name.get("en") or fallback
    chinese = name.get("zh") or fallback
    if english and chinese:
        return f"{chinese} / {english}"
    return chinese or english or fallback


def format_intent(intent_id: str | None) -> str:
    return format_bilingual_name(intent_name(intent_id), intent_id or "")


def extract_braced_block(source: str, open_brace: int) -> str | None:
    depth = 0
    in_string = False
    escaped = False
    for index in range(open_brace, len(source)):
        char = source[index]
        if in_string:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                in_string = False
            continue
        if char == '"':
            in_string = True
            continue
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return source[open_brace + 1 : index]
    return None


def extract_method_body(source: str, method_name: str | None) -> str | None:
    if not method_name or "=>" in method_name:
        return None
    match = re.search(
        rf"(?:private|protected|public)\s+(?:override\s+)?(?:async\s+)?Task\s+{re.escape(method_name)}\s*\([^)]*\)\s*\{{",
        source,
    )
    if not match:
        return None
    return extract_braced_block(source, match.end() - 1)


def find_monster_source(profile: dict[str, Any], decompile_root: Path) -> Path | None:
    full_type = profile.get("fullTypeName") or ""
    candidate: Path | None = None
    if full_type:
        nested_candidate = decompile_root / Path(*full_type.split(".")).with_suffix(".cs")
        if nested_candidate.exists():
            candidate = nested_candidate
    type_name = profile.get("typeName")
    if candidate is None and type_name and decompile_root.exists():
        matches = list(decompile_root.rglob(f"{type_name}.cs"))
        monster_matches = [path for path in matches if "Monsters" in path.parts]
        candidate = (monster_matches or matches or [None])[0]
    if candidate is None:
        return None
    candidate_source = candidate.read_text(encoding="utf-8", errors="replace")
    if "new MoveState" in candidate_source:
        return candidate
    base_candidate = find_direct_base_monster_source(candidate_source, decompile_root)
    if base_candidate is not None:
        base_source = base_candidate.read_text(encoding="utf-8", errors="replace")
        if "new MoveState" in base_source:
            return base_candidate
    return candidate


def find_direct_base_monster_source(source: str, decompile_root: Path) -> Path | None:
    match = re.search(r"class\s+[A-Za-z_][A-Za-z0-9_]*\s*:\s*(?P<base>[A-Za-z_][A-Za-z0-9_]*)", source)
    if not match:
        return None
    base_name = match.group("base")
    if base_name in {"MonsterModel", "object"}:
        return None
    matches = list(decompile_root.rglob(f"{base_name}.cs"))
    monster_matches = [path for path in matches if "Monsters" in path.parts]
    return (monster_matches or matches or [None])[0]


def parse_status_card_hints(body: str | None) -> list[dict[str, Any]]:
    if not body:
        return []

    hints: list[dict[str, Any]] = []
    for match in re.finditer(
        r"CardPileCmd\.AddToCombatAndPreview<(?P<card>[A-Za-z0-9_]+)>\([^,]+,\s*PileType\.(?P<pile>[A-Za-z0-9_]+),\s*(?P<count>[^,)\r\n]+)",
        body,
    ):
        hints.append(
            {
                "cardTypeName": match.group("card"),
                "pile": match.group("pile"),
                "countExpression": match.group("count").strip(),
                "source": "CardPileCmd.AddToCombatAndPreview",
            }
        )

    created_cards: dict[str, str] = {}
    for match in re.finditer(
        r"(?:CardModel\s+)?(?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*base\.CombatState\.CreateCard<(?P<card>[A-Za-z0-9_]+)>",
        body,
    ):
        created_cards[match.group("var")] = match.group("card")

    pile_assignments: dict[str, list[str]] = {}
    for match in re.finditer(
        r"PileType\s+(?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?P<expr>[^;\r\n]+)",
        body,
    ):
        piles = re.findall(r"PileType\.([A-Za-z0-9_]+)", match.group("expr"))
        if piles:
            pile_assignments[match.group("var")] = sorted(set(piles))

    for match in re.finditer(
        r"CardPileCmd\.AddGeneratedCardToCombat\((?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*(?P<pileArg>[^,\r\n]+)",
        body,
    ):
        var_name = match.group("var")
        pile_arg = match.group("pileArg").strip()
        literal_piles = re.findall(r"PileType\.([A-Za-z0-9_]+)", pile_arg)
        piles = literal_piles or pile_assignments.get(pile_arg, [pile_arg])
        for pile in piles:
            hints.append(
                {
                    "cardTypeName": created_cards.get(var_name),
                    "pile": pile,
                    "countExpression": None,
                    "source": "CardPileCmd.AddGeneratedCardToCombat",
                }
            )

    unique: list[dict[str, Any]] = []
    seen: set[tuple[Any, Any, Any]] = set()
    for hint in hints:
        key = (hint.get("cardTypeName"), hint.get("pile"), hint.get("source"))
        if key not in seen:
            seen.add(key)
            unique.append(hint)
    return unique


def load_source_hints(
    profiles: list[dict[str, Any]], decompile_root: Path
) -> dict[tuple[str, str], dict[str, Any]]:
    hints: dict[tuple[str, str], dict[str, Any]] = {}
    for profile in profiles:
        source_path = find_monster_source(profile, decompile_root)
        source = source_path.read_text(encoding="utf-8", errors="replace") if source_path else None
        for move in profile.get("moves", []):
            body = extract_method_body(source or "", move.get("moveMethod")) if source else None
            status_cards = parse_status_card_hints(body)
            hints[(profile["typeName"], move["stateId"])] = {
                "sourcePath": str(source_path) if source_path else None,
                "statusCardHints": status_cards,
            }
    return hints


def effect_action(effect: dict[str, Any], source_hint: dict[str, Any]) -> tuple[str, dict[str, Any]]:
    kind = effect.get("kind")
    amount_base = numeric_value(effect.get("amount"), ascension=False)
    amount_asc = numeric_value(effect.get("amount"), ascension=True)
    hit_base = numeric_value(effect.get("hitCount"), ascension=False)
    hit_asc = numeric_value(effect.get("hitCount"), ascension=True)

    if kind == "attack":
        hit_base = 1 if hit_base is None else hit_base
        hit_asc = 1 if hit_asc is None else hit_asc
        return (
            "attack",
            {
                "kind": "attack",
                "amount": amount_base,
                "ascensionAmount": amount_asc,
                "hitCount": hit_base,
                "ascensionHitCount": hit_asc,
                "total": None if amount_base is None else amount_base * hit_base,
                "ascensionTotal": None if amount_asc is None else amount_asc * hit_asc,
                "amountExpression": numeric_expression(effect.get("amount")),
                "target": effect.get("target"),
                "source": effect.get("source"),
                "confidence": effect.get("confidence"),
            },
        )
    if kind == "block":
        return (
            "defense",
            {
                "kind": "block",
                "amount": amount_base,
                "ascensionAmount": amount_asc,
                "amountExpression": numeric_expression(effect.get("amount")),
                "target": effect.get("target"),
                "source": effect.get("source"),
                "confidence": effect.get("confidence"),
            },
        )
    if kind == "status":
        return (
            "addCard",
            {
                "kind": "statusCard",
                "amount": amount_base,
                "ascensionAmount": amount_asc,
                "amountExpression": numeric_expression(effect.get("amount")),
                "target": effect.get("target"),
                "cards": source_hint.get("statusCardHints", []),
                "source": effect.get("source"),
                "confidence": effect.get("confidence"),
            },
        )
    if kind == "buffStrength":
        return (
            "selfBuff",
            {
                "kind": kind,
                "amount": amount_base,
                "ascensionAmount": amount_asc,
                "parameter": effect.get("parameter") or "power:Strength",
                "target": effect.get("target"),
                "source": effect.get("source"),
                "confidence": effect.get("confidence"),
            },
        )
    if kind in {"debuffWeak", "debuffVulnerable", "debuffFrail"}:
        return (
            "playerDebuff",
            {
                "kind": kind,
                "amount": amount_base,
                "ascensionAmount": amount_asc,
                "parameter": effect.get("parameter"),
                "target": effect.get("target"),
                "source": effect.get("source"),
                "confidence": effect.get("confidence"),
            },
        )
    if kind == "power":
        category = "selfBuff" if effect.get("target") == "self" else "playerDebuff"
        return (
            category,
            {
                "kind": "power",
                "amount": amount_base,
                "ascensionAmount": amount_asc,
                "parameter": effect.get("parameter"),
                "target": effect.get("target"),
                "source": effect.get("source"),
                "confidence": effect.get("confidence"),
            },
        )
    return (
        "special",
        {
            "kind": kind or "unknownEffect",
            "amount": amount_base,
            "ascensionAmount": amount_asc,
            "parameter": effect.get("parameter"),
            "target": effect.get("target"),
            "source": effect.get("source"),
            "confidence": effect.get("confidence"),
        },
    )


def categorize_move(
    monster_type: str, move: dict[str, Any], source_hints: dict[tuple[str, str], dict[str, Any]]
) -> dict[str, Any]:
    categories: dict[str, list[dict[str, Any]]] = {key: [] for key in CATEGORY_LABELS}
    source_hint = source_hints.get((monster_type, move["stateId"]), {})

    for effect in move.get("effects", []):
        category, action = effect_action(effect, source_hint)
        categories[category].append(action)

    intents = move.get("intents", [])
    if "SingleAttackIntent" in intents or "MultiAttackIntent" in intents or "DeathBlowIntent" in intents:
        if not categories["attack"]:
            categories["attack"].append({"kind": "intentOnly", "intent": "AttackIntent", "source": "intent"})
    if "DefendIntent" in intents and not categories["defense"]:
        categories["defense"].append({"kind": "intentOnly", "intent": "DefendIntent", "source": "intent"})
    if "BuffIntent" in intents and not categories["selfBuff"]:
        categories["selfBuff"].append({"kind": "intentOnly", "intent": "BuffIntent", "source": "intent"})
    if ("DebuffIntent" in intents or "CardDebuffIntent" in intents) and not categories["playerDebuff"]:
        intent_id = "CardDebuffIntent" if "CardDebuffIntent" in intents else "DebuffIntent"
        categories["playerDebuff"].append({"kind": "intentOnly", "intent": intent_id, "source": "intent"})
    if "StatusIntent" in intents and not categories["addCard"]:
        categories["addCard"].append({"kind": "intentOnly", "intent": "StatusIntent", "source": "intent"})

    for intent in intents:
        if intent in SPECIAL_INTENTS:
            categories["special"].append({"kind": "intentOnly", "intent": intent, "source": "intent"})

    if not any(categories.values()):
        categories["special"].append(
            {
                "kind": "unparsed",
                "description": "No supported v1 effect or intent category was parsed.",
                "source": "parser",
            }
        )

    for actions in categories.values():
        for action in actions:
            if action.get("intent"):
                action["intentName"] = intent_name(action.get("intent"))

    return {
        "stateId": move["stateId"],
        "moveMethod": move.get("moveMethod"),
        "intents": intents,
        "intentDetails": [intent_name(intent) for intent in intents],
        "categoryIds": [key for key, values in categories.items() if values],
        "categories": {key: values for key, values in categories.items() if values},
        "followUpStateIds": move.get("followUpStateIds", []),
        "warnings": move.get("warnings", []),
        "confidence": move.get("confidence"),
        "sourcePath": source_hint.get("sourcePath"),
    }


def expand_monster_turns(
    profile: dict[str, Any], categorized_moves: dict[str, dict[str, Any]], turn_count: int
) -> list[dict[str, Any]]:
    moves = profile.get("moves", [])
    moves_by_state = {move["stateId"]: move for move in moves}
    if not moves_by_state:
        return [
            {
                "turn": turn,
                "possibleStateCount": 0,
                "stateWeight": None,
                "states": [],
                "warnings": ["Monster has no parsed moves."],
            }
            for turn in range(1, turn_count + 1)
        ]

    initial = profile.get("initialStateId")
    if initial and initial in moves_by_state:
        current_states = [initial]
        initial_warning = None
    else:
        current_states = sorted(moves_by_state)
        initial_warning = "Initial state is unknown or conditional; using equal-weight move states."

    turns: list[dict[str, Any]] = []
    for turn in range(1, turn_count + 1):
        state_ids = [state for state in current_states if state in moves_by_state]
        states = [categorized_moves[state] for state in state_ids]
        turn_warnings: list[str] = []
        if turn == 1 and initial_warning:
            turn_warnings.append(initial_warning)
        if not states:
            turn_warnings.append("No parsed state was available for this turn.")

        turns.append(
            {
                "turn": turn,
                "possibleStateCount": len(states),
                "stateWeight": round(1 / len(states), 6) if states else None,
                "states": states,
                "warnings": turn_warnings,
            }
        )

        next_states: list[str] = []
        for state_id in state_ids:
            for follow_up in moves_by_state[state_id].get("followUpStateIds", []):
                if follow_up in moves_by_state:
                    next_states.append(follow_up)
        current_states = sorted(set(next_states)) if next_states else sorted(moves_by_state)

    return turns


def action_to_text(category: str, action: dict[str, Any]) -> str:
    if category == "attack":
        total = action.get("ascensionTotal")
        amount = action.get("ascensionAmount")
        hits = action.get("ascensionHitCount")
        if total is not None:
            if hits and hits != 1:
                return f"攻击 {fmt_num(total)} ({fmt_num(amount)}x{fmt_num(hits)})"
            return f"攻击 {fmt_num(total)}"
        return f"攻击 {format_intent(action.get('intent') or action.get('kind'))}"
    if category == "defense":
        amount = action.get("ascensionAmount")
        return f"防御 格挡 {fmt_num(amount)}" if amount is not None else "防御"
    if category == "selfBuff":
        param = format_intent(action["intent"]) if action.get("intent") else action.get("parameter") or action.get("kind")
        amount = action.get("ascensionAmount")
        return f"自buff {param} {fmt_num(amount)}" if amount is not None else f"自buff {param}"
    if category == "playerDebuff":
        param = format_intent(action["intent"]) if action.get("intent") else action.get("parameter") or action.get("kind")
        amount = action.get("ascensionAmount")
        return f"debuff {param} {fmt_num(amount)}" if amount is not None else f"debuff {param}"
    if category == "addCard":
        amount = action.get("ascensionAmount")
        cards = action.get("cards") or []
        card_bits = []
        for card in cards:
            card_name = card.get("cardTypeName") or "unknownCard"
            pile = card.get("pile") or "unknownPile"
            card_bits.append(f"{card_name}->{pile}")
        detail = ",".join(card_bits)
        count_text = fmt_num(amount) if amount is not None else (
            format_intent(action["intent"]) if action.get("intent") else "?"
        )
        return f"塞牌 {count_text}" + (f" [{detail}]" if detail else "")
    if category == "special":
        return f"特殊 {format_intent(action.get('intent')) if action.get('intent') else action.get('kind')}"
    return action.get("kind", category)


def state_to_text(state: dict[str, Any]) -> str:
    parts: list[str] = []
    for category in CATEGORY_LABELS:
        for action in state.get("categories", {}).get(category, []):
            parts.append(action_to_text(category, action))
    return f"{state['stateId']}: " + " + ".join(parts)


def monster_turn_text(turn: dict[str, Any]) -> str:
    states = turn.get("states", [])
    if not states:
        return "no parsed move"
    return " / ".join(state_to_text(state) for state in states)


def encounter_turn_summary(
    encounter: dict[str, Any],
    monster_turns: dict[str, list[dict[str, Any]]],
    monster_names: dict[str, dict[str, Any]],
    turn: int,
) -> dict[str, Any]:
    slot_summaries = []
    category_counts = {key: 0 for key in CATEGORY_LABELS}
    warnings: list[str] = []

    for slot in encounter.get("monsterSlots", []):
        possible_types = (
            [slot["monsterTypeName"]]
            if slot.get("monsterTypeName")
            else slot.get("possibleMonsterTypeNames", [])
        )
        possible_actions = []
        for monster_type in possible_types:
            turns = monster_turns.get(monster_type)
            if not turns:
                possible_actions.append(
                    {
                        "monsterTypeName": monster_type,
                        "monsterName": monster_names.get(monster_type),
                        "turn": turn,
                        "states": [],
                        "warnings": [f"Monster profile was not found for {monster_type}."],
                    }
                )
                warnings.append(f"Monster profile was not found for {monster_type}.")
                continue
            turn_entry = turns[turn - 1]
            for state in turn_entry.get("states", []):
                for category in state.get("categoryIds", []):
                    category_counts[category] += 1
            warnings.extend(turn_entry.get("warnings", []))
            possible_actions.append(
                {
                    "monsterTypeName": monster_type,
                    "monsterName": monster_names.get(monster_type),
                    "possibleStateCount": turn_entry.get("possibleStateCount"),
                    "stateWeight": turn_entry.get("stateWeight"),
                    "states": turn_entry.get("states", []),
                    "warnings": turn_entry.get("warnings", []),
                }
            )
        slot_summaries.append(
            {
                "position": slot.get("position"),
                "slotName": slot.get("slotName"),
                "monsterTypeName": slot.get("monsterTypeName"),
                "possibleMonsterTypeNames": possible_types,
                "possibleMonsterNames": [
                    {
                        "typeName": monster_type,
                        "name": monster_names.get(monster_type),
                    }
                    for monster_type in possible_types
                ],
                "possibleActions": possible_actions,
            }
        )

    return {
        "turn": turn,
        "slots": slot_summaries,
        "categoryCounts": {key: value for key, value in category_counts.items() if value},
        "warnings": sorted(set(warnings)),
    }


def slot_action_to_text(slot_action: dict[str, Any]) -> str:
    slot_name = slot_action.get("slotName") or f"slot{slot_action.get('position')}"
    actions = []
    for possible in slot_action.get("possibleActions", []):
        monster = format_bilingual_name(
            possible.get("monsterName"),
            possible["monsterTypeName"],
        )
        turn = {
            "states": possible.get("states", []),
            "possibleStateCount": possible.get("possibleStateCount"),
        }
        actions.append(f"{monster}: {monster_turn_text(turn)}")
    return f"{slot_name} [" + " || ".join(actions) + "]"


def turn_to_compact_codes(turn: dict[str, Any]) -> str:
    codes = []
    for category in CATEGORY_LABELS:
        if turn.get("categoryCounts", {}).get(category):
            codes.append(CATEGORY_CODES[category])
    return "".join(codes) or "-"


def turn_to_text(turn: dict[str, Any]) -> str:
    return "<br>".join(slot_action_to_text(slot) for slot in turn.get("slots", []))


def intent_list_text(intents: list[str]) -> str:
    return ",".join(format_intent(intent) for intent in intents)


def summarize_move_terms(move: dict[str, Any]) -> str:
    terms = []
    for category in CATEGORY_LABELS:
        for action in move.get("categories", {}).get(category, []):
            terms.append(action_to_text(category, action))
    return "; ".join(terms)


def act_label(encounter: dict[str, Any]) -> str:
    acts = encounter.get("acts", [])
    if not acts:
        return ""
    return ",".join(f"{act.get('actTypeName')}({act.get('actNumber')})" for act in acts)


def build_report(
    profiles: list[dict[str, Any]],
    patterns: list[dict[str, Any]],
    source_hints: dict[tuple[str, str], dict[str, Any]],
    localized_entries: dict[str, dict[str, str]],
    turn_count: int,
    source_paths: dict[str, str],
) -> dict[str, Any]:
    monster_catalog = []
    monster_turns_by_type: dict[str, list[dict[str, Any]]] = {}
    monster_names = {
        profile.get("typeName"): monster_name(profile, localized_entries)
        for profile in profiles
    }

    for profile in sorted(profiles, key=lambda item: item.get("typeName", "")):
        categorized = {
            move["stateId"]: categorize_move(profile["typeName"], move, source_hints)
            for move in profile.get("moves", [])
        }
        turns = expand_monster_turns(profile, categorized, turn_count)
        monster_turns_by_type[profile["typeName"]] = turns
        monster_catalog.append(
            {
                "modelId": profile.get("modelId"),
                "typeName": profile.get("typeName"),
                "name": monster_names.get(profile.get("typeName")),
                "fullTypeName": profile.get("fullTypeName"),
                "hpRange": profile.get("hpRange"),
                "initialStateId": profile.get("initialStateId"),
                "moves": list(categorized.values()),
                "turns": turns,
                "unresolved": profile.get("unresolved", []),
                "confidence": profile.get("confidence"),
                "provenance": profile.get("provenance"),
            }
        )

    encounters = []
    for pattern in sorted(
        patterns,
        key=lambda item: (
            min([act.get("actNumber", 99) for act in item.get("acts", [])] or [99]),
            item.get("category", ""),
            item.get("typeName", ""),
        ),
    ):
        turns = [
            encounter_turn_summary(pattern, monster_turns_by_type, monster_names, turn)
            for turn in range(1, turn_count + 1)
        ]
        encounters.append(
            {
                "modelId": pattern.get("modelId"),
                "typeName": pattern.get("typeName"),
                "fullTypeName": pattern.get("fullTypeName"),
                "acts": pattern.get("acts", []),
                "roomType": pattern.get("roomType"),
                "category": pattern.get("category"),
                "tags": pattern.get("tags", []),
                "monsterSlots": pattern.get("monsterSlots", []),
                "hasConditionalMonsterSelection": pattern.get("hasConditionalMonsterSelection"),
                "warnings": pattern.get("warnings", []),
                "confidence": pattern.get("confidence"),
                "turns": turns,
            }
        )

    needs_review_monsters = sum(
        1
        for profile in monster_catalog
        if profile.get("unresolved")
        or profile.get("confidence", 1) < 0.7
        or any(move.get("warnings") for move in profile.get("moves", []))
    )
    needs_review_encounters = sum(
        1
        for encounter in encounters
        if encounter.get("warnings") or encounter.get("confidence", 1) < 0.7
    )

    return {
        "schemaVersion": 1,
        "generatedAt": dt.datetime.now(dt.timezone.utc).isoformat(),
        "turnCount": turn_count,
        "damageBasis": "Ascension 10 values are primary when ascension-specific values exist; base values remain in each action.",
        "turnExpansionRule": (
            "Start from parsed initialStateId when known; otherwise all move states are equal-weight. "
            "Follow parsed FollowUpStateIds; if none are parsed, the next turn falls back to all move states equal-weight."
        ),
        "sourceFiles": source_paths,
        "nameLocalization": {
            "monsterNames": (
                "Monster names prefer official en/zhs entries from history-analysis/data/localized_names_en_zhs.json "
                "via modelId without the MONSTER. prefix; unmatched mock or segment-only names fall back explicitly."
            ),
            "intentNames": "Intent Chinese/English names use a report-local glossary keyed by decompiled intent typeName.",
            "officialLocalizationStatus": "Monster names use extracted PCK localization; intent class display names are not present in the extracted name map.",
        },
        "intentNames": {intent_id: intent_name(intent_id) for intent_id in sorted(INTENT_NAMES)},
        "actionCategories": [
            {"id": key, "code": CATEGORY_CODES[key], "label": label}
            for key, label in CATEGORY_LABELS.items()
        ],
        "summary": {
            "monsterCount": len(monster_catalog),
            "monsterMoveCount": sum(len(profile.get("moves", [])) for profile in monster_catalog),
            "monsterNeedsReviewCount": needs_review_monsters,
            "encounterCount": len(encounters),
            "encounterNeedsReviewCount": needs_review_encounters,
            "conditionalEncounterCount": sum(
                1 for encounter in encounters if encounter.get("hasConditionalMonsterSelection")
            ),
        },
        "monsterCatalog": monster_catalog,
        "encounters": encounters,
    }


def write_markdown(report: dict[str, Any], path: Path) -> None:
    lines: list[str] = []
    summary = report["summary"]
    lines.append("# Monster Encounter Turn Actions")
    lines.append("")
    lines.append(f"Generated at: {report['generatedAt']}")
    lines.append(f"Turns: 1-{report['turnCount']}")
    lines.append("Damage basis: Ascension 10 values are primary when available.")
    lines.append("")
    lines.append("## Sources")
    lines.append("")
    for key, value in report["sourceFiles"].items():
        lines.append(f"- {key}: `{value}`")
    lines.append("")
    lines.append("## Name Localization")
    lines.append("")
    for key, value in report.get("nameLocalization", {}).items():
        lines.append(f"- {key}: {value}")
    lines.append("")
    lines.append("## Category Legend")
    lines.append("")
    lines.append("| Code | Category | Meaning |")
    lines.append("| --- | --- | --- |")
    descriptions = {
        "attack": "Damage to the player, using total damage when hit count is known.",
        "defense": "Block or defense intent.",
        "selfBuff": "Strength or other power/buff applied to self or team.",
        "playerDebuff": "Weak/Vulnerable/Frail or other power/debuff applied to the player.",
        "addCard": "Status/card added to draw or discard pile when card details are inferable.",
        "special": "Heal, summon, escape, sleep, stun, hidden, deathblow, or unparsed special behavior.",
    }
    for category in report["actionCategories"]:
        lines.append(
            f"| {category['code']} | {category['label']} | {descriptions[category['id']]} |"
        )
    lines.append("")
    lines.append("## Summary")
    lines.append("")
    lines.append("| Metric | Value |")
    lines.append("| --- | ---: |")
    for key, value in summary.items():
        lines.append(f"| {key} | {value} |")
    lines.append("")

    lines.append("## Compact Encounter Matrix")
    lines.append("")
    lines.append("Each turn cell shows category codes present in that encounter turn.")
    lines.append("")
    header = ["Act", "Category", "Encounter", "Conditional"] + [
        f"T{turn}" for turn in range(1, report["turnCount"] + 1)
    ]
    lines.append("| " + " | ".join(header) + " |")
    lines.append("| " + " | ".join(["---"] * len(header)) + " |")
    for encounter in report["encounters"]:
        row = [
            act_label(encounter),
            encounter.get("category", ""),
            encounter.get("typeName", ""),
            "yes" if encounter.get("hasConditionalMonsterSelection") else "no",
        ]
        row.extend(turn_to_compact_codes(turn) for turn in encounter["turns"])
        lines.append("| " + " | ".join(escape_md(item) for item in row) + " |")
    lines.append("")

    lines.append("## Monster Move Catalog")
    lines.append("")
    lines.append("| Monster (ZH / EN) | Type | State | Intents (ZH / EN) | Categories | Parsed terms | Follow-up | Warnings |")
    lines.append("| --- | --- | --- | --- | --- | --- | --- | ---: |")
    for monster in report["monsterCatalog"]:
        for move in monster.get("moves", []):
            categories = ",".join(CATEGORY_CODES[item] for item in move.get("categoryIds", []))
            lines.append(
                "| "
                + " | ".join(
                    [
                        escape_md(format_bilingual_name(monster.get("name"), monster.get("typeName", ""))),
                        escape_md(monster.get("typeName", "")),
                        escape_md(move.get("stateId", "")),
                        escape_md(intent_list_text(move.get("intents", []))),
                        escape_md(categories),
                        escape_md(summarize_move_terms(move)),
                        escape_md(",".join(move.get("followUpStateIds", []))),
                        str(len(move.get("warnings", []))),
                    ]
                )
                + " |"
            )
    lines.append("")

    lines.append("## Detailed Encounter Turns")
    lines.append("")
    lines.append("| Act | Category | Encounter | Turn | Actions | Warnings |")
    lines.append("| --- | --- | --- | ---: | --- | ---: |")
    for encounter in report["encounters"]:
        for turn in encounter["turns"]:
            warnings = len(encounter.get("warnings", [])) + len(turn.get("warnings", []))
            lines.append(
                "| "
                + " | ".join(
                    [
                        escape_md(act_label(encounter)),
                        escape_md(encounter.get("category", "")),
                        escape_md(encounter.get("typeName", "")),
                        str(turn["turn"]),
                        escape_md(turn_to_text(turn)),
                        str(warnings),
                    ]
                )
                + " |"
            )
    lines.append("")

    lines.append("## Review Notes")
    lines.append("")
    lines.append(
        "- Conditional/random encounter slots list every possible monster candidate rather than sampling one actual roll."
    )
    lines.append(
        "- Conditional monster state machines are approximated with equal-weight states when the initial state or branch follow-up is not parsed."
    )
    lines.append(
        "- Buff/debuff powers are intentionally grouped at this stage; the JSON preserves power parameters for later splitting."
    )

    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    profiles_path = Path(args.profiles)
    patterns_path = Path(args.patterns)
    output_json = Path(args.output_json)
    output_md = Path(args.output_md)

    profiles = load_json(profiles_path)
    patterns = load_json(patterns_path)
    localized_entries = load_localized_entries(Path(args.localized_names))
    source_hints = load_source_hints(profiles, Path(args.decompile_root))
    report = build_report(
        profiles,
        patterns,
        source_hints,
        localized_entries,
        args.turns,
        {
            "monsterMoveProfiles": str(profiles_path),
            "encounterPatterns": str(patterns_path),
            "decompileRoot": args.decompile_root,
            "localizedNames": args.localized_names,
        },
    )

    output_json.parent.mkdir(parents=True, exist_ok=True)
    output_md.parent.mkdir(parents=True, exist_ok=True)
    output_json.write_text(
        json.dumps(report, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    write_markdown(report, output_md)

    print("monster encounter turn actions generated")
    print(f"monsters: {report['summary']['monsterCount']}")
    print(f"moves: {report['summary']['monsterMoveCount']}")
    print(f"encounters: {report['summary']['encounterCount']}")
    print(f"turns: 1-{report['turnCount']}")
    print(f"output: {output_json}")
    print(f"report: {output_md}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
