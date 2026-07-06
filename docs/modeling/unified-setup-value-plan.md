# Unified Setup Value Plan

Status: **cutover complete (2026-07-06).** The legacy setup-priority system
(`SimulationSetupPriorityCatalog`, `SimulationCard.SetupPriorityValue`, the star /
effective / `PowerSetupPriorityValue` members, and `SetupPriorityForCardType`) has
been deleted. The whole simulator, every Tools command, and the in-game runtime now
resolve setup values solely from `card_setup_values.json` via `SetupValueResolver`
into `SimulationCard.BeamSetupValue` / `PlaySetupValue`. No fallback remains. The
rollout batches below are done except where noted: `beam` and `play` are populated
but currently equal under the fixed-Constant catalog, the horizon-dependent
resource-reference proxy is still added on top (a known double-count with the
measured value, left for a later batch), and the Power floor (`99`) is preserved as
a resolver + simulator policy — so a Power's measured value below `99` is still
clamped up (finding B, an open decision).

## 1. Problem: "setup value" is four overlapping mechanisms

The Monte Carlo simulator biases its within-turn play search with a *setup
prior*. It is used in **two** places and only ever affects search decisions,
never the reported EV:

- **Beam entry** — `ScoreSearchCard` → `CardSearchScore` decides which cards are
  even branched on (top `MaxBranchingCards`).
- **Line valuation** — the per-play `DecisionValue`
  (`DeckMonteCarloSimulator` `PlayCard`) decides which fully-simulated line wins,
  compared by `IsBetter` on accumulated `DecisionValue`.

Today that prior is assembled from **four disjoint mechanisms**:

| Component | Where it lives today | Problem |
|-----------|----------------------|---------|
| Base value (damage/block) | `StaticEstimatedValue` / `IntrinsicValue`, mixed into the beam via `Math.Max(Static, Intrinsic + resource)` | state-dependent parts recomputed live; resource double-mixed |
| Resource value (draw/energy/star) | `ExplicitResourceReferenceValue` + hard-coded `Shortline/Midline/Longline` price constants | only 3 resources; cannot express forge/replay/draw-full/create |
| Special value (forge, replay, draws-to-hand-full, create/transform/move, power engine) | **no prior** — only realized in-turn | invisible to the beam if it has no in-turn payoff |
| Manual compensation | catalog value + `SetupPriorityForCardType`'s `Math.Max(99, …)` power floor + `StarSetupPriorityValue` bonus | floor applied twice; silently clamps curated Power values up to 99 |

Cross-referenced findings (from the audit): resource added/subtracted/re-added
(A), power floor applied twice and overriding curation (B), decision-dead
`EffectiveSetupPriorityValue`/`StarSetupPriorityValue` (C), five `CardSearchScore`
overloads (D), `--setup-priorities` wiring duplicated 8× (E), mixed
manual/generated catalog with no provenance (F), half-wired brain-2 (G),
abandoned training path still documented as active (H).

## 2. The beam is `MaxBranchingCards`-narrow, so the prior is load-bearing

Real beam widths: **in-game overlay = 3** (`RealtimeEvService.MaxBranch`),
play-value CLI default = 4, scenario JSONs = 64, `DeckSimulationOptions` struct
default = 64. beam-64 is impractically slow (≈`64^plays` orderings per turn) and
is being removed.

At beam-3, a card with ~0 in-turn realized value **and** no setup prior never
enters the top-3, is never simulated, and is effectively unplayable. The setup
prior is therefore not polish — it is what makes engine/draw/power cards
*reachable* by the search. This is also why brain-2 (a learned leaf `V(s)`) lost
to setup: it replaced only the leaf value, not the beam-reachability prior.

## 3. Target: one per-card override, two slots, three provider kinds

Model each card form's setup prior as a single per-card entry with two slots and
pluggable providers. This mirrors the already-proven runtime pattern in
`CardValueOverlay.Core/Values/` (`ValueSource`, `ValueResolver`,
`EffectiveValue<T>`) — we bring the same shape to the simulator side.

- **Two slots per form**: `beam` (reachability — does the card get explored) and
  `play` (line valuation — does its line win). Default: an unspecified slot
  mirrors the other; if both are unspecified, both fall back to the measured
  source. So `beam == play` by default; they diverge only where you say so.
- **Three provider kinds per slot**:
  - `constant` — a fixed number (e.g. a Power reachability floor).
  - `function` — a named, **stateless** function of the card's own fields +
    horizon (e.g. `star`, `resource`). State-dependent value stays in the
    realized layer, never in a provider.
  - `source` — a reference to a measured table (direct-play / deck-delta),
    per horizon.

The three provider kinds subsume every mechanism in §1:

| Provider | Replaces |
|----------|----------|
| `constant` | power floor 99, any hand-tuned value |
| `function` `star` | `StarSetupPriorityValue` (`(StarGain+StarNextTurn)×5`) |
| `function` `resource` | `ExplicitResourceReferenceValue` + the price constants |
| `source` | the measured direct-play / deck-delta table (the empirical default) |

Why two slots matter (the VoidForm / setup-Power case): you want the card
**always explored** (`beam` = high floor) but valued **truthfully**
(`play` = measured cross-turn value, which may be small or short-horizon
negative). One slot cannot express both; splitting them resolves the
"future-payoff card can never win on realized alone" and "measured value may be
negative" tensions cleanly.

## 4. The measured `source`: how the empirical value is produced

The `source` provider reads a measured table produced by the existing direct
play-value machinery (`estimate-direct-play-values`), which already computes both
quantities we need per form per horizon.

- **Non-Power** — value per direct play, strategy auto:
  - `source-credit` when every term is attributable (damage/block/energy/star/
    forge/power-trigger): `Σ credits ÷ plays`.
  - `play-delta` when a term is not attributable (`draw` / `createCard` /
    `transformCard` / `moveCardBetweenPiles` / `selectCards`): run the deck
    normally vs. with the probe in `BlockedPlayModelIds`, value it as
    `(normalEV − blockedEV) ÷ plays`. This is how **special value** enters the
    number without any hand formula — you measure the downstream difference.
- **Power** — deck-level delta EV: `EV(deck+power) − EV(reference deck)`
  (`JoinDeckDeltaValue`, already emitted per horizon). A Power is played ~once, so
  this is on the same per-play scale as the non-Power numbers.
- **Three horizons** — measured at short/mid/long (4/8/14 turns) separately.
- **Provenance** — each entry records whether a value is generated or manual;
  generation only overwrites generated slots.
- **Bootstrapping** — probes pin the measured card (`PinnedModelIdSearchCardScorer`)
  so its own value is measured regardless of its prior; other cards use the
  current table. Run → update generated → re-run → stop when two rounds agree.
  No online learning needed.

## 5. Skeleton (built now, not yet wired)

New, self-contained types under `CardValueOverlay.Modeling/Simulation/`:

- `SetupValueProvider` (`SetupValueProviderKind` = `Inherit`/`Constant`/`Function`/`Source`).
- `SetupValueContext` + `SetupHorizon` + `HorizonValues` — the stateless inputs a
  function/source sees.
- `SetupValueFunctions` — named stateless functions (`zero`, `powerFloor`,
  `star`, `resource`) re-expressing today's mechanisms.
- `CardSetupValueCatalog` / `CardSetupValueEntry` / `CardSetupValueForm` — the new
  per-card, per-form, per-horizon JSON schema + loader (mirrors
  `SimulationSetupPriorityCatalog`).
- `SetupValueResolver` → `ResolvedSetupValue { Beam, Play, BeamSource, PlaySource }`.

Covered by `SetupValueResolverResolvesProviders` in the modeling tests. The legacy
`SimulationSetupPriorityCatalog` / `SetupPriorityValue` machinery has been deleted;
the resolver is now the sole setup-prior path.

## 6. Rollout batches

1. **Skeleton** *(done)* — types + resolver + tests. No behavior change.
2. **Generation** — extend `estimate-direct-play-values` to emit the per-horizon
   `source` table with provenance; Powers via deck-delta; iterate to convergence.
3. **Runtime switch** — `SimulationCardLibraryBuilder` bakes
   `SimulationCard.BeamSetupValue` / `PlaySetupValue` from the resolver; the
   simulator reads `BeamSetupValue` in `ScoreSearchCard`/`CardSearchScore` and
   `PlaySetupValue` in `DecisionValue`. Re-benchmark against today's branch-3.
4. **Deletion** — remove `ExplicitResourceReferenceValues` + the price constants,
   `SetupPriorityForCardType`'s `Math.Max`, `StarSetupPriorityValue` /
   `EffectiveSetupPriorityValue`, the builder Power fallback, the beam-64 default,
   the 21 scenario fixtures + `compare-hegemony-energy`, and the abandoned
   training / brain-2 / neural-scorer commands and code. Update
   `CLAUDE.md`/`README`/`AGENTS`.

## 7. Open decisions

1. Slot shape: one entry with two optional slots defaulting equal (recommended)
   vs. two fully independent tables.
2. Provider JSON format: typed `{kind, value/function/source}` now, or start with
   `constant` + `source` and add `function` later.
3. Whether `beam` should apply a `max(floor, measured)` policy for Powers
   (reachability floor) — resolver policy vs. an explicit `constant` beam slot.
