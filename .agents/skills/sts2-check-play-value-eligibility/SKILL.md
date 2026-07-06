---
name: sts2-check-play-value-eligibility
description: Check which Regent and Colorless card forms are eligible for direct play-value attribution in CardValueOverlay simulations. Use when Codex needs to filter candidate cards, explain excluded forms, or verify simulator support before a user-specified direct play-value estimation run.
---

# StS2 Check Play-Value Eligibility

Use this for the candidate-filtering atom of a direct play-value workflow.
Eligibility is based on simulator support and attribution support, not on
manual card groups or a fixed deck group.

## Inputs

- `facts`: default `data/extracted/card_facts.generated.json`.
- `memberships`: default `data/extracted/card_pool_memberships.generated.json`.
- `calibration`: default `data/manual-tags/model_calibration.json`.
- `candidate`: optional model id or type name filter.
- `limitForms`: optional inspection limit.
- `skipForms`: optional batch offset.
- `layer`: inferred from the selected deck group or explicit deck floors.

## Eligibility Rule

Start from base cards whose pool membership includes `Regent` or `Colorless`.
Exclude multiplayer-only cards and any card/action target containing `Ally`.
Build unupgraded and upgraded simulation forms, then exclude a form when its
warnings include:

- `Unsupported simulation action ...`
- `Attribution incomplete ...`
- `Generic calculated damage scaling requires manual review`

Do not exclude merely because Weak is simplified; that is an accepted simulator
model. Do exclude unresolved draw source credit, selection, pile movement,
transform, create-card, unsupported Power, and unsupported scaling behavior.

This exclusion list defines **source-credit** ineligibility, not "unusable."
A probe whose only blocking warnings are non-numerically-attributable terms
(`draw`, `createCard`, `transformCard`, `moveCardBetweenPiles`, `selectCards`)
is still valued - by the **play-delta** strategy (normal EV minus the same deck
with the probe blocked from play). `BigBang` (it draws) is the canonical
play-delta card. Only `Unsupported simulation action` / unsupported-scaling
warnings make a probe truly ineligible for both strategies. `estimate-direct-play-values
--value-strategy auto` routes fully-attributable probes to source-credit and
incomplete-but-allowed probes to play-delta automatically.

## Command

Use a dry run of the estimator to print and persist the actual counts. The
current command name contains `floor8`, but pass the user-requested deck group
and output paths explicitly:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --deck-source <deckSource> --deck-group <deckGroup> --deck-count <deckCount> --deck-seed <deckSeed> --limit-decks 1 --limit-forms <limitForms> --skip-forms <skipForms> --runs 20 --max-branch <maxBranch> --candidate <candidate> --output-json <eligibilityJson> --output-md <eligibilityMd>
```

For the default full current library, expect approximately:

- `baseCandidates: 139`
- `allForms: 278`
- `eligibleForms: 188`
- `completedCards: 94`

## Output To Review

- the generated JSON path requested by the workflow
- the generated MD path requested by the workflow

Review `excludedForms` in the JSON or the `Excluded Forms` section in the MD
before changing simulator support assumptions.
