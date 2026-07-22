---
name: sts2-install-combat-training-values
description: Gate and install approved CardValueOverlay combat-aware paired-deck dEV outputs into runtime training values while preserving unrelated configuration. Use only after coverage, solver, confidence, HP-calibration, and portfolio-weight gates pass and the user explicitly asks to install values.
---

# StS2 Install Combat Training Values

Read `.agents/docs/combat-aware-simulation-contract.md` and
`docs/modeling/card-value-json-schema.md` before touching runtime data.

## Default State: Inactive

Phase 1 is currently No-Go. There is no approved combat-aware installer command,
and the legacy direct-play/floor8 installers do not implement this contract.
Do not use them as substitutes.

Refuse installation unless every selected report has:

- `runtimeCandidate: true`;
- non-null paired `primaryDeltaEv` for each requested horizon;
- passing cell and overall coverage gates;
- passing ESS, confidence, and risk-tail gates;
- an approved solver with recorded Exact-oracle error;
- empirical, failure-inclusive HP calibration;
- user-approved portfolio weights;
- matching schema, input hashes, card identity, and game-version scope;
- explicit user authorization to install the named artifacts.

Research, provisional, approximate-without-oracle, or coverage-blocked reports
must remain under `data/generated/combat_aware/`.

## Required Future Installer Behavior

When all gates pass, implement or invoke a combat-specific installer that:

1. Reads only approved combat-aware reports.
2. Validates card form and upgrade state exactly.
3. Maps 4, 8, and 12 turns to shortline, midline, and longline.
4. Writes paired deck dEV directly; never divides by draws or plays.
5. Updates only requested horizons and preserves unrequested values, metadata,
   display config, and unrelated cards.
6. Records `generation.method` as a combat-aware paired-dEV method and updates
   only the corresponding horizon timestamp.
7. Writes to a temporary file, validates schema version 3, and replaces the
   runtime JSON only after validation succeeds.
8. Produces a before/after audit containing source report hashes.

Do not automatically publish the mod. Installation and local deployment are
separate user-authorized actions.

## Verification

After an authorized install, run:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
& $dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
```

Diff `CardValueOverlay/data/card_values.json` and prove that only authorized
cards/horizons and their matching generation timestamps changed.

## Output

Report the gate evidence, input hashes, exact cards/forms/horizons, before/after
values, preserved fields, validation results, and whether deployment was
separately requested. If any gate fails, report No-Go and make no runtime edit.
