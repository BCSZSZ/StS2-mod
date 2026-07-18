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


if __name__ == "__main__":
    unittest.main()
