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
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- average --cards keyA,keyB --layer 20
```

`average` must resolve through the same layered value rules as runtime code.
