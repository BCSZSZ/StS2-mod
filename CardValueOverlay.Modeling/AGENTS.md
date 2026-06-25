# AGENTS.md

Instructions for mathematical modeling and data extraction code.

- Keep this project pure C# and outside the runtime mod package.
- Do not reference Godot UI/runtime projects or `CardValueOverlayCode/`.
- Prefer generated intermediate data with provenance and confidence over direct writes to `CardValueOverlay/data/card_values.json`.
- Treat `sts2.dll` and `sts2.xml` as local source inputs; do not load game assemblies casually in process unless the code isolates failures.
- Generated extraction outputs belong under `data/extracted/` or `data/generated/`; hand-authored card effect and monster move overrides belong under `data/manual-tags/`.
