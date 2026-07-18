<#
.SYNOPSIS
Builds and packages CardValueOverlay from temporary staging, then optionally
updates the existing Steam Workshop item.

.DESCRIPTION
The ordinary local development copy may remain installed only when
-AllowLocalMod is supplied explicitly. Even then, Workshop content is built
exclusively from dist/workshop-staging and never from the local mods directory.
#>
param(
    [string]$Version = "v0.2.0",
    [string]$PublishedFileId = "3762573646",
    [ValidateSet("0", "1", "2", "3")]
    [string]$Visibility = "0",
    [string]$ChangeNote,
    [string]$SteamCmdPath,
    [string]$SteamAccount = "BC_SZSZ",
    [switch]$AllowLocalMod,
    [switch]$PackageOnly
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repoRoot "dist"
$stagingRoot = Join-Path $distRoot "workshop-staging"
$stagedModFolder = Join-Path $stagingRoot "mods\CardValueOverlay"
$stageScript = Join-Path $PSScriptRoot "build-staged-mod.ps1"
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
if ([string]::IsNullOrWhiteSpace($profile.modsPath)) {
    throw "The active STS2_MOD_PROFILE does not define modsPath."
}
$localModFolder = [IO.Path]::GetFullPath(
    (Join-Path $profile.modsPath "CardValueOverlay")).TrimEnd(
        [IO.Path]::DirectorySeparatorChar)
if (Test-Path -LiteralPath $localModFolder) {
    if (-not $AllowLocalMod) {
        throw "Local development copy exists at $localModFolder. Re-run with -AllowLocalMod only when the publishing account is not subscribed to the Workshop item."
    }

    Write-Warning (
        "Local development copy remains installed at $localModFolder. " +
        "Workshop content will be built only from $resolvedStagingRoot. " +
        "Confirm that the publishing account is not subscribed to the Workshop item."
    )
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
    & $stageScript -StagingRoot $resolvedStagingRoot

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
