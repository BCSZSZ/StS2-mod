# AutoPlay Card Modeling Fix - BeatDown / Catastrophe / DecisionsDecisions (+ HeirloomHammer RepeatVar)

Date: 2026-07-01

## Problem

Three cards drive their value through `CardCmd.AutoPlay` (auto-playing OTHER cards), which the
extractor did not recognize. `BeatDown` and `Catastrophe` parsed to **"No card facts"** and scored
`0/0/0` as source-credit; `DecisionsDecisions` was play-delta but its triple-replay payoff was
invisible. Although all three use "AutoPlay", their target pile and scope differ substantially, so
they cannot share one hard-coded rule:

| Card | zh | Auto-play semantics |
|---|---|---|
| BeatDown | 狠揍 | Auto-play `Cards` (3, U+ 4) **random Attacks from the Discard pile** (N distinct). |
| Catastrophe | 横祸 | Auto-play `Cards` (2, U+ 3) **random playable cards from the Draw pile** (N distinct). |
| DecisionsDecisions | 抉择，抉择 | Draw `Cards` (3, U+ 5), then **replay ONE chosen Hand Skill** `Repeat` (3) times. |

`HeirloomHammer` (fix #3, RepeatVar) clones a chosen Colorless hand card `Repeat` (1) times - its
value was already correct because Repeat is 1, but `RepeatVar` was not captured.

## Design - AutoPlay as a feature class + per-card data

AutoPlay is now a modeled effect **class**, with the per-card differences supplied by a curated
data file (as requested), not hard-coded or ad-hoc source parsing:

- **Per-card descriptors:** `data/manual-tags/card_autoplay_effects.json` - one entry per card giving
  `sourcePile` (Draw/Hand/Discard), `cardTypeFilter` (Attack/Skill/Any), `selection`
  (chosen / randomFiltered / randomPlayable), `repeatSameCard` (replay one card N times vs. N
  distinct cards), and the resolved `count` / `countUpgraded`. This is where the three cards'
  different scopes live; adding future auto-play cards is a JSON edit.
- **Parser marker:** `CardFactParser` recognizes `CardCmd.AutoPlay(` and emits an `autoPlay` action
  (so the fact is recorded and drives classification), and captures `new RepeatVar(N)` as a `Repeat`
  dynamic var. It does **not** try to infer the pile/scope from source - that is the JSON's job.
- **Builder:** attaches the resolved `AutoPlayEffect` to the `SimulationCard` for descriptor cards,
  and flags them "Attribution incomplete for action 'autoPlay'" so the strategy resolver picks
  play-delta. `autoPlay` is a recognized (not "unsupported") action, so auto-play cards **without** a
  descriptor keep their existing classification.
- **Simulator:** `DeckMonteCarloSimulator.ResolveAutoPlayActions` executes the effect during
  `PlayCard`: it selects the described cards from the named pile, adds their play value to the deck
  EV (which is what play-delta measures), and consumes them from the pile so they are not
  double-played. `repeatSameCard` picks the best hand card and replays its value `count` times.

## Why these are all play-delta (not direct value / source-credit)

AutoPlay value accrues to the **auto-played cards** (their damage/block), i.e. to deck EV - it is
**not** source-creditable to the trigger card. Source-credit therefore scores the trigger ~0 (the
old bug). Play-delta (`normalEV - blockedEV`) captures it correctly. So every auto-play card resolves
to **play-delta**; `autoPlay` was added to the play-delta allowed-incomplete-action set.

## New classification (verified by smoke run)

Smoke run: `estimate-direct-play-values --value-strategy auto` on 4 decks x 30 runs x turns 8
(shortline 4 / midline 8), candidates limited to these cards. Values are indicative (small/noisy
run), not production numbers.

| Card | Before | **After** | Smoke value/play (S / M, unupgraded) |
|---|---|---|---|
| `CARD.BEAT_DOWN` BeatDown | source-credit, `0/0/0` | **play-delta** | 15.2 / 48.7 (U+ 15.2 / 59.3) |
| `CARD.CATASTROPHE` Catastrophe | source-credit, `0/0/0` | **play-delta** | 35.3 / 23.0 (U+ 27.6 / 30.5) |
| `CARD.DECISIONS_DECISIONS` DecisionsDecisions | play-delta (replay unmodeled) | **play-delta** (replay counted) | 2.5 / 0.6 (U+ 2.9 / 11.6) |
| `CARD.HEIRLOOM_HAMMER` HeirloomHammer | play-delta | **play-delta** (RepeatVar captured; value unchanged, Repeat=1) | 11.2 / 10.4 (U+ 15.7 / 14.8) |
| `CARD.BOMBARDMENT` Bombardment (out of scope) | source-credit | **source-credit (unchanged)** | 18 / 18 |

`excludedForms: []` - none excluded, no crash. BeatDown/Catastrophe flipped from 0 to substantial
play-delta value; DecisionsDecisions' replay now contributes; the scoping held (Bombardment and the
other uncurated AutoPlay cards - HowlFromBeyond, KnifeTrap, Uproar - are unchanged).

## Files changed

- `data/manual-tags/card_autoplay_effects.json` (new) - per-card descriptors.
- `CardValueOverlay.Modeling/Simulation/AutoPlayEffect.cs` (new) - `AutoPlayEffect` +
  `AutoPlayEffectEntry` + `AutoPlayEffectCatalog`.
- `CardValueOverlay.Modeling/Extraction/CardFactParser.cs` - `RepeatVar` var + `autoPlay` action.
- `CardValueOverlay.Modeling/Simulation/SimulationCard.cs` - `AutoPlay` field.
- `CardValueOverlay.Modeling/Simulation/SimulationCardLibraryBuilder.cs` - resolve/attach effect,
  descriptor-gated incomplete-attribution warning, `autoPlay` in `IsSimulatedAction`.
- `CardValueOverlay.Modeling/Simulation/DeckMonteCarloSimulator.cs` - `ResolveAutoPlayActions` +
  `MatchesAutoPlayFilter`, invoked in `PlayCard`.
- `CardValueOverlay.Tools/Program.DirectPlayValues.cs` - `autoPlay` in play-delta allowed set;
  load + pass the descriptor catalog.
- `CardValueOverlay.Tools/Program.cs`, `Program.TrainingValues.cs`, `Program.Floor8PlayValues.cs`
  - load + pass the descriptor catalog (value-pipeline + interactive commands).
- `CardValueOverlay.Modeling.Tests/Program.cs` - `CardFactParserParsesAutoPlayAndRepeat` test.

## Verification

- `dotnet build` (Tools + Modeling): 0 warnings / 0 errors.
- Core tests, Modeling tests (incl. new auto-play test): pass.
- `parse-card-facts`: BeatDown/Catastrophe now emit `autoPlay` (no longer "No facts parsed");
  DecisionsDecisions emits `draw + selectCards + autoPlay` with `Repeat=3`; HeirloomHammer captures
  `Repeat=1`.
- `validate-generated-data`: valid.
- Targeted `estimate-direct-play-values` run: see table above.

## Caveats / follow-ups

- **Value model is value-only + consume:** the dominant combat value of the auto-played cards
  (damage / block / scaling / power modifiers, via `PlayValue`) is credited; secondary chained
  effects of a replayed card (its own draw / forge / power installs) are intentionally not recursed,
  keeping the model bounded and recursion-safe. Adequate for these cards (mostly Attacks); can be
  deepened later.
- **Smoke numbers are indicative**, from a 4-deck / 30-run run. A full `d40 / r200 / t14` run is
  needed to publish production direct-play values for these cards.
- **Runtime `card_values.json` is unchanged** - no values were installed. `data/generated/.../
  latest.generated.json` was reset to the committed d40 run (the targeted smoke run had
  overwritten it).
- The 3 diagnostic commands (`DeckBenchmarks`, `ResourcePlayValues`, `SearchPolicy`) were left
  unwired for the descriptor catalog (the new `Build` param defaults to off there); wire identically
  if auto-play modeling is needed in those tools.
