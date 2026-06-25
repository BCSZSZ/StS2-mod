# C# Modeling Implementation Plan

This is the plan for implementing the card-value methodology outside the mod
runtime. Do not implement it inside `CardValueOverlayCode/`. The runtime mod
should remain a thin reader/display layer.

## Goals

1. Build a separate C# modeling layer for card valuation.
2. Extract required static game values from local game files where possible.
3. Produce fixed value estimates that can seed `card_values.json`.
4. Produce dynamic value estimates that use the same layered architecture as
   the mod's effective-value model.
5. Keep model experiments and generated data out of the packaged mod unless a
   runtime feature explicitly needs them.

## Proposed Folder Structure

```text
CardValueOverlay.Modeling/
  Domain/
  Extraction/
  Simulation/
  Optimization/
  Estimation/
  Export/

CardValueOverlay.Modeling.Tests/

CardValueOverlay.Tools/
  commands call Modeling library

data/
  extracted/
  generated/
  fixtures/
```

`CardValueOverlay.Modeling` should be a normal C# library. It may reference
`CardValueOverlay.Core` for shared value types such as `LayeredValueTable`, but
it must not reference Godot or runtime UI code.

`CardValueOverlay.Tools` should remain the CLI entry point. Add commands there
instead of making the modeling library parse console arguments.

## Data Sources

Use local files as authority for static facts:

- `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.xml`
- `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll`
- local base game PCK/resources, where extractable
- Workshop mod folders under
  `C:\Program Files (x86)\Steam\steamapps\workshop\content\2868840`
- local mod folders under the game `mods` directory
- localization JSON files from base game and mods when available

Offline extraction should be preferred for repeatability. If some card identity,
localization, or generated model data is only reliable after mods are loaded,
add a later runtime-exporter phase that dumps the live catalog to JSON. Do not
guess silently.

## Extracted Data Model

The extraction layer should normalize static game facts into generated files,
not directly into fixed value JSON.

Candidate output:

```text
data/extracted/card_catalog.generated.json
data/extracted/card_effect_terms.generated.json
data/extracted/localization.generated.json
data/extracted/enemy_catalog.generated.json
data/extracted/relic_catalog.generated.json
```

Minimum card fields:

- stable runtime id or model id;
- runtime type name;
- source package: base game, workshop mod id, local mod id;
- localized title and description;
- cost and upgraded cost if available;
- upgrade state metadata;
- card type, rarity, tags, targeting;
- mechanically parsed effect terms;
- raw extraction provenance.

Effect terms should be explicit enough for modeling:

- damage amount, hit count, target type;
- block amount;
- draw amount;
- energy gain or energy change;
- discard/exhaust/retain rules;
- powers, buffs, debuffs, or persistent effects;
- AoE, random target, all enemies, or priority target;
- conditional clauses and parser confidence.

Unknown or low-confidence text should be preserved as raw text plus manual tag
slots instead of discarded.

## Modeling Domain Objects

Initial domain objects:

- `CardDefinition`
- `CardInstanceState`
- `CardEffectTerm`
- `DeckState`
- `CombatScenario`
- `EnemyProfile`
- `ResourceState`
- `DrawState`
- `LayerContext`
- `ValueCalibration`
- `SimulationResult`
- `PmfResult`
- `CardValueEstimate`

The model should treat manual calibration values as inputs, not hidden
constants. Examples: block-to-damage conversion by layer, AoE penalty curve,
random-target penalty, normal/elite/boss expected turns, and energy/draw
exchange rates.

## Algorithms

### Phase 1: Static Extraction

- Read `sts2.xml` to discover public type/member names.
- Use reflection or IL inspection on `sts2.dll` to find card model classes and
  static card metadata.
- Parse localization files where available.
- Export generated catalog JSON with provenance and confidence.

### Phase 2: Effect Parser

- Convert card text and/or model fields into `CardEffectTerm` objects.
- Start with conservative parser rules for damage, block, draw, energy, AoE,
  random target, exhaust, retain, and simple powers.
- Preserve unparsed text for manual review.

### Phase 3: Single-Card Static Estimator

- Convert effect terms into normalized value using calibration tables.
- Produce unupgraded/upgraded estimates.
- Produce smith value as upgraded estimate minus unupgraded estimate, adjusted
  by opportunity cost if/when that model is defined.

### Phase 4: Deck PMF Simulator

- Simulate draw without replacement, discard, reshuffle, and per-turn hands.
- For each hand, solve the play-selection problem under energy constraints.
- Emit PMF, EV, variance, and covariance summaries.

### Phase 5: Dynamic Context Estimator

- Add layer, deck composition, draw count, current energy, and known enemy
  scenario inputs.
- Recompute marginal card values and common parameters.
- Export dynamic layered values without writing them into the fixed JSON.

### Phase 6: Value Export

- Export candidate fixed values in the schema used by the mod:

```json
{
  "manualValues": {
    "unupgraded": { "1": 0.0 },
    "upgraded": { "1": 0.0 }
  },
  "smithValues": {
    "unupgraded": { "1": 0.0 },
    "upgraded": { "1": 0.0 }
  }
}
```

- Include provenance fields in generated candidate files, but keep runtime
  `card_values.json` manually curated.

## Tool Commands

Planned CLI commands:

```powershell
dotnet run --project CardValueOverlay.Tools -- extract-game-data
dotnet run --project CardValueOverlay.Tools -- parse-card-effects
dotnet run --project CardValueOverlay.Tools -- estimate-card-values --layer 1
dotnet run --project CardValueOverlay.Tools -- simulate-deck --cards file.txt --layer 20
dotnet run --project CardValueOverlay.Tools -- export-value-candidates
dotnet run --project CardValueOverlay.Tools -- validate-generated-data
```

The existing `validate`, `extract-cards`, and `average` commands should either
remain compatible or be folded into this command set deliberately.

## Verification Plan

- Unit tests for formula helpers and calibration interpolation.
- Fixture tests for card text parsing.
- Deterministic simulation tests with fixed RNG seeds.
- Cross-check tests where small decks can be solved exactly and compared to
  Monte Carlo output.
- Generated data validation for duplicate ids, missing localization, unknown
  costs, and unparsed effect text.
- Snapshot tests for exported candidate JSON shape.

## Non-Goals For The First Implementation

- No runtime overlay changes.
- No automatic overwrite of hand-maintained `card_values.json`.
- No full combat AI.
- No exact dynamic programming for every game state.
- No Workshop publishing or external package format changes.

## Immediate Next Steps

1. Expand effect-term parsing beyond the current conservative set: damage,
   block, hit count, upgrade deltas, draw, energy, HP loss, common
   powers/debuffs, keywords, and simple scaling damage.
2. Add PCK or runtime-exporter localization extraction where generated
   localization records are incomplete.
3. Expand monster move profiles from conservative move/effect extraction into
   probability-weighted intent graphs.
4. Expand candidate value estimators from static play-value scoring into deck
   PMF and enemy-context estimators.
5. Add parser and estimator tests before promoting candidates into manual
   runtime values.

## Implemented Baseline

- `CardValueOverlay.Modeling` and `CardValueOverlay.Modeling.Tests` exist.
- `CardValueOverlay.Tools extract-game-data` writes v1 generated catalogs.
- `CardValueOverlay.Tools parse-card-effects` decompiles `sts2.dll` with
  `ilspycmd`, caches source under ignored `data/generated/decompiled/`, and
  writes `data/extracted/card_effect_terms.generated.json`.
- `CardValueOverlay.Tools validate-generated-data` verifies local extraction
  can find known cards, enemies, encounters, and intents.
- V1 extraction uses `ilspycmd -l c` for stable offline type discovery and does
  not directly load `sts2.dll` into the process.
- V1 effect parsing validates known Strike, Defend, and Perfected Strike terms
  from the local game DLL before writing generated effect data. It also
  validates Adrenaline draw/energy/exhaust, Bash Vulnerable, and Neutralize
  Weak.
- `CardValueOverlay.Tools estimate-card-values` consumes
  `card_effect_terms.generated.json` and `manual-tags/model_calibration.json`,
  then writes review-only `data/generated/card_value_candidates.*` artifacts
  with contribution breakdowns, smith deltas, confidence, and warnings.
- `CardValueOverlay.Tools parse-monster-moves` consumes decompiled monster
  model bodies and writes `data/extracted/monster_move_profiles.generated.json`
  with move ids, intents, attack/block/debuff/buff effects, HP ranges,
  follow-up links, confidence, and review warnings.
