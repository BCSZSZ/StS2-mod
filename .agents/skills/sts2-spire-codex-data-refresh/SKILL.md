---
name: sts2-spire-codex-data-refresh
description: "Guard, reuse, and refresh cached Spire Codex data for CardValueOverlay. Use before any task that might call Spire Codex APIs or bulk exports, change remote run filters or pagination logic, regenerate official card-adoption or Ancient-choice statistics, or fix an overlay/statistics bug that could otherwise trigger another data download."
---

# StS2 Spire Codex Data Refresh

Default to the checked-in cache. A code or display fix is not permission to
refresh remote data.

## Gate Every Remote Request

1. Identify the dataset in
   `data/spire-codex/remote_refresh_manifest.json`.
2. Run the checker from the repository root with the request-logic version that
   the current code actually implements:

```powershell
python .agents\skills\sts2-spire-codex-data-refresh\scripts\check_refresh.py `
  --dataset ancient-choice-stats `
  --request-logic-version ancient-bulk-export-a10-standard-all-outcomes-v1
```

3. Obey its decision:
   - `reuse-cache`: do not access the network. Reprocess the existing raw or
     derived artifacts locally.
   - `allow-refresh`: announce the qualifying reason, then make one resumable
     refresh.
   - `ask-user`: the recorded cache is missing or unusable without another
     qualifying reason. Ask before requesting remote data.

Remote refresh is allowed only when at least one condition is true:

- the user explicitly requests fresh data;
- the remote request logic genuinely changed; or
- the last successful remote refresh is at least 60 days old.

Pass `--explicit-refresh` only for an explicit request in the current task.

## Define Request-Logic Changes Narrowly

Count these as request-logic changes:

- endpoint or bulk-export protocol;
- remote filters, sample cohort, game mode, character, build, or outcome scope;
- required remote fields that are absent from the existing cache;
- pagination, time-window, checkpoint, or export decoding semantics.

Do not count these as request-logic changes:

- overlay layout, labels, colors, or formatting;
- a denominator or aggregation fix that can use cached raw records;
- output schema, serialization, validation, or documentation changes;
- refactoring local code without changing what is requested remotely.

If only local processing changed, regenerate from the existing archive or
derived source. Do not update `lastSuccessfulRemoteRefreshAt`.

## After an Allowed Refresh

1. Use the existing resumable command and preserve its checkpoint behavior.
2. Validate the completed artifact and its invariants before replacing runtime
   data.
3. Update the dataset's `lastSuccessfulRemoteRefreshAt`,
   `requestLogicVersion`, and artifact paths in
   `data/spire-codex/remote_refresh_manifest.json` only after success.
4. Update generated metadata and `data/spire-codex/SHA256SUMS`.
5. Remove obsolete competing artifacts when the request logic supersedes them.

Never treat a failed or partial request as a successful refresh. Never refresh
one dataset merely because another dataset is stale.
