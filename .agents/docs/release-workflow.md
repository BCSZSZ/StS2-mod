# Local Development And Workshop Release Workflow

## Decision

The ordinary local `mods/CardValueOverlay` copy is the development build and may
remain installed across iterations. The publishing account is intentionally not
subscribed to the CardValueOverlay Workshop item, so the local and subscribed
copies cannot be loaded together.

Workshop is a milestone release channel, not the development deployment loop.

## Local Iteration

Close Slay the Spire 2, then run from the repository root:

```powershell
& scripts\publish-local.ps1
```

The script:

1. builds a complete DLL/JSON/PCK/PDB package under `dist/local-staging`;
2. refuses to deploy if the game is running;
3. refuses to overwrite unexpected files in the local mod folder;
4. copies and hash-verifies exactly the four runtime files;
5. removes temporary staging;
6. never reads or writes Steam Workshop content.

After deployment, launch the game manually and treat `godot.log` plus the
interactive overlay as the runtime authority.

## Workshop Package Check

Keep the local development copy installed and generate a release candidate
without uploading it:

```powershell
& scripts\publish-workshop.ps1 `
    -Version v0.1.0 `
    -PackageOnly `
    -AllowLocalMod
```

Inspect `dist/workshop/CardValueOverlay/content`, the zip, checksums, bilingual
description, release notes, and VDF. The package is always built from
`dist/workshop-staging`; `-AllowLocalMod` only acknowledges that the unrelated
local development folder exists.

## Workshop Release

After local in-game validation and package inspection:

```powershell
& scripts\publish-workshop.ps1 `
    -Version v0.1.0 `
    -AllowLocalMod `
    -ChangeNote "Stable dEV update."
```

Before running it:

- confirm the publishing account is still not subscribed to this Workshop item;
- commit the intended source state or otherwise record the exact Git revision;
- update the manifest version, matching release notes, and one-line change note;
- confirm BaseLib remains configured as a required Workshop item.

The SteamCMD success message confirms upload. It does not replace local runtime
validation.

## If Workshop Subscription Is Re-enabled

Do not combine a subscribed Workshop copy with the ordinary local copy. Remove
or move the local `mods/CardValueOverlay` folder, omit `-AllowLocalMod`, subscribe,
and then test the downloaded Workshop artifact. Restore the local development
copy with `scripts\publish-local.ps1` after unsubscribing again.

## Invariants

- Neither publishing path packages from the ordinary local mods directory.
- `DeployToMods=true` is used only against temporary staging under `dist/`.
- Both paths use `scripts/build-staged-mod.ps1` so DLL/PCK generation is identical.
- Runtime packages contain only DLL, manifest JSON, PCK, and PDB.
- Workshop upload is never implied by local deployment or package-only mode.
