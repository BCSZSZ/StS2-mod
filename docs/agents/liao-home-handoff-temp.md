# liao-home Handoff Temp

Date: 2026-06-25.

This temporary note records why the local environment changed on `liao-work`,
what was changed in this repository, and what must be checked when work resumes
on `liao-home`.

## Background

Work was advanced on another computer, so `liao-work` first pulled the latest
`main`. After the pull, several path references still matched the older
`liao-home` setup, especially the Godot/MegaDot executable path.

`liao-work` also did not have the required local toolchain installed:

- Godot .NET / MegaDot compatible with project version `4.5.1`.
- .NET 9 SDK for the Godot mod project.
- .NET 8 runtime for the local `net8.0` tool and test projects.
- A DLL decompiler command-line tool.

## Done On liao-work

- Pulled `main` successfully.
- Installed Godot .NET 4.5.1 permanently under:
  `C:\Godot\4.5.1-mono`
- Installed .NET permanently under the user directory:
  `C:\Users\liaoweiran\.dotnet`
- Installed ILSpy CLI:
  `C:\Users\liaoweiran\.dotnet\tools\ilspycmd.exe`
- Created user environment variables:
  - `liao-work`
  - `liao-home`
  - `STS2_MOD_PROFILE=liao-work`
  - `GODOT_PATH`
  - `STS2_PATH`
  - `GODOT_NUGET_SOURCE`
  - `LIAO_DOTNET`
  - `DOTNET_ROOT`
- Added user `.dotnet` and `.dotnet\tools` to the user `Path`.
- Updated ignored local `Directory.Build.props` for the `liao-work` profile.
- Created ignored local `NuGet.Config` pointing at Godot's local SDK packages:
  `C:\Godot\4.5.1-mono\Godot_v4.5.1-stable_mono_win64\GodotSharp\Tools\nupkgs`
- Updated `Directory.Build.props.example` so future local props can select
  either `liao-work` or `liao-home`.
- Updated `.gitignore` so local `NuGet.Config` is not committed.
- Updated `docs/agents/local-environment.md` with the two-machine setup.
- Set `UseAppHost=false` for the local tool and test projects. This avoids
  Windows Application Control blocking generated apphost `.exe` files during
  local validation.

## Verified On liao-work

The following passed:

```powershell
& C:\Users\liaoweiran\.dotnet\dotnet.exe restore CardValueOverlay.sln -v minimal
& C:\Users\liaoweiran\.dotnet\dotnet.exe run --project CardValueOverlay.Core.Tests\CardValueOverlay.Core.Tests.csproj --no-restore
& C:\Users\liaoweiran\.dotnet\dotnet.exe run --project CardValueOverlay.Tools\CardValueOverlay.Tools.csproj --no-restore -- validate
& C:\Users\liaoweiran\.dotnet\dotnet.exe build CardValueOverlay.csproj --no-restore -v minimal
& C:\Users\liaoweiran\.dotnet\dotnet.exe publish CardValueOverlay.csproj --no-restore -v minimal
```

Publish produced the expected local mod files:

```text
C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\CardValueOverlay\
  CardValueOverlay.dll
  CardValueOverlay.json
  CardValueOverlay.pck
  CardValueOverlay.pdb
```

## Todo On liao-home

After returning to `liao-home`, pull the repo and confirm the local machine
state:

```powershell
git pull
$env:STS2_MOD_PROFILE
Test-Path C:\megadot\MegaDot_v4.5.1-stable_mono_win64.exe
Test-Path 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll'
```

If `liao-home` does not have the profile environment variables, set them once:

```powershell
[Environment]::SetEnvironmentVariable('STS2_MOD_PROFILE', 'liao-home', 'User')
[Environment]::SetEnvironmentVariable('GODOT_PATH', 'C:\megadot\MegaDot_v4.5.1-stable_mono_win64.exe', 'User')
[Environment]::SetEnvironmentVariable('STS2_PATH', 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2', 'User')
```

If `liao-home` does not already have its own ignored `Directory.Build.props`,
copy `Directory.Build.props.example` to `Directory.Build.props` and ensure it
uses the `liao-home` profile or the `liao-home` environment variables.

If `liao-home` cannot resolve `Godot.NET.Sdk/4.5.1` during restore/build, create
an ignored local `NuGet.Config` pointing to that machine's Godot
`GodotSharp\Tools\nupkgs` directory. Do not commit that local absolute path.

## Notes

- `Directory.Build.props` and `NuGet.Config` are machine-local ignored files.
- The committed profile example should not force `liao-work` paths onto
  `liao-home`.
- `UseAppHost=false` should make `dotnet run` avoid generated apphost `.exe`
  files for the local tool and test projects. If local policy still blocks a
  generated executable on any machine, run the built DLL via
  `dotnet path\to\tool.dll` instead.
