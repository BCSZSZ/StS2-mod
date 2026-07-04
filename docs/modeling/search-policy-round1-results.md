# Search-Policy Distillation — Round 1 results (2026-07-04)

First full-scale run of the forward-Q teacher pipeline. **Decision: do NOT scale
to 400k; volume is not the bottleneck. Next step is DAgger + label-noise fix, not
more data.**

## What ran

- **Base collection**: 100,020 teacher-labeled decision groups, forward-Q teacher
  (`--teacher-forward-turns 4`, branch-8), on the 316-deck v0.107.x filtered set
  (`regent_v107_wins_filtered_decks.json`), on a c7a.16xlarge Spot box (~1.3 h,
  ~$2–3). Artifact in `s3://sts2-search-policy-liaow/run-20260704/`.
- **Ranker** (128→64→1 MLP, pairwise + 0.2·MSE): trained at 40k/8ep and 100k/20ep.
- **Held-out eval**: 30 decks from 8 dashen v0.107.x Regent wins NOT in the
  training 77-set (disjoint), unsimulatable-card decks removed.

## Result: the ranker plateaus at top2Recall ~0.74

| ranker | top2Recall | held-out EV vs branch2 |
| --- | --- | --- |
| dry-run 2.7k / 25ep | 0.66 | −2.2% |
| proxy 40k / 8ep | 0.757 | −2.2% |
| **full 100k / 20ep** | **0.744** | **−0.9%** |

40k → 100k did **not** improve top2Recall (0.757 → 0.744) — it plateaued far below
Phase 0's 0.92 (Phase 0 used a *within-turn* teacher on floor8-only decks). So
**more data (400k) will not lift ranker quality** — the cap is elsewhere.

Per-group pattern (consistent across both rankers, so not noise):

- `floor8` **+3.5%** (helps), `act2Start`/`preAct2Boss` ≈ neutral, `final` **−3.4%**
  (hurts, individual decks −7%). Net −0.9%, 12/30 decks improved.

The ranker helps small/early decks but **mis-orders big-`final` beams and prunes
the good card**, at branch width 2.

## Why (hypotheses, ranked)

1. **Forward-Q labels are noisy.** The teacher route value is a multi-turn rollout
   with RNG (draws), so the "teacher-best card" label carries variance —
   especially on big/late decks with long rollouts. Noisy labels cap learnable
   top2Recall. Phase 0's within-turn labels were near-deterministic → 0.92. This is
   the price of the forward-Q口径 (it captures engine cards, but its labels are
   harder to learn). **Prime suspect for the plateau.**
2. **Cross-card synergy on big decks.** A pointwise MLP can't see "play the enabler
   before the payoff"; big `final` decks are exactly where that matters → mis-order
   → the `final` hurt.
3. **Distribution shift.** The ranker was trained on the *heuristic* student's
   trajectory; once it drives the beam it visits different states (esp. on finals)
   it never saw labeled → hurts there. This is the classic DAgger gap.

## Next-step plan (for review — NOT more volume)

Cheapest-first:

1. **Label-noise diagnostic (hours, local).** Re-collect a small set with 3–5
   different seeds; measure how often the teacher-best card flips. High flip rate
   ⇒ noise is the cap ⇒ do (2a). This decides the whole direction cheaply.
2. **Denoise or reshape the teacher label:**
   - **2a. Average K forward rollouts** per candidate (K=3–5) so the teacher-Q is
     a mean, not a single noisy sample → cleaner labels → higher top2Recall.
     Costs K× per group but needs far fewer groups; likely cheaper than 400k.
   - **2b. Hybrid teacher:** clean *within-turn* labels for cards whose value is
     within-turn (most cards) + forward-Q only for engine/persistent-power cards
     (where within-turn fails). Clean where possible, engine coverage where needed.
3. **DAgger round 2** (the doc's headline upgrade; targets the `final` hurt
   directly): add `--search-policy neural` to `collect-search-policy-data` (small
   code change), recollect with the *current ranker* driving the student, label
   those states with the teacher, retrain. 2–3 rounds. Fixes distribution shift.
4. **Model capacity:** bigger MLP or a listwise/attention-over-candidates model to
   capture cross-card synergy the pointwise MLP misses (helps big decks).

**Recommended order:** (1) diagnose noise → (2a/2b) fix labels → (3) DAgger →
(4) capacity if still short. Re-evaluate on the held-out set after each; only scale
data once top2Recall clears ~0.85 and held-out EV is net-positive.

## Artifacts

- 100k base (tagged): `s3://sts2-search-policy-liaow/run-20260704/` and local
  `data/generated/search_policy/` (git-ignored).
- Rankers + benchmarks: `tmp/` (scratch): `full100k_ranker.json`,
  `ho_b2.json` (branch2 baseline), `ho_b2r_full.json` (branch2+ranker),
  `heldout_eval.json` (30-deck held-out set).
- EC2 instance terminated; deck-sharded `run-collection.sh` ready for the next round.
