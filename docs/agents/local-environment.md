# Environment Todo

Current check date: 2026-06-25.

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
- Game XML API/documentation file:
  - `sts2.xml`
- Game bundled runtime target: `net9.0`.
- Local game mods folder:
  `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods`
- Existing local mod example:
  `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\OmniscientCrystalSphere`
- Steam Workshop mods folder:
  `C:\Program Files (x86)\Steam\steamapps\workshop\content\2868840`
- BaseLib workshop install:
  `C:\Program Files (x86)\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib`
- BaseLib installed files:
  - `BaseLib.dll`
  - `BaseLib.json`
  - `BaseLib.pck`
- Project manifest minimum BaseLib version: `3.3.0`.
- Installed .NET SDK:
  - `8.0.416`
  - `9.0.315`
- Installed .NET runtimes include:
  - `Microsoft.NETCore.App 9.0.12`
  - `Microsoft.NETCore.App 9.0.17`
  - `Microsoft.NETCore.App 10.0.1`
- Godot Mono executable:
  `C:\Godot\4.5.1-mono\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe`
- Godot Mono version:
  `4.5.1.stable.mono.official.f62fdbde1`
- Local machine MSBuild config:
  `Directory.Build.props`
- NuGet restore for the mod project succeeds.
- `dotnet build CardValueOverlay.csproj` succeeds.
- `dotnet publish CardValueOverlay.csproj` emits a local PCK and exits cleanly.
- Local `CardValueOverlay` output folder exists under the game mods path.
- Local `CardValueOverlay` output intentionally contains no separate
  `CardValueOverlay.Core.dll`; shared logic is compiled into the main mod DLL.

## Missing

- No hard blocker for local restore/build/publish is currently known.
- Game launch verification should confirm config loading after the latest
  `CardValueConfigLoader` static-initializer fix. Expected log line:

  ```text
  Loaded CardValueOverlay config. displayMode=CardName.
  ```

## Required Todo

1. Launch Slay the Spire 2 and confirm `Card Value Overlay` appears under
   Settings -> Mod Settings or in the latest Godot log.
2. Keep the executable path in `Directory.Build.props`:

   ```xml
   <GodotPath>C:/Godot/4.5.1-mono/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64.exe</GodotPath>
   ```

3. Restore packages when project dependencies change:

   ```powershell
   dotnet restore
   ```

4. Publish the complete local package:

   ```powershell
   dotnet publish CardValueOverlay.csproj
   ```

5. Verify this local output exists:

   ```text
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\CardValueOverlay\
     CardValueOverlay.dll
     CardValueOverlay.pdb
     CardValueOverlay.json
     CardValueOverlay.pck
   ```

## Not Required For This Milestone

- Steam Workshop publishing.
- Release packaging outside the local game folder.
- Final dynamic card scoring formulas.
- The exact formula for `turns_per_shuffle_cycle`.
