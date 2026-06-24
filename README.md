# StS2Mod

Minimal Slay the Spire 2 mod scaffold based on
[Alchyr/ModTemplate-StS2](https://github.com/Alchyr/ModTemplate-StS2).

This repository currently contains only the base skeleton:

- `StS2Mod.csproj` Godot .NET mod project.
- `StS2Mod.json` mod manifest.
- `StS2ModCode/MainFile.cs` empty mod initializer.
- `StS2Mod/` asset folder with the template mod icon.
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
