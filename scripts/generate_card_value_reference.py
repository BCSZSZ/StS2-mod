# /// script
# requires-python = ">=3.11"
# dependencies = ["openpyxl>=3.1"]
# ///
"""Generate the card-value reference table (Markdown + XLSX).

Reads the runtime overlay values from ``CardValueOverlay/data/card_values.json``
and the official EN/中文 card names from the extracted localization file, then
writes a sorted play-delta main table plus a static-layer17 appendix as both a
Markdown file and an XLSX workbook under the output directory.

Dependency management: this is a PEP 723 single-file script. Run it with uv and
the openpyxl dependency is provisioned automatically (no manual pip/venv):

    uv run scripts/generate_card_value_reference.py

The `write-card-value-reference` Tools command invokes exactly this. Running on
a fresh machine only requires uv on PATH; uv re-provisions openpyxl on demand.
"""

from __future__ import annotations

import argparse
import datetime
import json
from pathlib import Path

# Cards the deck simulator cannot model (unsupported action) still carry the old
# static layer-17 estimate, which is a DIFFERENT scale from play-delta and is not
# comparable to the main table. When one of these becomes simulatable and gets a
# play-delta value, remove it from this set. (Discovery/Jackpot are simulated now,
# so they are intentionally absent.)
STATIC_CARDS = {
    "CARD.ALCHEMIZE", "CARD.ANOINTED", "CARD.MAYHEM", "CARD.MONARCHS_GAZE", "CARD.NOSTALGIA",
    "CARD.PANIC_BUTTON", "CARD.REND", "CARD.ROYALTIES", "CARD.SPLASH", "CARD.THE_GAMBIT",
}


def parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(description="Generate the card-value reference table (MD + XLSX).")
    p.add_argument("--config", default="CardValueOverlay/data/card_values.json",
                   help="Runtime overlay value JSON (source of the values).")
    p.add_argument("--localization", default="history-analysis/data/localized_names_en_zhs.json",
                   help="Extracted EN/中文 card-name localization JSON.")
    p.add_argument("--output-dir", default="data/manual-tags",
                   help="Directory to write card_values_reference_<date>.{md,xlsx} into.")
    p.add_argument("--date", default=None,
                   help="Report date (YYYY-MM-DD). Defaults to today.")
    return p.parse_args()


def load_json(path: str) -> dict:
    return json.loads(Path(path).read_text(encoding="utf-8"))


def round1(x):
    return None if x is None else round(float(x), 1)


def fmt(x) -> str:
    return "" if x is None else f"{x:g}"


def build_rows(cards: dict, loc: dict) -> list[dict]:
    rows = []
    for mid, entry in cards.items():
        key = mid.split(".", 1)[1] if "." in mid else mid
        names = loc.get(key, {})
        tv = entry.get("trainingValues", {})
        u = tv.get("unupgraded", {})
        g = tv.get("upgraded", {})
        rows.append({
            "mid": mid,
            "en": names.get("en", key),
            "zh": names.get("zhs", key),
            "pools": ",".join(entry.get("pools", []) or []),
            "us": round1(u.get("shortline")), "um": round1(u.get("midline")), "ul": round1(u.get("longline")),
            "gs": round1(g.get("shortline")), "gm": round1(g.get("midline")), "gl": round1(g.get("longline")),
            "static": mid in STATIC_CARDS,
        })
    return rows


def write_markdown(path: Path, date: str, main: list[dict], static: list[dict]) -> None:
    out = []
    out.append(f"# 卡牌价值参考表 . Card Value Reference - {date}")
    out.append("")
    out.append("来源文件：`CardValueOverlay/data/card_values.json`（运行时覆盖层数值）。"
               "英/中卡名取自 `history-analysis/data/localized_names_en_zhs.json`（游戏官方本地化）。")
    out.append("")
    out.append("数值 = **play-delta（边际 dEV，每次直接打出的价值）**。分 short(4) / mid(8) / long(14) "
               "三个时间跨度，按'未升级 mid'从高到低排序。负值属正常：前期铺垫/稀释成本，靠后回合或联动才回本。")
    out.append("")
    out.append(f"> 只在同一口径内可比。主表 **{len(main)} 张** 为 play-delta；文末 **{len(static)} 张**"
               "（模拟器无法建模的卡）仍用旧静态 layer-17 估值，口径不同，不可直接比较。")
    out.append("> WARNING: 斜坡/生成类（Calamity/SpectrumShift/BundleOfJoy/RollingBoulder 等）的 long 值被"
               "'无战斗结束/无溢出上限'模型局限放大，长线偏高。")
    out.append("")
    out.append(f"## 主表 . play-delta（{len(main)} 张，按未升级 mid 降序）")
    out.append("")
    out.append("| # | English | 中文 | 卡池 | U.short | U.mid | U.long | +.short | +.mid | +.long |")
    out.append("|---:|---|---|---|---:|---:|---:|---:|---:|---:|")
    for i, r in enumerate(main, 1):
        out.append(f"| {i} | {r['en']} | {r['zh']} | {r['pools']} | {fmt(r['us'])} | {fmt(r['um'])} | "
                   f"{fmt(r['ul'])} | {fmt(r['gs'])} | {fmt(r['gm'])} | {fmt(r['gl'])} |")
    out.append("")
    out.append(f"## 附录 . 静态 layer-17 估值（{len(static)} 张，不可模拟，口径不同）")
    out.append("")
    out.append("| English | 中文 | 卡池 | U.short | U.mid | U.long | +.short | +.mid | +.long |")
    out.append("|---|---|---|---:|---:|---:|---:|---:|---:|")
    for r in static:
        out.append(f"| {r['en']} | {r['zh']} | {r['pools']} | {fmt(r['us'])} | {fmt(r['um'])} | "
                   f"{fmt(r['ul'])} | {fmt(r['gs'])} | {fmt(r['gm'])} | {fmt(r['gl'])} |")
    out.append("")
    path.write_text("\n".join(out) + "\n", encoding="utf-8")


def write_xlsx(path: Path, main: list[dict], static: list[dict]) -> None:
    from openpyxl import Workbook
    from openpyxl.styles import Alignment, Font

    wb = Workbook()
    ws = wb.active
    ws.title = "CardValues"
    headers = ["#", "English", "中文", "Pool", "Scale",
               "U short(4)", "U mid(8)", "U long(14)", "+ short(4)", "+ mid(8)", "+ long(14)"]
    ws.append(headers)
    for cell in ws[1]:
        cell.font = Font(bold=True)
        cell.alignment = Alignment(horizontal="center")
    for i, r in enumerate(main, 1):
        ws.append([i, r["en"], r["zh"], r["pools"], "play-delta",
                   r["us"], r["um"], r["ul"], r["gs"], r["gm"], r["gl"]])
    for r in static:
        ws.append(["", r["en"], r["zh"], r["pools"], "static-layer17",
                   r["us"], r["um"], r["ul"], r["gs"], r["gm"], r["gl"]])
    widths = [4, 24, 14, 12, 14, 10, 10, 10, 10, 10, 10]
    for i, w in enumerate(widths, 1):
        ws.column_dimensions[chr(64 + i)].width = w
    ws.freeze_panes = "A2"
    wb.save(path)


def main() -> int:
    ns = parse_args()
    date = ns.date or datetime.date.today().isoformat()
    stamp = date.replace("-", "")

    cards = load_json(ns.config)["cards"]
    loc = load_json(ns.localization)["entries"]

    rows = build_rows(cards, loc)
    main_rows = [r for r in rows if not r["static"]]
    static_rows = [r for r in rows if r["static"]]
    main_rows.sort(key=lambda r: (r["um"] is None, -(r["um"] or 0)))
    static_rows.sort(key=lambda r: r["en"])

    out_dir = Path(ns.output_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    md_path = out_dir / f"card_values_reference_{stamp}.md"
    xlsx_path = out_dir / f"card_values_reference_{stamp}.xlsx"
    write_markdown(md_path, date, main_rows, static_rows)
    write_xlsx(xlsx_path, main_rows, static_rows)

    print("card value reference written")
    print(f"date: {date}")
    print(f"main (play-delta): {len(main_rows)}  static (layer-17): {len(static_rows)}")
    print(f"md:   {md_path.as_posix()}")
    print(f"xlsx: {xlsx_path.as_posix()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
