# Modeling Data

This folder is for the mathematical modeling pipeline. It is not packaged into
the runtime mod.

## Tracked Inputs

- `fixtures/`: small deterministic test fixtures.
- `manual-tags/card_effect_overrides.json`: hand-authored corrections for card
  effects that cannot be extracted confidently.
- `manual-tags/monster_move_overrides.json`: hand-authored corrections for
  monster moves that cannot be extracted confidently.
- `manual-tags/model_calibration.json`: hand-authored calibration constants used
  by estimators.

## Generated Local Outputs

The extraction commands write generated files here, but Git ignores them because
they are derived from the local game install:

- `extracted/*.generated.json`
- `generated/*.generated.json`
- `generated/*.md`
- `generated/decompiled/`

Regenerate them from the repository root:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- extract-game-data
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-card-effects
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-monster-moves
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-card-values --layer 1
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-enemy-expectations
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- validate-generated-data
```

Current v1 extraction discovers game version, cards, enemies, encounters, intent
types, localization records, conservative card effect terms, and unresolved
follow-up work. `card_effect_terms.generated.json` currently covers damage,
block, hit count, upgrade deltas, draw, immediate and next-turn energy, HP loss,
common applied powers/debuffs, keywords, and tag or calculated damage scaling
from decompiled card bodies. It does not yet normalize monster move graphs or
runtime-ready curated values.

`card_value_candidates.generated.json` and `card_value_candidates.md` are first
pass review artifacts, not runtime config. They are generated from
`card_effect_terms.generated.json` plus `manual-tags/model_calibration.json` and
include contribution breakdowns and warnings for low-confidence or extreme
estimates.

`monster_move_profiles.generated.json` is the first-pass enemy behavior input.
It records move state ids, UI intents, parsed effects such as attack, block,
buffs, debuffs, hit counts, HP ranges, follow-up states, parser confidence, and
review warnings. Conditional state machines are preserved conservatively rather
than forced into exact probabilities.

`enemy_expectations.generated.json` and `enemy_expectations.md` summarize those
monster profiles as equal-weight v1 expectations: average damage per move,
ascension damage, attack rate, block, Weak, Frail, Vulnerable, Strength gain,
and review warnings. These files are inputs for later block calibration and
enemy-context card valuation.
