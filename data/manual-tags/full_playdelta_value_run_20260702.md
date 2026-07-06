# Full-Deck play-delta Value Run - 2026-07-02

Full re-estimation of every simulatable card on a single, consistent **play-delta (dEV)**
scale, installed into the runtime overlay and published to the mod.

## Configuration
- Command: `estimate-direct-play-values --value-strategy play-delta`
- Deck source: `history-analysis/data/dashen_77_selected_100_decks_clean_20260701.json`
  (cleaned pool: 14 snapshots holding simulator-unsupported cards swapped for clean
  same-group decks; 30 floor8 / 50 act2Start / 20 final preserved).
- `--deck-group all --deck-count 40 --deck-seed 20260630 --runs 200 --seed 1`
- `--max-branch 2 --horizons shortline:4,midline:8,longline:14`
- Degree of parallelism: 4. Wall time: **5579 s (~93 min)**.

## Result
- Base candidates: **139**; eligible forms: **254 / 278**; **127 cards valued** (all play-delta).
- **12 cards excluded** (action cannot be simulated, so no value produced):
  `Alchemize, Anointed, Discovery, Jackpot, Mayhem, MonarchsGaze, Nostalgia, PanicButton,
  Rend, Royalties, Splash, TheGambit`.

### Value/play distribution (unupgraded, 127 cards)
| Horizon | min | median | max | negative |
|---|---:|---:|---:|---:|
| shortline (4) | -12.0 | 5.8 | 38.1 | 26 |
| midline (8)   | -10.7 | 10.4 | 79.7 | 7 |
| longline (14) | -11.4 | 12.0 | 381.7 | 9 |

- Top (midline): VoidForm 79.7, RollingBoulder 75.8, Comet 41.5, DecisionsDecisions 39.7,
  BigBang 39.2, SpectrumShift 35.2 - power/flow ramps dominate longer horizons.
- Bottom (midline): Resonance -10.7, SevenStars -5.2, DyingStar -3.5, Alignment -2.3,
  Prophesize -2.2, Glimmer -2.1 - negative shortline/midline are honest: setup/dilution cost
  that pays off later (or synergy-gated cards without their synergy in the training decks).

## Method note
All cards now share one **play-delta** scale (marginal deck dEV). source-credit is retained in
code but no longer selected by `auto`; attribution is not exercised on this path. This removes the
previous scale mismatch where fully-attributable cards (source-credit, gross output) read higher
than draw/retrieval/power cards (play-delta, marginal). Values are lower and horizon-sensitive by
design; compare cards only within this consistent scale.

## Install + publish
- `install-direct-play-values --horizons shortline,midline,longline`: **127 cards / 254 forms**
  updated; other runtime config preserved; `validate` passed.
- `dotnet publish`: mod folder contains only `CardValueOverlay.dll/.json/.pck/.pdb` (no stale
  `CardValueOverlay.Core.dll`); PCK bundles the updated `card_values.json`.
