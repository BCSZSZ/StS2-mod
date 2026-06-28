---
name: sts2-check-play-value-eligibility
description: Check which Regent and Colorless card forms are eligible for direct play-value attribution in CardValueOverlay simulations. Use when Codex needs to filter the 139 candidate cards, explain excluded forms, or verify simulator support before floor8 play-value estimation.
---

# StS2 Check Play-Value Eligibility

Use this for the candidate-filtering atom of the floor8 direct play-value
workflow. Eligibility is based on simulator support and attribution support,
not on manual card groups.

## Inputs

- `facts`: default `data/extracted/card_facts.generated.json`.
- `memberships`: default `data/extracted/card_pool_memberships.generated.json`.
- `calibration`: default `data/manual-tags/model_calibration.json`.
- `candidate`: optional model id or type name filter.
- `limitForms`: optional inspection limit.
- `skipForms`: optional batch offset.
- `layer`: inferred from selected floor8 decks, normally `8`.

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

## Command

Use a dry run of the estimator to print and persist the actual counts:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-floor8-play-values --limit-decks 1 --limit-forms <limitForms> --skip-forms <skipForms> --runs 20 --max-branch 4 --candidate <candidate>
```

For the default full current library, expect approximately:

- `baseCandidates: 139`
- `allForms: 278`
- `eligibleForms: 188`
- `completedCards: 94`

## Output To Review

- `data/generated/floor8_play_values/latest.generated.json`
- `data/generated/floor8_play_values/latest.generated.md`

Review `excludedForms` in the JSON or the `Excluded Forms` section in the MD
before changing simulator support assumptions.
