from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from typing import Any

import torch

from search_policy_training import FEATURE_VERSION, SCHEMA_VERSION
from search_policy_training.ranker import SearchRanker


def export_checkpoint_to_json(checkpoint_path: Path, output_path: Path) -> dict[str, Any]:
    checkpoint = torch.load(checkpoint_path, map_location="cpu", weights_only=False)
    input_size = len(checkpoint["numericFeatureNames"]) + len(checkpoint["cardIdVocab"])
    model = SearchRanker(input_size)
    model.load_state_dict(checkpoint["stateDict"])
    linear_layers = [module for module in model.layers if isinstance(module, torch.nn.Linear)]
    activations = ["relu", "relu", "linear"]
    layers = []
    for layer, activation in zip(linear_layers, activations, strict=True):
        layers.append(
            {
                "weights": layer.weight.detach().cpu().numpy().tolist(),
                "bias": layer.bias.detach().cpu().numpy().tolist(),
                "activation": activation,
            }
        )

    model_json = {
        "schemaVersion": SCHEMA_VERSION,
        "featureVersion": FEATURE_VERSION,
        "numericFeatureNames": checkpoint["numericFeatureNames"],
        "cardIdVocab": checkpoint["cardIdVocab"],
        "normalization": checkpoint["normalization"],
        "layers": layers,
        "metadata": {
            **checkpoint.get("metadata", {}),
            "createdAt": datetime.now(timezone.utc).isoformat(),
            "trainingDatasetHash": _file_hash(Path(checkpoint["metadata"]["datasetPath"])),
        },
    }
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(model_json, ensure_ascii=False, indent=2), encoding="utf-8")
    return model_json


def _file_hash(path: Path) -> str | None:
    if not path.exists():
        return None
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
