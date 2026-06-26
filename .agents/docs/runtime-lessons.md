# CardValueOverlay Local Knowledge

This file records local lessons learned while building and debugging the
Slay the Spire 2 `CardValueOverlay` mod. It is intentionally more practical
than architectural: future changes should consult it before touching runtime
patches, Godot UI nodes, packaging, or game-log diagnosis.

## 2026-06-25 Config Loader Static Initialization Retrospective

### Symptom

The game showed the fixed Simplified Chinese overlay text `牌值` on every
overlay card even though the repository config had:

```json
"displayMode": "cardName"
```

The latest `godot.log` contained:

```text
[WARN] [CardValueOverlay] Failed to load CardValueOverlay config:
The type initializer for 'CardValueOverlay.Core.Configuration.CardValueConfigLoader' threw an exception.
```

Because config loading failed, runtime fell back to `CardValueConfig.CreateDefault()`.
The default overlay mode is `FixedText`, and the Simplified Chinese localization
for the fixed text is `牌值`.

### Root Cause

`CardValueConfigLoader` had a static `JsonSerializerOptions` field initialized
at type-load time. In the game runtime, that static initializer failed before
the loader could parse `card_values.json`. The catch block only logged the outer
exception message, so the real inner exception was hidden.

This failure mode is especially dangerous in a mod: a type initializer exception
can poison the type for the rest of the process. Every later attempt to use the
type can fail immediately, making fallback behavior look like a normal config
choice.

### Fix

- Removed the static `JsonSerializerOptions` field.
- Created JSON options inside explicit loader methods.
- Replaced `JsonStringEnumConverter` with a small
  `OverlayDisplayModeJsonConverter`.
- Added full exception-chain logging in `RuntimeConfigProvider`.
- Validated config immediately after parsing.
- Logged the successfully loaded display mode.

Expected success log after launch:

```text
Loaded CardValueOverlay config. displayMode=CardName.
```

### Future Rule

Avoid fragile work in static field initializers or static constructors for
runtime mod code. This includes:

- JSON converter setup
- reflection
- Godot API access
- game API access
- file/resource access
- config loading

Prefer explicit load methods with full logging and validation. When seeing
`The type initializer for ... threw an exception`, inspect static fields and
static constructors before changing unrelated runtime behavior.

## 2026-06-24 Overlay Debugging Retrospective

### Final User-Visible Result

The overlay now appears above cards in the two intended contexts:

- enlarged card inspection
- card reward selection

The remaining animation polish is known and can be deferred. The important
functional bug from this round was fixed: reward screens no longer need the
user to open deck view first before every reward card receives an overlay.

### What Was Changed In The Successful Round

- Removed the custom `CardOverlayLabel : Label` Godot node subclass.
- Replaced it with plain Godot `Label` instances created by
  `CardOverlayRenderer`.
- Stopped creating overlay labels for cards outside the target contexts.
- Removed the scattered `NCardHolder` patch attempts.
- Kept the runtime patch surface focused on:
  - `NInspectCardScreen.SetCard`
  - `NInspectCardScreen.UpdateCardDisplay`
  - `NCardRewardSelectionScreen._EnterTree`
  - `NCardRewardSelectionScreen._Ready`
  - `NCardRewardSelectionScreen.RefreshOptions`
  - `NCardRewardSelectionScreen.AfterOverlayOpened`
  - `NCardRewardSelectionScreen.AfterOverlayShown`
- Added `RewardScreenOverlayRefreshScheduler`, which uses `SceneTree.CreateTimer`
  to refresh reward card overlays several times over the first second after a
  reward screen opens or refreshes.

### Root Causes Of The Repeated Failed Iterations

1. The code treated an early reward-screen patch call as if all card nodes were
   already stable.

   On first reward-screen open, only part of the screen tree was ready when the
   overlay scan ran. Opening deck view and returning gave the game enough time
   to finish creating and positioning reward card nodes, which made the overlay
   seem to "fix itself." The real issue was timing, not card count or layout
   math.

2. The implementation used a custom Godot `Label` subclass for a very small
   behavior.

   The latest `godot.log` showed repeated errors like:

   ```text
   System.ArgumentException: Value does not fall within the expected range.
   at CardValueOverlay.CardValueOverlayCode.Overlay.CardOverlayLabel.InvokeGodotClassMethod(...)
   ```

   That meant the custom `CardOverlayLabel` node was not a harmless helper. It
   was participating in Godot C# native dispatch and producing runtime errors.
   The correct move was to delete it and use a plain engine `Label`.

3. Patch scope expanded before the observed behavior was understood.

   Earlier attempts added patches to `NCardHolder.SetCard`,
   `NCardHolder.ReassignToCard`, and `NCardHolder.OnCardReassigned`. These were
   understandable experiments, but they made the system harder to reason about:
   multiple patch paths could create or update labels at different times, and it
   became unclear which path owned correctness.

4. The code mixed ownership and lifetime responsibilities.

   A label attached to a card can work, but it should be a simple child node
   owned by that card. Context detection, card scanning, and delayed screen
   refresh should stay in renderer/scheduler code. Putting context and per-frame
   behavior into a custom label blurred these responsibilities.

5. Visual symptoms were chased before reading the log as the source of truth.

   The screen looked like a positioning/card-binding problem, but the log had a
   stronger signal: thousands of errors from `CardOverlayLabel`. For StS2/Godot
   mod work, the running game log must overrule guesses from screenshots.

### Correct Implementation Pattern For This Mod

Use this pattern for overlay UI until there is a proven reason to change it:

1. Use plain Godot UI nodes where possible.

   Prefer `Label`, `Control`, and other engine-provided classes directly. Avoid
   custom Godot node subclasses unless they are necessary and game-log verified.

2. Keep render code idempotent.

   `CardOverlayRenderer.Render(card, context)` should reuse an existing label by
   name, configure it, set text/visibility, and reposition it. It must not create
   duplicates.

3. Keep context filtering outside the label.

   The renderer decides whether a card belongs to an allowed display context.
   Labels only display text.

4. Treat reward screens as eventually stable.

   Reward cards can be created/assigned/animated after the first patch point.
   A single immediate scan is not enough. Schedule short delayed refreshes on
   the Godot main thread using `SceneTree.CreateTimer`.

5. Keep patch surfaces small and named by screen intent.

   Prefer screen-level patches for screen-level overlays. Add card-holder-level
   patches only after proving with logs that a specific holder lifecycle is the
   missing stable point.

6. Publish and inspect the actual game mod folder.

   Successful `dotnet build` is not enough. After runtime changes, run
   `dotnet publish CardValueOverlay.csproj -v minimal` and confirm the local mod
   folder contains only:

   ```text
   CardValueOverlay.dll
   CardValueOverlay.json
   CardValueOverlay.pdb
   CardValueOverlay.pck
   ```

### Debugging Checklist For Future Overlay Bugs

Before changing overlay code again:

1. Reproduce the exact screen and transition order.
2. Read `%APPDATA%\SlayTheSpire2\logs\godot.log`.
3. Search for:

   ```powershell
   rg -n "CardValueOverlay|Exception|ERROR|Fatal|CardOverlay|NCardReward" "$env:APPDATA\SlayTheSpire2\logs\godot.log"
   ```

4. Check whether the latest log timestamp is newer than the last publish.
5. Verify whether the symptom is:
   - node timing
   - wrong context filtering
   - wrong label ownership
   - coordinate/animation drift
   - packaging/stale DLL
6. Prefer deleting obsolete patch paths before adding a new one.
7. Build, run tests/tools, publish, then inspect the actual game mod directory.

### Specific Lesson From The Reward-Screen Bug

The reward screen is not a static container at the first callback. In this game,
card reward UI should be treated as a short-lived animated scene that converges
over several frames. For overlays that depend on each reward card's final card
node, the robust approach is:

```text
screen event -> immediate render -> delayed render -> delayed render -> ...
```

This is why `RewardScreenOverlayRefreshScheduler` is better than trying to make
one `RefreshOptions` postfix do all the work.

### Things Not To Reintroduce Without A Strong Reason

- A custom `CardOverlayLabel : Label` subclass.
- Per-label `_Process` logic just to follow the card.
- Broad `NCardHolder` lifecycle patches as a first response.
- Global one-label state for reward screens. Reward overlays must be tied to
  each card node, not to the screen as a single center label.
- Extra runtime helper DLLs in the game mod folder.

### Current Known Follow-Up

Animation polish is still imperfect. If that becomes the next task, first
determine whether the drift is caused by:

- delayed final card transform
- card tween movement after the last scheduled refresh
- label `TopLevel` coordinate behavior
- label size/gap constants

Do not mix animation polish with card identity, value calculation, or packaging
changes in the same iteration.
