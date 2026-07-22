---
name: sts2-combat-oracle-sharding
description: Build balanced CardValueOverlay combat-aware Exact-oracle and approximation-validation work shards. Use when Codex needs to profile or distribute expensive solver work across portfolio cells, decks, HP states, encounters, horizons, seeds, and candidate cards without hiding slow or unsupported target mass.
---

# StS2 Combat Oracle Sharding

Read `.agents/docs/combat-aware-simulation-contract.md`. A shard is an execution
unit for solver evidence, not a new sampling distribution.

## Work Unit

Use a stable key containing:

```text
(portfolio cell, deck snapshot, HP state, encounter realization,
 initial intent, horizon, semantic seed, candidate/reference pair, solver mode)
```

Keep candidate and reference runs together so paired dEV uses the same semantic
chance stream. Never split a pair across independently changing inputs.

## Profiling

Profile representative units with the exact intended solver, horizon, budgets,
instrumentation mode, and cache setting. Record separately:

- wall time and CPU time;
- allocated bytes and peak working set;
- expanded decision and chance nodes;
- exact, approximate, budget-exceeded, and unsupported status;
- portfolio target weight and proposal probability.

Do not use legacy search-policy node counts, `branch=3`, or
`collect-search-policy-data` timings as combat-solver cost estimates.

The existing benchmark command is useful for local solver scaling evidence:

```powershell
$dotnet = if ($env:LIAO_DOTNET) { $env:LIAO_DOTNET } else { "dotnet" }
& $dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- `
  benchmark-information-state-solver --iterations 20 --workers 1,2,4 `
  --output data\generated\combat_aware
```

It does not yet create production shards. Do not invent a sharding CLI; add and
test one in a separate implementation task when distributed execution is needed.

## Shard Construction

1. Freeze the portfolio, solver build, budgets, and all input hashes.
2. Run a pilot covering every cell and horizon.
3. Estimate time and memory by the full work-unit key; retain censored slow-tail
   observations instead of replacing them with a timeout value.
4. Keep paired candidate/reference units atomic.
5. Balance shards by longest-processing-time using both time and memory limits.
6. Mix cells and horizons only when this does not violate memory ceilings.
7. Persist every unsupported or budget-exceeded unit in the manifest.
8. Recompute balance when solver semantics, inputs, or instrumentation changes.

Do not exclude slow encounters, complex decks, or unsupported samples to make
throughput look better. Their target mass remains part of coverage and No-Go
decisions.

## Exact And Approximate Sets

- Exact-oracle shards must contain only fixtures proven to fit explicit budgets.
- Approximation-validation shards must pair each approximate run with the same
  Exact fixture and semantic seed.
- Compare EV, paired dEV, death probability, P90/CVaR HP loss, chosen action,
  runtime, and allocations.
- The Phase 1 highest-probability sparse truncation is diagnostic and not an
  approved production approximation.

## Output

Persist a manifest with input hashes, work-unit keys, predicted/actual cost,
target weights, solver status, retry lineage, and shard totals. Report max/median
shard cost, imbalance ratio, unsupported weight, budget-exceeded weight, and
oracle/approximation error. Sharding success does not make a report eligible for
runtime installation.
