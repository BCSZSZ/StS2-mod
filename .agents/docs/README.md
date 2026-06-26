# Agent Documentation

This folder holds long-lived context for Codex and human maintainers.

The root `AGENTS.md` is the concise source of instructions that should be read
for most tasks. These files are supporting references:

- `roadmap.md`: product direction, architecture, phased implementation plan.
- `runtime-lessons.md`: hard-won runtime, overlay, packaging, and debugging
  lessons.
- `local-environment.md`: machine-specific local setup facts and verification
  checklist.
- `../../docs/modeling/card-value-methodology.md`: mathematical basis for fixed and
  dynamic card valuation.
- `../../docs/modeling/csharp-modeling-plan.md`: plan for the separate C# modeling and
  game-data extraction layer.

Prefer updating `AGENTS.md` when a lesson should directly shape future Codex
behavior. Prefer updating these reference files when the information is longer
background or task history.
