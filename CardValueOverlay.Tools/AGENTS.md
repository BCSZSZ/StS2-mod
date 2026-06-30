# AGENTS.md

Instructions for local CLI tooling under `CardValueOverlay.Tools/`.

## Boundary

- Tools are local development utilities and must not be packaged into the game
  mod folder.
- Tools may reference `CardValueOverlay.Core/`.
- Keep command output deterministic and suitable for editing JSON data.

## Commands

Supported commands currently include:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- validate
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- extract-cards
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- extract-game-data
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-card-facts
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-card-pools
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-monster-moves
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-encounter-patterns
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-card-values --layer 1
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- write-card-review-list
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-enemy-expectations
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-encounter-weighted-enemy-pressure
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-defense-calibration
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- train-card-values
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- average --cards keyA,keyB --horizon midline
```

`average` must resolve through the same training horizon value rules as runtime
code.

`train-card-values` writes ignored training output by default and writes
`CardValueOverlay/data/card_values.json` only when `--write-config` is passed.
Every card entry produced by this command should include
`generation.method = "monteCarlo"` and matching shortline, midline, and longline
`generation.updatedAt` timestamps. These fields are tracking metadata and must
not alter `ValueResolver` behavior.
