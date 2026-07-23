#!/usr/bin/env python3
"""Decide whether a Spire Codex dataset may be refreshed remotely."""

from __future__ import annotations

import argparse
import datetime as dt
import json
from pathlib import Path
from typing import Any


def parse_timestamp(value: str) -> dt.datetime:
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("lastSuccessfulRemoteRefreshAt must include a UTC offset")
    return parsed.astimezone(dt.timezone.utc)


def load_manifest(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        manifest = json.load(handle)
    if manifest.get("schemaVersion") != 1:
        raise ValueError(f"Unsupported refresh manifest schema: {manifest.get('schemaVersion')}")
    return manifest


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Gate remote Spire Codex refreshes using the repo cache policy."
    )
    parser.add_argument("--dataset", required=True)
    parser.add_argument("--request-logic-version", required=True)
    parser.add_argument(
        "--explicit-refresh",
        action="store_true",
        help="Use only when the user explicitly requested fresh remote data.",
    )
    parser.add_argument(
        "--now",
        help="ISO-8601 timestamp for deterministic validation; defaults to current UTC.",
    )
    parser.add_argument("--repo-root", type=Path)
    parser.add_argument("--manifest", type=Path)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    repo_root = (
        args.repo_root.resolve()
        if args.repo_root
        else Path(__file__).resolve().parents[4]
    )
    manifest_path = (
        args.manifest.resolve()
        if args.manifest
        else repo_root / "data" / "spire-codex" / "remote_refresh_manifest.json"
    )
    manifest = load_manifest(manifest_path)
    datasets = manifest.get("datasets", {})
    if args.dataset not in datasets:
        raise ValueError(f"Unknown dataset: {args.dataset}")

    dataset = datasets[args.dataset]
    refreshed_at = parse_timestamp(dataset["lastSuccessfulRemoteRefreshAt"])
    now = (
        parse_timestamp(args.now)
        if args.now
        else dt.datetime.now(tz=dt.timezone.utc)
    )
    if now < refreshed_at:
        raise ValueError("Current time precedes the recorded successful refresh")

    max_age_days = int(dataset.get("maxAgeDays", manifest["maxAgeDays"]))
    age_days = (now - refreshed_at).total_seconds() / 86_400
    stale = age_days >= max_age_days
    logic_changed = args.request_logic_version != dataset["requestLogicVersion"]
    missing_artifacts = [
        relative_path
        for relative_path in dataset.get("cacheArtifacts", [])
        if not (repo_root / relative_path).is_file()
    ]

    reasons: list[str] = []
    if args.explicit_refresh:
        reasons.append("explicit-user-request")
    if logic_changed:
        reasons.append("request-logic-version-changed")
    if stale:
        reasons.append("cache-at-least-60-days-old")

    if reasons:
        decision = "allow-refresh"
    elif missing_artifacts:
        decision = "ask-user"
        reasons.append("recorded-cache-artifact-missing")
    else:
        decision = "reuse-cache"
        reasons.append("cache-fresh-and-request-logic-unchanged")

    result = {
        "dataset": args.dataset,
        "decision": decision,
        "reasons": reasons,
        "ageDays": round(age_days, 3),
        "maxAgeDays": max_age_days,
        "recordedRequestLogicVersion": dataset["requestLogicVersion"],
        "requestedLogicVersion": args.request_logic_version,
        "lastSuccessfulRemoteRefreshAt": dataset["lastSuccessfulRemoteRefreshAt"],
        "missingArtifacts": missing_artifacts,
    }
    print(json.dumps(result, ensure_ascii=True, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
