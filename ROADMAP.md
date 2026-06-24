# StS2Mod Roadmap

## Source Context

- Local game path: `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`.
- Template reference: `Alchyr/ModTemplate-StS2`, empty mod template.
- Project name: `StS2Mod`.
- GitHub repository target: `BCSZSZ/StS2-mod`.
- SSH remote target: `git@github.com-personal:BCSZSZ/StS2-mod.git`.
- Shared ChatGPT link was provided, but the current tooling could not import its contents. Decisions below are based on the explicit request in this thread and the upstream template files.

## Current Scaffold

The repository starts as an empty gameplay mod skeleton.

- `StS2Mod.csproj` uses `Godot.NET.Sdk/4.5.1`, targets `net9.0`, references Slay the Spire 2 assemblies from the local install, and keeps BaseLib as the initial dependency.
- `StS2Mod.json` declares the mod id, author, version, minimum game version, DLL/PCK support, and BaseLib dependency.
- `StS2ModCode/MainFile.cs` contains only the mod initializer and Harmony setup.
- `StS2Mod/` is the Godot/resource folder and currently contains only the template icon.
- `Sts2PathDiscovery.props` handles cross-platform game-path discovery and resolves `ModsPath`/`Sts2DataDir`.
- `Directory.Build.props.example` documents local machine paths; the real `Directory.Build.props` remains ignored.

## Phase 0: Environment Baseline

Goal: make the empty scaffold build and copy into the local game mods folder.

1. Install a .NET SDK capable of building `net9.0`.
2. Install or locate MegaDot/Godot Mono 4.5.1 for `GodotPath`.
3. Confirm Slay the Spire 2 assemblies exist under `data_sts2_windows_x86_64`.
4. Copy `Directory.Build.props.example` to `Directory.Build.props` only if automatic path discovery fails.
5. Run `dotnet restore` and `dotnet build`.

Exit criteria:

- Build emits `StS2Mod.dll`, `StS2Mod.pdb`, and `StS2Mod.json`.
- Build copies those files to the local Slay the Spire 2 `mods/StS2Mod/` folder.
- The game reaches the mod loading stage without this empty mod causing startup errors.

## Phase 1: Publish Path

Goal: verify the DLL plus PCK packaging route before adding real content.

1. Configure `GodotPath` in local `Directory.Build.props`.
2. Run `dotnet publish`.
3. Confirm the Godot export preset `BasicExport` creates `StS2Mod.pck`.
4. Confirm `StS2Mod.pck` is copied next to the DLL and manifest in `mods/StS2Mod/`.

Exit criteria:

- The mod folder contains `StS2Mod.dll`, `StS2Mod.pdb`, `StS2Mod.json`, and `StS2Mod.pck`.
- The game recognizes the mod package after a clean restart.

## Phase 2: First Real Feature

Goal: add the smallest visible behavior change after the base pipeline works.

Candidate order:

1. Add logging at initialization to prove the DLL is loaded.
2. Add a harmless Harmony patch around a stable method, with logging only.
3. Add one simple asset or localization file if the feature needs game-facing text.

Implementation method:

- Put C# code under `StS2ModCode/`.
- Put Godot resources, images, and localization under `StS2Mod/`.
- Keep mod identifiers stable as `StS2Mod` unless there is a deliberate rename.
- Keep patches small and reversible until the target game API is confirmed.

## Phase 3: Feature Expansion

Goal: build actual mod behavior with traceable, testable steps.

Working method:

1. Define one feature in `ROADMAP.md` before coding it.
2. Identify the game class or BaseLib hook required for that feature.
3. Add code in a dedicated namespace/folder under `StS2ModCode/`.
4. Add assets/localization only when the feature needs them.
5. Build, install, launch, and record observed behavior.

Suggested structure when the mod grows:

- `StS2ModCode/Patches/` for Harmony patches.
- `StS2ModCode/Features/` for feature orchestration.
- `StS2ModCode/Utils/` for shared helpers.
- `StS2Mod/localization/` for text data.
- `StS2Mod/images/` for visual assets.

## Phase 4: Release Hygiene

Goal: keep the GitHub repository usable as the mod becomes real.

1. Keep `main` buildable.
2. Tag working milestones after a confirmed local launch.
3. Document setup changes in `README.md`.
4. Do not commit local build output, `.godot/`, `bin/`, `publish/`, or machine-specific `Directory.Build.props`.
5. Record known game-version compatibility in `StS2Mod.json` and release notes.

## Git Workflow

- Default branch: `main`.
- Remote: `git@github.com-personal:BCSZSZ/StS2-mod.git`.
- Push model: direct push to `main` unless a future task explicitly asks for branches or pull requests.
- Commit style: short imperative messages, for example `Initial StS2 mod scaffold`.

## Open Items

- Install .NET SDK for `net9.0`; current `dotnet new`/`dotnet build` cannot run because no SDK is installed.
- Confirm the correct MegaDot/Godot Mono executable path.
- Re-import or manually summarize the prior ChatGPT share if it contains design decisions not present in this repository.
