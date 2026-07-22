---
name: sts2-validate-mod-value-release
description: Validate and, when explicitly requested, deploy approved CardValueOverlay runtime value JSON changes through the active profile. Use after an authorized value install to prove schema, scope, tests, package contents, hashes, and release gates without treating research combat dEV as publishable.
---

# StS2 Validate Mod Value Release

Use this only after `CardValueOverlay/data/card_values.json` was intentionally
changed. A combat-aware research report with `runtimeCandidate: false` or null
`primaryDeltaEv` must not reach this step.

Read `.agents/docs/combat-aware-simulation-contract.md`,
`docs/modeling/card-value-json-schema.md`, and the `sts2-mod-packaging` skill.

## Input Gate

Before running builds, prove:

- the user authorized the installed cards/forms/horizons;
- the JSON diff contains no unrelated display/config/card changes;
- every combat-aware source report says `runtimeCandidate: true` and carries
  approved coverage, confidence, solver, HP-calibration, and portfolio-weight
  evidence;
- report hashes and installed generation metadata match;
- 4/8/12-turn paired deck dEV maps to shortline/midline/longline without
  per-play division.

For legacy values, label the method and scope explicitly; do not mislabel them
as combat-aware.

## Validation Without Deployment

Resolve `$dotnet` through the active profile as documented by
`sts2-mod-packaging`, then run:

```powershell
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
& $dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
& $dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
& $dotnet build CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -c Release -v minimal
& $dotnet build CardValueOverlay.csproj --no-restore -v minimal
& $dotnet publish CardValueOverlay.csproj --no-restore -v minimal
```

Plain build/publish must not deploy because `DeployToMods=false`.

## Explicit Local Deployment

Only if the user asked to update the local mod:

```powershell
& scripts\publish-local.ps1
```

Do not copy files manually or inspect a hard-coded Steam path. Resolve the active
profile, then inspect its exact `modsPath\CardValueOverlay` directory. It must
contain only:

```text
CardValueOverlay.dll
CardValueOverlay.json
CardValueOverlay.pck
CardValueOverlay.pdb
```

Verify deployed hashes and confirm the packaged JSON contains the authorized
values. If the game is running, report the script's refusal and ask the user to
close it before retrying.

## Runtime Handoff

Codex does not launch the game unless explicitly asked in the current request.
Inspect the latest existing log for prior failures:

```powershell
rg -n "CardValueOverlay|Exception|ERROR|Fatal|FileNotFound|Could not load|ModManager|BaseLib|Harmony" "$env:APPDATA\SlayTheSpire2\logs\godot.log"
```

Then tell the user which cards/horizons to inspect on the next interactive
launch. A log from before deployment is diagnostic history, not proof of the new
package.

## Report

Report gate status, authorized diff scope, value/report hashes, schema/tests,
compile result, whether deployment was requested/performed, active target path,
four-file invariant, deployed JSON/hash result, existing log evidence, and the
specific in-game sanity check still owed by the user.
