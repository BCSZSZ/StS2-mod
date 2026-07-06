#!/usr/bin/env python3
"""Resample a group-tagged search-policy dataset to arbitrary per-group weights.

The expensive cloud collection produces one large, balanced BASE dataset (tagged
by group via tag-groups.py). Choosing how much each act-stage should influence the
ranker is then a CHEAP local knob: this script down-samples over-represented groups
and (optionally) over-samples under-represented ones to hit target weights, with no
re-collection. Run it as many times as you like with different weights and retrain.

Constraint: over-sampling only duplicates existing records, so the base must
contain ENOUGH of each group. Collect a generous base (large --max-groups) once.

Stdlib only. Usage:
  python reweight.py --jsonl tagged.jsonl \
    --weights "floor8:0.10,act2Start:0.35,preAct2Boss:0.20,final:0.35" \
    --out train.jsonl [--total N] [--seed 0] [--no-oversample]
"""
import argparse, json, random, sys
from collections import defaultdict


def parse_weights(spec: str) -> dict:
    out = {}
    for part in spec.split(","):
        part = part.strip()
        if not part:
            continue
        k, v = part.split(":")
        out[k.strip()] = float(v)
    if not out:
        sys.exit("--weights parsed to nothing")
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--jsonl", required=True, help="group-tagged JSONL (output of tag-groups.py)")
    ap.add_argument("--weights", required=True, help='e.g. "floor8:0.10,act2Start:0.35,preAct2Boss:0.20,final:0.35"')
    ap.add_argument("--out", required=True)
    ap.add_argument("--total", type=int, default=0, help="target total records (default: size of input)")
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--no-oversample", action="store_true",
                    help="never duplicate records; a short group just stays short (weights become a ceiling)")
    args = ap.parse_args()

    rng = random.Random(args.seed)
    weights = parse_weights(args.weights)
    wsum = sum(weights.values())
    if wsum <= 0:
        sys.exit("weights sum to <= 0")

    buckets = defaultdict(list)
    with open(args.jsonl, encoding="utf-8") as fin:
        for line in fin:
            line = line.strip()
            if not line:
                continue
            obj = json.loads(line)
            g = obj.get("group")
            if g is None:
                sys.exit("record missing 'group' - run tag-groups.py first")
            buckets[g].append(line)

    total = args.total or sum(len(v) for v in buckets.values())
    unlisted = [g for g in buckets if g not in weights]
    if unlisted:
        print(f"note: dropping groups not in --weights: {sorted(unlisted)}")

    out_lines = []
    print(f"{'group':>13} {'have':>8} {'target':>8} {'result':>8}")
    for g in sorted(weights):
        have = buckets.get(g, [])
        target = round(total * weights[g] / wsum)
        if not have:
            print(f"{g:>13} {0:>8} {target:>8} {'MISSING':>8}")
            continue
        if len(have) >= target:
            picked = rng.sample(have, target)
        elif args.no_oversample:
            picked = list(have)
        else:
            picked = list(have) + [rng.choice(have) for _ in range(target - len(have))]
        out_lines.extend(picked)
        print(f"{g:>13} {len(have):>8} {target:>8} {len(picked):>8}")

    rng.shuffle(out_lines)
    with open(args.out, "w", encoding="utf-8") as fout:
        for line in out_lines:
            fout.write(line + "\n")
    print(f"wrote {len(out_lines)} records -> {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
