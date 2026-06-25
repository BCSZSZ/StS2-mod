# Modeling Data

This folder is for the mathematical modeling pipeline. It is not packaged into
the runtime mod.

## Tracked Inputs

- `fixtures/`: small deterministic test fixtures.
- `manual-tags/card_effect_overrides.json`: hand-authored corrections for card
  effects that cannot be extracted confidently.
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
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- validate-generated-data
```

Current v1 extraction discovers game version, cards, enemies, encounters, intent
types, localization records, conservative card effect terms, and unresolved
follow-up work. `card_effect_terms.generated.json` currently covers basic
damage, block, hit count, upgrade deltas, and simple tag-scaling damage from
decompiled card bodies. It does not yet normalize monster move graphs or final
value candidates.
