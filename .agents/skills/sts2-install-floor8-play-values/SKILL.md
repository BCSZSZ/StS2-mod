---
name: sts2-install-floor8-play-values
description: Install generated floor8 direct play-value results into CardValueOverlay runtime card_values.json. Use when Codex needs to merge floor8 generated JSON into the mod while only updating matching cards' shortline and midline values and preserving longline plus unrelated runtime config.
---

# StS2 Install Floor8 Play Values

Use this for the runtime JSON merge atom after
`sts2-estimate-floor8-play-values` has produced reviewed generated output.

## Inputs

- `input`: default `data/generated/floor8_play_values/latest.generated.json`.
- `config`: default `CardValueOverlay/data/card_values.json`.
- Source precision: generated JSON keeps `0.001`.
- Runtime precision: installed values are rounded to `0.1`.

## Command

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- install-floor8-play-values --input <input> --config <config>
```

Default:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- install-floor8-play-values
```

## Merge Rules

- Update only cards present in both generated JSON and runtime config.
- Update only:
  - `trainingValues.unupgraded.shortline`
  - `trainingValues.unupgraded.midline`
  - `trainingValues.upgraded.shortline`
  - `trainingValues.upgraded.midline`
- Preserve `longline` exactly.
- Preserve overlay settings, common parameters, notes, pools, names, and cards
  not present in generated output.
- Fail if generated output references a card missing from runtime config; do
  not auto-add cards in this install path.

## Acceptance

The installer should report:

```text
floor8 play values installed
updatedHorizons: shortline, midline
preservedHorizons: longline
```

Then run:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
```

The config must be valid before publishing the mod.
