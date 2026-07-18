# Spire Codex A10 Win Dataset

This directory preserves the Spire Codex data used by CardValueOverlay for
version-aware card adoption and Ancient-option statistics across all five
official characters.

## Raw Snapshot

`spire-codex-v0.107.1plus-a10-wins-raw.tar.gz` contains the exact current list
snapshot, raw shared-run JSON, list-page responses, and final crawl state.

Snapshot scope:

- API: `https://spire-codex.com/api/runs/list` and `/api/runs/shared/{hash}`
- Captured: 2026-07-18
- Ascension: 10
- Result: win
- Players: 1
- Game mode: standard
- Builds strictly newer than `v0.107.0`: `v0.107.1`, `v0.108.0`, `v0.109.0`
- Current listed runs: 4,531
- Official-character runs used by static statistics: 4,448
- Community mod-character runs retained only in the raw snapshot: 83
- Cached list pages: 47 (`page-000047.json` is the terminating empty page)
- Archive entries: 4,582
- Archive size: 26,580,299 bytes

The crawler began with the previous 6,711-run cache, downloaded 1,922 missing
run details, and then performed full rescans from page 1. The final rescan has
4,531 listed rows, 4,531 unique hashes, no duplicate page-boundary rows, and
zero failed run downloads. Stale page-cache files beyond the terminal empty
page are removed automatically.

Extract with:

```powershell
tar -xzf data\spire-codex\spire-codex-v0.107.1plus-a10-wins-raw.tar.gz -C <output-directory>
```

## Derived Data

`derived/` contains readable JSON and CSV artifacts:

- `spire_codex_v0.107.1plus_a10_wins_card_adoption.generated.*`: exact local
  aggregation for the 4,448 official-character runs. It contains all/build/
  character/build-character groups, separate +0/+1 final appearance, reward
  picks, merchant buys, and Ancient option picks.
- `spire_codex_all_characters_card_adoption_runtime.generated.json`: compact
  schema-3 runtime snapshot used to build
  `CardValueOverlay/data/card_adoption.json`. It includes all 578 cards in the
  extracted base-game membership catalog, including zero-observation rows.
  Character-pool cards use their owning character's sample count. Every
  Colorless card stores five variants, one per official current character;
  appearance, reward pick, merchant buy, copies, and percentile bands all use
  the selected character's cohort. Non-character cards use the all-character
  sample count. Basic cards do not participate in copy-count percentiles.
- `spire_codex_all_characters_ancient_choice_runtime.generated.json`: compact
  runtime snapshot used to build
  `CardValueOverlay/data/ancient_choice_stats.json`. It contains all 417
  Ancient options observed across 13,352 choice screens.
- `spire_codex_card_appearance_a10_wins.generated.*`: version-unfiltered
  `/api/runs/stats` reference merge across official characters.
- `spire_codex_versions.generated.json`: API version metadata snapshot.

The simulator may model only part of the full card catalog, but these static
adoption and Ancient-option catalogs are identity-based and support every
base-game card/option present in their generated JSON.

## Regeneration

Refresh the version list, crawl all pages without a character filter, and
rescan until the listed row count equals the unique hash count:

```powershell
python scripts\fetch_spire_codex_runs.py versions `
  --output-json data\spire-codex\derived\spire_codex_versions.generated.json

python scripts\fetch_spire_codex_runs.py crawl-runs `
  --scope a10-wins `
  --build-ids v0.107.1,v0.108.0,v0.109.0 `
  --sort date `
  --page-size 100 `
  --output-root tmp\spire-codex-v107plus-all

python scripts\fetch_spire_codex_runs.py summarize-cache `
  --scope a10-wins `
  --build-ids v0.107.1,v0.108.0,v0.109.0 `
  --official-characters-only `
  --input-root <exact-current-list-snapshot> `
  --output-json data\spire-codex\derived\spire_codex_v0.107.1plus_a10_wins_card_adoption.generated.json `
  --output-csv data\spire-codex\derived\spire_codex_v0.107.1plus_a10_wins_card_adoption.generated.csv `
  --runtime-output-json data\spire-codex\derived\spire_codex_all_characters_card_adoption_runtime.generated.json `
  --runtime-ancient-output-json data\spire-codex\derived\spire_codex_all_characters_ancient_choice_runtime.generated.json
```

## Integrity

SHA-256 hashes for the archive, crawl state, and derived artifacts are stored
in `SHA256SUMS`.
