from __future__ import annotations

import argparse
import json
from pathlib import Path

from search_policy_training.data import (
    build_prepared_dataset,
    read_decision_groups,
    save_prepared_dataset,
    write_dataset_metadata,
)
from search_policy_training.export import export_checkpoint_to_json
from search_policy_training.ranker import TrainConfig, evaluate_checkpoint, train_ranker


DEFAULT_TEACHER_JSONL = Path("../data/generated/search_policy/search_policy_teacher.generated.jsonl")
DEFAULT_DATASET = Path("../data/generated/search_policy/search_policy_dataset.npz")
DEFAULT_CHECKPOINT = Path("../data/generated/search_policy/search_policy_ranker.pt")
DEFAULT_MODEL_JSON = Path("../data/manual-tags/search_policy_ranker.json")


def prepare_dataset_main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=Path, default=DEFAULT_TEACHER_JSONL)
    parser.add_argument("--output", type=Path, default=DEFAULT_DATASET)
    parser.add_argument("--max-groups", type=int)
    parser.add_argument("--train-ratio", type=float, default=0.8)
    parser.add_argument("--val-ratio", type=float, default=0.1)
    args = parser.parse_args()

    groups = read_decision_groups(args.input, args.max_groups)
    dataset = build_prepared_dataset(groups, args.train_ratio, args.val_ratio)
    save_prepared_dataset(dataset, args.output)
    write_dataset_metadata(dataset, args.output.with_suffix(".meta.json"))
    print(f"groups={len(groups)} actions={dataset.features.shape[0]} features={len(dataset.feature_names)} output={args.output}")


def train_ranker_main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", type=Path, default=DEFAULT_DATASET)
    parser.add_argument("--output", type=Path, default=DEFAULT_CHECKPOINT)
    parser.add_argument("--epochs", type=int, default=20)
    parser.add_argument("--lr", type=float, default=1e-3)
    parser.add_argument("--mse-weight", type=float, default=0.2)
    parser.add_argument("--seed", type=int, default=1)
    args = parser.parse_args()

    metrics = train_ranker(
        args.dataset,
        args.output,
        TrainConfig(
            epochs=args.epochs,
            learning_rate=args.lr,
            mse_weight=args.mse_weight,
            seed=args.seed,
        ),
    )
    print(json.dumps(metrics, ensure_ascii=False, indent=2))
    print(f"checkpoint={args.output}")


def export_model_main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--checkpoint", type=Path, default=DEFAULT_CHECKPOINT)
    parser.add_argument("--output", type=Path, default=DEFAULT_MODEL_JSON)
    args = parser.parse_args()

    model_json = export_checkpoint_to_json(args.checkpoint, args.output)
    print(f"model={args.output} features={len(model_json['numericFeatureNames'])} cards={len(model_json['cardIdVocab'])}")


def eval_ranker_main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--checkpoint", type=Path, default=DEFAULT_CHECKPOINT)
    parser.add_argument("--dataset", type=Path, default=DEFAULT_DATASET)
    parser.add_argument("--split", choices=["train", "val", "test"], default="test")
    args = parser.parse_args()

    split = {"train": 0, "val": 1, "test": 2}[args.split]
    metrics = evaluate_checkpoint(args.checkpoint, args.dataset, split)
    print(json.dumps(metrics, ensure_ascii=False, indent=2))
