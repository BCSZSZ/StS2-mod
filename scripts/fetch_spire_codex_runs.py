"""Fetch Spire Codex card stats and cache raw run JSON.

This script avoids the currently timing-out /api/exports/runs endpoint.
Use `stats` for the card appearance probabilities needed by the overlay, and
`crawl-runs` for a slow, resumable raw-run cache built from /runs/list +
/runs/shared/{hash}. Use `retry-failures` to recover exact hashes after a
network interruption because live list page numbers can shift over time. The
hosted /runs/stats endpoint does not support build_id filters, so use
`summarize-cache` for version-specific final-deck and reward-choice adoption
statistics.
"""

from __future__ import annotations

import argparse
import csv
import datetime as dt
import json
import random
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any


API_BASE = "https://spire-codex.com"
OFFICIAL_CHARACTERS = ("IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT")
DEFAULT_USER_AGENT = (
    "CardValueOverlay Spire Codex data pipeline "
    "(respectful cache; https://github.com/BCSZSZ/StS2-mod)"
)


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
        help="Compute +0/+1 final-deck and reward-choice adoption statistics.",
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
                    print(f"shared {run_hash}", file=sys.stderr)
                    run_data = client.get_json(f"/api/runs/shared/{run_hash}")
                    write_json(dest, run_data)
                    counters["downloadedRuns"] += 1
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


def summarize_cache(args: argparse.Namespace) -> int:
    input_root = Path(args.input_root)
    runs_dir = input_root / "runs"
    if not runs_dir.exists():
        raise SystemExit(f"cached runs directory does not exist: {runs_dir}")

    localization = load_localization(Path(args.localization))
    filters = build_scope_params(args, for_stats=False)
    groups: dict[str, dict[str, Any]] = {}
    total_cached_runs = 0
    matched_runs = 0

    for run_path in sorted(runs_dir.glob("*.json")):
        total_cached_runs += 1
        try:
            run = json.loads(run_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError:
            continue
        if not run_matches_filters(run, filters):
            continue
        matched_runs += 1

        build_id = str(run.get("build_id") or "")
        for player in run.get("players") or []:
            character = str(player.get("character") or "")
            if filters.get("character") and character != str(filters["character"]):
                continue
            cards = player.get("deck") or []
            if not cards:
                continue
            offers = read_card_offer_choices(run, player.get("id"))
            update_summary_group(
                groups,
                key="all",
                build_id="",
                character="",
                cards=cards,
                offers=offers,
                localization=localization,
            )
            update_summary_group(
                groups,
                key=f"build:{build_id or '<unknown>'}",
                build_id=build_id,
                character="",
                cards=cards,
                offers=offers,
                localization=localization,
            )
            update_summary_group(
                groups,
                key=f"character:{character or '<unknown>'}",
                build_id="",
                character=character,
                cards=cards,
                offers=offers,
                localization=localization,
            )
            update_summary_group(
                groups,
                key=f"build:{build_id or '<unknown>'}|character:{character or '<unknown>'}",
                build_id=build_id,
                character=character,
                cards=cards,
                offers=offers,
                localization=localization,
            )

    output_groups = []
    for group in groups.values():
        cards = finalize_summary_cards(group.pop("_cards"), group["totalRuns"])
        group["cards"] = cards
        output_groups.append(group)
    output_groups.sort(key=lambda group: (group["buildId"], group["character"], group["key"]))

    output = {
        "schemaVersion": 2,
        "generatedAt": now_iso(),
        "source": {
            "inputRoot": str(input_root),
            "runsDir": str(runs_dir),
            "method": "local +0/+1 adoption summary of cached /api/runs/shared/{hash} JSON",
        },
        "scope": {
            "filters": filters,
        },
        "totalCachedRuns": total_cached_runs,
        "matchedRuns": matched_runs,
        "groups": output_groups,
    }
    write_json(Path(args.output_json), output)
    write_cache_summary_csv(Path(args.output_csv), output)
    if args.runtime_output_json:
        write_json(Path(args.runtime_output_json), build_runtime_adoption_output(output))
    print(
        f"wrote {args.output_json} and {args.output_csv}; "
        f"matchedRuns={matched_runs} groups={len(output_groups)}"
    )
    return 0


def build_runtime_adoption_output(output: dict[str, Any]) -> dict[str, Any]:
    all_group = next(group for group in output["groups"] if group["key"] == "all")
    cards: dict[str, Any] = {}
    for card in all_group["cards"]:
        cards[card["modelId"]] = {
            "totalRunsWith": card["totalRunsWith"],
            "totalCopies": card["totalCopies"],
            "avgCopiesWhenPresent": card["avgCopiesWhenPresent"],
            "plus0": {
                "finalRunCount": card["plus0FinalRunCount"],
                "appearanceProbability": card["plus0AppearanceProbability"],
                "offerCount": card["plus0OfferCount"],
                "pickCount": card["plus0PickCount"],
                "pickRate": (
                    card["plus0PickRate"] if card["plus0OfferCount"] > 0 else None
                ),
            },
            "plus1": {
                "finalRunCount": card["plus1FinalRunCount"],
                "appearanceProbability": card["plus1AppearanceProbability"],
                "offerCount": card["plus1OfferCount"],
                "pickCount": card["plus1PickCount"],
                "pickRate": (
                    card["plus1PickRate"] if card["plus1OfferCount"] > 0 else None
                ),
            },
        }
    return {
        "schemaVersion": 1,
        "generatedAt": output["generatedAt"],
        "scope": output["scope"],
        "totalRuns": all_group["totalRuns"],
        "formRules": {
            "finalDeck": "+0 and +1 use each final card's current upgrade level, regardless of how it was upgraded.",
            "rewardChoice": "+0 and +1 use the card form originally shown in standard combat, elite, boss rewards, or merchant shops; later upgrades are not attributed back to the offer.",
            "avgCopiesWhenPresent": "+0 and +1 copies are combined and divided by runs containing either form.",
        },
        "cards": cards,
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
    offers: list[tuple[str, bool, bool]],
    localization: dict[str, dict[str, str]],
) -> None:
    group = groups.setdefault(
        key,
        {
            "key": key,
            "buildId": build_id,
            "character": character,
            "totalRuns": 0,
            "_cards": {},
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

    for card_id, is_upgraded, was_picked in offers:
        bucket = summary_card_bucket(
            group["_cards"],
            card_id,
            localization,
        )
        prefix = "plus1" if is_upgraded else "plus0"
        bucket[f"{prefix}OfferCount"] += 1
        if was_picked:
            bucket[f"{prefix}PickCount"] += 1


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
            "plus1OfferCount": 0,
            "plus1PickCount": 0,
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
) -> list[tuple[str, bool, bool]]:
    offers: list[tuple[str, bool, bool]] = []
    for act in run.get("map_point_history") or []:
        for node in act or []:
            if not isinstance(node, dict):
                continue
            room_types = {
                str(room.get("room_type") or "")
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
                    offers.extend(read_reward_card_choices(stats))
                if "shop" in room_types:
                    offers.extend(read_shop_card_choices(stats))
    return offers


def read_reward_card_choices(stats: dict[str, Any]) -> list[tuple[str, bool, bool]]:
    choices = [
        choice
        for choice in stats.get("card_choices") or []
        if isinstance(choice, dict)
        and read_card_identity(choice.get("card") or choice) is not None
    ]
    picked_count = sum(bool(choice.get("was_picked")) for choice in choices)
    if len(choices) not in {3, 4} or picked_count > 1:
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
        card_id, is_upgraded = identity
        offers.append((card_id, is_upgraded, True))

    return offers


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
        row["copiesPerRun"] = safe_ratio(row["totalCopies"], total_runs)
        row["avgCopiesWhenPresent"] = safe_ratio(row["totalCopies"], row["totalRunsWith"])
        rows.append(row)
    rows.sort(key=lambda row: (-row["appearanceProbability"], row["cardId"]))
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
