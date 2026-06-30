# CLAUDE.md

This file gives repo-wide instructions for Claude when working on
`CardValueOverlay`, a Slay the Spire 2 C# Godot mod plus local tooling.

It mirrors `AGENTS.md` (the Codex instruction set) for Claude. Keep the two in
sync when repo-wide rules change. For longer background, read only the relevant
files under `.agents/docs/`. Keep this root file concise: closer nested
`CLAUDE.md` files can add or override guidance for their subtree.

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
- `.agents/docs/`: long-lived roadmap, local environment facts, and debugging
  retrospectives for Claude, Codex, and maintainers.
- `.claude/skills/`: repo-scoped Claude skills for StS2 and CardValueOverlay
  workflows. They mirror `.agents/skills/` (the Codex skills). Keep StS2 and
  CardValueOverlay-specific workflows here, not in shared user skills.
- `docs/modeling/`: mathematical card-value methodology and the future C#
  modeling/extraction plan.

## Current Product State

- The overlay is intended to render one small line above cards.
- Active runtime display modes are `fixedText` and `cardName`.
- Training value mode is the active value direction. Core value models use
  training-output schema version 3 with shortline, midline, and longline values.
- `CardValueOverlay/data/card_values.json` may have an empty `cards`
  table only while generated training values are being prepared.
- The runtime value JSON contract is documented in
  `docs/modeling/card-value-json-schema.md`.
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
- Card training values use `trainingValues.unupgraded/upgraded.shortline`,
  `midline`, and `longline`. Do not reintroduce scalar `manualValue`,
  scalar `fixedValue`, or the old `manualValues` / `smithValues` card-value
  shape for generated training values.
- Each generated card entry may include optional tracking metadata under
  `generation.method` and `generation.updatedAt.shortline/midline/longline`.
  `method` is a string such as `monteCarlo` or `estimate`; the timestamps are
  ISO-8601 values with offsets. Runtime overlay display must ignore these
  metadata fields and resolve only `trainingValues`.
- Config schema version is `3`. Older value-file schemas are intentionally
  unsupported.
- Enemy damage, monster intent damage, enemy-pressure reports, and defense
  calibration should use Ascension 10 values as the primary modeling basis.
  Non-ascension values may be retained only as explicitly labeled reference
  data.
- V1 card valuation treats `1 damage = 1 value` at every layer. Defense value
  is the layer-dependent side and comes from `model_calibration.json`
  `blockToDamage`.
- V1 Weak and Vulnerable card terms are layer-dependent debuffs. Weak uses
  current defense pressure as prevented damage; Vulnerable uses the manual
  pressure-scaled formula and sublinear stack multipliers. Do not restore fixed
  `powerValues.Weak` or `powerValues.Vulnerable`.
- When creating simulator deck fixtures, create the deck JSON under
  `data/manual-tags/simulation_decks/` and matching shortline, midline, and
  longline scenario JSONs under `data/manual-tags/simulation_scenarios/`.
- Random card generation in the simulator should use manually curated
  source-specific JSON pools under `data/manual-tags/`, not ad hoc filtering of
  the full card library. This applies to generated-card cards and generated-card
  Powers alike. Each generating effect owns its own pool id, even when simplified
  v1 pools share contents; keep pools small, simulation-supported, and exclude
  multiplayer-only cards unless explicitly modeling multiplayer. Future pool
  completeness work should update the JSON pool contents, not replace the
  source-specific pool architecture.
- When running a deck simulation by default, run shortline, midline, and
  longline horizons: 4, 8, and 14 turns, with the same deck/scenario and seed
  unless the user asks for a different setup.
- Card value attribution should be reported as value per direct play. Per-run
  attribution is secondary context; the primary question is payoff when the
  card is actually played.
- For simulated Power card estimates, use deck-level delta EV against the
  matching reference deck as the primary card value. Continue reporting source
  attribution credits, but treat them as diagnostic realized payoff rather than
  the value estimate; they may be zero, positive while deck delta is negative,
  or larger than the net deck delta.

## Build And Verification

Run these from the repository root:

```powershell
dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-card-facts
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-card-pools
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-monster-moves
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-encounter-patterns
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-card-values --layer 1
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- write-card-review-list
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-enemy-expectations
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-encounter-weighted-enemy-pressure
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-defense-calibration
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
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-card-facts
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-card-pools
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-monster-moves
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-encounter-patterns
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-card-values --layer 1
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- write-card-review-list
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-enemy-expectations
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-encounter-weighted-enemy-pressure
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-defense-calibration
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
- `.agents/docs/runtime-lessons.md`
- `.agents/docs/local-environment.md`

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
`.agents/docs/runtime-lessons.md`.

## Editing Rules

- Highest priority: when a previous implementation, plan, or direction is
  rejected or superseded, clean it out completely. Do not leave inactive code,
  commented-out blocks, compatibility shims, fallback paths, dead interfaces, or
  "temporary" adapters merely to preserve history. Prefer a clean replacement;
  if cleanup causes a bug, fix the bug directly.
- Do not revert user changes unless explicitly requested.
- Use `rg` for search.
- Use the Edit tool for manual file edits; keep edits minimal and exact.
- Git publishing for this repo uses only `main`: do not create, push, or ask
  for feature branches unless the user explicitly overrides this rule. When the
  user asks to upload approved work, commit on `main` and push `origin main`.
- Keep changes narrow. Do not mix animation polish, value formulas, packaging,
  and card identity changes in the same edit unless the user asks.
- For resource, localization, scene, image, or JSON changes, publish before
  asking the user to launch the game.
