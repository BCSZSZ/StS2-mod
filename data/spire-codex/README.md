# Spire Codex A10 Win Dataset

This directory preserves the Spire Codex data used by CardValueOverlay for
version-aware card adoption statistics.

## Raw Snapshot

`spire-codex-v0.107.x-a10-wins-raw.tar.gz` contains the complete local crawl
directory, including raw shared-run JSON, list-page responses, final state, and
crawl logs.

Snapshot scope:

- API: `https://spire-codex.com/api/runs/list` and `/api/runs/shared/{hash}`
- Captured: 2026-07-10
- Ascension: 10
- Result: win
- Players: 1
- Game mode: standard
- Builds: `v0.107.1`, `v0.107.0`
- Raw runs: 6,711
- Cached list pages: 50 (`page-000050.json` is the terminating empty page)
- Archive entries: 6,778
- Archive size: 53,500,650 bytes

Extract with:

```powershell
tar -xzf data\spire-codex\spire-codex-v0.107.x-a10-wins-raw.tar.gz -C <output-directory>
```

The archived logs include earlier interrupted attempts and stale failure logs.
The authoritative final counters are in `crawl-state.json`: 50 list pages,
4,832 rows observed during the final rescan, 1,791 newly downloaded runs,
3,041 existing runs reused by hash, and zero failures. The final cache contains
6,711 unique raw runs accumulated across the resumable crawl.

## Derived Data

`derived/` contains readable JSON and CSV artifacts:

- `spire_codex_card_appearance_a10_wins.generated.*`: version-unfiltered
  `/api/runs/stats` merge covering 36,533 A10 winning runs across characters.
- `spire_codex_regent_v0.107.x_a10_wins_card_adoption.generated.*`: local raw
  run aggregation for 956 matching Regent wins, including separate +0/+1 final
  appearance and reward-pick statistics.
- `spire_codex_regent_card_adoption_runtime.generated.json`: compact runtime
  snapshot used to build `CardValueOverlay/data/card_adoption.json`.
- `spire_codex_versions.generated.json`: API version metadata snapshot.

The crawler and summarizer are tracked at
`scripts/fetch_spire_codex_runs.py`. Regenerate the Regent summary with its
`summarize-cache` command and `--input-root tmp\spire-codex-runs-v0.107.x`.

## Integrity

SHA-256 hashes are stored in `SHA256SUMS`. On PowerShell, verify the archive
with:

```powershell
Get-FileHash -Algorithm SHA256 data\spire-codex\spire-codex-v0.107.x-a10-wins-raw.tar.gz
```
