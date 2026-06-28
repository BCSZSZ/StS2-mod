from __future__ import annotations

import json

from search_policy_training.data import build_prepared_dataset, read_decision_groups


def test_prepare_dataset_keeps_grouped_actions(tmp_path):
    path = tmp_path / "teacher.jsonl"
    group = {
        "schemaVersion": 1,
        "groupId": "g1",
        "source": "test",
        "run": 0,
        "turn": 1,
        "actionsPlayed": 0,
        "contextFeatures": {"context.energy": 3},
        "actions": [
            {
                "cardModelId": "CARD.A",
                "cardTypeName": "A",
                "instanceId": 1,
                "features": {"card.damageValue": 6},
                "heuristicScore": 1.0,
                "teacherRouteValue": 8.0,
                "teacherRank": 1,
            },
            {
                "cardModelId": "CARD.B",
                "cardTypeName": "B",
                "instanceId": 2,
                "features": {"card.draw": 1},
                "heuristicScore": 0.5,
                "teacherRouteValue": 4.0,
                "teacherRank": 2,
            },
        ],
        "teacherBestActionIndex": 0,
        "metadata": {
            "runId": "run-a",
            "deckIndex": 0,
            "variant": "baseline",
            "teacherMaxBranchingCards": 8,
            "teacherMaxCardsPlayedPerTurn": 8,
        },
    }
    path.write_text(json.dumps(group) + "\n", encoding="utf-8")

    groups = read_decision_groups(path)
    dataset = build_prepared_dataset(groups, train_ratio=0.7, val_ratio=0.1)

    assert dataset.features.shape == (2, 3)
    assert dataset.group_ids.tolist() == [0, 0]
    assert dataset.card_vocab[0] == "<UNK>"
    assert "context.energy" in dataset.feature_names
    assert "card.damageValue" in dataset.feature_names
