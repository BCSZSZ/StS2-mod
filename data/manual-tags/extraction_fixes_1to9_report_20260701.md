# Extraction / Classification Fixes 1-9 - Completion Report

Date: 2026-07-01

Follow-up to `low_or_missing_value_extraction_audit_20260701.md`, which listed 9 recommended
parser/model fixes. This report records the status of **all 9** and the resulting per-card
value-strategy classification (play-delta vs. direct value / source-credit), verified by a smoke run.

## Status of items 1-9

| # | Fix | Cards | Status |
|---|---|---|---|
| 1 | Computed hit-count scaling (WithHitCount(CalculatedVar)) | LunarBlast, Radiate | [done] done |
| 2 | `CardCmd.AutoPlay` loops | BeatDown, Catastrophe, DecisionsDecisions | [done] done (earlier) |
| 3 | `RepeatVar(N)` | DecisionsDecisions, HeirloomHammer | [done] done (earlier) |
| 4 | State-sourced draw-to-full | Scrawl | [done] done |
| 5 | `BaseReplayCount +=` replay grant | HiddenGem | [done] done |
| 6 | Power-effect -> play-delta classification | Calamity, Entropy, PaleBlueDot, SpectrumShift, Stratagem, Tyranny, VoidForm | [done] done |
| 7 | `PlayerCmd.EndTurn` downside | VoidForm | [done] done |
| 8 | Self-return `CardPileCmd.Add(this,...)` | ShiningStrike, Bolas | [done] done |
| 9 | Minor / cosmetic | Discovery/Splash/Jackpot/CosmicIndifference/Purity/SummonForth | [ ] assessed - see below |

## New classification (smoke run: 4 decks x 30 runs x turns 8; magnitudes indicative)

| Card | zh | Before | **After** | Smoke value/play (S / M) |
|---|---|---|---|---|
| LunarBlast | 月面射击 | source-credit, flat 4/4/4 | **source-credit** (now scales x skills played) | 9.9 / 9.8 |
| Radiate | 辐射 | source-credit, flat ~3.9 | **source-credit** (now scales x stars gained, AoE) | 5.9 / 4.7 |
| Stardust | 星尘 | source-credit (control) | **source-credit** (unchanged; X-cost path intact) | 4.2 / 4.2 |
| Scrawl | 潦草急就 | source-credit `0/0/0` | **play-delta** | 4.9 / 9.6 |
| HiddenGem | 未掘宝石 | source-credit `0/0/0` | **play-delta** | 21.6 / 30.7 |
| Calamity | 劫难 | source-credit `0/0/0` | **play-delta** | -12.2 / 11.6 |
| Entropy | 熵 | source-credit `0/0/0` | **play-delta** | 0.8 / 39.4 |
| PaleBlueDot | 暗淡蓝点 | source-credit `0/0/0` | **play-delta** | -6.0 / 2.3 |
| SpectrumShift | 光谱偏移 | source-credit `0/0/0` | **play-delta** | 0.9 / 43.1 |
| Stratagem | 计策 | source-credit `0/0/0` | **play-delta** | -4.7 / 17.5 |
| Tyranny | 暴政 | source-credit `0/0/0` | **play-delta** | -4.4 / 36.5 |
| VoidForm | 虚空形态 | source-credit `0/0/0` | **play-delta** (end-turn downside captured) | -1.2 / 57.8 |
| ShiningStrike | 明耀打击 | play-delta | **source-credit** | 10.1 / 11.3 |
| Bolas | 流星锤 | excluded | **source-credit** | 3.0 / 3.0 |
| BeatDown | 狠揍 | source-credit `0/0/0` | **play-delta** | 15.2 / 48.7 |
| Catastrophe | 横祸 | source-credit `0/0/0` | **play-delta** | 35.2 / 23.0 |
| DecisionsDecisions | 抉择，抉择 | play-delta (replay unmodeled) | **play-delta** (replay counted) | 2.4 / 0.6 |

All 17 resolved; `excludedForms: []`. Negative shortline values are honest: installing a power (or
ending your turn with VoidForm) costs tempo early and pays off over later turns (positive midline).

## Total misclassifications: all 13 now corrected

The audit found 13 misclassified forms. Prior session fixed 2 (BeatDown, Catastrophe). This session
fixes the remaining 11: Scrawl, HiddenGem, Calamity, Entropy, PaleBlueDot, SpectrumShift, Stratagem,
Tyranny, VoidForm (source-credit -> play-delta), plus ShiningStrike (play-delta -> source-credit) and
Bolas (excluded -> source-credit).

## How each item was implemented

- **Item 1 (conditional multi-hit):** added per-turn counters `SkillsPlayedThisTurn` /
  `StarsGainedThisTurn` to the simulator and two scaling kinds (`skillsPlayedThisTurn`,
  `starsGainedThisTurn`) to `DynamicScalingDamageValue`. The builder routes LunarBlast/Radiate damage
  through the scaling channel (base x count) and zeroes the flat term (no double count). Confirmed
  game formula `hits = base + extra x multiplier` (LunarBlast = skills played; Radiate = stars gained,
  incl. its own). Strategy stays source-credit (damage is source-creditable); only the value changed.
- **Item 4 (draw-to-full):** parser emits a dynamic `draw` (parameter `toHandFull`) when the count is
  `MaxCardsInHand - Hand.Count`; `SimulationCard.DrawsToHandFull` drives a runtime draw count in the
  simulator. The `draw` term makes Scrawl play-delta.
- **Item 5 (replay grant):** parser emits `grantReplay` (amount = the `IntVar("Replay")` value) on
  detecting `BaseReplayCount +=`; the simulator approximates the deferred replay as
  `ReplayGrant x PlayValue(best draw-pile card)`. `grantReplay` is incomplete-attribution -> play-delta.
- **Item 6 (powers -> play-delta):** `PlayDeltaOnlyPowerKeys` set in the builder; installing one of
  those (already-simulated) powers now emits "Attribution incomplete for action 'power'", so the
  resolver picks play-delta and the value comes from the existing per-turn power EV. `power` added to
  the play-delta allowed-incomplete set.
- **Item 7 (end-turn):** parser emits an `endTurn` marker for `PlayerCmd.EndTurn`;
  `SimulationCard.EndsTurn` sets `state.TurnEnded` in `PlayCard`. This generalizes (and replaces) the
  previous VoidForm-only hard-coded `TurnEnded`.
- **Item 8 (self-return):** parser emits a distinct benign `selfReturn` kind for
  `CardPileCmd.Add(this, ...)` (vs. moving another card). It is a simulated, non-incomplete action, so
  ShiningStrike/Bolas are valued by their real terms (damage/stars) as source-credit. The simulator's
  return-to-draw-top placement now recognizes `selfReturn`.
- **Item 2 / 3:** see `autoplay_cards_fix_report_20260701.md`.

## Item 9 (cosmetic) - assessed, deliberately deferred

Each item-9 sub-item was reviewed. **None changes any card's classification, and none materially
changes value.** Doing them would touch shared parser paths affecting 100+ cards for no
classification/value benefit, or needs a separate workstream:

- **Jackpot createCard count (1 vs 3):** the real gap is that the generated cards are `source:`-based,
  not modeled by the sim's named/pool generation, so Jackpot's generation currently contributes ~0.
  Fixing it properly needs a curated entry in the generated-card-pool architecture
  (`simulation_generated_card_pools.json`) - a separate task. Jackpot is already correctly play-delta;
  only its generation value is undercounted.
- **SetToFreeThisTurn (Discovery/Splash), Discard-source retrieval (CosmicIndifference), hover-tip
  Retain (SummonForth), Purity's move-vs-exhaust label:** cosmetic; the cards are already correctly
  classified and reasonably valued. Deferred to avoid broad-parser regressions for negligible benefit.

Recommend treating item 9 as a separate, low-priority polish pass if desired.

## Files changed

Modeling: `Extraction/CardFactParser.cs`, `Simulation/AutoPlayEffect.cs` (new),
`Simulation/SimulationCard.cs`, `Simulation/SimulationCardLibraryBuilder.cs`,
`Simulation/DeckMonteCarloSimulator.cs`. Tools: `Program.cs`, `Program.DirectPlayValues.cs`,
`Program.TrainingValues.cs`, `Program.Floor8PlayValues.cs`. Data:
`data/manual-tags/card_autoplay_effects.json` (new). Tests: `Modeling.Tests/Program.cs`.

## Verification

- `dotnet build` (Tools + Modeling): 0 warnings / 0 errors.
- Core tests + Modeling tests: pass (the VoidForm fixture test was updated to set the new `EndsTurn`
  flag, reflecting the generalized end-turn model).
- `parse-card-facts`: Scrawl->`draw(toHandFull)`, HiddenGem->`grantReplay`, VoidForm->`power+endTurn`,
  ShiningStrike/Bolas->`selfReturn` (SummonForth's non-self `CardPileCmd.Add(cards,...)` unaffected).
- `validate-generated-data`: valid.
- Targeted `estimate-direct-play-values` run: table above.

## Caveats

- **Smoke magnitudes are indicative** (4 decks / 30 runs). A full `d40 / r200 / t14` run is needed for
  production values.
- **Value approximations** (documented in code): item 5 replays are valued one-shot (no chained
  secondary effects); item 6 power values rely on the existing per-turn power simulation; item 1
  counts are exact per the game formula.
- **Runtime `card_values.json` unchanged**; `latest.generated.json` was reset to the committed d40
  run after the smoke run overwrote it.
- Auto-play / power descriptors are wired into DirectPlayValues, TrainingValues, Floor8PlayValues and
  `simulate-deck-scenario`; the pure-diagnostic commands (DeckBenchmarks, ResourcePlayValues,
  SearchPolicy) use the off-by-default param.
