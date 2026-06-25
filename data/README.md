# Modeling Data

This folder is for the mathematical modeling pipeline. It is not packaged into
the runtime mod.

## Tracked Inputs

- `fixtures/`: small deterministic test fixtures.
- `manual-tags/card_effect_overrides.json`: hand-authored corrections for card
  effects that cannot be extracted confidently.
- `manual-tags/monster_move_overrides.json`: hand-authored corrections for
  monster moves that cannot be extracted confidently.
- `manual-tags/model_calibration.json`: hand-authored calibration constants used
  by estimators.

## Generated Local Outputs

The extraction commands write generated files here, but Git ignores them because
they are derived from the local game install:

- `extracted/*.generated.json`
- `generated/*.generated.json`
- `generated/*.md`
- `generated/decompiled/`

Regenerate them from the repository root:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- extract-game-data
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-card-effects
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-card-pools
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-monster-moves
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-encounter-patterns
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-card-values --layer 1
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- write-card-review-list
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-enemy-expectations
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-encounter-weighted-enemy-pressure
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-defense-calibration
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- validate-generated-data
```

Current v1 extraction discovers game version, cards, enemies, encounters, intent
types, localization records, conservative card effect terms, and unresolved
follow-up work. `card_effect_terms.generated.json` currently covers damage,
block, hit count, upgrade deltas, draw, immediate and next-turn energy, HP loss,
common applied powers/debuffs, keywords, and tag or calculated damage scaling
from decompiled card bodies. It does not yet normalize monster move graphs or
runtime-ready curated values.

`card_pool_memberships.generated.json` records each card's parsed card pool
membership and `MultiplayerOnly` / `SingleplayerOnly` flag from decompiled
`CardPoolModel` and `CardModel` sources.

`card_value_candidates.generated.json` and `card_value_candidates.md` are first
pass review artifacts, not runtime config. They are generated from
`card_effect_terms.generated.json` plus `manual-tags/model_calibration.json` and
include contribution breakdowns and warnings for low-confidence or extreme
estimates. `card_value_review_list.md` combines those candidates with card pool
memberships, keeps localization fields reserved for later extraction, and groups
review work by character/card pool, Ancient rarity, and special card pools.

`monster_move_profiles.generated.json` is the first-pass enemy behavior input.
It records move state ids, UI intents, parsed effects such as attack, block,
buffs, debuffs, hit counts, HP ranges, follow-up states, parser confidence, and
review warnings. Conditional state machines are preserved conservatively rather
than forced into exact probabilities.

`enemy_expectations.generated.json` and `enemy_expectations.md` summarize those
monster profiles as equal-weight v1 expectations: average damage per move,
ascension damage, attack rate, block, Weak, Frail, Vulnerable, Strength gain,
and review warnings. These files are inputs for later block calibration and
enemy-context card valuation.

`encounter_patterns.generated.json` and `encounter_patterns.md` parse
decompiled act and encounter sources into battle patterns by act index,
weak/normal/elite/boss/event category, monster slots, possible random monster
choices, and review warnings. Encounter-weighted enemy expectations should use
this data rather than averaging individual monsters directly.

`encounter_weighted_enemy_pressure.generated.json` and
`encounter_weighted_enemy_pressure.md` combine encounter patterns with monster
move profiles. Enemy damage is reported on an Ascension 10 basis. The v1
pressure metrics are opening damage over turns 1-3, sustain damage over turns
4-8, peak single-turn damage over turns 1-8, and the sustain-minus-opening
damage-per-turn delta. They also include a weighted pressure score: Weak and
Normal use `0.75 * openingDPT + 0.25 * sustainDPT`, Elite uses
`0.55 * openingDPT + 0.35 * sustainDPT + 0.10 * peak`, and Boss uses
`0.40 * openingDPT + 0.40 * sustainDPT + 0.20 * peak`. Layer bands are
currently hard-coded from act structure: Act 1 layers 1-5 use weak encounters,
Act 2 and Act 3 use the first three layers for weak encounters, boss-before
layers use normal+elite encounters, Act 1 and Act 2 use one boss layer, and Act
3 uses two boss layers.

`defense_calibration.generated.json` and `defense_calibration.md` combine enemy
expectations with `manual-tags/model_calibration.json` to summarize Ascension
10 fight damage pressure, debuff pressure, and per-layer block conversion
checks. `manual-tags/model_calibration.json` currently contains manually
smoothed defense pressure values at layer starts 1, 6, 16, 18, 21, 32, 34, 37,
and 47. Block conversion is calibrated from `1 block = 1.2 value` at the
initial calculated pressure of `8.881`, then scales upward with pressure while
flooring the first segment at `1.2`. Damage is intentionally fixed at
`1 damage = 1 value` for every layer; only defense value changes with pressure.
Parsed `Weak` uses `25%` of the current defense pressure converted through the
current block value. Parsed `Vulnerable` starts at `5` value at the minimum
manual pressure and scales upward with compressed pressure growth. Weak and
Vulnerable stack multipliers are sublinear: `1.0`, `1.5`, `1.9`, then decaying
marginal gains. The generated reports are review artifacts only; they do not
update runtime card values automatically.
