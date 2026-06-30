---
name: sts2-install-play-values
description: Install generated direct play-value results into CardValueOverlay runtime card_values.json according to user-specified horizons. Use when Claude needs to merge generated value JSON into the mod while updating only requested horizons and preserving unrequested horizons plus unrelated runtime config.
---

# StS2 Install Play Values

Use this for the runtime JSON merge atom after generated direct play-value
output has been reviewed. The horizon set is a user-provided parameter, not a
skill constant.

## Inputs

- `input`: generated play-value JSON selected by the user or workflow.
- `config`: default `CardValueOverlay/data/card_values.json`.
- `installHorizons`: required list, for example `shortline,midline`.
- Source precision: generated JSON keeps `0.001`.
- Runtime precision: installed values are rounded to the precision requested by
  the workflow, commonly `0.1`.

## Command

For the current 4/8 workflow, the repository command supports installing
`shortline,midline`:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- install-floor8-play-values --input <input> --config <config>
```

If `installHorizons` is anything other than `shortline,midline`, first extend
or use an installer that accepts the requested horizon set. Do not pretend the
short/mid-only command installed other horizons.

## Merge Rules

- Update only cards present in both generated JSON and runtime config.
- Update only the horizons listed in `installHorizons`, for both unupgraded and
  upgraded forms when present.
- Preserve all unrequested horizons, including `longline` unless the user
  explicitly includes it.
- Preserve overlay settings, common parameters, notes, pools, names, and cards
  not present in generated output.
- Fail if generated output references a card missing from runtime config; do
  not auto-add cards in this install path unless the user asks for that scope.

## Acceptance

The installer output must list the updated and preserved horizons. Then run:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
```

The config must be valid before publishing the mod.
