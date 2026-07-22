---
name: sts2-select-combat-portfolio-samples
description: Select and persist reproducible CardValueOverlay combat-aware portfolio samples across acts, encounter tiers, deck snapshots, HP states, and encounter realizations. Use when Codex needs to build or revise the twelve-cell sampling frame used by coverage audits, solver validation, or paired deck dEV estimation.
---

# StS2 Select Combat Portfolio Samples

Read `.agents/docs/combat-aware-simulation-contract.md` before changing a
portfolio. This skill defines the sampling frame; it does not make unsupported
samples disappear.

## Required Frame

Use all twelve cells:

- Act 1, 2, and 3;
- weak, normal, elite, and boss encounters in every act;
- horizons 4, 8, and 12 as separate evaluation outputs.

Every persisted sample must identify:

- cell id, act, encounter tier, and target weight;
- a stage-matched deck snapshot and its source run/event;
- player HP and max HP at combat entry;
- a concrete encounter realization and initial visible intent state;
- an HP-continuation context id;
- a deterministic semantic sample key and proposal probability;
- support status and explicit blocker reasons.

Do not substitute a final deck for an earlier-floor deck or fabricate missing HP,
encounter, or intent state.

## Source Requirements

- Use failure-inclusive run histories for empirical deck/HP distributions.
- Retain wins and losses with declared inclusion rules and version filters.
- Treat the current winning-run deck source and prior HP grids as smoke inputs,
  not calibration evidence.
- Keep target route weights separate from proposal sampling probabilities.
- Preserve unsupported target mass in coverage denominators.
- Require at least two distinct fully supported encounter realizations per cell
  before that cell can pass coverage.

If the available history extractor cannot reconstruct a required field, record
the field as unavailable and extend the extractor in a separate implementation
task. Never infer it from the final deck.

## Selection Procedure

1. Freeze source paths, game version policy, character, Ascension 10, and seed.
2. Enumerate eligible combat-entry snapshots without filtering on victory.
3. Assign each snapshot to exactly one act/tier cell from recorded encounter
   evidence.
4. Preserve the observed HP/max-HP distribution or declare an explicit prior
   sensitivity grid.
5. Select distinct encounter realizations within each cell using a documented
   proposal distribution.
6. Persist stable sample keys before checking simulator support.
7. Compile support afterward and retain every unsupported row and its weight.
8. Record selection counts, rejected source rows, and deterministic hashes.

Do not optimize the sample set for the current simulator's easiest cards or
monsters. That would turn implementation coverage into selection bias.

## Current Portfolio

The research fixture is
`data/manual-tags/combat_value_portfolios.json`. Its balanced cell weights,
winning-only deck source, and prior HP grids are provisional. They may support
smoke tests and blocker discovery only; they cannot make a runtime candidate.

After a portfolio edit, run:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  validate-combat-portfolio --verbose --output data\generated\combat_aware
```

A non-zero exit is expected while gates remain blocked. Review the generated
coverage artifacts instead of deleting unsupported samples.

## Output And Gate

Report source population, selected population, cell counts, target/proposal
weights, HP distributions, unique encounters, unsupported mass, and hashes.
The sampling frame is training-eligible only after it is failure-inclusive,
stage-matched, reproducible, and approved together with the HP calibration and
portfolio weights.
