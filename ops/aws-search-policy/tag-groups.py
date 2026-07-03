#!/usr/bin/env python3
"""Tag each collected search-policy record with its deck group.

collect-search-policy-data writes one JSONL record per decision. Each record
carries metadata.deckIndex (the deck's position in the --training-decks file) and
metadata.runId, but NOT the group label (floor8/act2Start/preAct2Boss/final),
because the same 77 runs appear in every group. deckIndex -> group is a fixed
map given the deck file, so we recover the group here and write it as a top-level
"group" field. This is what makes cheap train-time reweighting possible
(see reweight.py): collect once, tag once, reweight freely.

Stdlib only. Usage:
  python tag-groups.py --jsonl base.jsonl --decks history-analysis/data/dashen_77_all_308_decks.json --out tagged.jsonl
"""
import argparse, json, sys
from collections import Counter


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--jsonl", required=True, help="collected teacher JSONL (untagged)")
    ap.add_argument("--decks", required=True, help="the exact --training-decks file used for collection")
    ap.add_argument("--out", required=True, help="output tagged JSONL")
    ap.add_argument("--no-validate-runid", action="store_true",
                    help="skip the metadata.runId == decks[deckIndex].runId consistency check")
    args = ap.parse_args()

    decks = json.load(open(args.decks, encoding="utf-8"))["decks"]
    group_by_index = [d["group"] for d in decks]
    runid_by_index = [str(d.get("runId", "")) for d in decks]

    counts = Counter()
    mism = 0
    n = 0
    with open(args.jsonl, encoding="utf-8") as fin, open(args.out, "w", encoding="utf-8") as fout:
        for line in fin:
            line = line.strip()
            if not line:
                continue
            obj = json.loads(line)
            idx = obj["metadata"]["deckIndex"]
            if not (0 <= idx < len(group_by_index)):
                sys.exit(f"deckIndex {idx} out of range for {len(group_by_index)}-deck file "
                         f"({args.decks}). Wrong deck file for this collection?")
            if not args.no_validate_runid:
                rid = str(obj["metadata"].get("runId", ""))
                if rid and rid != runid_by_index[idx]:
                    mism += 1
            obj["group"] = group_by_index[idx]
            counts[obj["group"]] += 1
            fout.write(json.dumps(obj, ensure_ascii=False) + "\n")
            n += 1

    print(f"tagged {n} records -> {args.out}")
    for g in sorted(counts):
        print(f"  {g:>13}: {counts[g]}")
    if mism:
        sys.exit(f"ERROR: {mism} records had metadata.runId != decks[deckIndex].runId — "
                 f"the deck file does not match the collection. Aborting.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
