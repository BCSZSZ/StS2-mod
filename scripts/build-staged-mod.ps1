param(
    [Parameter(Mandatory = $true)]
    [string]$StagingRoot
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$distRoot = Join-Path $repoRoot "dist"
$resolvedDistRoot = [IO.Path]::GetFullPath($distRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar)
$resolvedStagingRoot = [IO.Path]::GetFullPath($StagingRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar)
if ($resolvedStagingRoot.Equals($resolvedDistRoot, [StringComparison]::OrdinalIgnoreCase) -or
    -not $resolvedStagingRoot.StartsWith(
        $resolvedDistRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a staging directory outside dist or dist itself: $resolvedStagingRoot"
}

$profileName = [Environment]::GetEnvironmentVariable("STS2_MOD_PROFILE", "User")
$profileJson = if ($profileName) {
    [Environment]::GetEnvironmentVariable($profileName, "User")
} else {
    $null
}
if (-not $profileJson) {
    throw "Configure STS2_MOD_PROFILE before building a staged mod package."
}
$profile = $profileJson | ConvertFrom-Json
$dotnet = if ($env:LIAO_DOTNET) {
    $env:LIAO_DOTNET
} elseif ($profile.dotnetPath) {
    $profile.dotnetPath
} else {
    "dotnet"
}

$stagingModsRoot = Join-Path $resolvedStagingRoot "mods"
$stagedModFolder = Join-Path $stagingModsRoot "CardValueOverlay"
$expectedFiles = @(
    "CardValueOverlay.dll",
    "CardValueOverlay.json",
    "CardValueOverlay.pck",
    "CardValueOverlay.pdb"
)

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
    throw "Staged mod publish failed with exit code $LASTEXITCODE."
}

foreach ($name in $expectedFiles) {
    $path = Join-Path $stagedModFolder $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing staged mod file: $path"
    }
}

$actualEntries = @(Get-ChildItem -LiteralPath $stagedModFolder -Force)
$unexpectedEntries = @($actualEntries | Where-Object { $_.Name -notin $expectedFiles })
if ($actualEntries.Count -ne $expectedFiles.Count -or $unexpectedEntries.Count -gt 0) {
    throw "Unexpected staged mod contents: $($actualEntries.Name -join ', ')"
}

Write-Output "Staged mod package ready: $stagedModFolder"
