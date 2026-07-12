param(
    [string]$Version = "v0.1.0",
    [string]$PublishedFileId = "3762573646",
    [ValidateSet("0", "1", "2", "3")]
    [string]$Visibility = "2",
    [string]$ChangeNote,
    [string]$SteamCmdPath,
    [string]$SteamAccount = "BC_SZSZ",
    [switch]$PackageOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repoRoot "dist"
$stagingRoot = Join-Path $distRoot "workshop-staging"
$stagingModsRoot = Join-Path $stagingRoot "mods"
$stagedModFolder = Join-Path $stagingModsRoot "CardValueOverlay"
$packageScript = Join-Path $PSScriptRoot "package-workshop.ps1"
$vdfPath = Join-Path $distRoot "workshop\CardValueOverlay\workshop_item.local.vdf"

$resolvedRepoRoot = [IO.Path]::GetFullPath($repoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedStagingRoot = [IO.Path]::GetFullPath($stagingRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $resolvedStagingRoot.StartsWith(
    $resolvedRepoRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a staging directory outside the repository: $resolvedStagingRoot"
}

$profileName = [Environment]::GetEnvironmentVariable("STS2_MOD_PROFILE", "User")
$profileJson = if ($profileName) {
    [Environment]::GetEnvironmentVariable($profileName, "User")
} else {
    $null
}
if (-not $profileJson) {
    throw "Configure STS2_MOD_PROFILE before publishing the Workshop item."
}
$profile = $profileJson | ConvertFrom-Json
$localModFolder = Join-Path $profile.modsPath "CardValueOverlay"
if (Test-Path -LiteralPath $localModFolder) {
    throw "Local duplicate exists at $localModFolder. Remove it before publishing or testing the Workshop item."
}

$dotnet = if ($env:LIAO_DOTNET) {
    $env:LIAO_DOTNET
} elseif ($profile.dotnetPath) {
    $profile.dotnetPath
} else {
    "dotnet"
}
if ([string]::IsNullOrWhiteSpace($ChangeNote)) {
    $ChangeNote = "$Version update."
}

if (-not $PackageOnly -and [string]::IsNullOrWhiteSpace($SteamCmdPath)) {
    $SteamCmdPath = if ($env:STEAMCMD_PATH) {
        $env:STEAMCMD_PATH
    } else {
        [Environment]::GetEnvironmentVariable("STEAMCMD_PATH", "User")
    }
}
if (-not $PackageOnly -and
    ([string]::IsNullOrWhiteSpace($SteamCmdPath) -or
     -not (Test-Path -LiteralPath $SteamCmdPath -PathType Leaf))) {
    throw "Set STEAMCMD_PATH or pass -SteamCmdPath with the full path to steamcmd.exe."
}

try {
    if (Test-Path -LiteralPath $resolvedStagingRoot) {
        Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stagingModsRoot -Force | Out-Null

    $stagingModsPath = [IO.Path]::GetFullPath($stagingModsRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    & $dotnet publish `
        (Join-Path $repoRoot "CardValueOverlay.csproj") `
        -v minimal `
        "-p:DeployToMods=true" `
        "-p:ModsPath=$stagingModsPath"
    if ($LASTEXITCODE -ne 0) {
        throw "Staged Workshop publish failed with exit code $LASTEXITCODE."
    }

    & $packageScript `
        -Version $Version `
        -PublishedFileId $PublishedFileId `
        -Visibility $Visibility `
        -ModFolder $stagedModFolder `
        -ChangeNote $ChangeNote
    if ($LASTEXITCODE -ne 0) {
        throw "Workshop packaging failed with exit code $LASTEXITCODE."
    }

    if ($PackageOnly) {
        Write-Output "Package-only mode complete. Steam Workshop was not updated."
        return
    }

    $steamOutput = & $SteamCmdPath `
        +login $SteamAccount `
        +workshop_build_item $vdfPath `
        +quit 2>&1 | Tee-Object -Variable steamLog
    if ($LASTEXITCODE -ne 0) {
        throw "SteamCMD failed with exit code $LASTEXITCODE."
    }
    $steamText = ($steamLog | Out-String)
    if ($steamText -notmatch 'Committing update\.\.\.Success') {
        throw "SteamCMD exited without confirming a successful Workshop commit."
    }

    Write-Output "Workshop item $PublishedFileId updated successfully."
} finally {
    if (Test-Path -LiteralPath $resolvedStagingRoot) {
        Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force
    }
}
