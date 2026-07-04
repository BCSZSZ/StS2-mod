# Search-Policy Value Network — Phase 0–2 results (2026-07-05)

Tested whether a learned state-value network `V(s)` can replace the hand-curated
**setup-priority** proxy as the search's line evaluator ("brain 2"). **Verdict:
the machinery works end-to-end, but the current `V(s)` (single-rollout labels,
53 features) LOSES to hand-tuned setup by a wide margin. Denoising labels helps
calibration but not ranking, so it will not close the gap alone. Recommendation:
ship branch-3-heuristic (+12.2%, setup) now; treat the value network as a
research bet gated on richer features + re-collection.**

## Why a value network (the "brain 2" framing)

The simulator is two nested loops: an OUTER turn-by-turn loop (future turns
actually happen here) and an INNER within-turn beam search (`Search`) that only
simulates the current turn. The inner search picks the play line maximizing
`DecisionValue`:

- Without a learned evaluator: `DecisionValue = realized(this turn) + setup + resource`.
  The inner search is myopic (can't see future turns); setup priority is a
  hand-curated proxy that keeps engine/power cards (0 realized this turn) in play.
- Value network: `DecisionValue = realized(this turn) + V(turn-end leaf)`, where
  `V(s)` is a learned estimate of forward realized value. It replaces the setup
  proxy with a learned, per-state forward value — no per-card curation.

`V(s)` slots into setup's exact role (steers line selection; the reported EV is
still realized value), so it cannot inflate the reported number — a bad `V`
just steers to a worse line.

## Phase 0–1: data + offline training (local, free)

- **Labels are free**: each of the 100,020 forward-Q decision groups already
  carries `contextFeatures` (53 state features: energy/stars, hand composition,
  active powers incl. Calamity/VoidForm/…, next-turn setup, buffs) and per-card
  `teacherRouteValue`. V label = `max_c teacherRouteValue` (state value under
  greedy play). Deck-level split (deckIndex %10) → 79.8k/10.2k/10.1k.
- **MLP** 53→128→64→1, MSE regression. Offline test (unseen decks):

  | metric | value | note |
  | --- | --- | --- |
  | Spearman ρ | **0.671** | ranks states moderately |
  | R² | 0.251 | explains 25% variance; overfits (val 0.43 → test 0.25) |
  | MAE | 56.6 | +22% vs median baseline |

  Moderate signal — learnable but weak; capped by single-rollout label noise and
  a possibly-insufficient 53-feature state.

## Phase 2: in-game A/B (held-out 30 decks, runs 30 / turns 8)

`NeuralStateValue` (mirrors `NeuralSearchCardScorer`) + `--state-value-model`
wired into `benchmark-training-decks`. The estimator takes the simulator's own
`BuildContextFeatures` output, so inference features match training by construction.

Integration bug found + fixed: `V(s)` is an optimal-*continuation* value (~200),
far larger than a single play's realized delta (~6–30). Valuing a mid-turn "stop
here" option by `V(mid)` let V's error swamp the play delta → the search stopped
immediately (EV 234→38). Fix: never voluntarily stop under a learned evaluator —
evaluate `V` only at genuine turn-end leaves; an internal node with playable cards
seeds `best` at −∞ so a complete line always wins (matches setup mode's play
propensity).

Result:

| config | EV sum | vs branch2 | gap closed |
| --- | ---: | ---: | ---: |
| branch2 (setup) | 14350 | +0.0% | 0% |
| branch2 + V | 12479 | −13.0% | −40% |
| branch3 (setup) | 16099 | **+12.2%** | 37% |
| branch3 + V | 12295 | −14.3% | −44% |
| branch8 (setup, ceiling) | 19067 | +32.9% | 100% |

Head-to-head at the same width: **V is −23.6% vs setup at branch-3, −13.0% at
branch-2.** V loses in every act group, worst on engine-heavy decks
(preAct2Boss −26.2%, final −26.3%) — matching the offline finding that noisy
labels are worst on big/late decks. This coherence indicates the C# integration
is faithful; the loss is V quality, not a wiring bug.

## Does denoising fix it? Calibration yes, ranking no

Trained V on K=1 vs K=5-averaged labels (same 16-deck A/B set):

| labels | RMSE | Spearman ρ |
| --- | ---: | ---: |
| K=1 | 67.8 | 0.612 |
| K=5 (denoised) | **35.1** (−48%) | 0.611 (flat) |

Denoising sharply improves *calibration* (RMSE) but leaves *ranking* (Spearman)
unchanged. Line selection depends on ranking, so **label denoising alone will not
close the −23.6% in-game gap.** Improving ranking most likely needs richer state
features (enemy HP/intent, deck-composition detail, more powers) — which requires
re-collection, not a local retrain.

## Recommendation

1. **Ship branch-3-heuristic (setup) now** — reliable +12.2%, no ML risk. This is
   the concrete win from this whole line of work.
2. **Park the value network** as a research bet. If pursued, the next step is an
   EC2 re-collection with (a) K≥5 denoised labels AND (b) a richer state-feature
   set, then re-screen OFFLINE (global + within-decision ranking) before another
   in-game A/B. Do not invest further until offline ranking clearly exceeds what
   setup achieves — beating a well-tuned setup is not guaranteed.
3. The infrastructure is built and reusable: `NeuralStateValue` +
   `DeckSimulationOptions.StateValue` + `--state-value-model`. Default behavior is
   unchanged (setup) when no model is supplied.

## Artifacts (uncommitted — pending review)

- Code: `CardValueOverlay.Modeling/Simulation/NeuralStateValue.cs` (new),
  `DeckSimulationOptions.cs` (`StateValue`), `DeckMonteCarloSimulator.cs`
  (leaf-only V, seed logic, per-play decisionValue), `Program.cs`
  (`LoadStateValueEstimator`), `Program.DeckBenchmarks.cs` (wiring).
- Scratch (`tmp/`, git-ignored): `extract_vnet.py`, `train_vnet.py`,
  `vnet_dataset.npz`, `vnet_model.json` (the tested V), `ho_b2v.json`/`ho_b3v.json`
  (A/B), `vnet_K1/K5_model.json` (denoise probe).
