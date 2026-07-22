---
name: sts2-mod-packaging
description: Build, stage, deploy, package, and diagnose the CardValueOverlay Slay the Spire 2 C# Godot mod through the active machine profile. Use for explicit runtime packaging work, local deployment, Workshop release, package-content checks, dependency failures, or existing game-log diagnosis.
---

# StS2 Mod Packaging

Use this skill only when the user requests runtime build, deployment, packaging,
release, or diagnosis. Offline modeling, combat coverage, solver benchmarks, and
research dEV reports do not authorize publishing the mod.

Treat a running-game result as runtime authority, but do not launch Slay the
Spire 2 unless the user explicitly asks in the current request. The usual handoff
is: build and deploy, inspect package contents and existing logs, then tell the
user what to verify interactively.

## Resolve The Active Profile

Do not hard-code a machine path:

```powershell
$profileName = [Environment]::GetEnvironmentVariable("STS2_MOD_PROFILE", "User")
$profileJson = [Environment]::GetEnvironmentVariable($profileName, "User")
if ([string]::IsNullOrWhiteSpace($profileJson)) { throw "Missing active StS2 profile: $profileName" }
$profile = $profileJson | ConvertFrom-Json
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } elseif ($profile.dotnetPath) { $profile.dotnetPath } else { "dotnet" }
```

Relevant profile fields are `sts2Path`, `modsPath`, `godotPath`,
`godotNugetSource`, `dotnetPath`, and `ilspycmdPath`. Legacy single-purpose
environment variables are fallbacks only.

## Package Invariant

The active local mod directory must contain exactly:

```text
CardValueOverlay.dll
CardValueOverlay.json
CardValueOverlay.pck
CardValueOverlay.pdb
```

The loader must not receive `CardValueOverlay.Core.dll`; shared core source is
compiled into the runtime DLL. Tools, tests, modeling assemblies, source, and
generated research reports must never enter the package.

## Verification Without Deployment

For compile and data checks that must not touch a Mods directory:

```powershell
& $dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
& $dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
& $dotnet build CardValueOverlay.csproj --no-restore -v minimal
& $dotnet publish CardValueOverlay.csproj --no-restore -v minimal
```

Plain `dotnet build` and `dotnet publish` leave `DeployToMods=false`. They must
not be described as a local installation.

## Explicit Local Deployment

Only when local deployment is requested:

```powershell
& scripts\publish-local.ps1
```

The script builds in `dist/local-staging`, copies exactly four files to the
active profile's ordinary `modsPath`, verifies hashes, and removes staging. It
must refuse while `SlayTheSpire2.exe` is running. Ask the user to close the game
if that guard fires; do not work around the lock or write to Workshop content.

Afterward, resolve `$profile.modsPath`, inspect the exact
`CardValueOverlay` child directory, and compare staged/deployed hashes. Do not
delete unrelated directories. If stale extra files are present inside this
exact mod folder, verify the target and use the repository publishing workflow
to replace the package cleanly.

## Explicit Workshop Release

Workshop work requires a version and release note supplied or approved for the
current release:

```powershell
& scripts\publish-workshop.ps1 -Version <version> -PackageOnly
& scripts\publish-workshop.ps1 -Version <version> -ChangeNote <approved-note>
```

Use `-AllowLocalMod` only when its documented publisher/subscription conditions
are true. It never changes the package source: Workshop content comes only from
`dist/workshop-staging`, never from the ordinary local mods directory. Do not
reuse an old version or release note from a skill example.

## Diagnosis

Before guessing, inspect the exact active package and the latest existing log:

```powershell
$modPath = Join-Path $profile.modsPath "CardValueOverlay"
Get-ChildItem -LiteralPath $modPath | Select-Object Name,Length,LastWriteTime
rg -n "CardValueOverlay|Exception|ERROR|Fatal|FileNotFound|Could not load|ModManager|BaseLib|Harmony" "$env:APPDATA\SlayTheSpire2\logs\godot.log"
```

Check manifest/DLL/PCK names, BaseLib availability, hash freshness, and absence
of helper assemblies. A pre-existing log can diagnose the previous launch but
cannot prove a newly deployed build loaded; hand that final interactive check to
the user unless launch was explicitly requested.

If build fails because Application Control blocks `Krafs.Publicizer`, record the
full error and validate unaffected core/modeling projects. Do not claim the
runtime package passed.

## Completion Report

State separately:

- tests and validation;
- compile/package result;
- whether local or Workshop deployment was explicitly requested and performed;
- resolved active profile and exact target directory;
- four-file invariant and hashes;
- what existing logs show;
- what the user must verify on the next game launch.
