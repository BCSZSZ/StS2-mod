---
name: clean-change-discipline
description: Enforce clean code replacement when Codex changes direction, abandons a partial implementation, removes a failed approach, responds to review feedback, rewrites a feature, or is tempted to leave inactive code, compatibility shims, temporary adapters, dead interfaces, commented-out blocks, or fallback clutter.
---

# Clean Change Discipline

## Rule

When a prior approach is wrong, superseded, or rejected, replace it cleanly.
Do not keep old code around as inactive history or compatibility clutter.

## Required Behavior

- Delete dead branches, commented-out implementations, `#if false` blocks,
  unused adapters, temporary shims, abandoned interfaces, and stale helper APIs.
- Remove call sites, docs, tests, fixtures, generated examples, and command
  entries that only exist for the rejected path.
- Prefer one clear implementation over parallel old/new paths.
- If deleting the old path reveals a bug, fix the bug directly instead of
  restoring the old path as a safety blanket.
- Preserve backward compatibility only when the user explicitly asks for it or
  an external public contract requires it; document that reason in the code or
  final response.

## Check Before Finishing

Search for names from the discarded approach and clean remaining references:

```powershell
rg -n "oldName|temporary|compat|shim|TODO|#if false|obsolete"
```

Run the relevant tests after cleanup. Report any intentionally retained
compatibility path and why it still exists.
