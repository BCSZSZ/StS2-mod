from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "fetch_spire_codex_runs.py"
SPEC = importlib.util.spec_from_file_location("fetch_spire_codex_runs", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load {SCRIPT_PATH}")
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def summary_card(model_id: str, total_runs_with: int) -> dict[str, object]:
    return {
        "modelId": model_id,
        "totalRunsWith": total_runs_with,
        "totalCopies": total_runs_with,
        "avgCopiesWhenPresent": 1.0,
        "plus0FinalRunCount": total_runs_with,
        "plus0AppearanceProbability": 0.0,
        "plus1FinalRunCount": 0,
        "plus1AppearanceProbability": 0.0,
        "plus0OfferCount": 10,
        "plus0PickCount": 5,
        "plus0PickRate": 0.5,
        "plus1OfferCount": 0,
        "plus1PickCount": 0,
        "plus1PickRate": 0.0,
        "plus0ShopOfferCount": 4,
        "plus0ShopBuyCount": 2,
        "plus0ShopBuyRate": 0.5,
        "plus1ShopOfferCount": 0,
        "plus1ShopBuyCount": 0,
        "plus1ShopBuyRate": 0.0,
    }


class SpireCodexSummaryTests(unittest.TestCase):
    def test_all_official_card_pools_are_mapped(self) -> None:
        self.assertEqual(
            {
                "Ironclad",
                "Silent",
                "Defect",
                "Necrobinder",
                "Regent",
            },
            set(MODULE.OFFICIAL_CARD_POOL_CHARACTERS),
        )

    def test_official_character_filter_excludes_mod_characters(self) -> None:
        run = {
            "players": [
                {"character": "CHARACTER.IRONCLAD"},
                {"character": "CHARACTER.MODDED_HERO"},
            ]
        }

        players = MODULE.summary_players(run, official_characters_only=True)

        self.assertEqual([{"character": "CHARACTER.IRONCLAD"}], players)

    def test_stale_page_cache_is_removed_after_terminal_page(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            for page in (1, 2, 3, 4):
                (root / f"page-{page:06d}.json").write_text("{}", encoding="utf-8")
            (root / "notes.json").write_text("{}", encoding="utf-8")

            MODULE.remove_stale_page_cache(root, last_page=2)

            self.assertTrue((root / "page-000001.json").exists())
            self.assertTrue((root / "page-000002.json").exists())
            self.assertFalse((root / "page-000003.json").exists())
            self.assertFalse((root / "page-000004.json").exists())
            self.assertTrue((root / "notes.json").exists())

    def test_card_metadata_uses_pool_and_basic_rarity(self) -> None:
        memberships = [
            {"modelId": "CARD.IRON", "pools": ["Ironclad"]},
            {"modelId": "CARD.BASIC", "pools": ["Silent"]},
            {"modelId": "CARD.COLOR", "pools": ["Colorless"]},
            {"modelId": "CARD.CURSE", "pools": ["Curse"]},
        ]
        facts = [
            {"modelId": "CARD.IRON", "rarity": "Uncommon"},
            {"modelId": "CARD.BASIC", "rarity": "Basic"},
            {"modelId": "CARD.COLOR", "rarity": "Rare"},
            {"modelId": "CARD.CURSE", "rarity": "Curse"},
        ]
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            memberships_path = root / "memberships.json"
            facts_path = root / "facts.json"
            memberships_path.write_text(json.dumps(memberships), encoding="utf-8")
            facts_path.write_text(json.dumps(facts), encoding="utf-8")
            metadata = MODULE.load_card_distribution_metadata(
                memberships_path,
                facts_path,
            )

        self.assertEqual("Ironclad", metadata["CARD.IRON"]["sourcePool"])
        self.assertEqual(
            "CHARACTER.IRONCLAD",
            metadata["CARD.IRON"]["sourceCharacter"],
        )
        self.assertTrue(metadata["CARD.IRON"]["copyDistributionEligible"])
        self.assertFalse(metadata["CARD.BASIC"]["copyDistributionEligible"])
        self.assertTrue(metadata["CARD.COLOR"]["isColorless"])
        self.assertIsNone(metadata["CARD.CURSE"]["sourcePool"])
        self.assertFalse(metadata["CARD.CURSE"]["isColorless"])

    def test_runtime_card_and_colorless_variants_use_character_cohorts(self) -> None:
        all_iron = summary_card("CARD.IRON", 40)
        character_iron = summary_card("CARD.IRON", 35)
        all_colorless = summary_card("CARD.COLOR", 30)
        output = {
            "generatedAt": "2026-07-18T00:00:00Z",
            "scope": {"filters": {"ascension": 10}},
            "groups": [
                {
                    "key": "all",
                    "totalRuns": 300,
                    "cards": [all_iron, all_colorless],
                },
                {
                    "key": "character:CHARACTER.IRONCLAD",
                    "totalRuns": 100,
                    "cards": [character_iron, summary_card("CARD.COLOR", 10)],
                },
                {
                    "key": "character:CHARACTER.SILENT",
                    "totalRuns": 60,
                    "cards": [summary_card("CARD.COLOR", 20)],
                },
                {
                    "key": "character:CHARACTER.DEFECT",
                    "totalRuns": 50,
                    "cards": [summary_card("CARD.COLOR", 15)],
                },
                {
                    "key": "character:CHARACTER.NECROBINDER",
                    "totalRuns": 40,
                    "cards": [summary_card("CARD.COLOR", 8)],
                },
                {
                    "key": "character:CHARACTER.REGENT",
                    "totalRuns": 50,
                    "cards": [summary_card("CARD.COLOR", 25)],
                },
            ],
        }
        metadata = {
            "CARD.IRON": {
                "pools": ["Ironclad"],
                "sourcePool": "Ironclad",
                "sourceCharacter": "CHARACTER.IRONCLAD",
                "isColorless": False,
                "copyDistributionEligible": True,
            },
            "CARD.COLOR": {
                "pools": ["Colorless"],
                "sourcePool": None,
                "sourceCharacter": None,
                "isColorless": True,
                "copyDistributionEligible": True,
            },
            "CARD.UNSEEN": {
                "pools": ["Ironclad"],
                "sourcePool": "Ironclad",
                "sourceCharacter": "CHARACTER.IRONCLAD",
                "isColorless": False,
                "copyDistributionEligible": True,
            },
        }

        runtime = MODULE.build_runtime_adoption_output(output, metadata)

        self.assertEqual(3, runtime["schemaVersion"])
        self.assertEqual(300, runtime["totalRuns"])
        iron = runtime["cards"]["CARD.IRON"]["variants"]["CHARACTER.IRONCLAD"]
        self.assertEqual(100, iron["sampleRuns"])
        self.assertEqual(35, iron["totalRunsWith"])
        colorless = runtime["cards"]["CARD.COLOR"]["variants"]
        self.assertEqual(5, len(colorless))
        self.assertEqual(100, colorless["CHARACTER.IRONCLAD"]["sampleRuns"])
        self.assertEqual(10, colorless["CHARACTER.IRONCLAD"]["totalRunsWith"])
        self.assertEqual(50, colorless["CHARACTER.REGENT"]["sampleRuns"])
        self.assertEqual(25, colorless["CHARACTER.REGENT"]["totalRunsWith"])
        self.assertEqual(
            "Regent:Colorless",
            colorless["CHARACTER.REGENT"]["distributionGroup"],
        )
        unseen = runtime["cards"]["CARD.UNSEEN"]["variants"]["CHARACTER.IRONCLAD"]
        self.assertEqual(100, unseen["sampleRuns"])
        self.assertEqual(0, unseen["totalRunsWith"])

    def test_runtime_ancient_choices_use_character_cohorts(self) -> None:
        groups = [
            {
                "key": "all",
                "totalRuns": 50,
                "totalAncientChoiceScreens": 100,
                "ancientChoices": [],
            }
        ]
        outcome_groups = [
            {
                "key": "all",
                "totalRuns": 80,
                "totalWins": 50,
                "totalAncientChoiceScreens": 140,
                "ancientChoices": [],
            }
        ]
        for index, character_id in enumerate(
            MODULE.OFFICIAL_CARD_POOL_CHARACTERS.values(),
            start=1,
        ):
            groups.append(
                {
                    "key": f"character:{character_id}",
                    "totalRuns": index * 10,
                    "totalAncientChoiceScreens": index * 20,
                    "ancientChoices": [
                        {
                            "textKey": "SAME_OPTION",
                            "offerCount": index * 4,
                            "pickCount": index,
                            "pickRate": 0.25,
                        }
                    ],
                }
            )
            outcome_groups.append(
                {
                    "key": f"character:{character_id}",
                    "totalRuns": index * 16,
                    "totalWins": index * 10,
                    "totalAncientChoiceScreens": index * 28,
                    "ancientChoices": [
                        {
                            "textKey": "SAME_OPTION",
                            "pickedRunCount": index * 3,
                            "pickedWinCount": index * 2,
                            "pickedWinRate": 2 / 3,
                        }
                    ],
                }
            )
        output = {
            "generatedAt": "2026-07-18T00:00:00Z",
            "scope": {"filters": {"ascension": 10}},
            "groups": groups,
            "ancientOutcomeGroups": outcome_groups,
        }

        runtime = MODULE.build_runtime_ancient_choice_output(output)

        self.assertEqual(3, runtime["schemaVersion"])
        self.assertNotIn("choices", runtime)
        self.assertEqual(5, len(runtime["characters"]))
        regent = runtime["characters"]["CHARACTER.REGENT"]
        self.assertEqual(50, regent["sampleRuns"])
        self.assertEqual(100, regent["totalChoiceScreens"])
        self.assertEqual(80, regent["outcomeSampleRuns"])
        self.assertEqual(50, regent["outcomeWins"])
        self.assertEqual(140, regent["outcomeChoiceScreens"])
        self.assertEqual(20, regent["choices"]["SAME_OPTION"]["offerCount"])
        self.assertEqual(5, regent["choices"]["SAME_OPTION"]["pickCount"])
        self.assertEqual(15, regent["choices"]["SAME_OPTION"]["pickedRunCount"])
        self.assertEqual(10, regent["choices"]["SAME_OPTION"]["pickedWinCount"])
        self.assertEqual(2 / 3, regent["choices"]["SAME_OPTION"]["pickedWinRate"])

    def test_ancient_picked_outcomes_count_once_per_run(self) -> None:
        groups: dict[str, dict[str, object]] = {}
        screens = [
            [("ANCIENT.TEST.options.SAME", True)],
            [("SAME", True), ("OTHER", False)],
        ]
        MODULE.update_ancient_outcome_group(
            groups,
            key="character:CHARACTER.REGENT",
            build_id="",
            character="CHARACTER.REGENT",
            won=True,
            ancient_choice_screens=screens,
        )
        MODULE.update_ancient_outcome_group(
            groups,
            key="character:CHARACTER.REGENT",
            build_id="",
            character="CHARACTER.REGENT",
            won=False,
            ancient_choice_screens=screens,
        )

        group = groups["character:CHARACTER.REGENT"]
        rows = MODULE.finalize_ancient_outcomes(group["_ancientChoices"])

        self.assertEqual(2, group["totalRuns"])
        self.assertEqual(1, group["totalWins"])
        self.assertEqual(4, group["totalAncientChoiceScreens"])
        self.assertEqual(1, len(rows))
        self.assertEqual(2, rows[0]["pickedRunCount"])
        self.assertEqual(1, rows[0]["pickedWinCount"])
        self.assertEqual(0.5, rows[0]["pickedWinRate"])

    def test_ancient_outcomes_merge_role_specific_version_counts(self) -> None:
        runtime = {
            "schemaVersion": 3,
            "scope": {"filters": {"win": "true"}},
            "characters": {
                "CHARACTER.REGENT": {
                    "sampleRuns": 10,
                    "totalChoiceScreens": 30,
                    "choices": {
                        "OPTION_A": {
                            "offerCount": 8,
                            "pickCount": 3,
                            "pickRate": 0.375,
                        }
                    },
                }
            },
        }
        community = {
            "v1": {"by_character": [{"id": "REGENT", "runs": 20, "wins": 4}]},
            "v2": {"by_character": [{"id": "REGENT", "runs": 5, "wins": 1}]},
        }
        entity = {
            "OPTION_A": {
                "brackets": {
                    "solo:a10:v1": {
                        "by_character": [
                            {"character": "REGENT", "picks": 7, "wins": 2}
                        ]
                    },
                    "solo:a10:v2": {
                        "by_character": [
                            {"character": "REGENT", "picks": 2, "wins": 1}
                        ]
                    },
                }
            }
        }

        outcome = MODULE.build_ancient_outcome_output(
            runtime,
            ["v1", "v2"],
            community,
            entity,
            {"data_through": "2026-07-20T00:00:00Z"},
        )
        merged = MODULE.merge_ancient_outcomes(runtime, outcome)

        regent = outcome["characters"]["CHARACTER.REGENT"]
        choice = regent["choices"]["OPTION_A"]
        self.assertEqual(25, regent["outcomeSampleRuns"])
        self.assertEqual(5, regent["outcomeWins"])
        self.assertEqual(9, regent["outcomeChoiceScreens"])
        self.assertEqual(9, choice["pickedRunCount"])
        self.assertEqual(3, choice["pickedWinCount"])
        self.assertEqual(1 / 3, choice["pickedWinRate"])
        self.assertEqual(0, choice["winnerPickCountDifference"])
        merged_choice = merged["characters"]["CHARACTER.REGENT"]["choices"]["OPTION_A"]
        self.assertEqual(8, merged_choice["offerCount"])
        self.assertEqual(9, merged_choice["pickedRunCount"])
        self.assertEqual(3, merged_choice["pickedWinCount"])


if __name__ == "__main__":
    unittest.main()
