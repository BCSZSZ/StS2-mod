# Card Value Overlay v0.1.0

Current public build.

## Included

- A dEV-only overlay for rewards, shops, deck views, and upgrade previews.
- Paired 4 / 8 / 14-turn deck comparisons with displayed 95% Student-t confidence intervals.
- Adaptive 20 / 40 / 60 / 80-run sampling, with a maximum of 80 runs.
- Stricter four-look Bonferroni intervals for early stopping and positive/negative colors.
- Stable counterfactual shuffling and reusable per-run samples for lower-noise comparisons.
- Runtime tracking and simulation support for currently recognized enchantments.
- Primary modeling and historical-data coverage for Regent and Colorless cards.
- Client-side display only (`affects_gameplay: false`); multiplayer teammates do not need the mod.

## Dependency

- BaseLib `3.3.5` or newer.

## Publishing

Use `scripts\publish-workshop.ps1`. It builds into a temporary staging folder
under `dist/`, packages that output, uploads it through SteamCMD, and removes the
staging folder. It does not read from or write to the game's ordinary local
`mods` directory. When the publishing account is not subscribed, pass
`-AllowLocalMod` to acknowledge that the separate local development copy remains
installed.

## Public Release Checklist

1. Add BaseLib Workshop item `3737335127` as a required item on the Workshop page.
2. Verify the intended Git revision and complete local in-game testing through `scripts\publish-local.ps1`.
3. Run `scripts\publish-workshop.ps1 -PackageOnly -AllowLocalMod` and inspect the staged content, zip, checksums, bilingual description, preview image, and VDF.
4. Confirm the publishing account is not subscribed to the CardValueOverlay Workshop item.
5. Publish the selected stable version with `scripts\publish-workshop.ps1 -AllowLocalMod` and a one-line change note.
