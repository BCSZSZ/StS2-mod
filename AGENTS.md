# AGENTS.md

This file gives repo-wide instructions for Codex when working on
`CardValueOverlay`, a Slay the Spire 2 C# Godot mod plus local tooling.

For longer background, read only the relevant files under `docs/agents/`.
Keep this root file concise: Codex merges `AGENTS.md` files by directory, and
closer nested files can add or override guidance for their subtree.

## Project Map

- `CardValueOverlay.csproj`: runtime packaging project. It produces the mod DLL
  and PCK.
- `CardValueOverlayCode/`: runtime mod code, Harmony patches, Godot labels,
  game-state reads.
- `CardValueOverlay.Core/`: pure config, value, fallback, and calculation logic.
  It must not reference Godot or StS2 assemblies.
- `CardValueOverlay.Tools/`: local CLI. Never package it into the game mod.
- `CardValueOverlay.Core.Tests/`: executable tests for shared logic.
- `CardValueOverlay.Modeling/`: pure mathematical modeling, extraction, and
  generated-data validation. Never package it into the game mod.
- `CardValueOverlay.Modeling.Tests/`: executable tests for modeling logic.
- `data/`: modeling fixtures, manual tags, and generated extraction outputs.
- `CardValueOverlay/`: Godot resources, runtime config JSON, localization, icon.
- `docs/agents/`: long-lived roadmap, local environment facts, and debugging
  retrospectives.
- `docs/modeling/`: mathematical card-value methodology and the future C#
  modeling/extraction plan.

## Current Product State

- The overlay is intended to render one small line above cards.
- Active runtime display modes are `fixedText` and `cardName`.
- Manual/effective value modes are planned next. Core value models already use
  layered schema version 2.
- `CardValueOverlay/data/card_values.json` intentionally has an empty `cards`
  table until real card ids and values are prepared.
- Fixed values should come from the modeling methodology in
  `docs/modeling/card-value-methodology.md`, then be manually curated.

## Architecture Rules

- Keep runtime, shared core, and CLI tools separate.
- Keep modeling/extraction code outside the runtime mod. It may feed candidate
  values into review artifacts, but it must not automatically overwrite
  `CardValueOverlay/data/card_values.json`.
- The game mod loader should load only `CardValueOverlay.dll`; compile shared
  core source into the runtime DLL instead of deploying `CardValueOverlay.Core.dll`.
- Keep pure value rules in `CardValueOverlay.Core/`; do not duplicate them in
  runtime or tools.
- Dynamic values and manual values use the same layered table shape. Do not
  reintroduce scalar `manualValue`, scalar `fixedValue`, or
  `Dictionary<string, double?>` dynamic values.
- Config schema version is `2`. Version 1 scalar value files are intentionally
  unsupported.

## Build And Verification

Run these from the repository root:

```powershell
dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
dotnet build CardValueOverlay.csproj --no-restore -v minimal
dotnet publish CardValueOverlay.csproj -v minimal
```

If `SlayTheSpire2.exe` is running, build or publish may fail while copying to
the real Steam mod folder because the game locks `CardValueOverlay.dll`. In that
case, either close the game or verify with a temporary `ModsPath`.

After publish, the local game mod folder must contain only:

```text
CardValueOverlay.dll
CardValueOverlay.json
CardValueOverlay.pck
CardValueOverlay.pdb
```

No `CardValueOverlay.Core.dll` should be present.

Modeling extraction writes generated local reference data under `data/`:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- extract-game-data
```

Generated extraction outputs are ignored by Git; commit only source, fixtures,
manual tags, and documentation.

## Runtime Debugging

Treat the running game as the authority. A clean build is not proof that the
mod works in game.

Read the latest log before guessing:

```powershell
rg -n "CardValueOverlay|Exception|ERROR|Fatal|FileNotFound|Could not load|ModManager|BaseLib|Harmony" "$env:APPDATA\SlayTheSpire2\logs\godot.log"
```

For packaging or startup failures, inspect:

- `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\CardValueOverlay`
- `%APPDATA%\SlayTheSpire2\logs\godot.log`
- `docs/agents/runtime-lessons.md`
- `docs/agents/local-environment.md`

## StS2/Godot Lessons To Preserve

- Prefer plain Godot nodes such as `Label`; do not reintroduce a custom
  `CardOverlayLabel : Label` without a logged, verified need.
- Overlay rendering must be idempotent: reuse the named label, update text,
  update visibility, reposition, and avoid duplicates.
- Reward screens become stable over several frames. Use scheduled refreshes
  rather than assuming the first screen callback has all final card nodes.
- Keep patch surfaces small and screen-intent based. Add holder-level patches
  only after logs prove a specific holder lifecycle is required.

## Static Initialization Rule

Avoid fragile runtime work in static field initializers or static constructors,
especially for config loading, JSON converters, reflection, Godot APIs, or game
APIs. If a type initializer fails in the game runtime, that type can remain
unusable for the whole process and force silent fallback behavior.

Prefer explicit load methods that:

- build options locally or lazily;
- catch and log the full exception chain, including inner exceptions;
- validate config immediately after parsing;
- log the loaded mode or the exact fallback reason.

The `CardValueConfigLoader` incident is documented in
`docs/agents/runtime-lessons.md`.

## Editing Rules

- Do not revert user changes unless explicitly requested.
- Use `rg` for search.
- Use `apply_patch` for manual file edits.
- Keep changes narrow. Do not mix animation polish, value formulas, packaging,
  and card identity changes in the same edit unless the user asks.
- For resource, localization, scene, image, or JSON changes, publish before
  asking the user to launch the game.
