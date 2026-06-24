# AGENTS.md

Instructions for shared core code under `CardValueOverlay.Core/`.

## Boundary

- This project must remain pure C# logic.
- Do not reference Godot, Harmony, `sts2.dll`, or game runtime APIs.
- Runtime and tools should share value/config behavior through this project.

## Value Schema

- Current config schema is version `2`.
- Card values are layered by upgrade state:
  - `manualValues.unupgraded`
  - `manualValues.upgraded`
  - `smithValues.unupgraded`
  - `smithValues.upgraded`
- Common parameters use layered `fixedValues`.
- Layer tables resolve by nearest lower threshold.
- Dynamic values use the same `LayeredValueTable` shape as manual values.
- Do not restore scalar v1 compatibility.

Example:

```json
"manualValues": {
  "unupgraded": { "1": 1.0, "20": 1.4 },
  "upgraded": { "1": 1.3, "20": 1.8 }
}
```

## Testing

Run after core changes:

```powershell
dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
```

Tests should continue to reject old scalar value JSON.
