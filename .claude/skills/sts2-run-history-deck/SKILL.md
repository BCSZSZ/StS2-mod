---
name: sts2-run-history-deck
description: Reconstruct factual Slay the Spire 2 decks from local run histories and assess whether history fields are sufficient for combat-entry sampling. Use for floor-N Regent deck extraction, event-order audits, or planning failure-inclusive deck/HP/encounter snapshots without estimating them from final decks.
---

# StS2 Run History Deck

Use this skill for factual run-history reconstruction. Do not judge card value,
simulator support, or HP value here.

## Current Command Boundary

`list-run-history-decks` currently:

- filters to winning runs in code;
- reconstructs the deck at a requested floor;
- does not emit combat-entry player HP/max HP, encounter tier/identity, initial
  intent, or proposal weight.

It is valid for reproducing winning-run deck fixtures and historical strategy
analysis. It is not sufficient for empirical combat-aware portfolio or HP
calibration data. Do not describe its output as failure-inclusive.

## Deck Reconstruction

1. Start Regent A10 from `StrikeRegent x4`, `DefendRegent x4`,
   `FallingStar x1`, `Venerate x1`, and `AscendersBane x1`.
2. Apply map-point history in recorded order: removals, transformations, gained
   cards, then upgrades.
3. Treat `--floor 5` as after map points 1 through 5, including floor-5 rewards.
   Use `--before-floor-rewards` only when explicitly requested.
4. Preserve upgrade and enchantment identity.
5. Report `runId`, build, seed, source path, floor semantics, deck count, events,
   and grouped cards.

Never reverse-engineer an earlier deck from final contents alone.

## Commands

Latest five matching winning Regent A10 floor-5 decks:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  list-run-history-decks --floor 5 --limit 5
```

Specific run:

```powershell
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  list-run-history-decks --run-id 1781920615 --floor 5 `
  --output-json tmp\run-1781920615-floor5.json
```

The command uses `data/extracted/card_catalog.generated.json` only to label card
model ids with type names. It does not use value candidates or simulator rules.

## Combat-Aware Snapshot Requirement

For `.agents/docs/combat-aware-simulation-contract.md`, a future extractor must
enumerate both wins and losses and persist each actual combat-entry observation:

- run/build/seed and source event identity;
- character, Ascension 10, act, floor, and encounter tier;
- stage-matched deck with card-instance facts;
- player HP and max HP immediately before combat;
- encounter identity/realization and, only if recorded or reproducible from
  sourced state, initial visible intent;
- outcome/censoring fields needed to fit continuation value without leakage.

Audit raw `.run` schemas before implementation. Mark absent fields unavailable;
do not fabricate intent state or infer starting HP from end-of-combat damage.
Selection must occur before simulator-support filtering so unsupported mass stays
visible.

## Verification And Output

For current deck extraction, spot-check event order against the raw run and
confirm the JSON/text deck totals agree. For a future combat snapshot extractor,
test wins and losses, pre/post-reward boundaries, healing/damage timing,
transform/remove/upgrade order, censored runs, and deterministic output hashes.

State clearly whether the artifact is:

- a winning-only deck reconstruction;
- a provisional smoke fixture; or
- a failure-inclusive combat-entry dataset.
