---
name: sts2-teacher-deck-sharding
description: Build balanced CardValueOverlay StS2 search-policy teacher deck shards. Use when selecting teacher decks, probing slow decks, planning collect-search-policy-data runs, distributing heavy deck variants across EC2/local tasks, or preventing slow-tail shards in baseline-only or candidate-variant teacher collection.
---

# StS2 Teacher Deck Sharding

Use this when planning search-policy teacher collection. The goal is stable
runtime, not merely equal deck counts: profile teacher cost per deck, split
heavy decks into work units, and assign units to tasks by estimated seconds.

## Inputs

- `sourceDecks`: cleaned supported training deck JSON.
- `targetGroups`: total decision groups to collect.
- `taskCount`: number of parallel tasks or EC2 workers.
- `teacherParams`: the exact collection parameters to use later:
  `runs`, `turns`, `maxBranch`, `maxPlays`, `teacherMaxBranch`,
  `teacherMaxPlays`, `teacherForwardTurns`, `teacherRollouts`,
  `candidateDecks`, and optional candidate filter.
- `probeGroups`: default `40` for first pass; use `100` for final gate or
  for high-variance decks.
- `probeSeeds`: default two seeds, for example `2001,2002`.
- `chunkGroups`: default `100` or `200`. Smaller chunks balance better; larger
  chunks create fewer deck-file entries.

## Workflow

1. Clean unsupported decks first. Do not profile or shard decks that contain
   simulator-blocked cards.
2. Freeze the candidate teacher deck file before profiling. The speed cache is
   valid only for that deck file and teacher parameter set.
3. Run a teacher speed probe per deck, using the real teacher settings, not a
   branch3 baseline substitute.
4. Compute robust `secPerGroup` for each deck.
5. Convert each deck quota into work units.
6. Assign units to tasks with Longest Processing Time first (LPT).
7. Write a manifest with expected seconds per task, heavy-unit distribution,
   command lines, output paths, and source deck indexes.
8. After collection, compare actual task runtimes with estimated runtimes and
   update the speed cache before creating supplemental shards.

## Teacher Speed Probe

Use `collect-search-policy-data` with one deck at a time. Keep `candidateDecks`
the same as the planned run. For baseline-only teacher collection, pass
`--candidate-decks 0`.

```powershell
dotnet CardValueOverlay.Tools\bin\Release\net8.0\CardValueOverlay.Tools.dll collect-search-policy-data `
  --training-decks <sourceDecks> `
  --skip-decks <deckIndex> --limit-decks 1 `
  --output-jsonl <probeOutput> `
  --runs <runs> --turns <turns> `
  --max-branch <maxBranch> --max-plays <maxPlays> `
  --teacher-max-branch <teacherMaxBranch> `
  --teacher-max-plays <teacherMaxPlays> `
  --teacher-forward-turns <teacherForwardTurns> `
  --teacher-rollouts <teacherRollouts> `
  --candidate-decks <candidateDecks> `
  --max-groups <probeGroups> `
  --groups-per-deck-variant <probeGroups> `
  --seed <probeSeed>
```

Record:

- `groupsWritten`
- wall-clock seconds
- `secPerGroup = seconds / groupsWritten`
- exit code and stderr
- deck metadata: source index, runId, group, card count, key cards.

Reject or quarantine decks with failed probes or fewer groups than requested.
Do not exclude a deck only because branch3 baseline was slow; use teacher probe
time as the authority.

For two probe seeds, use:

```text
robustSecPerGroup = max(seed1SecPerGroup, seed2SecPerGroup)
```

For three or more probe seeds, use:

```text
robustSecPerGroup = max(p75SecPerGroup, medianSecPerGroup + 1.5 * MAD)
```

Keep `branch3Seconds` as a diagnostic column only.

## Slow Deck Policy

Use explicit thresholds so the selection is auditable:

- `normalMedian`: median `robustSecPerGroup` among non-failed decks.
- `heavy`: `robustSecPerGroup >= 2 * normalMedian`.
- `veryHeavy`: `robustSecPerGroup >= 5 * normalMedian`.
- `hardCap`: user-provided cap, otherwise `10 * normalMedian`.

Default policy:

- Keep `heavy` and `veryHeavy` decks if they are strategically important.
- Split them into smaller work units so they are spread across tasks.
- Exclude only failed decks, unsupported decks, or decks above `hardCap` unless
  the user explicitly wants to keep them.

Common slow-deck features in this simulator include many create/move/transform
cards, high average playable actions, large generated-card state, high Forge,
and dense resource cards. Cards often involved include
`CosmicIndifference`, `Charge`, `SummonForth`, `JackOfAllTrades`,
`ManifestAuthority`, `Discovery`, `CollisionCourse`, and `CrashLanding`.
Treat these as speed-risk signals, not automatic exclusions.

## Unitization

Do not assign whole decks directly when slow tails matter. Convert deck quotas
to work units.

1. Compute desired groups per deck. For equal deck weighting:

```text
base = floor(targetGroups / deckCount)
remainder = targetGroups % deckCount
deckDesiredGroups[i] = base + (i < remainder ? 1 : 0)
```

Use a deterministic order for the remainder, such as source deck index, unless
the user asks for group weights.

2. Split each deck into chunks:

```text
while remainingGroups > 0:
    unitGroups = min(chunkGroups, remainingGroups)
    unitCost = unitGroups * robustSecPerGroup[deck]
    emit unit(deckIndex, unitGroups, unitCost)
    remainingGroups -= unitGroups
```

3. Very heavy decks may use a smaller chunk size, for example
`min(chunkGroups, 50)`, so one task never receives a full heavy deck quota.

## LPT Assignment

Assign work units by estimated cost, not by deck count.

```text
tasks = taskCount empty bins
for unit in units sorted by unitCost descending:
    task = task with smallest estimatedTotalCost
    assign unit to task
    task.estimatedTotalCost += unit.unitCost
```

Acceptance targets:

- `max(taskCost) <= mean(taskCost) * 1.15`, or explain why the largest single
  unit makes this impossible.
- Heavy unit counts should differ by at most one across tasks when possible.
- No task should contain all copies of the same very-heavy deck.
- The manifest must show estimated seconds and heavy units per task.

## Command Layout

`collect-search-policy-data` accepts one `--groups-per-deck-variant` per
command. Therefore each task may contain multiple command buckets:

- Group units in the same task by identical `unitGroups`.
- For each bucket, write a task deck file containing one deck entry per unit.
  If a source deck has multiple units in the bucket, repeat the deck entry.
  Repeated deck entries are acceptable; local deck indexes and command seeds
  create different random streams.
- Run one command per bucket with:

```text
--groups-per-deck-variant <unitGroups>
--max-groups <unitGroups * deckEntriesInBucket>
--seed <baseSeed + taskIndex * 1000 + bucketIndex>
```

This avoids variable per-deck quota inside one command while still spreading
slow decks across tasks.

## Manifest

Write a JSON manifest next to the shard files. Include:

- source deck file path and hash or timestamp
- teacher parameters
- probe settings and speed cache
- `targetGroups`, `taskCount`, `chunkGroups`
- per-task estimated seconds
- per-command deck file, output JSONL, stdout/stderr logs, seed, quota, and
  max groups
- per-unit source deck index, runId, group, unit groups, robust sec/group,
  estimated seconds, and heavy flag

Also write a short Markdown summary with:

- top slow decks
- feature notes for slow decks
- per-task estimated cost table
- max/mean cost ratio
- expected total groups

## Collection And Supplemental Runs

During collection, monitor lines written per task and actual elapsed seconds.
If a run must be stopped after crossing `targetGroups`, record which commands
were partial and preserve completed JSONL lines.

For supplemental collection:

1. Recompute actual `secPerGroup` for partial or slow commands.
2. Update the speed cache.
3. Create supplemental units only for missing group count.
4. Run LPT again instead of assigning supplemental work to the next available
   task in deck order.

## Do Not

- Do not use branch3 baseline benchmark as the only speed gate.
- Do not shard by equal deck count when teacher forward rollouts are enabled.
- Do not put all late decks or all generated-card decks in the same task.
- Do not silently discard slow decks; either exclude them with a threshold
  reason or split and balance them.
- Do not change teacher parameters between probe and collection without
  invalidating the speed cache.
