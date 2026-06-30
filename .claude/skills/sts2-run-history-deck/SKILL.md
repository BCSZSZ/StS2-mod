---
name: sts2-run-history-deck
description: Extract reproducible Slay the Spire 2 run-history decks for this CardValueOverlay repo. Use when Claude needs to list or reconstruct Regent A10 winning decks, floor-N decks, or local .run history decks by applying starter deck plus map-point gained/removed/transformed/upgraded card events instead of estimating from final deck contents.
---

# StS2 Run History Deck

Use this skill for deck discovery from local StS2 `.run` files. Do not judge card quality or simulator support here; only reconstruct and print factual deck contents.

## Workflow

1. Run the `CardValueOverlay.Tools` C# command from the repo root.
2. For Regent A10, start from the real A10 starter deck:
   `StrikeRegent x4`, `DefendRegent x4`, `FallingStar x1`, `Venerate x1`, `AscendersBane x1`.
3. Apply map-point history in order: card removals, transformations, gained cards, then upgrades.
4. Treat `--floor 5` as "after applying map points 1 through 5", including floor-5 rewards. Use `--before-floor-rewards` only when the user explicitly asks for the deck before floor reward resolution.
5. Print `runId`, build, seed, source path, deck count, and grouped cards. If the user needs a durable handoff, add `--output-json tmp/run-history-decks.json`.

## Commands

Latest five winning Regent A10 floor-5 decks:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  list-run-history-decks `
  --floor 5 `
  --limit 5
```

Specific run:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  list-run-history-decks `
  --run-id 1781920615 `
  --floor 5 `
  --output-json tmp\run-1781920615-floor5.json
```

The C# command reads `data/extracted/card_catalog.generated.json` only for `CARD.*` to `TypeName` labels. It does not use `card_value_candidates.generated.json`, confidence, warning text, or valuation rules. On the `liao-work` machine, prefer `$env:LIAO_DOTNET` because the system `dotnet.exe` path may point to a host-only install without SDKs.
