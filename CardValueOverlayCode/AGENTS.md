# AGENTS.md

Instructions for runtime mod code under `CardValueOverlayCode/`.

## Runtime Boundary

- This folder owns Harmony patches, Godot UI nodes, runtime config loading, and
  direct StS2 node/model reads.
- Do not put pure scoring rules here. Use `CardValueOverlay.Core/`.
- Do not assume a card node is stable at the first screen callback.

## Overlay Rendering

- Use plain Godot `Label` nodes unless a custom node is absolutely necessary
  and proven safe in `godot.log`.
- Reuse labels by name. Rendering must be idempotent and must not duplicate
  labels.
- Keep visibility decisions in renderer/context code, not inside label nodes.
- Current intended contexts are enlarged card inspection and card reward
  selection.

## Config Loading

- Never hide config parse failures. Log the full exception chain.
- Validate loaded config before use.
- If falling back to defaults, log the exact reason.
- Avoid static field initialization for JSON options, reflection-heavy
  converters, Godot access, or game API access.

## Verification

After runtime changes:

```powershell
dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
dotnet publish CardValueOverlay.csproj -v minimal
```

Then launch the game and confirm the latest `godot.log` shows initialization,
config load, patch install, and no `CardValueOverlay` exception.
