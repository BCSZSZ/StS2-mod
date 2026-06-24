# Environment Todo

Current check date: 2026-06-24.

Goal: produce a complete local mod package for `CardValueOverlay` and confirm
the game can load it from the local `mods` folder.

## Already Present

- Slay the Spire 2 install:
  `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`
- Game data folder:
  `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64`
- Required game assemblies:
  - `sts2.dll`
  - `0Harmony.dll`
- Game runtime target: `net9.0`.
- Game bundled runtime: `Microsoft.NETCore.App 9.0.7`.
- BaseLib workshop install:
  `C:\Program Files (x86)\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib`
- BaseLib installed version: `v3.3.2`.
- Project manifest minimum BaseLib version: `3.3.0`.

## Missing

- .NET SDK 9.0 or newer.
- MegaDot/Godot Mono 4.5.1 executable.
- NuGet-restored project packages:
  - `Godot.NET.Sdk/4.5.1`
  - `Alchyr.Sts2.BaseLib`
  - `Alchyr.Sts2.ModAnalyzers`
  - `Krafs.Publicizer/2.3.0`
- Local game mods folder:
  `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods`

## Required Todo

1. Install .NET SDK 9.0 x64.
2. Install MegaDot 4.5.1 mono, or Godot .NET 4.5.1 if MegaDot is unavailable.
3. Put the executable at the configured path, or update `Directory.Build.props`:

   ```xml
   <GodotPath>C:/megadot/MegaDot_v4.5.1-stable_mono_win64.exe</GodotPath>
   ```

4. Create the local mods folder if publish does not create it automatically:

   ```powershell
   New-Item -ItemType Directory -Force 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods'
   ```

5. Restore packages:

   ```powershell
   dotnet restore
   ```

6. Publish the complete local package:

   ```powershell
   dotnet publish CardValueOverlay.csproj
   ```

7. Verify this local output exists:

   ```text
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\CardValueOverlay\
     CardValueOverlay.dll
     CardValueOverlay.pdb
     CardValueOverlay.json
     CardValueOverlay.pck
   ```

8. Launch Slay the Spire 2 and confirm `Card Value Overlay` appears under
   Settings -> Mod Settings.

## Not Required For This Milestone

- Steam Workshop publishing.
- Release packaging outside the local game folder.
- Real card-value overlay behavior.

Those come after the local `.dll` + `.json` + `.pck` package loads correctly.
