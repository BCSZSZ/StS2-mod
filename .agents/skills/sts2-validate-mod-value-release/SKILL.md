---
name: sts2-validate-mod-value-release
description: Validate, build, publish, and sanity-check CardValueOverlay value JSON changes. Use after installing generated card values into CardValueOverlay/data/card_values.json, before asking the user to launch Slay the Spire 2 or when Codex needs to confirm the mod folder contains the updated DLL, JSON, and PCK.
---

# StS2 Validate Mod Value Release

Use this as the final atom after runtime value JSON changes. Treat the running
game as authority; a clean build alone is not enough.

## Inputs

- `config`: default `CardValueOverlay/data/card_values.json`.
- `toolsProject`: default `CardValueOverlay.Tools/CardValueOverlay.Tools.csproj`.
- `coreTests`: default `CardValueOverlay.Core.Tests/CardValueOverlay.Core.Tests.csproj`.
- `modelingTests`: default `CardValueOverlay.Modeling.Tests/CardValueOverlay.Modeling.Tests.csproj`.
- `runtimeProject`: default `CardValueOverlay.csproj`.
- `modsPath`: default Steam mod folder from project publish settings.

## Required Checks

Run from the repo root:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
dotnet build CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -c Release -v minimal
dotnet build CardValueOverlay.csproj --no-restore -v minimal
dotnet publish CardValueOverlay.csproj -v minimal
```

If `SlayTheSpire2.exe` is running and publish cannot copy the DLL, report the
lock clearly and ask the user to close the game before retrying publish.

## Sanity Checks

After publish, inspect:

```powershell
Get-ChildItem 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\CardValueOverlay' | Select-Object Name,Length,LastWriteTime
```

The folder should contain only:

```text
CardValueOverlay.dll
CardValueOverlay.json
CardValueOverlay.pck
CardValueOverlay.pdb
```

For runtime startup problems, read the latest Godot log with:

```powershell
rg -n "CardValueOverlay|Exception|ERROR|Fatal|FileNotFound|Could not load|ModManager|BaseLib|Harmony" "$env:APPDATA\SlayTheSpire2\logs\godot.log"
```

## Report Back

Summarize only the important pass/fail results, generated value counts if
relevant, and whether publish updated the real mod folder.
