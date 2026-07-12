param(
    [string]$Version = "v0.1.0",
    [string]$PublishedFileId = "3762573646",
    [ValidateSet("0", "1", "2", "3")]
    [string]$Visibility = "2",
    [Parameter(Mandatory = $true)]
    [string]$ModFolder,
    [string]$ChangeNote
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot "workshop\CardValueOverlay"
$distRoot = Join-Path $repoRoot "dist"
$workshopRoot = Join-Path $distRoot "workshop\CardValueOverlay"
$contentFolder = Join-Path $workshopRoot "content"
$descriptionPath = Join-Path $workshopRoot "workshop_description.md"
$bilingualDescriptionPath = Join-Path $workshopRoot "workshop_description.bilingual.bbcode.txt"
$releaseNotesName = "release_notes_$Version.md"
$releaseNotesPath = Join-Path $workshopRoot $releaseNotesName
$descriptionLanguages = @("english", "schinese")
$previewPath = Join-Path $workshopRoot "preview.png"
$vdfPath = Join-Path $workshopRoot "workshop_item.local.vdf"
$zipPath = Join-Path $workshopRoot "CardValueOverlay-$Version.zip"

if ($PublishedFileId -notmatch '^\d+$') {
    throw "PublishedFileId must contain digits only: $PublishedFileId"
}
if ([string]::IsNullOrWhiteSpace($ChangeNote)) {
    $ChangeNote = "$Version update."
}
if ($ChangeNote.Contains('"') -or $ChangeNote.Contains("`r") -or $ChangeNote.Contains("`n")) {
    throw "ChangeNote must be one line and contain no double quotes."
}

$resolvedDistRoot = [IO.Path]::GetFullPath($distRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedWorkshopRoot = [IO.Path]::GetFullPath($workshopRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$resolvedModFolder = [IO.Path]::GetFullPath($ModFolder).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (-not $resolvedWorkshopRoot.StartsWith(
    $resolvedDistRoot + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to generate Workshop output outside dist: $resolvedWorkshopRoot"
}

$profileName = [Environment]::GetEnvironmentVariable("STS2_MOD_PROFILE", "User")
$profileJson = if ($profileName) {
    [Environment]::GetEnvironmentVariable($profileName, "User")
} else {
    $null
}
if ($profileJson) {
    $profile = $profileJson | ConvertFrom-Json
    $localModFolder = [IO.Path]::GetFullPath(
        (Join-Path $profile.modsPath "CardValueOverlay")).TrimEnd([IO.Path]::DirectorySeparatorChar)
    if ($resolvedModFolder.Equals($localModFolder, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to package from the game's local mods directory. Use publish-workshop.ps1."
    }
}

$expectedFiles = @(
    "CardValueOverlay.dll",
    "CardValueOverlay.json",
    "CardValueOverlay.pck",
    "CardValueOverlay.pdb"
)
$sourceFiles = @(
    "workshop_description.md",
    "workshop_description.bilingual.bbcode.txt",
    $releaseNotesName,
    "preview.png"
)

foreach ($name in $expectedFiles) {
    $source = Join-Path $resolvedModFolder $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing packaged mod file: $source"
    }
}
foreach ($name in $sourceFiles) {
    $source = Join-Path $sourceRoot $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing Workshop source file: $source"
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $resolvedModFolder "CardValueOverlay.json") -Raw |
    ConvertFrom-Json
if ($manifest.version -ne $Version) {
    throw "Manifest version is '$($manifest.version)', expected '$Version'."
}

if (Test-Path -LiteralPath $resolvedWorkshopRoot) {
    Remove-Item -LiteralPath $resolvedWorkshopRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $contentFolder -Force | Out-Null

foreach ($name in $sourceFiles) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination (Join-Path $workshopRoot $name) -Force
}
foreach ($name in $expectedFiles) {
    Copy-Item -LiteralPath (Join-Path $resolvedModFolder $name) -Destination (Join-Path $contentFolder $name) -Force
}

Compress-Archive -Path (Join-Path $contentFolder "*") -DestinationPath $zipPath -CompressionLevel Optimal

function ConvertTo-VdfString([string]$Value) {
    return $Value.Replace("\", "\\").Replace('"', '\"').Replace("`r", "").Replace("`n", "\n")
}

$descriptionSections = @(
    [regex]::Split(
        (Get-Content -LiteralPath $descriptionPath -Raw).Trim(),
        '(?m)^\s*---\s*$') |
        ForEach-Object { $_.Trim() }
)
if ($descriptionSections.Count -ne $descriptionLanguages.Count) {
    throw "Expected English and Simplified Chinese sections separated by '---' in $descriptionPath."
}

$localizedDescriptionPaths = for ($index = 0; $index -lt $descriptionLanguages.Count; $index++) {
    $language = $descriptionLanguages[$index]
    $section = $descriptionSections[$index]
    $byteCount = [Text.Encoding]::UTF8.GetByteCount($section)
    if ($byteCount -gt 8000) {
        throw "Workshop $language description is $byteCount UTF-8 bytes; Steam allows at most 8000."
    }

    $path = Join-Path $workshopRoot "workshop_description.$language.md"
    Set-Content -LiteralPath $path -Value $section -Encoding utf8
    $path
}

# SteamCMD uses a legacy KeyValues parser. Keep the upload description on one
# line with no embedded quotes, and let BBCode provide visual structure.
$description = (Get-Content -LiteralPath $bilingualDescriptionPath -Raw).Trim()
$descriptionByteCount = [Text.Encoding]::UTF8.GetByteCount($description)
if ($descriptionByteCount -gt 8000) {
    throw "Bilingual Workshop description is $descriptionByteCount UTF-8 bytes; Steam allows at most 8000."
}
if ($description.Contains('"') -or $description.Contains("`r") -or $description.Contains("`n")) {
    throw "Bilingual Workshop description must be one line and contain no double quotes."
}

$resolvedContentFolder = [IO.Path]::GetFullPath($contentFolder).TrimEnd([IO.Path]::DirectorySeparatorChar)
$contentVdf = ConvertTo-VdfString $resolvedContentFolder
$previewVdf = ConvertTo-VdfString ([IO.Path]::GetFullPath($previewPath))
$vdf = @"
"workshopitem"
{
    "appid"                "2868840"
    "publishedfileid"      "$PublishedFileId"
    "contentfolder"        "$contentVdf"
    "previewfile"          "$previewVdf"
    "visibility"           "$Visibility"
    "title"                "Card Value Overlay"
    "description"          "$description"
    "changenote"           "$ChangeNote"
}
"@
Set-Content -LiteralPath $vdfPath -Value $vdf -Encoding utf8

$checksumFiles = @(
    $expectedFiles | ForEach-Object { Join-Path $contentFolder $_ }
) + @(
    $previewPath,
    $zipPath,
    $descriptionPath,
    $bilingualDescriptionPath,
    $releaseNotesPath
) + $localizedDescriptionPaths
$checksums = foreach ($path in $checksumFiles) {
    $relative = [IO.Path]::GetRelativePath($workshopRoot, $path).Replace("\", "/")
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $relative"
}
Set-Content -LiteralPath (Join-Path $workshopRoot "SHA256SUMS") -Value $checksums -Encoding ascii

Write-Output "Workshop package ready: $contentFolder"
Write-Output "Shareable zip: $zipPath"
Write-Output "SteamCMD VDF: $vdfPath"
Write-Output "Localized descriptions: $($localizedDescriptionPaths -join ', ')"
Write-Output "Bilingual upload description: $descriptionByteCount UTF-8 bytes"
Write-Output "Visibility $Visibility (0 public, 1 friends-only, 2 private, 3 unlisted)"
