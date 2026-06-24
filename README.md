# CardValueOverlay

Minimal Slay the Spire 2 mod scaffold based on
[Alchyr/ModTemplate-StS2](https://github.com/Alchyr/ModTemplate-StS2).

This repository currently contains only the base skeleton for a future card value
overlay mod:

- `CardValueOverlay.csproj` Godot .NET mod project.
- `CardValueOverlay.json` mod manifest.
- `CardValueOverlayCode/MainFile.cs` empty mod initializer.
- `CardValueOverlay/` asset folder with the template mod icon.
- `ROADMAP.md` project roadmap, implementation path, and working method.

## Local Setup

The local game install verified for this machine is:

```text
C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2
```

The template's `Sts2PathDiscovery.props` can discover this default Steam path on
Windows. If discovery fails, copy `Directory.Build.props.example` to
`Directory.Build.props` and set local paths there. `Directory.Build.props` is
ignored by Git because it is machine-specific.

Current local blocker: this machine has the .NET runtime, but no .NET SDK, so
`dotnet build` cannot run until the required SDK is installed.
