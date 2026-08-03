# Release gate: verifies the package still satisfies the Unity Asset Store
# submission guidelines that are checkable from source.
#   1. Package-set mutation (Client.Add/Remove/Embed) appears ONLY in
#      Editor/Distribution/AtePackageInstaller.cs, which CI swaps for a
#      refusing stub on the store branch.
#   2. Reflection into Editor internals appears ONLY in the Distribution
#      folder, for the same reason.
#   3. No file path is 150 characters or longer (measured from the package
#      root, which is what a consumer unpacks).
#   4. The files the guidelines require are present (documentation, third-
#      party notices, package.json metadata).
# With -Store, the checks are tightened for a generated upm-store tree:
#   5. AteBuildFlavor.AssetStore must be true.
#   6. UnityEditor.PackageManager mutation APIs and Editor-internal
#      reflection must not appear ANYWHERE, not even in Distribution.
#   7. LICENSE.md (the MIT grant) must be gone — the store copy is governed
#      by the Asset Store EULA.
# Exits 0 when clean, 1 with details otherwise.
# Run from anywhere: pwsh -File Tools/check-store-compliance.ps1 [-Store] [-PackageRoot <path>]

param(
    [switch]$Store,
    [string]$PackageRoot
)

$ErrorActionPreference = 'Stop'

if (-not $PackageRoot) {
    $PackageRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'Packages/com.adkom.text-editor'
}
if (-not (Test-Path $PackageRoot)) { Write-Error "Package folder not found: $PackageRoot"; exit 1 }
$root = (Resolve-Path $PackageRoot).Path

$failures = @()
$sources = @(Get-ChildItem $root -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/]Samples~[\\/]' })

function Relative([string]$full) {
    $r = $full.Substring($root.Length).TrimStart('\', '/')
    return ($r -replace '\\', '/')
}

# The one file allowed to touch the project's package set / Editor internals.
$distribution = 'Editor/Distribution/'

# --- 1 + 2. Forbidden APIs outside the swappable Distribution folder ---
$mutation = 'Client\.(Add|Remove|Embed|AddAndRemove)\s*\('
$internals = @(
    'GetType\("UnityEditor',              # resolving internal Editor types by name
    'UnityEditorInternal\.',              # internal utility namespace
    'BindingFlags\.NonPublic'             # private members (see allowlist below)
)

# AteInspectorWindow reflects over the TYPES A USER INSPECTS, not over Unity's
# internals — that is the whole point of a debug inspector, so it is exempt
# from the NonPublic rule (it names no Unity type).
$nonPublicAllowed = @('Editor/AteInspectorWindow.cs')

foreach ($f in $sources) {
    $rel = Relative $f.FullName
    $text = Get-Content $f.FullName -Raw -Encoding UTF8
    $inDistribution = $rel.StartsWith($distribution)

    if ($text -match $mutation) {
        if ($Store) {
            $failures += "STORE: package-set mutation in $rel (the store build must contain none)"
        }
        elseif (-not $inDistribution) {
            $failures += "Package-set mutation outside ${distribution}: $rel"
        }
    }

    foreach ($pattern in $internals) {
        if ($text -notmatch $pattern) { continue }
        if ($pattern -eq 'BindingFlags\.NonPublic' -and $nonPublicAllowed -contains $rel) { continue }
        if ($Store) {
            $failures += "STORE: Editor-internal reflection ($pattern) in $rel"
        }
        elseif (-not $inDistribution) {
            $failures += "Editor-internal reflection ($pattern) outside ${distribution}: $rel"
        }
    }
}

# --- 3. Consumer-visible path length ---
foreach ($f in Get-ChildItem $root -Recurse -File) {
    $rel = Relative $f.FullName
    if ($rel.Length -ge 150) { $failures += "Path >= 150 chars ($($rel.Length)): $rel" }
}

# --- 4. Required files and metadata ---
foreach ($required in @('THIRD-PARTY-NOTICES.md', 'Manual.md', 'README.md', 'CHANGELOG.md', 'package.json')) {
    if (-not (Test-Path (Join-Path $root $required))) { $failures += "Missing required file: $required" }
}

$pkgPath = Join-Path $root 'package.json'
if (Test-Path $pkgPath) {
    $pkg = Get-Content $pkgPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($field in @('name', 'version', 'displayName', 'description', 'unity', 'documentationUrl')) {
        if (-not $pkg.PSObject.Properties.Name.Contains($field) -or -not $pkg.$field) {
            $failures += "package.json is missing '$field' (the store listing and Package Manager both read it)"
        }
    }
}

# --- 5-7. Store-tree-only checks ---
if ($Store) {
    $flavor = Join-Path $root 'Editor/Distribution/AteBuildFlavor.cs'
    if (-not (Test-Path $flavor)) {
        $failures += 'STORE: Editor/Distribution/AteBuildFlavor.cs is missing'
    }
    elseif ((Get-Content $flavor -Raw -Encoding UTF8) -notmatch 'AssetStore\s*=\s*true') {
        $failures += 'STORE: AteBuildFlavor.AssetStore is not true (the store override was not applied)'
    }

    if (Test-Path (Join-Path $root 'LICENSE.md')) {
        $failures += 'STORE: LICENSE.md (MIT grant) must not ship in the store build — it is governed by the Asset Store EULA'
    }
}

if ($failures.Count -gt 0) {
    Write-Host "Asset Store compliance check FAILED ($($failures.Count)):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}

$mode = if ($Store) { 'store tree' } else { 'development tree' }
Write-Host "Asset Store compliance check passed ($mode, $($sources.Count) sources)." -ForegroundColor Green
exit 0
