# CardValueOverlay

Minimal Slay the Spire 2 mod scaffold based on
[Alchyr/ModTemplate-StS2](https://github.com/Alchyr/ModTemplate-StS2).

This repository contains the base mod plus the first shared tooling layer for a
future card value overlay:

- `CardValueOverlay.csproj` Godot .NET mod project.
- `CardValueOverlay.json` mod manifest.
- `CardValueOverlayCode/` runtime mod initializer, Harmony patch, and overlay UI code.
- `CardValueOverlay.Core/` pure value/config/expectation logic. Tools reference this as a project; the mod compiles these source files into its main DLL.
- `CardValueOverlay.Tools/` local C# CLI for config validation, card extraction, and averages.
- `CardValueOverlay.Core.Tests/` lightweight executable tests for shared logic.
- `CardValueOverlay.Modeling/` offline modeling and game-data extraction library; never packaged into the mod.
- `CardValueOverlay.Modeling.Tests/` lightweight executable tests for modeling/extraction logic.
- `data/` local/generated modeling inputs, fixtures, and manual tags.
- `CardValueOverlay/` asset/config/localization folder with the template mod icon,
  JSON value data, and English/Simplified Chinese mod strings.
- `AGENTS.md` repository instructions for Codex.
- `.agents/docs/` project roadmap, local environment facts, and lessons learned
  from StS2/Godot mod debugging.
- `.agents/skills/` repo-scoped Codex skills and reusable scripts for StS2
  modeling workflows.
- `docs/modeling/` card-value methodology and the planned separate C# modeling
  layer.

## Local Setup

The local game install verified for the `liao-work` profile is:

```text
C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2
```

Machine-specific paths are selected by `STS2_MOD_PROFILE`. On this computer it
should be `liao-work`; the paired user environment variable `${env:liao-work}`
contains the real local paths for StS2, Godot, .NET, and ILSpy. If discovery
fails, copy `Directory.Build.props.example` to `Directory.Build.props` and keep
the `liao-work` / `liao-home` profile blocks aligned with those environment
records. `Directory.Build.props` is ignored by Git because it is
machine-specific.

Current local state: .NET SDK 9.0.315 and Godot Mono 4.5.1 are installed.
`dotnet build CardValueOverlay.csproj` and `dotnet publish
CardValueOverlay.csproj` both succeed locally.

## Modeling Extraction

Generate local modeling reference data from the installed game:

```powershell
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
```

This writes generated files under `data/extracted/` and `data/generated/`.
Those generated files are ignored by Git because they are derived from the
local game install. `parse-card-facts` uses `ilspycmd` to decompile `sts2.dll`
into the ignored `data/generated/decompiled/` cache, then writes
`data/extracted/card_facts.generated.json` with card metadata, keywords, action
facts, raw operations, and source evidence. `estimate-card-values` consumes
those facts plus `data/manual-tags/model_calibration.json` to write reviewable
value candidates under `data/generated/`. `parse-card-pools` writes card pool
membership and multiplayer-only flags for review grouping.
`write-card-review-list` combines those memberships with value candidates into
a grouped review report. `parse-monster-moves` writes monster move profiles for
later enemy-damage and debuff expectation models. `estimate-enemy-expectations`
turns those profiles into equal-weight enemy damage/debuff expectation
summaries under `data/generated/`.
`estimate-defense-calibration` combines those summaries with
`data/manual-tags/model_calibration.json` and writes review-only block and
enemy-pressure calibration reports under `data/generated/`.
The current primary modeling direction is the offline combat-aware information-
state simulator under `CardValueOverlay.Modeling/Combat`. Its value equation is
actual enemy HP lost minus enemy healing plus the change in player HP
continuation utility. Card values are paired candidate-deck versus reference-
deck dEV across Act 1/2/3 x Weak/Normal/Elite/Boss and horizons 4/8/12.

Phase 1 is explicitly `No-Go` for training and runtime cutover. Coverage and
long-horizon performance gates are not yet satisfied, HP continuation parameters
are still research priors, and generated reports keep `runtimeCandidate: false`.

Current combat-aware research entry points are:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- validate-combat-portfolio --verbose --output data\generated\combat_aware
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- replay-monster-intents --encounter <modelIdOrTypeName> --turns 12
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- benchmark-information-state-solver --iterations 20 --workers 1,2,4 --output data\generated\combat_aware
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-combat-aware-deck-delta --candidate <modelIdOrTypeName> --horizons 4,8,12 --output data\generated\combat_aware
```

The portfolio validator currently returns non-zero when it correctly finds a
blocked gate; inspect its generated coverage/smoke reports instead of treating
that expected No-Go as a malformed fixture.

The legacy Monte Carlo/direct-play/source-credit commands remain available only
for regression and migration comparison; they must not generate new primary
training values.

See `.agents/docs/combat-aware-simulation-contract.md` for the canonical model,
solver, coverage, and cutover contract. No offline modeling command may update
`CardValueOverlay/data/card_values.json` or publish the mod without a later
approved runtime-candidate report and explicit user approval.

For Codex-specific working rules, start with `AGENTS.md`.
