# Agent Documentation

This folder holds long-lived context for Codex and human maintainers.

The root `AGENTS.md` is the concise source of instructions that should be read
for most tasks. These files are supporting references:

- `roadmap.md`: product direction, architecture, phased implementation plan.
- `runtime-lessons.md`: hard-won runtime, overlay, packaging, and debugging
  lessons.
- `local-environment.md`: machine-specific local setup facts and verification
  checklist.
- `persistent-power-simulation.md`: simulator rules for Power cards with
  active event-triggered value, including ChildOfTheStars and BlackHole.
- `card-object-action-simulation.md`: simulator rules for choosing, moving,
  discarding, exhausting, and transforming card objects in combat piles.
- `monster-matrix-lessons.md`: monster encounter action, damage-detail, and
  damage-matrix generation lessons, including the required all-zero audit.
- `../../docs/modeling/card-value-methodology.md`: mathematical basis for fixed and
  dynamic card valuation.
- `../../docs/modeling/csharp-modeling-plan.md`: plan for the separate C# modeling and
  game-data extraction layer.

Prefer updating `AGENTS.md` when a lesson should directly shape future Codex
behavior. Prefer updating these reference files when the information is longer
background or task history.
