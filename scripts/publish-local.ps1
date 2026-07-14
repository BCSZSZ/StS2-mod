<#
.SYNOPSIS
Builds a complete CardValueOverlay package through temporary staging and deploys
the four runtime files to the active profile's ordinary local mods directory.

.DESCRIPTION
This is the normal development-iteration command. It never reads from or writes
to Steam Workshop content and never launches the game. Slay the Spire 2 must be
closed so the runtime DLL can be replaced safely.
#>
param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repoRoot "dist"
$stagingRoot = Join-Path $distRoot "local-staging"
$stageScript = Join-Path $PSScriptRoot "build-staged-mod.ps1"
$expectedFiles = @(
    "CardValueOverlay.dll",
    "CardValueOverlay.json",
    "CardValueOverlay.pck",
    "CardValueOverlay.pdb"
)
$knownStaleFiles = @(
    "CardValueOverlay.Core.dll",
    "CardValueOverlay.Core.pdb"
)

$resolvedDistRoot = [IO.Path]::GetFullPath($distRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar)
$resolvedStagingRoot = [IO.Path]::GetFullPath($stagingRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar)
if (-not $resolvedStagingRoot.StartsWith(
    $resolvedDistRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a local staging directory outside dist: $resolvedStagingRoot"
}

$profileName = [Environment]::GetEnvironmentVariable("STS2_MOD_PROFILE", "User")
$profileJson = if ($profileName) {
    [Environment]::GetEnvironmentVariable($profileName, "User")
} else {
    $null
}
if (-not $profileJson) {
    throw "Configure STS2_MOD_PROFILE before publishing the local mod."
}
$profile = $profileJson | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($profile.modsPath)) {
    throw "The active STS2_MOD_PROFILE does not define modsPath."
}

$modsRoot = [IO.Path]::GetFullPath($profile.modsPath).TrimEnd(
    [IO.Path]::DirectorySeparatorChar)
$localModFolder = [IO.Path]::GetFullPath(
    (Join-Path $modsRoot "CardValueOverlay")).TrimEnd(
        [IO.Path]::DirectorySeparatorChar)
if (-not $localModFolder.StartsWith(
    $modsRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to deploy outside the configured mods root: $localModFolder"
}

if (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue) {
    throw "Slay the Spire 2 is running. Close the game before publishing the local mod."
}

try {
    & $stageScript -StagingRoot $resolvedStagingRoot
    $stagedModFolder = Join-Path $resolvedStagingRoot "mods\CardValueOverlay"

    if (Test-Path -LiteralPath $localModFolder) {
        $existingEntries = @(Get-ChildItem -LiteralPath $localModFolder -Force)
        $unexpectedEntries = @($existingEntries | Where-Object {
            $_.Name -notin $expectedFiles -and $_.Name -notin $knownStaleFiles
        })
        if ($unexpectedEntries.Count -gt 0) {
            throw "Refusing to overwrite a local mod folder with unexpected contents: $($unexpectedEntries.Name -join ', ')"
        }
    } else {
        New-Item -ItemType Directory -Path $localModFolder -Force | Out-Null
    }

    foreach ($name in $expectedFiles) {
        Copy-Item `
            -LiteralPath (Join-Path $stagedModFolder $name) `
            -Destination (Join-Path $localModFolder $name) `
            -Force
    }
    foreach ($name in $knownStaleFiles) {
        $stalePath = Join-Path $localModFolder $name
        if (Test-Path -LiteralPath $stalePath -PathType Leaf) {
            Remove-Item -LiteralPath $stalePath -Force
        }
    }

    $actualEntries = @(Get-ChildItem -LiteralPath $localModFolder -Force)
    $unexpectedEntries = @($actualEntries | Where-Object { $_.Name -notin $expectedFiles })
    if ($actualEntries.Count -ne $expectedFiles.Count -or $unexpectedEntries.Count -gt 0) {
        throw "Unexpected local mod contents after deployment: $($actualEntries.Name -join ', ')"
    }

    foreach ($name in $expectedFiles) {
        $sourceHash = (Get-FileHash `
            -LiteralPath (Join-Path $stagedModFolder $name) `
            -Algorithm SHA256).Hash
        $targetHash = (Get-FileHash `
            -LiteralPath (Join-Path $localModFolder $name) `
            -Algorithm SHA256).Hash
        if ($sourceHash -ne $targetHash) {
            throw "Hash mismatch after local deployment: $name"
        }
    }

    Write-Output "Local mod published and verified: $localModFolder"
} finally {
    if (Test-Path -LiteralPath $resolvedStagingRoot) {
        Remove-Item -LiteralPath $resolvedStagingRoot -Recurse -Force
    }
}
