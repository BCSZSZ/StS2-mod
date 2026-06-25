# Modeling Documentation

This folder documents the mathematical card-valuation track. It is separate
from runtime overlay work.

- `card-value-methodology.md`: summarized methodology and calibration anchors.
- `csharp-modeling-plan.md`: proposed C# modeling layer, extraction sources,
  algorithms, outputs, and verification plan.

Use these documents before implementing `CardValueOverlay.Modeling` or adding
new CLI commands that estimate card values. The current implementation already
has v1 extraction, card effect parsing, monster move parsing, enemy expectation
summaries, and first-pass candidate value estimation. Later estimator work
should consume generated data and calibration inputs instead of reading game
files directly.
