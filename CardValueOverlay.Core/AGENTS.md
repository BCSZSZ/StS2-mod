# AGENTS.md

Instructions for shared core code under `CardValueOverlay.Core/`.

## Boundary

- This project must remain pure C# logic.
- Do not reference Godot, Harmony, `sts2.dll`, or game runtime APIs.
- Runtime and tools should share value/config behavior through this project.

## Value Schema

- Current config schema is version `3`.
- Card values are training horizon values by upgrade state:
  - `trainingValues.unupgraded.shortline`
  - `trainingValues.unupgraded.midline`
  - `trainingValues.unupgraded.longline`
  - `trainingValues.upgraded.shortline`
  - `trainingValues.upgraded.midline`
  - `trainingValues.upgraded.longline`
- Card value generation metadata is optional and card-scoped:
  - `generation.method`
  - `generation.updatedAt.shortline`
  - `generation.updatedAt.midline`
  - `generation.updatedAt.longline`
- Generation metadata is for audit/statistics only. Do not use it in runtime
  value resolution or overlay rendering.
- Common parameters use layered `fixedValues`.
- Layer tables resolve by nearest lower threshold.
- Do not restore scalar v1 compatibility or the old `manualValues` /
  `smithValues` card-value shape for generated training values.

Example:

```json
"trainingValues": {
  "unupgraded": { "shortline": 1.0, "midline": 1.4, "longline": 1.8 },
  "upgraded": { "shortline": 1.3, "midline": 1.8, "longline": 2.2 }
},
"generation": {
  "method": "monteCarlo",
  "updatedAt": {
    "shortline": "2026-06-29T00:00:00Z",
    "midline": "2026-06-29T00:00:00Z",
    "longline": "2026-06-29T00:00:00Z"
  }
}
```

## Testing

Run after core changes:

```powershell
dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
```

Tests should continue to reject old scalar value JSON.
