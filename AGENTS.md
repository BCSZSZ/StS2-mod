# AGENTS.md

This file gives repo-wide instructions for Codex when working on
`CardValueOverlay`, a Slay the Spire 2 C# Godot mod plus local tooling.

For longer background, read only the relevant files under `.agents/docs/`.
Keep this root file concise: Codex merges `AGENTS.md` files by directory, and
closer nested files can add or override guidance for their subtree.

## Project Map

- `CardValueOverlay.csproj`: runtime packaging project. It produces the mod DLL
  and PCK.
- `CardValueOverlayCode/`: runtime mod code, Harmony patches, Godot labels,
  game-state reads.
- `CardValueOverlay.Core/`: pure config, value, fallback, and calculation logic.
  It must not reference Godot or StS2 assemblies.
- `CardValueOverlay.Tools/`: local CLI. Never package it into the game mod.
- `CardValueOverlay.Core.Tests/`: executable tests for shared logic.
- `CardValueOverlay.Modeling/`: pure mathematical modeling, extraction, and
  generated-data validation. Never package it into the game mod.
- `CardValueOverlay.Modeling.Tests/`: executable tests for modeling logic.
- `data/`: modeling fixtures, manual tags, and generated extraction outputs.
- `CardValueOverlay/`: Godot resources, runtime config JSON, localization, icon.
- `.agents/docs/`: long-lived roadmap, local environment facts, and debugging
  retrospectives for Codex and maintainers.
- `.agents/skills/`: repo-scoped Codex skills and reusable scripts. Keep StS2
  and CardValueOverlay-specific workflows here, not in shared user skills.
- `docs/modeling/`: mathematical card-value methodology and the future C#
  modeling/extraction plan.

## Current Product State

- The overlay is intended to render one small line above cards.
- Active runtime display modes are `fixedText` and `cardName`.
- Training value mode is the active value direction. Core value models use
  training-output schema version 3 with shortline, midline, and longline values.
- `CardValueOverlay/data/card_values.json` may have an empty `cards`
  table only while generated training values are being prepared.
- The runtime value JSON contract is documented in
  `docs/modeling/card-value-json-schema.md`.
- Fixed values should come from the modeling methodology in
  `docs/modeling/card-value-methodology.md`, then be manually curated.

## Architecture Rules

- Keep runtime, shared core, and CLI tools separate.
- Keep modeling/extraction code outside the runtime mod. It may feed candidate
  values into review artifacts, but it must not automatically overwrite
  `CardValueOverlay/data/card_values.json`.
- The game mod loader should load only `CardValueOverlay.dll`; compile shared
  core source into the runtime DLL instead of deploying `CardValueOverlay.Core.dll`.
- Keep pure value rules in `CardValueOverlay.Core/`; do not duplicate them in
  runtime or tools.
- Register simulator rules that depend on a specific card identity in
  `CardBehaviorCatalog`. Generic card facts remain on `SimulationCard`; do not
  scatter new `TypeName == ...` branches through simulator lifecycle methods.
- Card training values use `trainingValues.unupgraded/upgraded.shortline`,
  `midline`, and `longline`. Do not reintroduce scalar `manualValue`,
  scalar `fixedValue`, or the old `manualValues` / `smithValues` card-value
  shape for generated training values.
- Each generated card entry may include optional tracking metadata under
  `generation.method` and `generation.updatedAt.shortline/midline/longline`.
  `method` is a string such as `monteCarlo` or `estimate`; the timestamps are
  ISO-8601 values with offsets. Runtime overlay display must ignore these
  metadata fields and resolve only `trainingValues`.
- Config schema version is `3`. Older value-file schemas are intentionally
  unsupported.
- Enemy damage, monster intent damage, enemy-pressure reports, and defense
  calibration should use Ascension 10 values as the primary modeling basis.
  Non-ascension values may be retained only as explicitly labeled reference
  data.
- New combat-aware valuation follows
  `.agents/docs/combat-aware-simulation-contract.md`. It uses
  `actualEnemyHpLost - enemyHpRestored + delta Phi(playerHp)`, with one point of
  actual enemy HP loss equal to one value. Block, overkill, attempted damage,
  broken block, and unused block have no direct value.
- In the combat-aware path, Weak, Vulnerable, Frail, Strength, Dexterity, Stars,
  Forge, draw, and Powers change physical state or legal actions. Do not import
  legacy `blockToDamage`, fixed debuff values, setup values, or source credits
  into its EV.
- When creating simulator deck fixtures, create the deck JSON under
  `data/manual-tags/simulation_decks/` and matching shortline, midline, and
  longline scenario JSONs under `data/manual-tags/simulation_scenarios/`.
- Random card generation in the simulator should use manually curated
  source-specific JSON pools under `data/manual-tags/`, not ad hoc filtering of
  the full card library. This applies to generated-card cards and generated-card
  Powers alike. Each generating effect owns its own pool id, even when simplified
  v1 pools share contents; keep pools small, simulation-supported, and exclude
  multiplayer-only cards unless explicitly modeling multiplayer. Future pool
  completeness work should update the JSON pool contents, not replace the
  source-specific pool architecture.
- Run combat-aware horizons 4, 8, and 12 independently. Pair the candidate and
  reference deck on the same stage-matched deck/HP snapshot, encounter
  realization, visible initial intent, and semantic random streams.
- The primary card value is portfolio-weighted paired deck dEV. Do not divide by
  direct play count and do not publish source-credit or play-delta-per-play as a
  competing value scale.
- Unsupported samples remain in the coverage denominator. Do not redistribute
  missing target mass onto supported encounters or decks.
- Exact search is a small-state oracle. Any scalable approximation must have an
  explicit mode/status and measured bias against Exact; budget exhaustion,
  unsupported semantics, and inconclusive results must never become normal
  numeric values.

## Build And Verification

Run these from the repository root:

```powershell
dotnet run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Modeling.Tests\CardValueOverlay.Modeling.Tests.csproj --no-restore
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate-generated-data
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-card-facts
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-card-pools
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-monster-moves
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-encounter-patterns
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-card-values --layer 1
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- write-card-review-list
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-enemy-expectations
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-encounter-weighted-enemy-pressure
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-defense-calibration
dotnet build CardValueOverlay.csproj --no-restore -v minimal
dotnet publish CardValueOverlay.csproj -v minimal
```

Local development iterations must use `scripts\publish-local.ps1`. It builds a
complete package under temporary `dist/local-staging`, copies only the four
runtime files into the active profile's ordinary local `mods` directory,
verifies hashes, and removes staging. It must never read from or write to Steam
Workshop content.

Workshop releases must use `scripts\publish-workshop.ps1`. That script builds
under temporary `dist/workshop-staging`, packages the staged output, updates the
existing Workshop item through SteamCMD, and removes staging. A local development
copy may remain installed only when the publisher is not subscribed and passes
`-AllowLocalMod` explicitly. That flag never changes the package source: Workshop
content must still come only from staging, never from the ordinary local `mods`
directory. Use `scripts\package-workshop.ps1` only with an explicit staged
`-ModFolder`.

Plain `dotnet build` and `dotnet publish` leave `DeployToMods=false` and must not
write to any Mods directory. Only `scripts\build-staged-mod.ps1`, as called by
the local and Workshop publishing scripts, sets `DeployToMods=true`, always with
a temporary `ModsPath` under `dist/`. See `.agents/docs/release-workflow.md`.

`scripts\publish-local.ps1` must refuse to deploy while `SlayTheSpire2.exe` is
running because the game may lock `CardValueOverlay.dll`. Close the game before
updating the ordinary local mod copy.

After publish, the local game mod folder must contain only:

```text
CardValueOverlay.dll
CardValueOverlay.json
CardValueOverlay.pck
CardValueOverlay.pdb
```

No `CardValueOverlay.Core.dll` should be present.

Modeling extraction writes generated local reference data under `data/`:

```powershell
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- extract-game-data
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-card-facts
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-card-pools
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-monster-moves
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- parse-encounter-patterns
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-card-values --layer 1
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- write-card-review-list
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-enemy-expectations
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-encounter-weighted-enemy-pressure
dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- estimate-defense-calibration
```

Generated extraction outputs are ignored by Git; commit only source, fixtures,
manual tags, and documentation.

## Combat-Aware Simulation And Performance

- Use `.agents/docs/combat-aware-simulation-contract.md` as the canonical
  contract and `sts2-deck-simulation` as the workflow entry point.
- Default to at most four independent workers and never combine outer portfolio
  parallelism with inner solve parallelism unless a benchmark proves the total
  worker count remains bounded.
- Report wall time, allocated bytes, canonical states, decision/chance nodes,
  outcome branches, memo hits, solver mode/status, and Exact-oracle error where
  applicable.
- The Phase 1 Exact solver is an oracle, not a production engine. The existing
  long-horizon allocation and wall time are a No-Go until a separately named
  approximation passes accuracy and risk-tail gates.
- The Phase 1 top-probability `Sparse` truncation is not approved for HP-risk
  dEV because it can remove rare high-loss outcomes and renormalize the rest.
- `DeckMonteCarloSimulator`, `simulate-deck-scenario`, direct-play attribution,
  setup-value heuristics, and search-policy teacher collection are legacy-only.
  Use them only for an explicitly requested regression or migration comparison,
  never for new primary training values.

## Combat-Aware Deck Delta Strategy

- Evaluate the reference and candidate decks on identical twelve-cell portfolio
  samples and semantic random streams.
- Compute `dEV = candidate combatEV - reference combatEV` for horizons 4, 8,
  and 12; do not normalize by card play count.
- Keep physical ledgers, risk metrics, coverage, ESS, and confidence intervals
  as diagnostics around dEV.
- Leave `primaryDeltaEv` null and `runtimeCandidate` false when coverage, solver,
  HP calibration, or portfolio-weight gates fail.
- Do not install values until every cutover gate in the canonical contract has
  passed and the user explicitly approves the runtime change.

## Runtime Debugging

Treat the running game as the authority. A clean build is not proof that the
mod works in game.

Do not launch Slay the Spire 2 for validation unless the user explicitly asks
for it in the current request. The user performs interactive in-game checks.
Codex should publish the mod, inspect package contents and existing logs, then
report what the user needs to verify in game without starting the game itself.

Read the latest log before guessing:

```powershell
rg -n "CardValueOverlay|Exception|ERROR|Fatal|FileNotFound|Could not load|ModManager|BaseLib|Harmony" "$env:APPDATA\SlayTheSpire2\logs\godot.log"
```

For packaging or startup failures, inspect:

- `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\CardValueOverlay`
- `%APPDATA%\SlayTheSpire2\logs\godot.log`
- `.agents/docs/runtime-lessons.md`
- `.agents/docs/local-environment.md`

## StS2/Godot Lessons To Preserve

- Prefer plain Godot nodes such as `Label`; do not reintroduce a custom
  `CardOverlayLabel : Label` without a logged, verified need.
- Overlay rendering must be idempotent: reuse the named label, update text,
  update visibility, reposition, and avoid duplicates.
- Reward screens become stable over several frames. Use scheduled refreshes
  rather than assuming the first screen callback has all final card nodes.
- Keep patch surfaces small and screen-intent based. Add holder-level patches
  only after logs prove a specific holder lifecycle is required.

## Static Initialization Rule

Avoid fragile runtime work in static field initializers or static constructors,
especially for config loading, JSON converters, reflection, Godot APIs, or game
APIs. If a type initializer fails in the game runtime, that type can remain
unusable for the whole process and force silent fallback behavior.

Prefer explicit load methods that:

- build options locally or lazily;
- catch and log the full exception chain, including inner exceptions;
- validate config immediately after parsing;
- log the loaded mode or the exact fallback reason.

The `CardValueConfigLoader` incident is documented in
`.agents/docs/runtime-lessons.md`.

## GitHub Publishing

Hard constraint. This applies whenever the user asks to upload, publish, or
push approved work to GitHub:

- NEVER create, push, or propose a new branch - no `codex/...`, `claude/...`,
  `feature/...`, or any other branch. Work on `main` only.
- Commit on `main` and publish with `git push origin main`. Do not open pull
  requests or ask the user to create one.
- Push to the user's personal GitHub account (`BCSZSZ`) only. Never switch to a
  different account or credential.
- Override the no-branch rule only when the user explicitly asks for a branch in
  that same request; a prior branch request does not carry over to later uploads.

The correct `origin` transport is **machine-specific** - check which machine you
are on (`STS2_MOD_PROFILE` / hostname) before pushing:

- **liao-home** (hostname `LIAO`, `STS2_MOD_PROFILE=liao-home`): there is NO
  `github.com-personal` SSH alias or `id_ed25519_personal` key here. Use the
  plain HTTPS remote `https://github.com/BCSZSZ/StS2-mod.git`. Do not rewrite it
  to the SSH alias on this machine - that host does not resolve and the push
  fails.
- **liao-work**: use the `github.com-personal` SSH host alias so the personal
  identity key is used: `git@github.com-personal:BCSZSZ/StS2-mod.git`. Its
  `~/.ssh/config` entry:

  ```text
  Host github.com-personal
    HostName github.com
    User git
    IdentityFile ~/.ssh/id_ed25519_personal
    IdentitiesOnly yes
  ```

Either way the push is `git push origin main` to the personal `BCSZSZ` account;
only the remote transport (HTTPS vs SSH alias) differs by machine.

## Editing Rules

- Highest priority: when a previous implementation, plan, or direction is
  rejected or superseded, clean it out completely. Do not leave inactive code,
  commented-out blocks, compatibility shims, fallback paths, dead interfaces, or
  "temporary" adapters merely to preserve history. Prefer a clean replacement;
  if cleanup causes a bug, fix the bug directly.
- Do not revert user changes unless explicitly requested.
- Use `rg` for search.
- Use `apply_patch` for manual file edits.
- Git publishing for this repo uses only `main` and the user's personal GitHub
  account. See `## GitHub Publishing` for the hard no-new-branch constraint and
  the SSH identity to push with.
- Keep changes narrow. Do not mix animation polish, value formulas, packaging,
  and card identity changes in the same edit unless the user asks.
- For resource, localization, scene, image, or JSON changes, publish before
  asking the user to launch the game.
