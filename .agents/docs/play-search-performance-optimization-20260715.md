# Play-search performance optimization (2026-07-15)

## Corrected search budgets

The play search now separates four limits:

- `fullBranchDecisions`: increments only when more than one candidate is actually explored;
- `resolvedPlays`: counts every play and retains the hard 64-play safety cap;
- deterministic-chain length: defaults to 32 and falls back to ordinary search when reached;
- shared work nodes: defaults to 500,000 per turn and degrades remaining search to branch one.

Deterministic plays never consume the configured ordinary branch decisions. The initial benchmark
below used six; the current default is eight. Six or more committed plays therefore leave the
ordinary branch3 search intact, and exhausting the branch or work budget never ends a turn while
legal plays remain.

## Hot-path changes

1. Consecutive deterministic plays mutate the current branch-private state in one loop instead of
   cloning and recursively searching one forced successor at a time.
2. Pure expected-value search carries a value-type numeric result and no `PlayEvent` route. Full
   reports retain a linked route and materialize it once at the turn boundary.
3. Playable, policy, and Top-B candidate buffers are reused by resolved-play depth. Exceptional
   temporary hands larger than `MaxHandSize` expand only their affected buffer.
4. Branch and best-result states are reused by depth. `SimulationState.CopyFrom` reuses existing
   card-instance objects and list capacity.
5. Card/string identities are converted once to stable numeric hashes. One state traversal produces
   both the structural loop hash and exact cache hash.
6. Loop-path state is held in depth-indexed arrays. Resource-neutral structural repeats terminate;
   resource-positive repeats remain bounded by the 64-play cap.
7. Nested card-object continuations share the parent turn's work budget.
8. A bounded exact transposition-policy cache is available for pure EV search. Its key includes pile
   order, mutable card state, powers/resources, branch RNG seed, horizon, and search budgets.

The exact cache defaults off. The five-deck audit produced zero hits after RNG was included, so the
dictionary cost had no payoff. This result also rejected a higher-risk full mutate/undo rewrite;
reusable copy buffers deliver the allocation reduction with a smaller correctness surface.

## Five-deck paired benchmark

Inputs: five real Regent history decks, 15 runs, seed 20260715, branch3, six full ordinary branch
decisions, max 64 plays, and four-way deck parallelism. Generated artifacts remain under
`data/generated/deck_benchmarks/`.

The exact 4-turn comparison is:

| implementation | wall time | final-deck EV/turn | decision nodes | forced plays |
|---|---:|---:|---:|---:|
| pre-policy `b3465e7b` | 26.126s | 82.26 | 1,061,796 | 0 |
| deterministic policy before optimization | 201.729s | 135.14 | 2,791,303 | 1,315,709 |
| optimized policy, 500k work budget | 55.022s | 139.567 | 803,713 | 399,013 |

The optimized policy is 3.67x faster than the unoptimized deterministic policy while preserving and
slightly increasing its final-deck value. It remains slower than the old shallow policy because it
keeps the intended deeper search and gains 69.7% final-deck EV/turn over that baseline.

The optimized 15-run wall times were 55.022s at 4 turns, 30.766s at 8 turns, and 464.676s at 12
turns. Longline remains dominated by the final deck's heavy tail. The 8-turn old-policy batch took
647.6s, so the optimized 30.766s batch is about 21x faster on the paired deck set.

At 4 turns, the first three deck EV/turn results exactly matched the unoptimized deterministic
policy. The fourth fell from 35.21 to 33.557 because resource-neutral loop states are now pruned. The
final deck increased from 135.14 to 139.567.

## Branch-depth-8 follow-up

The follow-up keeps the deterministic policy unchanged, sets ordinary branch depth to eight, and
uses the same branch width, seed, runs, work budget, and 64-play safety cap. The first seven
hot-path stages used a strict acceptance rule: total EV, search nodes, decision nodes, state clones,
forced plays, fallback nodes, and the complete selected-branch histogram all had to match. A later
single-candidate-tail stage uses the separately approved tolerance of at most 1% EV drift.

Additional result-equivalent hot-path changes:

1. When the exact transposition cache is disabled, state traversal computes only the structural
   loop fingerprint. The unused exact fingerprint previously doubled much of the hash work.
2. Each card instance caches its immutable card identity and its complete mutable search
   fingerprint. Each active Power similarly caches its fingerprint. Copies inherit valid cached
   values; setters invalidate them.
3. Scalar hash folding uses one 64-bit avalanche per value rather than eight dependent byte-wise
   FNV iterations.
4. EV-only simulation does not carry energy/star/vulnerable attribution sources. Delayed block
   retains a scalar decision value, while full-report attribution retains the detailed credit rows.
5. State-buffer copies reuse active-Power objects and optional source-list capacity instead of
   allocating replacements.
6. Generated-card selection shuffles a pooled buffer and scans the offered prefix directly. It no
   longer allocates a copied candidate List, a selected List, and an OrderBy structure per branch.
7. Pure EV card plays sum Power resolution values directly in original order. They materialize the
   combined resolution list only when attribution is requested; empty Power/generated results are
   allocated lazily.

### Rejected after measurement

- The first single-candidate compression changed work-budget fallback and was removed completely
  while exact equivalence was required. It was later reimplemented as a per-node iterative state
  machine after the acceptance rule explicitly changed to at most 1% EV drift; see the slow-tail
  follow-up below.
- Replacing the hot `List<T>` pile read path with an interface-backed container increased the final
  deck time from 46.65s to 52.62s. That container was removed. The retained incremental component
  hash uses a `List<T>` subclass so reads and enumeration stay on the direct list path.
- Raising one-deck run parallelism from four to eight workers did not improve the final-deck time
  (53.44s versus 53.53s on this 14-core/20-thread machine). Four remains the default.
- Branch-internal parallelism cannot preserve the ordered shared 500,000-node budget. Identical-card
  route merging is also not exact because branch RNG seeds include card instance ids. Neither is
  enabled.
- Exact transpositions still have no useful hits once RNG state is included. Removing RNG would
  make cache hits incorrect, so the cache remains opt-in and defaults to zero capacity.

### Depth-8 paired result

Final deck `1781548523`, 15 runs, 8 turns, seed 20260715, branch3, full ordinary branch depth 8,
run-degree 4:

| implementation | wall time | EV/turn | search nodes | state clones | fallback nodes |
|---|---:|---:|---:|---:|---:|
| depth-8 baseline | 94.567s | 150.916 | 2,801,210 | 1,862,255 | 22,373 |
| final optimized code | 53.440s | 150.916 | 2,801,210 | 1,862,255 | 22,373 |

This is a conservative 1.77x speedup (43.5% less wall time). A neighboring run measured 52.201s;
the final report uses the slower repeat. Effective wall time fell from 59.79 to 33.79 microseconds
per decision node. Total EV remained 1207.326, and every tree invariant listed above matched.

The final five-real-deck depth-8 run completed in 70.310s:

| runId | group | EV/turn | seconds |
|---|---|---:|---:|
| 1781605206 | floor8 | 38.891 | 0.075 |
| 1782117795 | floor8 | 38.531 | 0.029 |
| 1781194308 | act2Start | 46.161 | 0.138 |
| 1782123326 | act2Start | 49.823 | 16.232 |
| 1781548523 | final | 150.916 | 53.831 |

Against the same optimized policy at depth 6, the first three decks are unchanged, the fourth is
-0.166 EV/turn, and the final deck is +47.484 EV/turn (+45.9%). Depth 8 costs 70.310s versus
23.584s for the five-deck depth-6 run, so the remaining time is the intended extra search, not
attribution, hashing, or container-allocation overhead.

## Slow-tail follow-up

`benchmark-training-decks --slow-tail-profile` now records every root run/turn's wall time, search
nodes, decision kind, forced plays, state copies, work-budget fallbacks, loop outcomes, generated
pool activity, candidate-card descendant nodes, and active-Power exposure. Nested preview turns are
folded into the owning root turn instead of being misreported as independent combat turns. The
retained node-heavy candidate subtrees include their inclusive wall time and full candidate-path
prefix, so the report exposes concrete slow routes rather than only individual card totals.

The two slow decks showed that the batch is not uniformly slow. A few late-turn searches repeatedly
hit the 500,000-node budget:

- act-2-start run 9 turn 8 used 505,396 nodes and 10.91s; run 12 turn 8 used 506,242 nodes and
  10.64s;
- final-deck run 7 turn 8 used 501,088 nodes and 15.63s; run 7 turn 7 used 500,643 nodes and
  14.70s;
- the largest retained paths included `ManifestAuthority+1`, `Glimmer -> CosmicIndifference`,
  `HeavenlyDrill -> Glimmer+1`, and `Glimmer+1 -> Discovery+1`. The correlated active Powers were
  Pillar of Creation, Spectrum Shift, Orbit, Persistent, and Void Form;
- Discovery, Manifest Authority, Spectrum Shift, Jackpot, and Calamity generation pools accounted
  for most generated-card activity in those tails.

The retained single-candidate-tail implementation simulates every node, stop comparison, forced
play, loop check, RNG step, and reverse floating-point fold explicitly, but avoids recursive calls
and intermediate best-state copies along a branch-one suffix. On the final deck it changed EV/turn
from 150.916 to 150.828 (-0.058%), within the approved 1% tolerance, while reducing search nodes
from 2,801,210 to 2,514,986 and state copies from 1,862,255 to 1,662,746.

Card piles and active Powers now own cached order-sensitive component fingerprints. Appending to a
clean component updates its hash in constant time; removals/reorders invalidate only that component;
card and Power mutable-field setters invalidate their owner. Card-instance and Power mutable fields
are packed into structs, immutable `SimulationCard` definitions remain shared, reusable state
buffers copy only active lengths, and no per-branch pile arrays are allocated. A full arena/index
indirection was not retained because the measured container indirection erased the hash win.

The final paired five-deck depth-8 run completed in 60.678s versus 70.310s before this follow-up
(1.159x, 13.7% less wall time). Aggregate total EV moved from 2594.574 to 2593.869 (-0.027%). The
first four decks matched rounded EV exactly; the final deck was -0.058%:

| deck | group | before EV/turn | after EV/turn | before seconds | after seconds |
|---:|---|---:|---:|---:|---:|
| 0 | floor8 | 38.891 | 38.891 | 0.075 | 0.081 |
| 1 | floor8 | 38.531 | 38.531 | 0.029 | 0.028 |
| 2 | act2Start | 46.161 | 46.161 | 0.138 | 0.091 |
| 3 | act2Start | 49.823 | 49.823 | 16.232 | 11.954 |
| 4 | final | 150.916 | 150.828 | 53.831 | 48.521 |
