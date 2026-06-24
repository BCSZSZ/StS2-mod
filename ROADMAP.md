# CardValueOverlay Roadmap

## Source Context

- Local game path: `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`.
- Template reference: `Alchyr/ModTemplate-StS2`, empty mod template.
- Imported planning context: `https://chatgpt.com/share/6a3ba3b5-6db4-83e8-879a-8ae9720293e2`.
- Project name: `CardValueOverlay`.
- GitHub repository target: `BCSZSZ/StS2-mod`.
- SSH remote target: `git@github.com-personal:BCSZSZ/StS2-mod.git`.

## Product Direction

This mod is not a new card, relic, character, or content pack. The intended
feature is an overlay on existing card UI that shows an evaluation value near a
card.

Recommended implementation path from the imported context:

```text
ModTemplate-StS2
-> Empty Slay the Spire 2 Mod
-> build a mod the game can recognize
-> patch the card display node
-> first show a fixed "8.5"
-> then use a cardId -> value manual table
-> finally calculate values dynamically
```

## Current Scaffold

The repository starts as an empty gameplay mod skeleton.

- `CardValueOverlay.csproj` uses `Godot.NET.Sdk/4.5.1`, targets `net9.0`, references Slay the Spire 2 assemblies from the local install, and keeps BaseLib as the initial dependency.
- `CardValueOverlay.json` declares the mod id, author, version, minimum game version, DLL/PCK support, and BaseLib dependency.
- `CardValueOverlayCode/MainFile.cs` contains only the mod initializer and Harmony setup.
- `CardValueOverlay/` is the Godot/resource folder and currently contains only the template icon.
- `Sts2PathDiscovery.props` handles cross-platform game-path discovery and resolves `ModsPath`/`Sts2DataDir`.
- `Directory.Build.props.example` documents local machine paths; the real `Directory.Build.props` remains ignored.

## Phase 0: Environment Baseline

Goal: make the empty scaffold publish a complete local mod package and copy it
into the local game mods folder.

1. Install a .NET SDK capable of building `net9.0`.
2. Install or locate MegaDot/Godot Mono 4.5.1 for `GodotPath`.
3. Keep BaseLib installed and available to the game; Steam Workshop is the preferred route.
4. Confirm Slay the Spire 2 assemblies exist under `data_sts2_windows_x86_64`.
5. Copy `Directory.Build.props.example` to `Directory.Build.props` only if automatic path discovery fails.
6. Run `dotnet restore` and `dotnet publish CardValueOverlay.csproj`.

Exit criteria:

- Publish emits `CardValueOverlay.dll`, `CardValueOverlay.pdb`, and `CardValueOverlay.json`.
- Publish emits `CardValueOverlay.pck`.
- Publish copies `CardValueOverlay.dll`, `CardValueOverlay.pdb`, `CardValueOverlay.json`, and `CardValueOverlay.pck` to the local Slay the Spire 2 `mods/CardValueOverlay/` folder.
- The game reaches the mod loading stage and can see `CardValueOverlay`.

## Phase 1: First Load Proof

Goal: prove the DLL is loaded before touching card UI.

1. Add one log line in `MainFile.Initialize`.
2. Build and launch the game with the mod enabled.
3. Check the latest Windows log at `%appdata%/SlayTheSpire2/logs/godot.log`.

Exit criteria:

- The log proves `CardValueOverlay` initialization ran.
- No UI patching has been added yet.

## Phase 2: Find The Card UI Target

Goal: identify the stable node or method to patch.

Use Rider, ILSpy, dnSpy, or dotPeek to inspect `sts2.dll` under the game's
`data_sts2_windows_x86_64` folder.

Search targets:

```text
NCard
CardModel
CardReward
Reward
CardGrid
Deck
Library
Reload
SetModel
Create
_Ready
```

Preferred target: patch the shared card display node, likely around an
`NCard`-style lifecycle such as create, model binding, or reload. That should
cover reward screens and deck/card-grid screens before adding scene-specific
filters.

Exit criteria:

- `ROADMAP.md` records the exact class and method chosen for the first patch.
- The choice is based on decompiled symbols or runtime logs, not naming guesses.

## Phase 3: Fixed Overlay

Goal: add the smallest visible card overlay.

1. Add a Harmony postfix patch around the chosen card display refresh method.
2. Attach or update a Godot `Label` child on the card node.
3. Display a fixed value, initially `8.5`.
4. Check reward, deck, library, shop, and combat hand contexts.

Implementation rules:

- Do not change card data or gameplay logic.
- Keep the overlay node easy to find and update.
- Add filtering only after observing where the overlay appears.

Exit criteria:

- At least one non-combat card display shows `8.5`.
- Unwanted contexts are documented for filtering.

## Phase 4: Manual Value Table

Goal: replace the fixed value with a deterministic lookup.

1. Identify the card id or model field exposed by the card UI node.
2. Add a `ValueProvider` that maps `cardId` to a display value.
3. Return an empty result for unknown cards.
4. Keep table data separate from UI patch code.

Suggested structure:

- `CardValueOverlayCode/Patches/` for Harmony patches.
- `CardValueOverlayCode/Features/` for overlay orchestration.
- `CardValueOverlayCode/Values/` for card id lookup and later scoring.
- `CardValueOverlayCode/Utils/` for shared helpers.
- `CardValueOverlay/localization/` for future text data.
- `CardValueOverlay/images/` for future visual assets.

Exit criteria:

- Known cards show table-driven values.
- Unknown cards do not break rendering.

## Phase 5: Dynamic Value Calculation

Goal: compute values from run context after the UI route is stable.

Inputs to investigate:

- Current deck.
- Relics.
- Character.
- Act or path stage.
- Upgrade state.
- Card reward or shop context.

Exit criteria:

- Dynamic scoring is isolated behind the same `ValueProvider` contract.
- UI patch code does not contain scoring rules.

## Local Package Gate

Local acceptance requires `dotnet publish`, not only `dotnet build`.

`dotnet build` is useful for fast C# iteration, but it is not enough for the
baseline. The baseline is a complete local mod folder containing the manifest,
DLL, symbols, and PCK.

Expected installed files:

```text
Slay the Spire 2/mods/CardValueOverlay/
  CardValueOverlay.json
  CardValueOverlay.dll
  CardValueOverlay.pdb
  CardValueOverlay.pck
```

## Git Workflow

- Default branch: `main`.
- Remote: `git@github.com-personal:BCSZSZ/StS2-mod.git`.
- Push model: direct push to `main` unless a future task explicitly asks for branches or pull requests.
- Commit style: short imperative messages, for example `Initial StS2 mod scaffold`.

## Open Items

- Install .NET SDK for `net9.0`; current `dotnet new`/`dotnet build` cannot run because no SDK is installed.
- Install or locate MegaDot/Godot Mono 4.5.1 and update `GodotPath` in `Directory.Build.props`.
- Run `dotnet publish CardValueOverlay.csproj` and verify the local mods folder contains `.dll`, `.pdb`, `.json`, and `.pck`.
- Launch the game and confirm `Card Value Overlay` appears under Settings -> Mod Settings.
- Decompile `sts2.dll` and record the exact card UI class/method to patch.
- Add the first initialization log after the build environment works.
