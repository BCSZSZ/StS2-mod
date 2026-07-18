# Overlay Search Budget Validation - 2026-07-17

## Goal

Bound realtime overlay search tails while keeping add-card dEV error below 10%.

## Method

- Deck: `regent_runtime_signature_20260716_floor29_brightest_flame.json`
- Candidates: Crush Under, Astral Pulse, Alignment, Terraforming, Spectrum Shift,
  and Knockout Blow (the six cards recorded in the latest game log).
- Layer 29, seed 20260705, 15 paired runs, branch width 3, full-branch depth 6,
  four run workers, counterfactual stable shuffle, attribution disabled.
- Reference budget: 250,000 nodes per turn at both horizons.
- Relative error: `abs(limited dEV - reference dEV) / abs(reference dEV)`.

## Accepted policy

| Horizon | Reference nodes | Overlay nodes | Maximum dEV error | Reference wall time | Limited wall time | Speedup |
|---:|---:|---:|---:|---:|---:|---:|
| 4 | 250,000 | 250,000 | 0% | unchanged | unchanged | 1.00x |
| 8 | 250,000 | 60,000 | 7.21% | 75.707s | 57.002s | 1.33x |
| 12 | 250,000 | 100,000 | 4.62% | 109.377s | 109.472s | 1.00x measured |
| 8 + 12 | - | - | 7.21% | 185.084s | 166.474s | 1.11x |

The slowest 8-turn candidate, Spectrum Shift, fell from 16.690s to 11.228s
(1.49x). The 12-turn limit is a pathological-tail safety cap; this 15-run batch
did not show a repeatable aggregate speedup.

## Candidate dEV comparison

| Candidate | 8-turn reference | 8-turn limited | Error | 12-turn reference | 12-turn limited | Error |
|---|---:|---:|---:|---:|---:|---:|
| Crush Under | -9.460 | -9.460 | 0% | 62.904 | 62.904 | 0% |
| Astral Pulse | -0.193 | -0.193 | 0% | 46.371 | 46.371 | 0% |
| Alignment | -56.159 | -56.159 | 0% | -47.726 | -47.726 | 0% |
| Terraforming | -17.001 | -17.401 | 2.35% | 50.109 | 50.109 | 0% |
| Spectrum Shift | 171.305 | 158.955 | 7.21% | 525.038 | 549.292 | 4.62% |
| Knockout Blow | -26.115 | -26.115 | 0% | -38.284 | -38.284 | 0% |

## Rejected policies

- Full-branch depth 5 was fast (8-turn batch 2.45x faster) but changed several
  dEVs drastically, including reversing Crush Under from -9.460 to +19.208.
- An 8-turn 50,000-node cap was 1.38x faster but changed Crush Under by 44%.
- A 12-turn 75,000-node cap shifted several dEVs by 16.236 absolute and exceeded
  the 10% limit.
- Reducing only turns 9-12 to 60,000 nodes exceeded the error limit while
  improving wall time by only about 3%.

The accepted policy therefore keeps branch width and full-branch depth intact
and changes only deterministic per-turn node caps. Ordinary decks that never
reach a cap retain identical search behavior.

## Runtime follow-up: floor-49 slow deck

The next game log showed that search limits alone were insufficient. With JMC
disabled, a 15-run / 12-turn baseline still took 96.435s, allocated 116,669.9
MiB, and searched about 5.27M nodes. There were no CardValueOverlay exceptions;
the pause was allocation/GC and card-object lookahead work, not JMC contention.

The simulator hot path was changed to reuse search buffers, use indexed card
lookup, cache immutable card facts, and avoid result/iterator/string allocations.
A controlled layer-49, fixed-seed one-run comparison retained exactly the same
EV (4001.577) and node count (712,588), while wall time fell from 19.470s to
10.924s and allocation from 12,597.8 MiB to 1,051.1 MiB.

With the accepted 100,000-node longline budget, the full floor-49 15-run runtime
benchmark now takes 45.142s, allocates 7,158.9 MiB, and searches 5.29M nodes.
Against the logged deployed DLL, that is 2.14x faster with 16.30x less allocation
at effectively the same amount of search work.

Realtime scheduling now advances deterministic sample streams cooperatively:

- Combat: one worker and one run per queue slice at every horizon.
- Non-combat: at most 4 / 2 / 1 workers and runs per slice for 4 / 8 / 12 turns.
- Deck-signature staleness is checked after every slice.
- Results still publish only at paired 15-run checkpoints; horizons, seeds,
  branch width, search depth, confidence intervals, and stopping rules are
  unchanged.

A test verifies that a 30-run stream is sample-for-sample identical when run as
one batch, two 15-run batches, or thirty single-run combat slices.

An additional per-card-object preview cap was tested and rejected. At two nodes
it did not change dEV but produced no useful speedup; at one node it finally cut
work but pushed candidate dEV error past the 10% acceptance limit. The abandoned
mechanism was removed rather than retained as an inactive runtime option.

## Selective Branch 3 Validation - 2026-07-18

The overlay now retains global branch width 3 and the configured full-branch
depth, but may omit only the third candidate at an otherwise fully branched
width-three node. The accepted switch requires both conditions:

- second-candidate score minus third-candidate score is at least 13
  damage-equivalent points;
- the third candidate has no engine, resource, draw, generation, Power,
  card-object, dynamic setup, special admission, X-cost, or other stateful
  simulator behavior.

All protected third candidates remain Branch 3. Branch 1/2 modes and nodes with
fewer or more than three selected candidates are unchanged. This does not lower
global search depth.

The representative screen used 16 floor-8, 16 act-2-start, and 8 final decks,
15 fixed-seed runs, a 250,000-node reference cap, and all three horizons.

| Turns | Maximum deck EV error | Mean absolute error | Node reduction |
|---:|---:|---:|---:|
| 4 | 1.92% | 0.08% | 2.35% |
| 8 | 2.17% | 0.17% | 1.43% |
| 12 | 2.15% | 0.23% | 3.08% |

The floor-29 Brightest Flame overlay screen used the six logged reward
candidates, 15 paired runs, 8 turns, stable counterfactual shuffle, and the same
250,000-node reference cap. Five candidate dEVs were identical; Crush Under
changed from -67.114 to -67.380 (0.40%). Total nodes fell 5.69%, allocation fell
5.33%, and measured simulation time improved 1.22x. The deterministic node-work
ratio is 1.06x and is the more conservative repeatable performance estimate.

Rejected thresholds:

- Gap 2 exceeded 7% deck EV error on the representative screen.
- Gap 3 passed the representative 15-run screen (2.61% maximum error and 10.86%
  fewer nodes), but failed the Brightest Flame candidate screen; Alignment and
  Knockout Blow dEV errors reached 25.53% and 88.68%.
- Gap 10 still changed Spectrum Shift dEV by 6.66% in the 15-run slow-deck test.
- Gaps 11 and 12 still changed the Brightest Flame baseline path.
- Gap 15 was safe but reduced slow-deck nodes by only 4.95%, so gap 13 is the
  validated boundary with better retained savings.

Realtime cache semantics were bumped and the selected gap is embedded in the
cache key, so results computed before this policy cannot be reused.
