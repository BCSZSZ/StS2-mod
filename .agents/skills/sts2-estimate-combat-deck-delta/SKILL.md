---
name: sts2-estimate-combat-deck-delta
description: Estimate CardValueOverlay combat-aware card values as paired candidate-deck versus reference-deck dEV across the twelve-cell portfolio and 4/8/12 horizons. Use for dry runs, research estimates, Exact-oracle comparisons, confidence/risk review, or future formal training runs after coverage gates pass.
---

# StS2 Estimate Combat Deck Delta

Read `.agents/docs/combat-aware-simulation-contract.md`. Use paired deck dEV
as the only primary card-value scale.

## Preconditions

Before a numeric estimate:

1. Run `sts2-check-combat-coverage`.
2. Confirm the candidate form is fully supported.
3. Confirm the requested samples preserve portfolio target mass.
4. Confirm HP parameters and portfolio weights are labeled prior, provisional,
   or empirical.
5. Confirm the requested solver mode has an explicit status and validation
   evidence.

If no supported paired samples exist, generate a null/blocked report rather
than relaxing support.

## Value Definition

For every sample and horizon:

```text
pairedDelta = combatEV(candidate deck, semanticRunKey)
            - combatEV(reference deck, same semanticRunKey)
```

Aggregate with declared portfolio/importance weights. Do not divide by candidate
draws or plays. Do not substitute source-credit or blocked-play-per-play values.

## Command

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  estimate-combat-aware-deck-delta `
  --candidate <modelIdOrTypeName> `
  --portfolio data\manual-tags\combat_value_portfolios.json `
  --hp-calibration data\manual-tags\hp_continuation_calibration.json `
  --horizons 4,8,12 `
  --minimum-samples 12 `
  --maximum-samples 240 `
  --degree-of-parallelism 4 `
  --output data\generated\combat_aware
```

Use `--no-cache` only to diagnose baseline-cache behavior. Do not raise
parallelism above four without measuring physical cores and memory headroom.

## Required Report Fields

Review each horizon and portfolio cell for:

- sample count, target/support weight, ESS, and confidence interval;
- baseline, candidate, and paired dEV;
- actual enemy HP damage and healing;
- player HP loss, death probability, loss-budget exceedance, P90, and CVaR;
- Exact/approximate/budget-exceeded/unsupported fractions;
- model/input hashes and semantic seeds;
- `primaryDeltaEv` and `runtimeCandidate`.

A research run may report conditional diagnostics, but the primary value remains
null when the support or approval gate fails.

## Exact And Approximate Runs

Use Exact only on fixtures that fit explicit budgets. When evaluating an
approximation, first compare it with the same Exact fixture and report EV, dEV,
risk-tail, action, time, and allocation error. Do not accept the Phase 1
highest-probability truncation as an approved approximation.

## Acceptance

A formal training estimate requires:

- all coverage gates pass;
- paired confidence and ESS gates pass in every requested horizon;
- approximate-solver error is within an approved tolerance;
- HP calibration and portfolio weights are empirical/user-approved;
- the report explicitly says `runtimeCandidate: true`.

Until then, keep outputs under `data/generated/combat_aware/` and do not run an
installer or publish the mod.
