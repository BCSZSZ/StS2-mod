# AGENTS.md

Instructions for runtime resources under `CardValueOverlay/`.

## Resource Boundary

- Files here are packaged into the PCK.
- After JSON, localization, image, scene, or other resource changes, run:

```powershell
dotnet publish CardValueOverlay.csproj -v minimal
```

## Config

- `data/card_values.json` is schema version `2`.
- The `cards` table may be empty while real card values are being prepared.
- Do not add temporary sample card values to runtime config.
- Do not use v1 scalar fields such as `manualValue` or `fixedValue`.

## Localization

- The mod currently supports English and Simplified Chinese.
- Use game-discovered localization table names such as `gameplay_ui.json`.
- Fixed overlay text should have a hardcoded fallback so rendering never fails
  because of missing localization.
