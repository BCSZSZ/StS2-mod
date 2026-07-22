# Modeling Data

This folder is for the mathematical modeling pipeline. It is not packaged into
the runtime mod.

## Tracked Inputs

- `fixtures/`: small deterministic test fixtures.
- `manual-tags/monster_move_overrides.json`: hand-authored corrections for
  monster moves that cannot be extracted confidently.
- `manual-tags/model_calibration.json`: hand-authored calibration constants used
  by estimators.
- `manual-tags/simulation_scenarios/`: hand-authored simulator fixtures. New
  combat-aware fixtures carry explicit HP, encounter, initial intent, support
  expectation, and matching 4/8/12 horizons. Legacy DIY/variant scenarios remain
  regression inputs only.
- `manual-tags/combat_value_portfolios.json`: the twelve-cell Act x encounter-tier
  research portfolio. Its balanced target weights are priors, not production
  exposure weights.
- `manual-tags/hp_continuation_calibration.json`: monotone HP continuation
  sensitivity priors. Only the twelve loss-budget knees are confirmed; the HP
  price/curvature/reserve parameters are not empirical or approved.
- `manual-tags/combat_encounter_overrides.json`: sourced encounter realization
  overrides. An empty file means unresolved conditional selections remain
  unsupported.

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
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-card-facts
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-card-pools
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-monster-moves
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- parse-encounter-patterns
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-card-values --layer 1
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- write-card-review-list
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-enemy-expectations
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-encounter-weighted-enemy-pressure
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-defense-calibration
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- validate-generated-data
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- validate-combat-portfolio --verbose
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- replay-monster-intents --encounter FuzzyWurmCrawlerWeak --turns 8
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- benchmark-information-state-solver --iterations 20 --workers 1,2,4
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- estimate-combat-aware-deck-delta --candidate CARD.STRIKE_REGENT+0
```

The four `combat-*`/information-state commands belong to the offline Phase 1
review path. They never write `CardValueOverlay/data/card_values.json` and never
publish the mod. Generated reports and baseline vectors stay under
`generated/combat_aware/`; every Phase 1 dEV report has
`runtimeCandidate: false`. Portfolio validation also executes the three
`regent_combat_phase1_*line.json` fixtures through the new solver and writes
`phase1_smoke.generated.json` / `.md`.

The following retained commands belong to the legacy Monte Carlo path. Run them
only for an explicitly requested regression or migration comparison, never to
produce new primary training values:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- train-card-values
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj -- simulate-deck-scenario --scenario data\manual-tags\simulation_scenarios\hegemony_energy_comparison.json
```

Current v1 extraction discovers game version, cards, enemies, encounters,
intent types, localization records, card facts, and unresolved follow-up work.
`card_facts.generated.json` records card metadata, keywords, tags, upgrade
facts, semantic action facts, raw operations, unresolved notes, and source
evidence from decompiled card and related power bodies. It preserves unsupported
or low-confidence card behavior as facts/raw operations instead of discarding
cards that are not yet simulatable.

`card_pool_memberships.generated.json` records each card's parsed card pool
membership and `MultiplayerOnly` / `SingleplayerOnly` flag from decompiled
`CardPoolModel` and `CardModel` sources.

`card_value_candidates.generated.json` and `card_value_candidates.md` are first
pass review artifacts, not runtime config. They are generated from
`card_facts.generated.json` plus `manual-tags/model_calibration.json` and include
contribution breakdowns and warnings for low-confidence, unsupported, unknown,
or extreme estimates. `card_value_review_list.md` combines those candidates with
card pool memberships, keeps localization fields reserved for later extraction,
and groups review work by character/card pool, Ancient rarity, and special card
pools.

Legacy `training_card_values.generated.json` is produced by `train-card-values`. It
contains schema version 3 card entries with `trainingValues` plus per-card
`generation` metadata. `generation.method` records whether the value came from
`monteCarlo` training or an `estimate` import path, and
`generation.updatedAt.shortline/midline/longline` records the last update time
for each horizon. Even though the command has a `--write-config` switch, do not
use it for combat-aware output. Combat-aware installation requires a passing
`runtimeCandidate` report, approved HP/portfolio calibration, a dedicated
installer, and explicit user approval.

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

Legacy/static `defense_calibration.generated.json` and
`defense_calibration.md` combine enemy
expectations with `manual-tags/model_calibration.json` to summarize Ascension
10 fight damage pressure, debuff pressure, and per-layer block conversion
checks. `manual-tags/model_calibration.json` currently contains manually
smoothed defense pressure values at layer starts 1, 6, 16, 18, 21, 32, 34, 37,
and 47. In the legacy static estimator, block conversion uses the pressure-shaped curve
scaled so interpolated layer 8 equals `1 block = 1.2 value`. Damage is
intentionally fixed at `1 damage = 1 value` for every layer; only defense value
changes with pressure.
Parsed `Weak` uses `25%` of the current defense pressure converted through the
current block value. Parsed `Vulnerable` starts at `5` value at the minimum
manual pressure and scales upward with compressed pressure growth. Weak and
Vulnerable stack multipliers are sublinear: `1.0`, `1.5`, `1.9`, then decaying
marginal gains. The generated reports are review artifacts only; they do not
update runtime card values automatically. None of these block/debuff conversion
terms enters combat-aware physical EV; defense matters there only through actual
player HP outcomes and the approved continuation function.
