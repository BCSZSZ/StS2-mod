# Card Value Modeling Methodology

> Current-direction note: the static conversion and legacy Monte Carlo material
> below is retained as historical background for the existing estimator. It is
> not the primary training methodology. New card-value work follows
> `.agents/docs/combat-aware-simulation-contract.md`: physical combat EV,
> twelve-cell stage/encounter sampling, and paired deck dEV. Do not use the
> legacy block/debuff/setup conversions below to produce combat-aware values.

Source note: this document summarizes the methodology from the shared ChatGPT
conversation at `https://chatgpt.com/share/6a3c6d54-5018-83ee-a2e4-747f091a0ca0`.
It is not the combat-aware implementation specification. Its semi-computed and
semi-empirical estimates may remain review references, but they cannot seed new
runtime training values without passing the combat-aware cutover gates.

## Purpose

The project needs one coherent value system for three related outputs:

- configured training horizon card values in `card_values.json`;
- dynamic runtime values when current deck, layer, enemies, energy, and draw
  state are known;
- local analysis reports for comparing cards, upgrades, drafts, relic changes,
  and deck construction decisions.

The value unit is a normalized card-value unit, not raw damage. Raw card text
must be converted through combat context, resource constraints, timing, and
uncertainty before it becomes a card value.

## Core Combat Model

Given a deck where each card has a cost and an estimated value, each combat can
be modeled as two nested problems.

The draw problem is a stochastic process. Cards are drawn without replacement
from the draw pile, with discard reshuffle behavior creating a sequence of
hands over time. Monte Carlo simulation is the practical first implementation;
exact dynamic programming can be added later for small state spaces.

The play problem is a constrained optimization problem. For a given hand and
available energy, choose the playable subset that maximizes total value. The
first version can solve this as a 0-1 knapsack problem. More advanced versions
can add card-generated energy, extra draw, exhaust, retention, forced plays, and
ordering constraints.

The combined model estimates the probability distribution of per-turn playable
value. That distribution is the bridge from card/deck composition to expected
combat output.

## PMF Outputs

Simulation should approximate a probability mass function over turn value. From
that PMF, the model should compute:

- expected value: average playable value per turn;
- variance: risk of bad hands and unstable combat;
- covariance across turns: whether one bad turn makes later turns better or
  worse because draw piles are not independent.

These outputs let us estimate marginal value changes caused by adding/removing
cards, upgrading cards, changing energy, changing draw, or shifting deck size.

## Damage And Defense Normalization

The v1 model treats direct damage as the fixed value unit:

- `1 damage = 1 value` at every layer and in every act segment.

Block is the layer-dependent side of the exchange rate. Its value depends on:

- current card pool and deck shape;
- enemy health and damage;
- expected combat length;
- whether enemy damage is smooth, bursty, or collapses after a key enemy dies.

The first simplified model treats both sides' offense and defense as quantities
that ultimately minimize player HP loss. This is intentionally rough but good
enough to seed reviewable configured values.

Working estimates:

- as of 2026-06-30, the calibrated defensive anchor is `1 block = 1.2 value`
  at simulator `floor8` / layer `8`;
- higher-pressure layers increase `1 block` through the layered
  `blockToDamage` table, with the whole table scaled by `0.7791196` from the
  earlier enemy-pressure curve;
- damage stays fixed at `1 damage = 1 value` and should not be increased by
  layer pressure.

This implies block conversion should be a layered table, not a single constant.
The exact conversion can vary by act/layer, character, and enemy mix.

Current reference points from `data/manual-tags/model_calibration.json`:

| Context | Layer | 1 block value | 5 block Defend | 8 block Defend+ |
| --- | ---: | ---: | ---: | ---: |
| Early floor8 | 8 | 1.200 | 6.000 | 9.600 |
| Act 2 start | 17 | 1.369 | 6.845 | 10.951 |
| Final deck | 47 | 2.527 | 12.633 | 20.213 |

Enemy debuffs use the same layer pressure:

- `Weak` is valued as equivalent prevention: current defense pressure * `25%`,
  then converted through the current layer's block value.
- `Vulnerable` is anchored at `5 value` at the minimum manual pressure, then
  scales with pressure using compressed growth.
- Multi-stack debuffs are intentionally sublinear: 1 stack = `1.0x`, 2 stacks =
  `1.5x`, 3 stacks = `1.9x`, and later stacks continue with decaying marginal
  gains.

Unsupported card behavior remains unsupported in V1. These layered constants
only change parsed damage/block/Weak/Vulnerable action facts; they do not assign
values to cards whose actions are still unresolved.

## Long-Term Effects

Long-term effects need timing. A full solution is dynamic programming, but the
first implementation can use two estimates:

- combat length: when the effect stops mattering;
- deck cycle timing: when the card is likely to be drawn or redrawn.

Working combat-length estimates:

- normal fight: about `4` turns;
- elite fight: about `5.5` turns;
- boss fight: about `9` turns.

Working deck-cycle estimate:

- typical cycle: about `3-4` turns, adjusted by deck size and draw effects.

Approximation:

```text
long-term value = per-turn benefit * effective duration
```

The effective duration starts when the card is expected to take effect and ends
when the combat or relevant phase ends.

## AoE And Spatial Value

AoE is not simply "damage times enemy count" when the objective is reducing
player HP loss. Enemy death timing matters because incoming damage often drops
sharply once a key enemy dies.

If enemies have equal health and output, pure AoE has a theoretical received-
damage-side multiplier:

```text
(n + 1) / 2
```

where `n` is enemy count.

When single-target and AoE damage are mixed, the best case is usually to apply
AoE early and then finish priority targets. If AoE and single-target damage are
uniformly mixed over time, a conversion expression using enemy count `n` and
the AoE-to-single-target ratio at first kill `c` can estimate the transition:

```text
(c^2 * n * (n + 1) * (c + 1)^n - 2 * c * n * (c + 1)^n + 2 * ((c + 1)^n - 1))
/ (2 * c * (c * n * (c + 1)^n - (c + 1)^n + 1))
```

In real Slay the Spire combat, enemy health and enemy damage are not equal.
Single-target damage should often focus high-threat enemies, especially those
with high damage-to-health ratio.

Enemy output curvature also matters:

- growth-shaped enemy output makes AoE relatively better;
- burst-shaped enemy output makes single-target focus relatively better;
- mixed enemy timing tends to pull estimates back toward single-target value.

As a general design prior, `sqrt(n)` is a reasonable rough upper neighborhood
for enemy-count conversion. For this game, actual AoE value should usually be
below `sqrt(n)` because key-unit deaths sharply reduce future incoming damage.

Random-target damage should receive an uncertainty penalty. Treat it as a
special AoE-like effect, then reduce value for target-control loss.

## Energy And Draw Conversion

After card action facts are normalized, energy and draw can be evaluated through the
PMF. They improve deck performance differently.

Energy mainly raises expected playable value by lifting the per-turn ceiling,
but it can increase variance because extra energy is only good when the hand has
enough quality cards to spend it on. It tends to matter earlier and in more
homogeneous decks.

Draw improves both expected value and stability. It smooths resource allocation,
improves access to core cards, and reduces the impact of bad hands. It tends to
matter more as the deck becomes more built around high-impact cards.

Working comparison:

- Slay the Spire 1 baseline: `1 energy ~= 1.5 draw`;
- Slay the Spire 2 baseline: `1 energy ~= 1.2 draw`.

The implied direction is that StS2 shifts value from "not enough energy" toward
"not enough access to the right cards", because card quality and deck variance
are higher while starter structure is similar.

## Baseline Card Strength

The current baseline for normalized card value by cost is:

| Cost | Baseline value |
| ---: | ---: |
| 0 | 7 |
| 1 | 15 |
| 2 | 23 |

These are not final truths. They are calibration anchors for first-pass fixed
values and should be revised once extraction, simulation, and hand tests produce
evidence.

## How This Feeds Training And Dynamic Values

Training values should be generated from this model as semi-computed estimates
or Monte Carlo deck-delta results. They are reviewable tables because the model
still needs judgment, calibration, and human correction. These values should
live in the current schema:

```json
"trainingValues": {
  "unupgraded": {
    "shortline": 0.0,
    "midline": 0.0,
    "longline": 0.0
  },
  "upgraded": {
    "shortline": 0.0,
    "midline": 0.0,
    "longline": 0.0
  }
},
"generation": {
  "method": "monteCarlo",
  "updatedAt": {
    "shortline": "2026-06-29T00:00:00Z",
    "midline": "2026-06-29T00:00:00Z",
    "longline": "2026-06-29T00:00:00Z"
  }
}
```

Dynamic values should use the same methodology and the same layered output
shape, but with live context:

- current layer;
- deck size and draw cycle;
- current card list and upgrade states;
- known enemy count and enemy profile where available;
- current energy/draw context;
- common parameters such as deck count, cards drawn per turn, and estimated
  shuffle-cycle turns.

Dynamic values should not write back into the curated JSON. They should remain
runtime/tool outputs and override configured values only at display or analysis
time.

## Open Calibration Questions

- Which game layer thresholds should be used for value tables: act starts,
  floor bands, boss breaks, or user-defined bands?
- How should enemy pools be represented before live enemy context is available?
- How large should the random-target penalty be for different targeting rules?
- Which card text effects can be parsed mechanically, and which require manual
  tags?
- How should upgrade timing opportunity cost be reported separately from the
  upgraded minus unupgraded training value delta?
