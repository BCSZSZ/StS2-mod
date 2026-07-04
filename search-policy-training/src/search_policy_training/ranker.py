from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import numpy as np
import torch
from torch import nn
import torch.nn.functional as F

from search_policy_training import FEATURE_VERSION, SCHEMA_VERSION
from search_policy_training.data import (
    SPLIT_TEST,
    SPLIT_TRAIN,
    SPLIT_VAL,
    PreparedDataset,
    compute_normalization,
    group_indices,
    load_prepared_dataset,
    make_dense_inputs,
)


class SearchRanker(nn.Module):
    def __init__(self, input_size: int) -> None:
        super().__init__()
        self.layers = nn.Sequential(
            nn.Linear(input_size, 128),
            nn.ReLU(),
            nn.Linear(128, 64),
            nn.ReLU(),
            nn.Linear(64, 1),
        )

    def forward(self, inputs: torch.Tensor) -> torch.Tensor:
        return self.layers(inputs).squeeze(-1)


@dataclass(frozen=True)
class TrainConfig:
    epochs: int = 20
    learning_rate: float = 1e-3
    mse_weight: float = 0.2
    seed: int = 1


def train_ranker(dataset_path: Path, output_path: Path, config: TrainConfig) -> dict[str, Any]:
    torch.manual_seed(config.seed)
    np.random.seed(config.seed)

    dataset = load_prepared_dataset(dataset_path)
    numeric_mean, numeric_std, label_mean, label_std = compute_normalization(dataset)
    inputs = make_dense_inputs(dataset, numeric_mean, numeric_std)
    input_tensor = torch.from_numpy(inputs)
    label_tensor = torch.from_numpy(((dataset.labels - label_mean) / label_std).astype(np.float32))
    raw_label_tensor = torch.from_numpy(dataset.labels.astype(np.float32))

    model = SearchRanker(dataset.input_size)
    optimizer = torch.optim.AdamW(model.parameters(), lr=config.learning_rate)
    train_groups = group_indices(dataset, SPLIT_TRAIN)
    if not train_groups:
        train_groups = group_indices(dataset, None)

    for epoch in range(1, config.epochs + 1):
        model.train()
        np.random.shuffle(train_groups)
        total_loss = 0.0
        trained_groups = 0
        for indices in train_groups:
            index_tensor = torch.from_numpy(indices)
            scores = model(input_tensor[index_tensor])
            labels = raw_label_tensor[index_tensor]
            ranking_loss = pairwise_ranking_loss(scores, labels)
            if ranking_loss is None:
                continue
            mse_loss = F.mse_loss(scores, label_tensor[index_tensor])
            loss = ranking_loss + config.mse_weight * mse_loss
            optimizer.zero_grad()
            loss.backward()
            optimizer.step()
            total_loss += float(loss.detach())
            trained_groups += 1

        if epoch == config.epochs or epoch == 1 or epoch % 5 == 0:
            metrics = evaluate_model(model, dataset, inputs, split=SPLIT_VAL)
            mean_loss = total_loss / max(1, trained_groups)
            print(f"epoch={epoch} loss={mean_loss:.6f} val_top2={metrics['top2Recall']:.4f} val_mean_regret={metrics['meanRegret']:.4f}")

    output_path.parent.mkdir(parents=True, exist_ok=True)
    checkpoint = {
        "schemaVersion": SCHEMA_VERSION,
        "featureVersion": FEATURE_VERSION,
        "stateDict": model.state_dict(),
        "numericFeatureNames": dataset.feature_names,
        "cardIdVocab": dataset.card_vocab,
        "normalization": {
            "mean": numeric_mean.tolist(),
            "std": numeric_std.tolist(),
        },
        "labelNormalization": {
            "mean": label_mean,
            "std": label_std,
        },
        "metadata": {
            "datasetPath": str(dataset_path),
            "epochs": config.epochs,
            "learningRate": config.learning_rate,
            "mseWeight": config.mse_weight,
        },
    }
    torch.save(checkpoint, output_path)
    return evaluate_checkpoint(output_path, dataset_path, split=SPLIT_TEST)


def pairwise_ranking_loss(scores: torch.Tensor, labels: torch.Tensor) -> torch.Tensor | None:
    label_diff = labels[:, None] - labels[None, :]
    mask = label_diff > 1e-7
    if not bool(mask.any()):
        return None
    score_diff = scores[:, None] - scores[None, :]
    return F.softplus(-score_diff[mask]).mean()


def evaluate_checkpoint(
    checkpoint_path: Path,
    dataset_path: Path,
    split: int = SPLIT_TEST,
) -> dict[str, float]:
    checkpoint = torch.load(checkpoint_path, map_location="cpu", weights_only=False)
    dataset = load_prepared_dataset(dataset_path)
    model = SearchRanker(dataset.input_size)
    model.load_state_dict(checkpoint["stateDict"])
    inputs = make_dense_inputs(
        dataset,
        np.asarray(checkpoint["normalization"]["mean"], dtype=np.float32),
        np.asarray(checkpoint["normalization"]["std"], dtype=np.float32),
    )
    return evaluate_model(model, dataset, inputs, split)


def evaluate_model(
    model: SearchRanker,
    dataset: PreparedDataset,
    inputs: np.ndarray,
    split: int = SPLIT_TEST,
) -> dict[str, float]:
    model.eval()
    with torch.no_grad():
        scores = model(torch.from_numpy(inputs)).numpy()

    groups = group_indices(dataset, split)
    if not groups and split != SPLIT_TRAIN:
        groups = group_indices(dataset, SPLIT_VAL)
    if not groups:
        groups = group_indices(dataset, None)

    top1_hits = 0
    top2_hits = 0
    top3_hits = 0
    regrets: list[float] = []
    ndcgs: list[float] = []
    for indices in groups:
        labels = dataset.labels[indices]
        group_scores = scores[indices]
        teacher_best = int(np.argmax(labels))
        predicted_order = np.argsort(-group_scores, kind="stable")
        predicted_best = int(predicted_order[0])
        top1_hits += int(predicted_best == teacher_best)
        top2_hits += int(teacher_best in predicted_order[:2])
        top3_hits += int(teacher_best in predicted_order[:3])
        regrets.append(float(labels[teacher_best] - labels[predicted_best]))
        ndcgs.append(_ndcg_at_2(labels, predicted_order))

    denominator = max(1, len(groups))
    return {
        "groups": float(len(groups)),
        "top1Accuracy": top1_hits / denominator,
        "top2Recall": top2_hits / denominator,
        "top3Recall": top3_hits / denominator,
        "ndcgAt2": float(np.mean(ndcgs)) if ndcgs else 0.0,
        "meanRegret": float(np.mean(regrets)) if regrets else 0.0,
        "p95Regret": float(np.percentile(regrets, 95)) if regrets else 0.0,
    }


def _ndcg_at_2(labels: np.ndarray, predicted_order: np.ndarray) -> float:
    gains = labels - labels.min()
    discounts = 1.0 / np.log2(np.arange(2, 4))
    predicted = gains[predicted_order[:2]]
    ideal = np.sort(gains)[::-1][:2]
    ideal_dcg = float(np.sum(ideal * discounts[: len(ideal)]))
    if ideal_dcg <= 1e-9:
        return 1.0
    return float(np.sum(predicted * discounts[: len(predicted)]) / ideal_dcg)
