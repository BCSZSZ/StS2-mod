"""Fetch Spire Codex card stats and cache raw run JSON.

This script avoids the currently timing-out /api/exports/runs endpoint.
Use `stats` for the card appearance probabilities needed by the overlay, and
`crawl-runs` for a slow, resumable raw-run cache built from /runs/list +
/runs/shared/{hash}. Use `retry-failures` to recover exact hashes after a
network interruption because live list page numbers can shift over time. The
hosted /runs/stats endpoint does not support build_id filters, so use
`summarize-cache` for version-specific final-deck, reward-pick, and merchant-buy
adoption statistics.
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import io
import json
import random
import sys
import tarfile
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any


API_BASE = "https://spire-codex.com"
OFFICIAL_CHARACTERS = ("IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT")
OFFICIAL_RUN_CHARACTERS = frozenset(
    f"CHARACTER.{character}" for character in OFFICIAL_CHARACTERS
)
OFFICIAL_CARD_POOL_CHARACTERS = {
    "Ironclad": "CHARACTER.IRONCLAD",
    "Silent": "CHARACTER.SILENT",
    "Defect": "CHARACTER.DEFECT",
    "Necrobinder": "CHARACTER.NECROBINDER",
    "Regent": "CHARACTER.REGENT",
}
COLORLESS_CARD_POOL = "Colorless"
DEFAULT_USER_AGENT = (
    "CardValueOverlay Spire Codex data pipeline "
    "(respectful cache; https://github.com/BCSZSZ/StS2-mod)"
)

FREE_EVENT_CARD_CHOICE_RULES: dict[str, tuple[set[int], int]] = {
    # The game presents eight common cards and lets the player take two. Current
    # v0.107.x shared-run JSON records only cards_gained, not the eight card_choices;
    # this rule starts working automatically if a later export includes the screen.
    "EVENT.ROOM_FULL_OF_CHEESE": ({8}, 2),
}


class RequestBudgetExhausted(RuntimeError):
    pass


class ApiClient:
    def __init__(
        self,
        *,
        base_url: str,
        user_agent: str,
        request_interval: float,
        timeout: float,
        max_retries: int,
        max_requests: int | None = None,
    ) -> None:
        self.base_url = base_url.rstrip("/")
        self.user_agent = user_agent
        self.request_interval = request_interval
        self.timeout = timeout
        self.max_retries = max_retries
        self.max_requests = max_requests
        self.request_count = 0
        self._last_request_started = 0.0

    def get_json(self, path: str, params: dict[str, Any] | None = None) -> Any:
        url = self._url(path, params)
        raw = self.get_bytes(url)
        return json.loads(raw.decode("utf-8"))

    def get_bytes(self, url: str) -> bytes:
        last_error: Exception | None = None
        for attempt in range(self.max_retries + 1):
            self._wait_for_rate_limit()
            if self.max_requests is not None and self.request_count >= self.max_requests:
                raise RequestBudgetExhausted(
                    f"request budget exhausted after {self.request_count} requests"
                )

            self.request_count += 1
            request = urllib.request.Request(
                url,
                headers={
                    "Accept": "application/json",
                    "User-Agent": self.user_agent,
                },
            )
            try:
                with urllib.request.urlopen(request, timeout=self.timeout) as response:
                    return response.read()
            except urllib.error.HTTPError as ex:
                last_error = ex
                if ex.code == 429:
                    self._sleep_for_retry_after(ex, attempt)
                    continue
                if 500 <= ex.code <= 599:
                    self._sleep_for_server_retry(attempt)
                    continue
                raise
            except (TimeoutError, urllib.error.URLError) as ex:
                last_error = ex
                self._sleep_for_server_retry(attempt)

        raise RuntimeError(f"request failed after retries: {url}") from last_error

    def _url(self, path: str, params: dict[str, Any] | None) -> str:
        if path.startswith("http://") or path.startswith("https://"):
            base = path
        else:
            base = f"{self.base_url}/{path.lstrip('/')}"
        if not params:
            return base
        clean_params = {
            key: value
            for key, value in params.items()
            if value is not None and value != ""
        }
        return base + "?" + urllib.parse.urlencode(clean_params)

    def _wait_for_rate_limit(self) -> None:
        now = time.monotonic()
        elapsed = now - self._last_request_started
        wait = self.request_interval - elapsed
        if wait > 0:
            time.sleep(wait)
        self._last_request_started = time.monotonic()

    def _sleep_for_retry_after(self, ex: urllib.error.HTTPError, attempt: int) -> None:
        retry_after = ex.headers.get("Retry-After")
        if retry_after:
            try:
                seconds = max(1.0, float(retry_after))
            except ValueError:
                seconds = 60.0
        else:
            seconds = max(60.0, self._backoff_seconds(attempt))
        print(f"rate limited; sleeping {seconds:.1f}s", file=sys.stderr)
        time.sleep(seconds)

    def _sleep_for_server_retry(self, attempt: int) -> None:
        if attempt >= self.max_retries:
            return
        seconds = self._backoff_seconds(attempt)
        print(f"request failed; retrying in {seconds:.1f}s", file=sys.stderr)
        time.sleep(seconds)

    @staticmethod
    def _backoff_seconds(attempt: int) -> float:
        return min(120.0, (2.0**attempt) + random.random())


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    client = ApiClient(
        base_url=args.base_url,
        user_agent=args.user_agent,
        request_interval=args.request_interval,
        timeout=args.timeout,
        max_retries=args.max_retries,
        max_requests=getattr(args, "max_requests", None),
    )
    if args.command == "stats":
        return fetch_stats(args, client)
    if args.command == "crawl-runs":
        return crawl_runs(args, client)
    if args.command == "fetch-ancient-outcomes":
        return fetch_ancient_outcomes(args, client)
    if args.command == "retry-failures":
        return retry_failures(args, client)
    if args.command == "versions":
        return fetch_versions(args, client)
    if args.command == "summarize-cache":
        return summarize_cache(args)
    parser.print_help()
    return 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Fetch Spire Codex stats and raw run JSON without using /api/exports/runs."
    )
    parser.add_argument("--base-url", default=API_BASE)
    parser.add_argument("--user-agent", default=DEFAULT_USER_AGENT)
    parser.add_argument(
        "--request-interval",
        type=float,
        default=1.1,
        help="Minimum seconds between requests. 1.1 stays under 60/min.",
    )
    parser.add_argument("--timeout", type=float, default=90.0)
    parser.add_argument("--max-retries", type=int, default=4)

    subparsers = parser.add_subparsers(dest="command", required=True)
    add_stats_parser(subparsers)
    add_crawl_parser(subparsers)
    add_fetch_ancient_outcomes_parser(subparsers)
    add_retry_failures_parser(subparsers)
    add_versions_parser(subparsers)
    add_summarize_cache_parser(subparsers)
    return parser


def add_stats_parser(subparsers: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    parser = subparsers.add_parser(
        "stats",
        help="Fetch card appearance probabilities from /api/runs/stats.",
    )
    add_scope_filter_arguments(parser, default_scope="a10-wins")
    parser.add_argument(
        "--characters",
        default=",".join(OFFICIAL_CHARACTERS),
        help="Comma-separated character ids. Default: official characters.",
    )
    parser.add_argument(
        "--localization",
        default="history-analysis/data/localized_names_en_zhs.json",
        help="Optional localization JSON for English/ZHS names.",
    )
    parser.add_argument(
        "--output-json",
        default="data/generated/spire_codex_card_appearance_a10_wins.generated.json",
    )
    parser.add_argument(
        "--output-csv",
        default="data/generated/spire_codex_card_appearance_a10_wins.generated.csv",
    )


def add_crawl_parser(subparsers: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    parser = subparsers.add_parser(
        "crawl-runs",
        help="Slowly cache runs from /api/runs/list and /api/runs/shared/{hash}.",
    )
    add_scope_filter_arguments(parser, default_scope="a10-wins")
    parser.add_argument("--output-root", default="tmp/spire-codex-runs")
    parser.add_argument("--page-size", type=int, default=100)
    parser.add_argument("--start-page", type=int, default=1)
    parser.add_argument("--max-pages", type=int)
    parser.add_argument("--max-runs", type=int)
    parser.add_argument("--max-requests", type=int)
    parser.add_argument(
        "--resume-from-state",
        action="store_true",
        help="Continue from state.json nextPage. Default starts at page 1 and skips cached runs.",
    )
    parser.add_argument(
        "--force-refresh",
        action="store_true",
        help="Re-download shared run JSON even if the hash already exists locally.",
    )
    parser.add_argument(
        "--no-page-cache",
        action="store_true",
        help="Do not save /runs/list page responses.",
    )


def add_fetch_ancient_outcomes_parser(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser(
        "fetch-ancient-outcomes",
        help="Fetch role-specific Ancient picked-win counts from Spire Codex entity snapshots.",
    )
    parser.add_argument("--runtime-input-json", required=True)
    parser.add_argument("--output-json", required=True)
    parser.add_argument("--runtime-output-json", required=True)
    parser.add_argument(
        "--build-ids",
        default="v0.107.1,v0.108.0,v0.109.0",
        help="Comma-separated versions composed with the solo:a10 snapshot bracket.",
    )


def add_retry_failures_parser(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser(
        "retry-failures",
        help="Retry exact run hashes recorded by a previous crawl.",
    )
    parser.add_argument("--output-root", default="tmp/spire-codex-runs")
    parser.add_argument(
        "--page",
        type=int,
        help="Retry only failures originally recorded for this list page.",
    )
    parser.add_argument("--max-runs", type=int)
    parser.add_argument("--max-requests", type=int)
    parser.add_argument(
        "--force-refresh",
        action="store_true",
        help="Re-download shared run JSON even if the hash already exists locally.",
    )


def add_versions_parser(subparsers: argparse._SubParsersAction[argparse.ArgumentParser]) -> None:
    parser = subparsers.add_parser(
        "versions",
        help="Fetch distinct Spire Codex build ids.",
    )
    parser.add_argument(
        "--output-json",
        default="data/generated/spire_codex_versions.generated.json",
    )


def add_summarize_cache_parser(
    subparsers: argparse._SubParsersAction[argparse.ArgumentParser],
) -> None:
    parser = subparsers.add_parser(
        "summarize-cache",
        help="Compute +0/+1 final-deck, reward-pick, and merchant-buy adoption statistics.",
    )
    add_scope_filter_arguments(parser, default_scope="a10-wins")
    parser.add_argument("--input-root", default="tmp/spire-codex-runs")
    parser.add_argument(
        "--localization",
        default="history-analysis/data/localized_names_en_zhs.json",
        help="Optional localization JSON for English/ZHS names.",
    )
    parser.add_argument(
        "--output-json",
        default="data/generated/spire_codex_cached_card_appearance.generated.json",
    )
    parser.add_argument(
        "--output-csv",
        default="data/generated/spire_codex_cached_card_appearance.generated.csv",
    )
    parser.add_argument(
        "--runtime-output-json",
        help="Optional compact adoption JSON for the runtime mod.",
    )
    parser.add_argument(
        "--card-pool-memberships",
        default="data/extracted/card_pool_memberships.generated.json",
        help=(
            "Card pool membership JSON used to select each card's character "
            "cohort and runtime percentile group."
        ),
    )
    parser.add_argument(
        "--card-facts",
        default="data/extracted/card_facts.generated.json",
        help="Card fact JSON used to exclude Basic cards from copy-count percentiles.",
    )
    parser.add_argument(
        "--runtime-ancient-output-json",
        help="Optional compact ancient choice JSON for the runtime mod.",
    )
    parser.add_argument(
        "--official-characters-only",
        action="store_true",
        help="Exclude community mod characters while retaining all five official characters.",
    )


def add_scope_filter_arguments(
    parser: argparse.ArgumentParser,
    *,
    default_scope: str,
) -> None:
    parser.add_argument(
        "--scope",
        choices=("a10-wins", "all"),
        default=default_scope,
        help="a10-wins applies the current project filters; all sends no default filters.",
    )
    parser.add_argument("--character", help="Optional single character filter.")
    parser.add_argument("--ascension", type=int, help="Optional ascension filter.")
    parser.add_argument(
        "--win",
        choices=("true", "false", "any"),
        help="Optional win filter. For scope=a10-wins this defaults to true.",
    )
    parser.add_argument("--players", type=int, help="Optional player-count filter.")
    parser.add_argument("--game-mode", help="Optional game mode filter.")
    parser.add_argument("--build-id", help="Optional build id filter for /runs/list.")
    parser.add_argument("--build-ids", help="Optional comma-separated build ids for /runs/list.")
    parser.add_argument("--sort", help="Optional /runs/list sort key.")


def fetch_stats(args: argparse.Namespace, client: ApiClient) -> int:
    if args.build_id or args.build_ids or args.sort:
        raise SystemExit(
            "/api/runs/stats does not support build_id/build_ids/sort. "
            "Use crawl-runs with build filters, then summarize-cache for version splits."
        )

    characters = parse_characters(args.characters)
    localization = load_localization(Path(args.localization))
    generated_at = now_iso()
    scope_params = build_scope_params(args, for_stats=True)

    by_character: dict[str, Any] = {}
    aggregate: dict[str, dict[str, Any]] = {}
    global_total_runs = 0

    for character in characters:
        params = dict(scope_params)
        params["character"] = character
        print(f"fetch stats character={character}", file=sys.stderr)
        data = client.get_json("/api/runs/stats", params)
        total_runs = int(data.get("total_runs") or 0)
        global_total_runs += total_runs
        rows = []
        for item in data.get("top_cards") or []:
            card_id = str(item.get("card_id") or "")
            if not card_id:
                continue
            total_runs_with = int(item.get("total_runs_with") or 0)
            total_copies = int(item.get("count") or 0)
            row = make_card_stat_row(
                card_id=card_id,
                total_runs=total_runs,
                total_runs_with=total_runs_with,
                total_copies=total_copies,
                localization=localization,
            )
            rows.append(row)

            bucket = aggregate.setdefault(
                card_id,
                {
                    "cardId": card_id,
                    "modelId": normalize_model_id(card_id),
                    "name": localized_name(card_id, localization),
                    "totalRunsWith": 0,
                    "totalCopies": 0,
                    "characters": {},
                },
            )
            bucket["totalRunsWith"] += total_runs_with
            bucket["totalCopies"] += total_copies
            bucket["characters"][character] = {
                "totalRuns": total_runs,
                "totalRunsWith": total_runs_with,
                "appearanceProbability": safe_ratio(total_runs_with, total_runs),
                "totalCopies": total_copies,
                "copiesPerRun": safe_ratio(total_copies, total_runs),
            }

        rows.sort(key=lambda row: (-row["appearanceProbability"], row["cardId"]))
        by_character[character] = {
            "totalRuns": total_runs,
            "cards": rows,
            "rawStatsKeys": sorted(data.keys()),
        }

    all_cards = []
    for item in aggregate.values():
        item["totalRuns"] = global_total_runs
        item["appearanceProbability"] = safe_ratio(item["totalRunsWith"], global_total_runs)
        item["copiesPerRun"] = safe_ratio(item["totalCopies"], global_total_runs)
        all_cards.append(item)
    all_cards.sort(key=lambda row: (-row["appearanceProbability"], row["cardId"]))

    output = {
        "schemaVersion": 1,
        "generatedAt": generated_at,
        "source": {
            "apiBase": args.base_url,
            "endpoint": "/api/runs/stats",
            "method": "per-character queries merged locally",
            "versionFiltering": "not supported by /api/runs/stats",
        },
        "scope": {
            "description": "Card appears in A10 winning decks unless filters are overridden.",
            "filters": scope_params,
            "characters": characters,
        },
        "allCharacters": {
            "totalRuns": global_total_runs,
            "cards": all_cards,
        },
        "byCharacter": by_character,
    }

    write_json(Path(args.output_json), output)
    write_stats_csv(Path(args.output_csv), output)
    print(
        f"wrote {args.output_json} and {args.output_csv}; "
        f"totalRuns={global_total_runs} cards={len(all_cards)}"
    )
    return 0


def fetch_versions(args: argparse.Namespace, client: ApiClient) -> int:
    data = client.get_json("/api/runs/versions")
    versions = data.get("versions") or []
    output = {
        "schemaVersion": 1,
        "generatedAt": now_iso(),
        "source": {
            "apiBase": args.base_url,
            "endpoint": "/api/runs/versions",
        },
        "versions": versions,
    }
    write_json(Path(args.output_json), output)
    print(f"wrote {args.output_json}; versions={len(versions)}")
    return 0


def retry_failures(args: argparse.Namespace, client: ApiClient) -> int:
    output_root = Path(args.output_root)
    runs_dir = output_root / "runs"
    failures_path = output_root / "failures.jsonl"
    retry_failures_path = output_root / "retry_failures.jsonl"
    if not failures_path.exists():
        raise SystemExit(f"crawl failures file does not exist: {failures_path}")
    runs_dir.mkdir(parents=True, exist_ok=True)

    failures_by_hash: dict[str, dict[str, Any]] = {}
    with failures_path.open("r", encoding="utf-8") as handle:
        for line_number, line in enumerate(handle, start=1):
            if not line.strip():
                continue
            try:
                failure = json.loads(line)
            except json.JSONDecodeError as ex:
                raise SystemExit(
                    f"invalid JSON in {failures_path} at line {line_number}"
                ) from ex
            run_hash = failure.get("runHash")
            if not run_hash or (args.page is not None and failure.get("page") != args.page):
                continue
            failures_by_hash[run_hash] = failure

    counters = {
        "listedFailures": len(failures_by_hash),
        "downloadedRuns": 0,
        "skippedExistingRuns": 0,
        "failedRuns": 0,
    }
    for run_hash in failures_by_hash:
        dest = runs_dir / f"{run_hash}.json"
        if dest.exists() and not args.force_refresh:
            counters["skippedExistingRuns"] += 1
            continue
        if args.max_runs is not None and counters["downloadedRuns"] >= args.max_runs:
            break

        try:
            print(f"shared {run_hash}", file=sys.stderr)
            run_data = client.get_json(f"/api/runs/shared/{run_hash}")
            write_json(dest, run_data)
            counters["downloadedRuns"] += 1
        except RequestBudgetExhausted as ex:
            print(str(ex), file=sys.stderr)
            break
        except Exception as ex:  # noqa: BLE001 - retry remains resumable by hash.
            counters["failedRuns"] += 1
            append_jsonl(
                retry_failures_path,
                {
                    "at": now_iso(),
                    "runHash": run_hash,
                    "page": failures_by_hash[run_hash].get("page"),
                    "error": str(ex),
                },
            )

    print(
        "failure retry stopped: "
        + ", ".join(f"{key}={value}" for key, value in counters.items())
        + f", requests={client.request_count}"
    )
    return 0


def crawl_runs(args: argparse.Namespace, client: ApiClient) -> int:
    output_root = Path(args.output_root)
    runs_dir = output_root / "runs"
    pages_dir = output_root / "list_pages"
    state_path = output_root / "state.json"
    failures_path = output_root / "failures.jsonl"
    runs_dir.mkdir(parents=True, exist_ok=True)
    if not args.no_page_cache:
        pages_dir.mkdir(parents=True, exist_ok=True)

    params_base = build_scope_params(args, for_stats=False)
    page = args.start_page
    if args.resume_from_state and state_path.exists():
        state = json.loads(state_path.read_text(encoding="utf-8"))
        page = int(state.get("nextPage") or page)

    counters = {
        "listPages": 0,
        "listedRuns": 0,
        "downloadedRuns": 0,
        "skippedExistingRuns": 0,
        "failedRuns": 0,
    }
    started_at = now_iso()

    try:
        while True:
            if args.max_pages is not None and counters["listPages"] >= args.max_pages:
                break
            params = dict(params_base)
            params["page"] = page
            params["limit"] = min(max(1, args.page_size), 100)
            print(f"list page={page}", file=sys.stderr)
            list_data = client.get_json("/api/runs/list", params)
            runs = list_data.get("runs") or []
            counters["listPages"] += 1
            counters["listedRuns"] += len(runs)
            if not args.no_page_cache:
                write_json(pages_dir / f"page-{page:06d}.json", list_data)

            if not runs:
                if not args.no_page_cache:
                    remove_stale_page_cache(pages_dir, page)
                break

            for row in runs:
                run_hash = row.get("run_hash")
                if not run_hash:
                    continue
                dest = runs_dir / f"{run_hash}.json"
                if dest.exists() and not args.force_refresh:
                    counters["skippedExistingRuns"] += 1
                    continue
                if args.max_runs is not None and counters["downloadedRuns"] >= args.max_runs:
                    write_crawl_state(state_path, args, params_base, page, counters, started_at)
                    print_crawl_summary(counters, client.request_count)
                    return 0

                try:
                    run_data = client.get_json(f"/api/runs/shared/{run_hash}")
                    write_json(dest, run_data)
                    counters["downloadedRuns"] += 1
                    if (
                        counters["downloadedRuns"] == 1
                        or counters["downloadedRuns"] % 100 == 0
                    ):
                        print(
                            "downloaded "
                            f"{counters['downloadedRuns']} missing runs "
                            f"through list page {page}",
                            file=sys.stderr,
                        )
                except RequestBudgetExhausted:
                    raise
                except Exception as ex:  # noqa: BLE001 - cache crawl should keep going.
                    counters["failedRuns"] += 1
                    append_jsonl(
                        failures_path,
                        {
                            "at": now_iso(),
                            "runHash": run_hash,
                            "page": page,
                            "error": str(ex),
                        },
                    )

            write_crawl_state(state_path, args, params_base, page + 1, counters, started_at)
            page += 1

    except RequestBudgetExhausted as ex:
        print(str(ex), file=sys.stderr)

    write_crawl_state(state_path, args, params_base, page, counters, started_at)
    print_crawl_summary(counters, client.request_count)
    return 0


def fetch_ancient_outcomes(args: argparse.Namespace, client: ApiClient) -> int:
    runtime_path = Path(args.runtime_input_json)
    runtime = json.loads(runtime_path.read_text(encoding="utf-8"))
    build_ids = [value.strip() for value in args.build_ids.split(",") if value.strip()]
    if not build_ids:
        raise SystemExit("--build-ids must contain at least one build id")

    snapshot_status = client.get_json("/api/runs/snapshot-status")
    ancient_pools = client.get_json("/api/ancient-pools")
    official_choice_ids = {
        str(relic.get("id") or "")
        for ancient in ancient_pools
        for pool in ancient.get("pools") or []
        for relic in pool.get("relics") or []
        if relic.get("id")
    }
    community_by_build: dict[str, dict[str, Any]] = {}
    for build_id in build_ids:
        bracket = f"solo:a10:{build_id}"
        print(f"community snapshot bracket={bracket}", file=sys.stderr)
        community_by_build[build_id] = client.get_json(
            "/api/runs/community-stats",
            {"bracket": bracket},
        )

    choice_ids = sorted(
        {
            text_key
            for character in (runtime.get("characters") or {}).values()
            for text_key in (character.get("choices") or {})
            if text_key in official_choice_ids
        }
    )
    entity_stats: dict[str, dict[str, Any]] = {}
    for text_key in choice_ids:
        print(f"relic snapshot={text_key}", file=sys.stderr)
        entity_stats[text_key] = client.get_json(
            f"/api/runs/stats/relics/{urllib.parse.quote(text_key, safe='')}"
        )

    outcome = build_ancient_outcome_output(
        runtime,
        build_ids,
        community_by_build,
        entity_stats,
        snapshot_status,
    )
    merged_runtime = merge_ancient_outcomes(runtime, outcome)
    write_json(Path(args.output_json), outcome)
    write_json(Path(args.runtime_output_json), merged_runtime)
    differences = sum(
        1
        for character in outcome["characters"].values()
        for choice in character["choices"].values()
        if choice["winnerPickCountDifference"] not in (None, 0)
    )
    print(
        f"wrote {args.output_json} and {args.runtime_output_json}; "
        f"choices={len(choice_ids)} winnerPickCountDifferences={differences}"
    )
    return 0


def build_ancient_outcome_output(
    runtime: dict[str, Any],
    build_ids: list[str],
    community_by_build: dict[str, dict[str, Any]],
    entity_stats: dict[str, dict[str, Any]],
    snapshot_status: dict[str, Any] | None = None,
) -> dict[str, Any]:
    characters: dict[str, Any] = {}
    for character_id, runtime_character in (runtime.get("characters") or {}).items():
        bare_character = character_id.removeprefix("CHARACTER.")
        outcome_runs = 0
        outcome_wins = 0
        for build_id in build_ids:
            row = next(
                (
                    item
                    for item in community_by_build[build_id].get("by_character") or []
                    if str(item.get("id") or "").upper() == bare_character.upper()
                ),
                None,
            )
            if row:
                outcome_runs += int(row.get("runs") or 0)
                outcome_wins += int(row.get("wins") or 0)

        choices: dict[str, Any] = {}
        for text_key, runtime_choice in (runtime_character.get("choices") or {}).items():
            picked_runs = 0
            picked_wins = 0
            stats = entity_stats.get(text_key) or {}
            brackets = stats.get("brackets") or {}
            for build_id in build_ids:
                bracket = brackets.get(f"solo:a10:{build_id}") or {}
                row = next(
                    (
                        item
                        for item in bracket.get("by_character") or []
                        if str(item.get("character") or "").upper()
                        == bare_character.upper()
                    ),
                    None,
                )
                if row:
                    picked_runs += int(row.get("picks") or 0)
                    picked_wins += int(row.get("wins") or 0)
            winner_pick_count = int(runtime_choice.get("pickCount") or 0)
            has_outcome_data = text_key in entity_stats
            choices[text_key] = {
                "pickedRunCount": picked_runs,
                "pickedWinCount": picked_wins,
                "pickedWinRate": safe_ratio(picked_wins, picked_runs),
                "winnerPickCount": winner_pick_count,
                "winnerPickCountDifference": (
                    picked_wins - winner_pick_count if has_outcome_data else None
                ),
            }

        characters[character_id] = {
            "outcomeSampleRuns": outcome_runs,
            "outcomeWins": outcome_wins,
            "outcomeChoiceScreens": sum(
                choice["pickedRunCount"] for choice in choices.values()
            ),
            "choices": choices,
        }

    return {
        "schemaVersion": 1,
        "generatedAt": now_iso(),
        "source": {
            "apiBase": API_BASE,
            "communityEndpoint": "/api/runs/community-stats?bracket=solo:a10:{buildId}",
            "entityEndpoint": "/api/runs/stats/relics/{textKey}",
            "ancientPoolsEndpoint": "/api/ancient-pools",
            "snapshotStatus": snapshot_status or {},
            "method": "For official Ancient-pool relics, per-run relic membership identifies the option as picked; entity snapshot picks/wins provide the outcome counts.",
        },
        "scope": {
            "filters": {
                "ascension": 10,
                "players": 1,
                "buildIds": ",".join(build_ids),
                "characters": sorted((runtime.get("characters") or {}).keys()),
            },
            "gameModeNote": "The official materialized solo:a10 version bracket has no separate standard-mode slice.",
        },
        "characters": characters,
    }


def merge_ancient_outcomes(
    runtime: dict[str, Any],
    outcome: dict[str, Any],
) -> dict[str, Any]:
    merged = json.loads(json.dumps(runtime))
    merged["schemaVersion"] = 3
    merged.setdefault("scope", {})["ancientWinRateFilters"] = outcome["scope"]["filters"]
    merged["outcomeSource"] = outcome["source"]
    merged["choiceRules"] = {
        "sampleCohort": "Each character uses only that character's runs and Ancient choice screens.",
        "pick": "Pick rate uses the configured winning-run cohort: each option shown contributes one offer and was_chosen contributes one pick.",
        "pickedWinRate": "Picked win rate uses the official solo A10 version snapshots: each run containing the chosen Ancient relic contributes once, and a winning run contributes one picked win.",
        "key": "Runtime matching uses the option key after the final .options. segment.",
    }
    for character_id, character_outcome in outcome["characters"].items():
        character = merged["characters"][character_id]
        character["outcomeSampleRuns"] = character_outcome["outcomeSampleRuns"]
        character["outcomeWins"] = character_outcome["outcomeWins"]
        character["outcomeChoiceScreens"] = character_outcome["outcomeChoiceScreens"]
        for text_key, choice_outcome in character_outcome["choices"].items():
            choice = character["choices"][text_key]
            choice["pickedRunCount"] = choice_outcome["pickedRunCount"]
            choice["pickedWinCount"] = choice_outcome["pickedWinCount"]
            choice["pickedWinRate"] = choice_outcome["pickedWinRate"]
    return merged


def remove_stale_page_cache(pages_dir: Path, last_page: int) -> None:
    for page_path in pages_dir.glob("page-*.json"):
        try:
            page_number = int(page_path.stem.removeprefix("page-"))
        except ValueError:
            continue
        if page_number > last_page:
            page_path.unlink()


def summarize_cache(args: argparse.Namespace) -> int:
    input_root = Path(args.input_root)
    localization = load_localization(Path(args.localization))
    card_metadata = load_card_distribution_metadata(
        Path(args.card_pool_memberships),
        Path(args.card_facts),
    )
    filters = build_scope_params(args, for_stats=False)
    ancient_win_rate_filters = dict(filters)
    ancient_win_rate_filters.pop("win", None)
    groups: dict[str, dict[str, Any]] = {}
    ancient_outcome_groups: dict[str, dict[str, Any]] = {}
    total_cached_runs = 0
    matched_runs = 0
    outcome_matched_runs = 0

    for _run_name, run in iter_cached_runs(input_root):
        total_cached_runs += 1
        if not isinstance(run, dict):
            continue
        if not run_matches_filters(run, ancient_win_rate_filters):
            continue
        build_id = str(run.get("build_id") or "")
        players = summary_players(run, args.official_characters_only)
        if not players:
            continue
        outcome_matched_runs += 1
        matches_primary_scope = run_matches_filters(run, filters)
        if matches_primary_scope:
            matched_runs += 1
        won = bool(run.get("win"))
        for player in players:
            character = str(player.get("character") or "")
            if filters.get("character") and character != str(filters["character"]):
                continue
            ancient_choice_screens = read_ancient_choice_screens(run, player.get("id"))
            for key, group_build_id, group_character in (
                ("all", "", ""),
                (f"build:{build_id or '<unknown>'}", build_id, ""),
                (f"character:{character or '<unknown>'}", "", character),
                (
                    f"build:{build_id or '<unknown>'}|character:{character or '<unknown>'}",
                    build_id,
                    character,
                ),
            ):
                update_ancient_outcome_group(
                    ancient_outcome_groups,
                    key=key,
                    build_id=group_build_id,
                    character=group_character,
                    won=won,
                    ancient_choice_screens=ancient_choice_screens,
                )

            if not matches_primary_scope:
                continue
            cards = player.get("deck") or []
            if not cards:
                continue
            reward_offers, shop_offers = read_card_offer_choices(run, player.get("id"))
            update_summary_group(
                groups,
                key="all",
                build_id="",
                character="",
                cards=cards,
                reward_offers=reward_offers,
                shop_offers=shop_offers,
                ancient_choice_screens=ancient_choice_screens,
                localization=localization,
            )
            update_summary_group(
                groups,
                key=f"build:{build_id or '<unknown>'}",
                build_id=build_id,
                character="",
                cards=cards,
                reward_offers=reward_offers,
                shop_offers=shop_offers,
                ancient_choice_screens=ancient_choice_screens,
                localization=localization,
            )
            update_summary_group(
                groups,
                key=f"character:{character or '<unknown>'}",
                build_id="",
                character=character,
                cards=cards,
                reward_offers=reward_offers,
                shop_offers=shop_offers,
                ancient_choice_screens=ancient_choice_screens,
                localization=localization,
            )
            update_summary_group(
                groups,
                key=f"build:{build_id or '<unknown>'}|character:{character or '<unknown>'}",
                build_id=build_id,
                character=character,
                cards=cards,
                reward_offers=reward_offers,
                shop_offers=shop_offers,
                ancient_choice_screens=ancient_choice_screens,
                localization=localization,
            )

    output_groups = []
    for group in groups.values():
        cards = finalize_summary_cards(group.pop("_cards"), group["totalRuns"])
        ancient_choices = finalize_ancient_choices(group.pop("_ancientChoices"))
        group["cards"] = cards
        group["ancientChoices"] = ancient_choices
        output_groups.append(group)
    output_groups.sort(key=lambda group: (group["buildId"], group["character"], group["key"]))

    output_ancient_outcome_groups = []
    for group in ancient_outcome_groups.values():
        choices = finalize_ancient_outcomes(group.pop("_ancientChoices"))
        group["ancientChoices"] = choices
        output_ancient_outcome_groups.append(group)
    output_ancient_outcome_groups.sort(
        key=lambda group: (group["buildId"], group["character"], group["key"])
    )

    output = {
        "schemaVersion": 3,
        "generatedAt": now_iso(),
        "source": {
            "inputRoot": str(input_root),
            "method": "local +0/+1 deck, reward-pick, shop-buy, and ancient-choice summary of cached /api/runs/shared/{hash} JSON",
            "cardPoolMemberships": str(Path(args.card_pool_memberships)),
            "cardFacts": str(Path(args.card_facts)),
        },
        "scope": {
            "filters": {
                **filters,
                **(
                    {"characters": sorted(OFFICIAL_RUN_CHARACTERS)}
                    if args.official_characters_only
                    else {}
                ),
            },
            "ancientWinRateFilters": ancient_win_rate_filters,
        },
        "totalCachedRuns": total_cached_runs,
        "matchedRuns": matched_runs,
        "ancientOutcomeMatchedRuns": outcome_matched_runs,
        "groups": output_groups,
        "ancientOutcomeGroups": output_ancient_outcome_groups,
    }
    write_json(Path(args.output_json), output)
    write_cache_summary_csv(Path(args.output_csv), output)
    if args.runtime_output_json:
        write_json(
            Path(args.runtime_output_json),
            build_runtime_adoption_output(output, card_metadata),
        )
    if args.runtime_ancient_output_json:
        write_json(Path(args.runtime_ancient_output_json), build_runtime_ancient_choice_output(output))
    print(
        f"wrote {args.output_json} and {args.output_csv}; "
        f"matchedRuns={matched_runs} outcomeMatchedRuns={outcome_matched_runs} "
        + f"groups={len(output_groups)}"
    )
    return 0


def summary_players(run: dict[str, Any], official_characters_only: bool) -> list[dict[str, Any]]:
    players = [player for player in run.get("players") or [] if isinstance(player, dict)]
    if not official_characters_only:
        return players
    return [
        player
        for player in players
        if str(player.get("character") or "") in OFFICIAL_RUN_CHARACTERS
    ]


def iter_cached_runs(input_root: Path):
    if input_root.is_file() and (
        "".join(input_root.suffixes[-2:]) == ".tar.gz"
        or input_root.suffix == ".tgz"
    ):
        with tarfile.open(input_root, "r:gz") as archive:
            members = sorted(
                (
                    member
                    for member in archive.getmembers()
                    if member.isfile()
                    and "/runs/" in member.name.replace("\\", "/")
                    and member.name.endswith(".json")
                ),
                key=lambda member: member.name,
            )
            for member in members:
                file = archive.extractfile(member)
                if file is None:
                    continue
                try:
                    with io.TextIOWrapper(file, encoding="utf-8") as handle:
                        yield member.name, json.load(handle)
                except json.JSONDecodeError:
                    continue
        return

    runs_dir = input_root / "runs"
    if not runs_dir.exists() and input_root.name == "runs":
        runs_dir = input_root
    if not runs_dir.exists():
        raise SystemExit(f"cached runs directory does not exist: {runs_dir}")

    for run_path in sorted(runs_dir.glob("*.json")):
        try:
            yield str(run_path), json.loads(run_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            continue


def load_card_distribution_metadata(
    memberships_path: Path,
    card_facts_path: Path,
) -> dict[str, dict[str, Any]]:
    memberships = json.loads(memberships_path.read_text(encoding="utf-8"))
    if not isinstance(memberships, list):
        raise SystemExit(
            f"card pool membership JSON must be a list: {memberships_path}"
        )
    card_facts = json.loads(card_facts_path.read_text(encoding="utf-8"))
    if not isinstance(card_facts, list):
        raise SystemExit(f"card fact JSON must be a list: {card_facts_path}")

    rarity_by_model_id = {
        str(entry.get("modelId") or ""): str(entry.get("rarity") or "")
        for entry in card_facts
        if isinstance(entry, dict) and entry.get("modelId")
    }
    result: dict[str, dict[str, Any]] = {}
    for entry in memberships:
        if not isinstance(entry, dict):
            continue
        model_id = str(entry.get("modelId") or "")
        if not model_id:
            continue
        pools = [
            str(pool)
            for pool in entry.get("pools") or []
            if isinstance(pool, str) and pool
        ]
        character_pools = [
            pool for pool in pools if pool in OFFICIAL_CARD_POOL_CHARACTERS
        ]
        if len(character_pools) > 1:
            raise SystemExit(
                f"card belongs to multiple official character pools: {model_id} "
                + ", ".join(character_pools)
            )
        source_pool = character_pools[0] if character_pools else None
        is_colorless = COLORLESS_CARD_POOL in pools
        result[model_id] = {
            "pools": pools,
            "sourcePool": source_pool,
            "sourceCharacter": (
                OFFICIAL_CARD_POOL_CHARACTERS[source_pool] if source_pool else None
            ),
            "isColorless": is_colorless,
            "copyDistributionEligible": bool(source_pool or is_colorless)
            and rarity_by_model_id.get(model_id) != "Basic",
        }
    return result


def build_runtime_adoption_output(
    output: dict[str, Any],
    card_metadata: dict[str, dict[str, Any]],
) -> dict[str, Any]:
    groups_by_key = {group["key"]: group for group in output["groups"]}
    all_group = groups_by_key["all"]
    cards_by_group = {
        key: {card["modelId"]: card for card in group["cards"]}
        for key, group in groups_by_key.items()
    }
    cards: dict[str, Any] = {}
    for model_id in sorted(card_metadata):
        metadata = card_metadata[model_id]
        source_character = metadata["sourceCharacter"]
        variants: dict[str, Any] = {}
        if metadata["isColorless"]:
            for pool_name, character_id in OFFICIAL_CARD_POOL_CHARACTERS.items():
                source_group_key = f"character:{character_id}"
                variants[character_id] = build_runtime_card_variant(
                    model_id,
                    groups_by_key[source_group_key],
                    cards_by_group[source_group_key],
                    f"{pool_name}:Colorless",
                    metadata["copyDistributionEligible"],
                )
        elif source_character:
            source_group_key = f"character:{source_character}"
            variants[source_character] = build_runtime_card_variant(
                model_id,
                groups_by_key[source_group_key],
                cards_by_group[source_group_key],
                metadata["sourcePool"],
                metadata["copyDistributionEligible"],
            )
        else:
            variants["all"] = build_runtime_card_variant(
                model_id,
                all_group,
                cards_by_group["all"],
                None,
                False,
            )
        cards[model_id] = {
            "pools": metadata["pools"],
            "variants": variants,
        }
    return {
        "schemaVersion": 3,
        "generatedAt": output["generatedAt"],
        "scope": output["scope"],
        "totalRuns": all_group["totalRuns"],
        "formRules": {
            "sampleCohort": "Character-pool cards use their owning character's runs. Colorless cards store one variant per official character and runtime selects the current character. Non-character cards use all runs.",
            "finalDeck": "+0 and +1 use each final card's current upgrade level, regardless of how it was upgraded.",
            "rewardChoice": "+0 and +1 use the card form originally shown in standard combat, elite, or boss rewards; later upgrades are not attributed back to the offer.",
            "shopBuy": "+0 and +1 use the card form originally offered in merchant shops; cards_gained marks purchases when card_choices does not.",
            "unobservedEventChoices": "Room Full of Cheese is excluded from p when shared-run JSON stores the gained cards but not all offered cards needed for the denominator.",
            "avgCopiesWhenPresent": "+0 and +1 copies are combined and divided by runs containing either form; the runtime displays this as copy.",
            "percentileBands": "Character-card bands are computed within the owning character pool. Colorless bands are computed separately for each current character. Copy bands additionally exclude Basic cards and require at least 30 final decks containing the card; other pools or low-sample copy values display gray.",
        },
        "cards": cards,
    }


def build_runtime_card_variant(
    model_id: str,
    source_group: dict[str, Any],
    cards_by_model_id: dict[str, dict[str, Any]],
    distribution_group: str | None,
    copy_distribution_eligible: bool,
) -> dict[str, Any]:
    card = cards_by_model_id.get(model_id)
    if card is None:
        card = empty_summary_card(model_id)
    return {
        "sampleRuns": source_group["totalRuns"],
        "distributionGroup": distribution_group,
        "copyDistributionEligible": copy_distribution_eligible,
        "totalRunsWith": card["totalRunsWith"],
        "totalCopies": card["totalCopies"],
        "avgCopiesWhenPresent": card["avgCopiesWhenPresent"],
        "plus0": {
            "finalRunCount": card["plus0FinalRunCount"],
            "appearanceProbability": card["plus0AppearanceProbability"],
            "offerCount": card["plus0OfferCount"],
            "pickCount": card["plus0PickCount"],
            "pickRate": card["plus0PickRate"] if card["plus0OfferCount"] > 0 else None,
            "shopOfferCount": card["plus0ShopOfferCount"],
            "shopBuyCount": card["plus0ShopBuyCount"],
            "shopBuyRate": (
                card["plus0ShopBuyRate"]
                if card["plus0ShopOfferCount"] > 0
                else None
            ),
        },
        "plus1": {
            "finalRunCount": card["plus1FinalRunCount"],
            "appearanceProbability": card["plus1AppearanceProbability"],
            "offerCount": card["plus1OfferCount"],
            "pickCount": card["plus1PickCount"],
            "pickRate": card["plus1PickRate"] if card["plus1OfferCount"] > 0 else None,
            "shopOfferCount": card["plus1ShopOfferCount"],
            "shopBuyCount": card["plus1ShopBuyCount"],
            "shopBuyRate": (
                card["plus1ShopBuyRate"]
                if card["plus1ShopOfferCount"] > 0
                else None
            ),
        },
    }


def empty_summary_card(model_id: str) -> dict[str, Any]:
    return {
        "modelId": model_id,
        "totalRunsWith": 0,
        "totalCopies": 0,
        "avgCopiesWhenPresent": 0.0,
        "plus0FinalRunCount": 0,
        "plus0AppearanceProbability": 0.0,
        "plus1FinalRunCount": 0,
        "plus1AppearanceProbability": 0.0,
        "plus0OfferCount": 0,
        "plus0PickCount": 0,
        "plus0PickRate": 0.0,
        "plus1OfferCount": 0,
        "plus1PickCount": 0,
        "plus1PickRate": 0.0,
        "plus0ShopOfferCount": 0,
        "plus0ShopBuyCount": 0,
        "plus0ShopBuyRate": 0.0,
        "plus1ShopOfferCount": 0,
        "plus1ShopBuyCount": 0,
        "plus1ShopBuyRate": 0.0,
    }


def build_runtime_ancient_choice_output(output: dict[str, Any]) -> dict[str, Any]:
    groups_by_key = {group["key"]: group for group in output["groups"]}
    outcome_groups_by_key = {
        group["key"]: group for group in output["ancientOutcomeGroups"]
    }
    characters: dict[str, Any] = {}
    for character_id in OFFICIAL_CARD_POOL_CHARACTERS.values():
        group = groups_by_key[f"character:{character_id}"]
        outcome_group = outcome_groups_by_key[f"character:{character_id}"]
        pick_choices = {
            choice["textKey"]: choice
            for choice in group.get("ancientChoices") or []
        }
        outcome_choices = {
            choice["textKey"]: choice
            for choice in outcome_group.get("ancientChoices") or []
        }
        choices: dict[str, Any] = {}
        for text_key in sorted(set(pick_choices) | set(outcome_choices)):
            pick_choice = pick_choices.get(text_key) or {}
            outcome_choice = outcome_choices.get(text_key) or {}
            choices[text_key] = {
                "offerCount": pick_choice.get("offerCount", 0),
                "pickCount": pick_choice.get("pickCount", 0),
                "pickRate": pick_choice.get("pickRate"),
                "pickedRunCount": outcome_choice.get("pickedRunCount", 0),
                "pickedWinCount": outcome_choice.get("pickedWinCount", 0),
                "pickedWinRate": outcome_choice.get("pickedWinRate"),
            }
        characters[character_id] = {
            "sampleRuns": group["totalRuns"],
            "totalChoiceScreens": group.get("totalAncientChoiceScreens", 0),
            "outcomeSampleRuns": outcome_group["totalRuns"],
            "outcomeWins": outcome_group["totalWins"],
            "outcomeChoiceScreens": outcome_group.get("totalAncientChoiceScreens", 0),
            "choices": choices,
        }
    return {
        "schemaVersion": 3,
        "generatedAt": output["generatedAt"],
        "scope": output["scope"],
        "choiceRules": {
            "sampleCohort": "Each character uses only that character's runs and Ancient choice screens.",
            "pick": "Pick rate uses the configured winning-run cohort: each option shown contributes one offer and was_chosen contributes one pick.",
            "pickedWinRate": "Picked win rate removes the win filter: each run where the option was chosen contributes once, and a winning run contributes one picked win.",
            "key": "Runtime matching uses the option key after the final .options. segment.",
        },
        "characters": characters,
    }


def build_scope_params(args: argparse.Namespace, *, for_stats: bool) -> dict[str, Any]:
    params: dict[str, Any] = {}
    if args.scope == "a10-wins":
        params.update(
            {
                "ascension": 10,
                "win": "true",
                "players": 1,
                "game_mode": "standard",
            }
        )

    if args.character:
        params["character"] = args.character
    if args.ascension is not None:
        params["ascension"] = args.ascension
    if args.win and args.win != "any":
        params["win"] = args.win
    elif args.win == "any":
        params.pop("win", None)
    if args.players is not None:
        params["players"] = args.players
    if args.game_mode:
        params["game_mode"] = args.game_mode
    if not for_stats:
        if args.build_id:
            params["build_id"] = args.build_id
        if args.build_ids:
            params["build_ids"] = args.build_ids
        if args.sort:
            params["sort"] = args.sort
    return params


def run_matches_filters(run: dict[str, Any], filters: dict[str, Any]) -> bool:
    if "ascension" in filters and int(run.get("ascension") or -1) != int(filters["ascension"]):
        return False
    if "win" in filters and bool(run.get("win")) != (str(filters["win"]).lower() == "true"):
        return False
    if "game_mode" in filters and str(run.get("game_mode") or "") != str(filters["game_mode"]):
        return False
    if "players" in filters and len(run.get("players") or []) != int(filters["players"]):
        return False
    if "build_id" in filters and str(run.get("build_id") or "") != str(filters["build_id"]):
        return False
    if "build_ids" in filters:
        allowed = {item for item in str(filters["build_ids"]).split(",") if item}
        if str(run.get("build_id") or "") not in allowed:
            return False
    if "character" in filters:
        wanted = str(filters["character"])
        if not any(str(player.get("character") or "") == wanted for player in run.get("players") or []):
            return False
    return True


def update_summary_group(
    groups: dict[str, dict[str, Any]],
    *,
    key: str,
    build_id: str,
    character: str,
    cards: list[Any],
    reward_offers: list[tuple[str, bool, bool]],
    shop_offers: list[tuple[str, bool, bool]],
    ancient_choice_screens: list[list[tuple[str, bool]]],
    localization: dict[str, dict[str, str]],
) -> None:
    group = groups.setdefault(
        key,
        {
            "key": key,
            "buildId": build_id,
            "character": character,
            "totalRuns": 0,
            "totalAncientChoiceScreens": 0,
            "_cards": {},
            "_ancientChoices": {},
        },
    )
    group["totalRuns"] += 1

    copies_by_card: dict[str, dict[str, int]] = {}
    for card in cards:
        identity = read_card_identity(card)
        if identity is None:
            continue
        card_id, is_upgraded = identity
        copies = copies_by_card.setdefault(card_id, {"plus0": 0, "plus1": 0})
        copies["plus1" if is_upgraded else "plus0"] += 1

    for card_id, copies in copies_by_card.items():
        bucket = summary_card_bucket(
            group["_cards"],
            card_id,
            localization,
        )
        bucket["totalRunsWith"] += 1
        bucket["totalCopies"] += copies["plus0"] + copies["plus1"]
        if copies["plus0"] > 0:
            bucket["plus0FinalRunCount"] += 1
            bucket["plus0FinalCopyCount"] += copies["plus0"]
        if copies["plus1"] > 0:
            bucket["plus1FinalRunCount"] += 1
            bucket["plus1FinalCopyCount"] += copies["plus1"]

    for card_id, is_upgraded, was_picked in reward_offers:
        bucket = summary_card_bucket(
            group["_cards"],
            card_id,
            localization,
        )
        prefix = "plus1" if is_upgraded else "plus0"
        bucket[f"{prefix}OfferCount"] += 1
        if was_picked:
            bucket[f"{prefix}PickCount"] += 1

    for card_id, is_upgraded, was_bought in shop_offers:
        bucket = summary_card_bucket(
            group["_cards"],
            card_id,
            localization,
        )
        prefix = "plus1" if is_upgraded else "plus0"
        bucket[f"{prefix}ShopOfferCount"] += 1
        if was_bought:
            bucket[f"{prefix}ShopBuyCount"] += 1

    for screen in ancient_choice_screens:
        if not screen:
            continue
        group["totalAncientChoiceScreens"] += 1
        for text_key, was_chosen in screen:
            bucket = ancient_choice_bucket(group["_ancientChoices"], text_key)
            bucket["offerCount"] += 1
            if was_chosen:
                bucket["pickCount"] += 1


def update_ancient_outcome_group(
    groups: dict[str, dict[str, Any]],
    *,
    key: str,
    build_id: str,
    character: str,
    won: bool,
    ancient_choice_screens: list[list[tuple[str, bool]]],
) -> None:
    group = groups.setdefault(
        key,
        {
            "key": key,
            "buildId": build_id,
            "character": character,
            "totalRuns": 0,
            "totalWins": 0,
            "totalAncientChoiceScreens": 0,
            "_ancientChoices": {},
        },
    )
    group["totalRuns"] += 1
    if won:
        group["totalWins"] += 1
    group["totalAncientChoiceScreens"] += sum(
        1 for screen in ancient_choice_screens if screen
    )

    picked_keys = {
        normalize_ancient_choice_key(text_key)
        for screen in ancient_choice_screens
        for text_key, was_chosen in screen
        if was_chosen
    }
    for text_key in picked_keys:
        bucket = ancient_choice_bucket(group["_ancientChoices"], text_key)
        bucket["pickedRunCount"] = bucket.get("pickedRunCount", 0) + 1
        if won:
            bucket["pickedWinCount"] = bucket.get("pickedWinCount", 0) + 1


def ancient_choice_bucket(
    choices: dict[str, dict[str, Any]],
    text_key: str,
) -> dict[str, Any]:
    normalized = normalize_ancient_choice_key(text_key)
    return choices.setdefault(
        normalized,
        {
            "textKey": normalized,
            "offerCount": 0,
            "pickCount": 0,
        },
    )


def summary_card_bucket(
    cards: dict[str, dict[str, Any]],
    card_id: str,
    localization: dict[str, dict[str, str]],
) -> dict[str, Any]:
    return cards.setdefault(
        card_id,
        {
            "cardId": card_id,
            "modelId": normalize_model_id(card_id),
            "name": localized_name(card_id, localization),
            "totalRunsWith": 0,
            "totalCopies": 0,
            "plus0FinalRunCount": 0,
            "plus0FinalCopyCount": 0,
            "plus1FinalRunCount": 0,
            "plus1FinalCopyCount": 0,
            "plus0OfferCount": 0,
            "plus0PickCount": 0,
            "plus0ShopOfferCount": 0,
            "plus0ShopBuyCount": 0,
            "plus1OfferCount": 0,
            "plus1PickCount": 0,
            "plus1ShopOfferCount": 0,
            "plus1ShopBuyCount": 0,
        },
    )


def read_card_identity(card: Any) -> tuple[str, bool] | None:
    if isinstance(card, str):
        value = card
        upgrade_level = 0
    elif isinstance(card, dict):
        value = str(card.get("id") or "")
        upgrade_level = int(card.get("current_upgrade_level") or 0)
    else:
        return None
    if not value:
        return None
    if value.startswith("CARD."):
        value = value[5:]
    if value.endswith("+1"):
        value = value[:-2]
        upgrade_level = max(upgrade_level, 1)
    return value, upgrade_level > 0


def read_card_offer_choices(
    run: dict[str, Any],
    player_id: Any,
) -> tuple[list[tuple[str, bool, bool]], list[tuple[str, bool, bool]]]:
    reward_offers: list[tuple[str, bool, bool]] = []
    shop_offers: list[tuple[str, bool, bool]] = []
    for act in run.get("map_point_history") or []:
        for node in act or []:
            if not isinstance(node, dict):
                continue
            room_types = {
                str(room.get("room_type") or "")
                for room in node.get("rooms") or []
                if isinstance(room, dict)
            }
            room_model_ids = {
                str(room.get("model_id") or "")
                for room in node.get("rooms") or []
                if isinstance(room, dict)
            }
            for stats in node.get("player_stats") or []:
                if not isinstance(stats, dict):
                    continue
                stats_player_id = stats.get("player_id")
                if player_id is not None and stats_player_id not in {None, player_id}:
                    continue
                if room_types.intersection({"monster", "elite", "boss"}):
                    reward_offers.extend(read_reward_card_choices(stats))
                if "shop" in room_types:
                    shop_offers.extend(read_shop_card_choices(stats))
                for model_id in room_model_ids.intersection(FREE_EVENT_CARD_CHOICE_RULES):
                    allowed_counts, max_picked = FREE_EVENT_CARD_CHOICE_RULES[model_id]
                    reward_offers.extend(
                        read_observed_card_choices(
                            stats,
                            allowed_counts=allowed_counts,
                            max_picked=max_picked,
                        )
                    )
    return reward_offers, shop_offers


def read_reward_card_choices(stats: dict[str, Any]) -> list[tuple[str, bool, bool]]:
    return read_observed_card_choices(stats, allowed_counts={3, 4}, max_picked=1)


def read_observed_card_choices(
    stats: dict[str, Any],
    *,
    allowed_counts: set[int],
    max_picked: int,
) -> list[tuple[str, bool, bool]]:
    choices = [
        choice
        for choice in stats.get("card_choices") or []
        if isinstance(choice, dict)
        and read_card_identity(choice.get("card") or choice) is not None
    ]
    picked_count = sum(bool(choice.get("was_picked")) for choice in choices)
    if len(choices) not in allowed_counts or picked_count > max_picked:
        return []

    offers: list[tuple[str, bool, bool]] = []
    for choice in choices:
        identity = read_card_identity(choice.get("card") or choice)
        if identity is None:
            continue
        card_id, is_upgraded = identity
        offers.append((card_id, is_upgraded, bool(choice.get("was_picked"))))
    return offers


def read_shop_card_choices(stats: dict[str, Any]) -> list[tuple[str, bool, bool]]:
    offers: list[tuple[str, bool, bool]] = []
    picked_choices: dict[tuple[str, bool], int] = {}

    for choice in stats.get("card_choices") or []:
        if not isinstance(choice, dict):
            continue
        identity = read_card_identity(choice.get("card") or choice)
        if identity is None:
            continue
        card_id, is_upgraded = identity
        was_picked = bool(choice.get("was_picked"))
        offers.append((card_id, is_upgraded, was_picked))
        if was_picked:
            picked_key = (card_id, is_upgraded)
            picked_choices[picked_key] = picked_choices.get(picked_key, 0) + 1

    for card in stats.get("cards_gained") or []:
        identity = read_card_identity(card)
        if identity is None:
            continue
        picked_key = identity
        already_counted = picked_choices.get(picked_key, 0)
        if already_counted > 0:
            picked_choices[picked_key] = already_counted - 1
            continue
        matching_offer = next(
            (
                index
                for index, offer in enumerate(offers)
                if offer[:2] == picked_key and not offer[2]
            ),
            None,
        )
        if matching_offer is not None:
            card_id, is_upgraded, _ = offers[matching_offer]
            offers[matching_offer] = (card_id, is_upgraded, True)
        else:
            card_id, is_upgraded = identity
            offers.append((card_id, is_upgraded, True))

    return offers


def read_ancient_choice_screens(
    run: dict[str, Any],
    player_id: Any,
) -> list[list[tuple[str, bool]]]:
    screens: list[list[tuple[str, bool]]] = []
    for act in run.get("map_point_history") or []:
        for node in act or []:
            if not isinstance(node, dict):
                continue
            for stats in node.get("player_stats") or []:
                if not isinstance(stats, dict):
                    continue
                stats_player_id = stats.get("player_id")
                if player_id is not None and stats_player_id not in {None, player_id}:
                    continue
                raw_choices = (
                    stats.get("ancient_choice")
                    or stats.get("ancient_choices")
                    or stats.get("ancientChoices")
                    or []
                )
                choices: list[tuple[str, bool]] = []
                for choice in raw_choices:
                    if not isinstance(choice, dict):
                        continue
                    text_key = read_ancient_choice_key(choice)
                    if not text_key:
                        continue
                    choices.append((text_key, bool(choice.get("was_chosen"))))
                if choices:
                    screens.append(choices)
    return screens


def read_ancient_choice_key(choice: Any) -> str | None:
    if not isinstance(choice, dict):
        return None
    text_key = str(choice.get("TextKey") or choice.get("textKey") or choice.get("text_key") or "")
    if text_key:
        return normalize_ancient_choice_key(text_key)
    title = choice.get("title")
    if isinstance(title, dict):
        loc_key = str(title.get("key") or "")
        if loc_key:
            return normalize_ancient_choice_key(loc_key)
    return None


def normalize_ancient_choice_key(text_key: str) -> str:
    value = text_key.strip()
    if value.endswith(".title"):
        value = value[: -len(".title")]
    marker = ".options."
    marker_index = value.lower().rfind(marker)
    if marker_index >= 0:
        return value[marker_index + len(marker) :]
    if "." in value:
        return value.rsplit(".", 1)[-1]
    return value


def finalize_summary_cards(cards: dict[str, dict[str, Any]], total_runs: int) -> list[dict[str, Any]]:
    rows = []
    for card in cards.values():
        row = dict(card)
        row["totalRuns"] = total_runs
        row["appearanceProbability"] = safe_ratio(row["totalRunsWith"], total_runs)
        row["plus0AppearanceProbability"] = safe_ratio(row["plus0FinalRunCount"], total_runs)
        row["plus1AppearanceProbability"] = safe_ratio(row["plus1FinalRunCount"], total_runs)
        row["plus0PickRate"] = safe_ratio(row["plus0PickCount"], row["plus0OfferCount"])
        row["plus1PickRate"] = safe_ratio(row["plus1PickCount"], row["plus1OfferCount"])
        row["plus0ShopBuyRate"] = safe_ratio(
            row["plus0ShopBuyCount"], row["plus0ShopOfferCount"]
        )
        row["plus1ShopBuyRate"] = safe_ratio(
            row["plus1ShopBuyCount"], row["plus1ShopOfferCount"]
        )
        row["copiesPerRun"] = safe_ratio(row["totalCopies"], total_runs)
        row["avgCopiesWhenPresent"] = safe_ratio(row["totalCopies"], row["totalRunsWith"])
        rows.append(row)
    rows.sort(key=lambda row: (-row["appearanceProbability"], row["cardId"]))
    return rows


def finalize_ancient_choices(choices: dict[str, dict[str, Any]]) -> list[dict[str, Any]]:
    rows = []
    for row in choices.values():
        row["pickRate"] = safe_ratio(row["pickCount"], row["offerCount"])
        rows.append(row)
    rows.sort(key=lambda row: (-row["offerCount"], row["textKey"]))
    return rows


def finalize_ancient_outcomes(choices: dict[str, dict[str, Any]]) -> list[dict[str, Any]]:
    rows = []
    for row in choices.values():
        row["pickedRunCount"] = row.get("pickedRunCount", 0)
        row["pickedWinCount"] = row.get("pickedWinCount", 0)
        row["pickedWinRate"] = safe_ratio(
            row["pickedWinCount"],
            row["pickedRunCount"],
        )
        row.pop("offerCount", None)
        row.pop("pickCount", None)
        rows.append(row)
    rows.sort(key=lambda row: (-row["pickedRunCount"], row["textKey"]))
    return rows


def make_card_stat_row(
    *,
    card_id: str,
    total_runs: int,
    total_runs_with: int,
    total_copies: int,
    localization: dict[str, dict[str, str]],
) -> dict[str, Any]:
    return {
        "cardId": card_id,
        "modelId": normalize_model_id(card_id),
        "name": localized_name(card_id, localization),
        "totalRuns": total_runs,
        "totalRunsWith": total_runs_with,
        "appearanceProbability": safe_ratio(total_runs_with, total_runs),
        "totalCopies": total_copies,
        "copiesPerRun": safe_ratio(total_copies, total_runs),
    }


def parse_characters(value: str) -> list[str]:
    characters = [item.strip().upper() for item in value.split(",") if item.strip()]
    if not characters:
        raise SystemExit("--characters must contain at least one character")
    return characters


def normalize_model_id(card_id: str) -> str:
    return card_id if card_id.startswith("CARD.") else f"CARD.{card_id}"


def localized_name(
    card_id: str,
    localization: dict[str, dict[str, str]],
) -> dict[str, str | None]:
    key = card_id[5:] if card_id.startswith("CARD.") else card_id
    entry = localization.get(key) or localization.get(card_id) or {}
    return {
        "en": entry.get("en"),
        "zhs": entry.get("zhs"),
    }


def load_localization(path: Path) -> dict[str, dict[str, str]]:
    if not path.exists():
        return {}
    data = json.loads(path.read_text(encoding="utf-8"))
    entries = data.get("entries") if isinstance(data, dict) else None
    if not isinstance(entries, dict):
        return {}
    result: dict[str, dict[str, str]] = {}
    for key, value in entries.items():
        if not isinstance(value, dict):
            continue
        result[str(key)] = {
            "en": str(value.get("en")) if value.get("en") is not None else "",
            "zhs": str(value.get("zhs")) if value.get("zhs") is not None else "",
        }
    return result


def write_stats_csv(path: Path, output: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = [
        "scope",
        "character",
        "card_id",
        "model_id",
        "name_en",
        "name_zhs",
        "total_runs_with",
        "total_runs",
        "appearance_probability",
        "total_copies",
        "copies_per_run",
    ]
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for card in output["allCharacters"]["cards"]:
            writer.writerow(csv_row("allCharacters", "", card))
        for character, data in output["byCharacter"].items():
            for card in data["cards"]:
                writer.writerow(csv_row("character", character, card))


def write_cache_summary_csv(path: Path, output: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    fields = [
        "group_key",
        "build_id",
        "character",
        "card_id",
        "model_id",
        "name_en",
        "name_zhs",
        "total_runs_with",
        "total_runs",
        "appearance_probability",
        "plus0_final_run_count",
        "plus0_appearance_probability",
        "plus1_final_run_count",
        "plus1_appearance_probability",
        "total_copies",
        "copies_per_run",
        "avg_copies_when_present",
        "plus0_offer_count",
        "plus0_pick_count",
        "plus0_pick_rate",
        "plus1_offer_count",
        "plus1_pick_count",
        "plus1_pick_rate",
        "plus0_shop_offer_count",
        "plus0_shop_buy_count",
        "plus0_shop_buy_rate",
        "plus1_shop_offer_count",
        "plus1_shop_buy_count",
        "plus1_shop_buy_rate",
    ]
    with path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(handle, fieldnames=fields)
        writer.writeheader()
        for group in output["groups"]:
            for card in group["cards"]:
                row = csv_row("cached", group["character"], card)
                writer.writerow(
                    {
                        "group_key": group["key"],
                        "build_id": group["buildId"],
                        "character": group["character"],
                        "card_id": row["card_id"],
                        "model_id": row["model_id"],
                        "name_en": row["name_en"],
                        "name_zhs": row["name_zhs"],
                        "total_runs_with": row["total_runs_with"],
                        "total_runs": row["total_runs"],
                        "appearance_probability": row["appearance_probability"],
                        "plus0_final_run_count": card["plus0FinalRunCount"],
                        "plus0_appearance_probability": card["plus0AppearanceProbability"],
                        "plus1_final_run_count": card["plus1FinalRunCount"],
                        "plus1_appearance_probability": card["plus1AppearanceProbability"],
                        "total_copies": row["total_copies"],
                        "copies_per_run": row["copies_per_run"],
                        "avg_copies_when_present": card["avgCopiesWhenPresent"],
                        "plus0_offer_count": card["plus0OfferCount"],
                        "plus0_pick_count": card["plus0PickCount"],
                        "plus0_pick_rate": card["plus0PickRate"],
                        "plus1_offer_count": card["plus1OfferCount"],
                        "plus1_pick_count": card["plus1PickCount"],
                        "plus1_pick_rate": card["plus1PickRate"],
                        "plus0_shop_offer_count": card["plus0ShopOfferCount"],
                        "plus0_shop_buy_count": card["plus0ShopBuyCount"],
                        "plus0_shop_buy_rate": card["plus0ShopBuyRate"],
                        "plus1_shop_offer_count": card["plus1ShopOfferCount"],
                        "plus1_shop_buy_count": card["plus1ShopBuyCount"],
                        "plus1_shop_buy_rate": card["plus1ShopBuyRate"],
                    }
                )


def csv_row(scope: str, character: str, card: dict[str, Any]) -> dict[str, Any]:
    name = card.get("name") or {}
    return {
        "scope": scope,
        "character": character,
        "card_id": card.get("cardId"),
        "model_id": card.get("modelId"),
        "name_en": name.get("en"),
        "name_zhs": name.get("zhs"),
        "total_runs_with": card.get("totalRunsWith"),
        "total_runs": card.get("totalRuns"),
        "appearance_probability": card.get("appearanceProbability"),
        "total_copies": card.get("totalCopies"),
        "copies_per_run": card.get("copiesPerRun"),
    }


def write_crawl_state(
    path: Path,
    args: argparse.Namespace,
    params_base: dict[str, Any],
    next_page: int,
    counters: dict[str, int],
    started_at: str,
) -> None:
    write_json(
        path,
        {
            "schemaVersion": 1,
            "startedAt": started_at,
            "updatedAt": now_iso(),
            "nextPage": next_page,
            "scope": args.scope,
            "filters": params_base,
            "counters": counters,
        },
    )


def print_crawl_summary(counters: dict[str, int], request_count: int) -> None:
    print(
        "crawl stopped: "
        + ", ".join(f"{key}={value}" for key, value in counters.items())
        + f", requests={request_count}"
    )


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temp.replace(path)


def append_jsonl(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(value, ensure_ascii=False) + "\n")


def safe_ratio(numerator: int, denominator: int) -> float:
    if denominator <= 0:
        return 0.0
    return numerator / denominator


def now_iso() -> str:
    return dt.datetime.now(dt.UTC).isoformat(timespec="seconds").replace("+00:00", "Z")


if __name__ == "__main__":
    raise SystemExit(main())
