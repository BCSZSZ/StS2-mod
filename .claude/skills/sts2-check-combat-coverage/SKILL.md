---
name: sts2-check-combat-coverage
description: Audit CardValueOverlay combat-aware simulation support and target-weight coverage. Use when Codex needs to identify unsupported cards, monsters, encounters, deck snapshots, portfolio cells, or blocker frequencies before Exact/approximate solver work or paired deck dEV estimation.
---

# StS2 Check Combat Coverage

Read `.agents/docs/combat-aware-simulation-contract.md`.

## Coverage Unit

Do not classify a card form in isolation as sufficient. A runnable sample requires
all of the following:

- every card form in the stage-matched deck compiles to complete physical
  semantics;
- every monster and current/future reachable intent is supported;
- the encounter realization and slot selection are concrete;
- transition probabilities are source-backed;
- player HP/max HP and visible initial intent are present;
- the requested horizon and solver mode have valid budgets.

A sample with any missing element is unsupported.

## Strict Rules

- Preserve unsupported samples and target weight in the denominator.
- Do not redistribute missing mass to supported samples.
- Do not map unknown actions or transitions to zero value.
- Do not invent uniform probabilities.
- Do not use legacy source-credit/play-delta warnings as combat support.
- Report each blocker with scope, stable identity, frequency, and evidence.

## Command

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  validate-combat-portfolio --verbose --output data\generated\combat_aware
```

The current Phase 1 gate is expected to return non-zero. Treat the generated
coverage and smoke artifacts as the result:

- `phase1_coverage.generated.json`
- `phase1_coverage.md`
- `phase1_smoke.generated.json`
- `phase1_smoke.md`

## Review Order

1. Check portfolio schema, twelve cells, target-weight sum, and HP contexts.
2. Check card forms and rank blockers by affected deck snapshots.
3. Check monster initial-intent and follow-up support.
4. Check encounter realization and unique-encounter counts per cell.
5. Check fully supported deck snapshots.
6. Check supported sample fraction per cell and overall target-weight mass.
7. Confirm unsupported reasons are not collapsed or redistributed.
8. Use blocker frequency to choose the next vertical-slice implementation.

## Gate

Training remains No-Go unless:

- every cell has at least two fully supported encounters;
- every cell has at least 70 percent supported sample mass;
- overall supported target-weight mass is at least 80 percent;
- the deck/HP source is stage-matched and failure-inclusive;
- solver and HP calibration gates also pass.

Coverage passing is necessary but not sufficient for runtime installation.

## Output

Report:

- supported/total card forms, monsters, encounter realizations, and deck
  snapshots;
- cell-by-cell supported samples, target mass, and unique encounters;
- overall supported target-weight mass;
- top blockers by scope and affected sample count;
- the exact Go/No-Go state.

Do not emit a numeric primary dEV when the coverage gate fails.
