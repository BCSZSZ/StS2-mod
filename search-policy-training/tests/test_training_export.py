from __future__ import annotations

import json

from search_policy_training.data import build_prepared_dataset, save_prepared_dataset
from search_policy_training.export import export_checkpoint_to_json
from search_policy_training.ranker import TrainConfig, train_ranker


def test_train_and_export_tiny_ranker(tmp_path):
    groups = [
        _group("g1", "run-a", 3.0, 8.0, 4.0),
        _group("g2", "run-b", 2.0, 3.0, 7.0),
    ]
    dataset = build_prepared_dataset(groups, train_ratio=0.5, val_ratio=0.25)
    dataset_path = tmp_path / "dataset.npz"
    checkpoint_path = tmp_path / "ranker.pt"
    model_path = tmp_path / "ranker.json"
    save_prepared_dataset(dataset, dataset_path)

    train_ranker(dataset_path, checkpoint_path, TrainConfig(epochs=1, seed=7))
    model_json = export_checkpoint_to_json(checkpoint_path, model_path)
    written = json.loads(model_path.read_text(encoding="utf-8"))

    assert model_json["schemaVersion"] == 1
    assert model_json["featureVersion"] == 1
    assert written["layers"][0]["activation"] == "relu"
    assert written["layers"][-1]["activation"] == "linear"
    assert len(written["normalization"]["mean"]) == len(written["numericFeatureNames"])
    assert written["cardIdVocab"][0] == "<UNK>"


def _group(group_id: str, run_id: str, energy: float, value_a: float, value_b: float):
    return {
        "schemaVersion": 1,
        "groupId": group_id,
        "source": "test",
        "run": 0,
        "turn": 1,
        "actionsPlayed": 0,
        "contextFeatures": {"context.energy": energy},
        "actions": [
            {
                "cardModelId": "CARD.A",
                "cardTypeName": "A",
                "instanceId": 1,
                "features": {"card.damageValue": 6},
                "heuristicScore": 1.0,
                "teacherRouteValue": value_a,
                "teacherRank": 1 if value_a >= value_b else 2,
            },
            {
                "cardModelId": "CARD.B",
                "cardTypeName": "B",
                "instanceId": 2,
                "features": {"card.draw": 1},
                "heuristicScore": 0.5,
                "teacherRouteValue": value_b,
                "teacherRank": 1 if value_b > value_a else 2,
            },
        ],
        "teacherBestActionIndex": 0 if value_a >= value_b else 1,
        "metadata": {
            "runId": run_id,
            "deckIndex": 0,
            "variant": "baseline",
            "teacherMaxBranchingCards": 8,
            "teacherMaxCardsPlayedPerTurn": 8,
        },
    }
