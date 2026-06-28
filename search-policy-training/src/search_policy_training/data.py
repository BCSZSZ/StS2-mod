from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
from typing import Any

import numpy as np

from search_policy_training import UNKNOWN_CARD_TOKEN


SPLIT_TRAIN = 0
SPLIT_VAL = 1
SPLIT_TEST = 2


@dataclass(frozen=True)
class PreparedDataset:
    features: np.ndarray
    labels: np.ndarray
    group_ids: np.ndarray
    action_indices: np.ndarray
    card_id_indices: np.ndarray
    splits: np.ndarray
    feature_names: list[str]
    card_vocab: list[str]
    group_keys: list[str]

    @property
    def input_size(self) -> int:
        return len(self.feature_names) + len(self.card_vocab)


def read_decision_groups(path: Path, max_groups: int | None = None) -> list[dict[str, Any]]:
    groups: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as handle:
        for line in handle:
            if not line.strip():
                continue
            group = json.loads(line)
            if len(group.get("actions", [])) >= 2:
                groups.append(group)
            if max_groups is not None and len(groups) >= max_groups:
                break
    return groups


def build_prepared_dataset(
    groups: list[dict[str, Any]],
    train_ratio: float = 0.8,
    val_ratio: float = 0.1,
) -> PreparedDataset:
    if not groups:
        raise ValueError("No decision groups were provided.")
    if not 0 < train_ratio < 1:
        raise ValueError("train_ratio must be between 0 and 1.")
    if not 0 <= val_ratio < 1:
        raise ValueError("val_ratio must be between 0 and 1.")
    if train_ratio + val_ratio >= 1:
        raise ValueError("train_ratio + val_ratio must be less than 1.")

    feature_names = sorted(_collect_feature_names(groups))
    feature_index = {name: index for index, name in enumerate(feature_names)}
    card_vocab = [UNKNOWN_CARD_TOKEN, *sorted(_collect_card_ids(groups))]
    card_index = {card_id: index for index, card_id in enumerate(card_vocab)}

    rows: list[np.ndarray] = []
    labels: list[float] = []
    group_ids: list[int] = []
    action_indices: list[int] = []
    card_id_indices: list[int] = []
    splits: list[int] = []
    group_keys: list[str] = []

    for group_index, group in enumerate(groups):
        group_key = _group_split_key(group)
        group_keys.append(group.get("groupId") or group_key)
        split = _split_for_key(group_key, train_ratio, val_ratio)
        context_features = group.get("contextFeatures", {})
        for action_index, action in enumerate(group.get("actions", [])):
            vector = np.zeros(len(feature_names), dtype=np.float32)
            _apply_features(vector, feature_index, context_features)
            _apply_features(vector, feature_index, action.get("features", {}))
            rows.append(vector)
            labels.append(float(action["teacherRouteValue"]))
            group_ids.append(group_index)
            action_indices.append(action_index)
            card_id_indices.append(card_index.get(action["cardModelId"], 0))
            splits.append(split)

    return PreparedDataset(
        features=np.vstack(rows).astype(np.float32),
        labels=np.asarray(labels, dtype=np.float32),
        group_ids=np.asarray(group_ids, dtype=np.int64),
        action_indices=np.asarray(action_indices, dtype=np.int64),
        card_id_indices=np.asarray(card_id_indices, dtype=np.int64),
        splits=np.asarray(splits, dtype=np.int8),
        feature_names=feature_names,
        card_vocab=card_vocab,
        group_keys=group_keys,
    )


def save_prepared_dataset(dataset: PreparedDataset, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    np.savez_compressed(
        path,
        features=dataset.features,
        labels=dataset.labels,
        group_ids=dataset.group_ids,
        action_indices=dataset.action_indices,
        card_id_indices=dataset.card_id_indices,
        splits=dataset.splits,
        feature_names=np.asarray(dataset.feature_names),
        card_vocab=np.asarray(dataset.card_vocab),
        group_keys=np.asarray(dataset.group_keys),
    )


def load_prepared_dataset(path: Path) -> PreparedDataset:
    data = np.load(path, allow_pickle=False)
    return PreparedDataset(
        features=data["features"].astype(np.float32),
        labels=data["labels"].astype(np.float32),
        group_ids=data["group_ids"].astype(np.int64),
        action_indices=data["action_indices"].astype(np.int64),
        card_id_indices=data["card_id_indices"].astype(np.int64),
        splits=data["splits"].astype(np.int8),
        feature_names=[str(item) for item in data["feature_names"].tolist()],
        card_vocab=[str(item) for item in data["card_vocab"].tolist()],
        group_keys=[str(item) for item in data["group_keys"].tolist()],
    )


def write_dataset_metadata(dataset: PreparedDataset, path: Path) -> None:
    split_counts = {
        "train": int(np.sum(dataset.splits == SPLIT_TRAIN)),
        "val": int(np.sum(dataset.splits == SPLIT_VAL)),
        "test": int(np.sum(dataset.splits == SPLIT_TEST)),
    }
    metadata = {
        "actions": int(dataset.features.shape[0]),
        "groups": int(len(set(dataset.group_ids.tolist()))),
        "numericFeatures": len(dataset.feature_names),
        "cardVocab": len(dataset.card_vocab),
        "splitActionCounts": split_counts,
        "featureNames": dataset.feature_names,
        "cardVocabItems": dataset.card_vocab,
    }
    path.write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")


def make_dense_inputs(
    dataset: PreparedDataset,
    numeric_mean: np.ndarray,
    numeric_std: np.ndarray,
) -> np.ndarray:
    numeric = (dataset.features - numeric_mean) / numeric_std
    one_hot = np.zeros((dataset.features.shape[0], len(dataset.card_vocab)), dtype=np.float32)
    one_hot[np.arange(dataset.features.shape[0]), dataset.card_id_indices] = 1.0
    return np.concatenate([numeric.astype(np.float32), one_hot], axis=1)


def compute_normalization(dataset: PreparedDataset) -> tuple[np.ndarray, np.ndarray, float, float]:
    train_mask = dataset.splits == SPLIT_TRAIN
    if not np.any(train_mask):
        train_mask = np.ones_like(dataset.splits, dtype=bool)
    numeric_mean = dataset.features[train_mask].mean(axis=0).astype(np.float32)
    numeric_std = dataset.features[train_mask].std(axis=0).astype(np.float32)
    numeric_std[numeric_std < 1e-6] = 1.0
    label_mean = float(dataset.labels[train_mask].mean())
    label_std = float(dataset.labels[train_mask].std())
    if label_std < 1e-6:
        label_std = 1.0
    return numeric_mean, numeric_std, label_mean, label_std


def group_indices(dataset: PreparedDataset, split: int | None = None) -> list[np.ndarray]:
    groups: list[np.ndarray] = []
    for group_id in np.unique(dataset.group_ids):
        indices = np.flatnonzero(dataset.group_ids == group_id)
        if split is not None and not np.any(dataset.splits[indices] == split):
            continue
        if split is not None:
            indices = indices[dataset.splits[indices] == split]
        if len(indices) >= 2:
            groups.append(indices)
    return groups


def _collect_feature_names(groups: list[dict[str, Any]]) -> set[str]:
    names: set[str] = set()
    for group in groups:
        names.update(group.get("contextFeatures", {}).keys())
        for action in group.get("actions", []):
            names.update(action.get("features", {}).keys())
    return names


def _collect_card_ids(groups: list[dict[str, Any]]) -> set[str]:
    card_ids: set[str] = set()
    for group in groups:
        for action in group.get("actions", []):
            card_ids.add(str(action["cardModelId"]))
    return card_ids


def _apply_features(
    vector: np.ndarray,
    feature_index: dict[str, int],
    features: dict[str, Any],
) -> None:
    for name, value in features.items():
        index = feature_index.get(name)
        if index is not None:
            vector[index] = float(value)


def _group_split_key(group: dict[str, Any]) -> str:
    metadata = group.get("metadata", {})
    return str(metadata.get("runId") or group.get("groupId") or len(group.get("actions", [])))


def _split_for_key(key: str, train_ratio: float, val_ratio: float) -> int:
    digest = hashlib.sha256(key.encode("utf-8")).digest()
    bucket = int.from_bytes(digest[:8], "big") / float(2**64 - 1)
    if bucket < train_ratio:
        return SPLIT_TRAIN
    if bucket < train_ratio + val_ratio:
        return SPLIT_VAL
    return SPLIT_TEST
