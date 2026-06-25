# Environment Todo

Current check date: 2026-06-25.

Goal: keep machine-dependent paths out of source defaults and make the
`liao-work` and `liao-home` setups explicit.

## Machine Profiles

Two user environment variables are defined as compact JSON records:

- `liao-work`: this computer.
- `liao-home`: the other computer; most older repo path references came from
  this setup.

The active profile is selected by:

```powershell
$env:STS2_MOD_PROFILE
```

On this computer it should be:

```text
liao-work
```

Because the profile variables contain hyphens, use `${env:liao-work}` in
PowerShell if reading them directly.

## liao-work Paths

- Repository root:
  `C:\code\StS2-mod`
- Slay the Spire 2 install:
  `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`
- Game data folder:
  `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64`
- Local game mods folder:
  `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods`
- Godot .NET 4.5.1 executable:
  `C:\Godot\4.5.1-mono\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe`
- .NET 9 SDK executable:
  `C:\Users\liaoweiran\.dotnet\dotnet.exe`
- .NET 8 runtime:
  `C:\Users\liaoweiran\.dotnet\shared\Microsoft.NETCore.App\8.0.28`
- ILSpy CLI:
  `C:\Users\liaoweiran\.dotnet\tools\ilspycmd.exe`

The machine-level `C:\Program Files\dotnet\dotnet.exe` currently has only a
.NET 8 runtime on `liao-work`, so use the user-level SDK path above when a
plain `dotnet` command resolves to the runtime-only install.

## liao-home Paths

- Slay the Spire 2 install:
  `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`
- Godot/MegaDot executable used by older repo-local references:
  `C:\megadot\MegaDot_v4.5.1-stable_mono_win64.exe`
- Expected .NET executable:
  `C:\Program Files\dotnet\dotnet.exe`

## Already Present On liao-work

- Slay the Spire 2 game data exists.
- Required game assemblies exist:
  - `sts2.dll`
  - `0Harmony.dll`
- Game XML API/documentation file exists:
  - `sts2.xml`
- Game bundled runtime target: `net9.0`.
- Local game mods folder exists.
- Steam Workshop mods folder exists:
  `C:\Program Files (x86)\Steam\steamapps\workshop\content\2868840`
- BaseLib workshop install exists:
  `C:\Program Files (x86)\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib`
- Project manifest minimum BaseLib version: `3.3.0`.
- User-level .NET SDK/runtime installed:
  - `9.0.315`
  - `Microsoft.NETCore.App 8.0.28`
  - `Microsoft.NETCore.App 9.0.17`
- Godot .NET installed:
  - `4.5.1.stable.mono.official.f62fdbde1`
- ILSpy CLI installed:
  - `9.1.0.7988`
- Local machine MSBuild config:
  `Directory.Build.props`
- Local machine NuGet source config:
  `NuGet.Config`
- Modeling extraction command verified:
  `dotnet run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- extract-game-data`

## Required Todo

1. Use the user-level .NET SDK on `liao-work`:

   ```powershell
   $dotnet = [Environment]::GetEnvironmentVariable('LIAO_DOTNET', 'User')
   & $dotnet --list-sdks
   ```

2. Restore packages when project dependencies change:

   ```powershell
   & $dotnet restore
   ```

   `liao-work` uses an ignored local `NuGet.Config` to point SDK resolution at:

   ```text
   C:\Godot\4.5.1-mono\Godot_v4.5.1-stable_mono_win64\GodotSharp\Tools\nupkgs
   ```

3. Publish the complete local package:

   ```powershell
   & $dotnet publish CardValueOverlay.csproj -v minimal
   ```

4. On `liao-work`, Application Control can block a generated apphost `.exe`.
   The local tool and test projects set `UseAppHost=false` so `dotnet run`
   avoids that generated executable. If local policy still blocks a generated
   executable, build and run the DLL:

   ```powershell
   & $dotnet build CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore
   & $dotnet CardValueOverlay.Tools\bin\Debug\net8.0\CardValueOverlay.Tools.dll validate
   ```

5. Verify this local output exists:

   ```text
   C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\CardValueOverlay\
     CardValueOverlay.dll
     CardValueOverlay.pdb
     CardValueOverlay.json
     CardValueOverlay.pck
   ```

6. Launch Slay the Spire 2 and confirm `Card Value Overlay` appears under
   Settings -> Mod Settings or in the latest Godot log. Expected log line:

   ```text
   Loaded CardValueOverlay config. displayMode=CardName.
   ```

## Not Required For This Milestone

- Steam Workshop publishing.
- Release packaging outside the local game folder.
- Final dynamic card scoring formulas.
- The exact formula for `turns_per_shuffle_cycle`.
