---
name: sts2-mod-packaging
description: Build, publish, package, and debug the CardValueOverlay Slay the Spire 2 C# Godot mod, including environment-variable path setup, BaseLib/Godot dependencies, runtime output folder checks, Godot logs, and stale DLL packaging failures.
---

# StS2 Mod Packaging

Treat the running game as the authority. Restore/build/publish success is not
proof that the mod loads in Slay the Spire 2.

## Environment

Use environment variables and the active profile. Do not write machine-specific
absolute paths into shared docs or code.

```powershell
$profileName = [Environment]::GetEnvironmentVariable("STS2_MOD_PROFILE", "User")
$profile = [Environment]::GetEnvironmentVariable($profileName, "User") | ConvertFrom-Json
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } elseif ($profile.dotnetPath) { $profile.dotnetPath } else { "dotnet" }
```

Important profile keys and fallback environment variables:

- `sts2Path` / `STS2_PATH`
- `modsPath` / `STS2_MODS_PATH`
- `godotPath` / `GODOT_PATH`
- `godotNugetSource` / `GODOT_NUGET_SOURCE`
- `dotnetPath` / `LIAO_DOTNET`
- `ilspycmdPath` / `ILSPYCMD_PATH`

Local machine files such as `Directory.Build.props` and `NuGet.Config` are
ignored and should read environment variables rather than hard-coded paths.

## Runtime Packaging Invariant

The game mod loader should load only the runtime mod package. The local mod
folder should contain only:

```text
CardValueOverlay.dll
CardValueOverlay.json
CardValueOverlay.pck
CardValueOverlay.pdb
```

Do not leave stale helper DLLs such as `CardValueOverlay.Core.dll` in the game
mod folder. Shared core logic may be a separate project for tools/tests, but
runtime packaging should compile the needed source into `CardValueOverlay.dll`.

## Build And Publish

From repo root:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
& $dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
& $dotnet build CardValueOverlay.csproj --no-restore -v minimal
& $dotnet publish CardValueOverlay.csproj -v minimal
```

Build is enough for code-only compile checks. Publish before asking the user to
launch the game after resource, localization, scene, image, JSON, or packaging
changes.

## Debugging Workflow

1. Inspect the latest game log before guessing:

   ```powershell
   rg -n "CardValueOverlay|Exception|ERROR|Fatal|FileNotFound|Could not load|ModManager|BaseLib|Harmony" "$env:APPDATA\SlayTheSpire2\logs\godot.log"
   ```

2. Inspect the actual mod folder under the active profile's `modsPath`.
3. Confirm the manifest id matches the DLL/PCK names.
4. Confirm `has_dll` and `has_pck` expectations are satisfied.
5. Confirm BaseLib exists under the active Workshop or mods path.
6. Delete stale extra DLLs from the mod folder if architecture changed.
7. Publish again, launch the game, and re-read the log.

## Common Failures

If the game reports it cannot load `CardValueOverlay.Core`:

- remove runtime deployment of the core DLL;
- compile shared source into the runtime mod assembly;
- keep `CardValueOverlay.Core` for tools/tests only;
- delete stale `CardValueOverlay.Core.dll` and `.pdb` from the game mod folder;
- rebuild and publish.

If Godot packages unrelated C# files:

- check export presets;
- exclude tools, tests, core, `bin/`, `obj/`, and source files that should not
  be PCK resources.

If restore cannot find Godot packages:

- confirm `GODOT_NUGET_SOURCE` or the active profile's `godotNugetSource`;
- run restore with the local ignored `Directory.Build.props` present.

## Release Gate

Do not call packaging work done until:

- tests and tool validation pass where relevant;
- runtime mod build and publish pass;
- the actual game mod folder contains only expected runtime files;
- latest `godot.log` shows the mod initializer ran;
- latest `godot.log` has no exception from this mod.
