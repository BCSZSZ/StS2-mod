# Card Value Overlay v0.1.0

Initial public release.

## Included

- Static `est` and real-time `calc` card play values for short, mid, and long horizons.
- Deck-level `dEV` and `after` comparisons for rewards, shops, deck views, and upgrade previews.
- Historical `deck`, reward-only `p+0/p+1`, merchant-only `b+0/b+1`, and filtered `copy` statistics.
- Historical pick rates for Ancient choices.
- Runtime tracking and simulation support for currently recognized enchantments.
- Primary modeling and historical-data coverage for Regent and Colorless cards.
- Client-side display only (`affects_gameplay: false`); multiplayer teammates do not need the mod.

## Dependency

- BaseLib `3.3.5` or newer.

## Publishing

Use `scripts\publish-workshop.ps1`. It builds into a temporary staging folder
under `dist/`, packages that output, uploads it through SteamCMD, and removes the
staging folder. It does not write to the game's ordinary local `mods` directory.

## Public Release Checklist

1. Add BaseLib Workshop item `3737335127` as a required item on the Workshop page.
2. Remove any duplicate copy from the game's ordinary local `mods` directory.
3. Upload with private visibility and verify the subscribed Workshop copy in game.
4. Confirm the bilingual description, preview image, reward/shop/deck/Ancient overlays, and BaseLib settings.
5. Change visibility to public after the subscribed Workshop copy passes the launch check.
