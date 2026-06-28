# Search Policy Training

Python/uv tooling for training a small search action ranker used by the C#
deck simulator. The model only ranks currently playable cards before recursive
search expansion; C# remains responsible for all game-rule resolution.

```powershell
uv sync
uv run prepare-dataset --input ../data/generated/search_policy/search_policy_teacher.generated.jsonl
uv run train-ranker
uv run export-model
uv run eval-ranker
```

The exported JSON is intended for `data/manual-tags/search_policy_ranker.json`.
Training checkpoints, generated datasets, and teacher JSONL files should stay
under `data/generated/` or another ignored scratch path.
