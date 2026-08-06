# Release gate: verifies nothing in the package can leak into user builds.
#   1. Every .cs file's first non-blank line is "#if UNITY_EDITOR" and its
#      last non-blank line is "#endif" (belt) — so even a file that ends up
#      outside an Editor assembly compiles to nothing in a player build.
#   2. Every .asmdef declares includePlatforms exactly ["Editor"] (braces).
# Exits 0 when clean, 1 with a list of offending files otherwise.
# Run from anywhere: powershell -File Tools/check-editor-guards.ps1
# -PackageRoot <path> checks a different tree — CI runs it a second time
# against the assembled upm-store branch, whose Distribution files come from
# Tools/store-overrides and are otherwise never seen by this gate.

param([string]$PackageRoot)

$ErrorActionPreference = 'Stop'
$pkg = if ($PackageRoot) { $PackageRoot } else { Join-Path (Split-Path $PSScriptRoot -Parent) 'Packages\com.adkom.text-editor' }
if (-not (Test-Path $pkg)) { Write-Error "Package folder not found: $pkg"; exit 1 }

$failures = @()

foreach ($f in Get-ChildItem $pkg -Recurse -Filter *.cs -File) {
    # Samples~ and Addons~ are invisible to Unity's asset pipeline (the ~
    # suffix), so they can never compile into a build — and sample ADDONS
    # must NOT be guarded: ATE's Roslyn addon compiler defines no
    # UNITY_EDITOR symbol, so a guard would compile them to nothing.
    if ($f.FullName -match '[\\/](Samples~|Addons~)[\\/]') { continue } # CI runs on Linux: match both separators
    $lines = Get-Content $f.FullName | Where-Object { $_.Trim().Length -gt 0 }
    if ($lines.Count -eq 0) { $failures += "EMPTY: $($f.FullName)"; continue }
    $first = @($lines)[0].Trim()
    $last  = @($lines)[-1].Trim()
    if (-not $first.StartsWith('#if UNITY_EDITOR')) {
        $failures += "MISSING '#if UNITY_EDITOR' at top: $($f.FullName) (first line: '$first')"
    }
    if ($last -ne '#endif') {
        $failures += "MISSING trailing '#endif': $($f.FullName) (last line: '$last')"
    }
}

foreach ($f in Get-ChildItem $pkg -Recurse -Filter *.asmdef -File) {
    $json = Get-Content $f.FullName -Raw | ConvertFrom-Json
    $platforms = @($json.includePlatforms)
    if ($platforms.Count -ne 1 -or $platforms[0] -ne 'Editor') {
        $failures += "ASMDEF not Editor-only: $($f.FullName) (includePlatforms: [$($platforms -join ', ')])"
    }
}

# --- 3. Nothing that ships in player builds UNCONDITIONALLY ---
# Resources/ and StreamingAssets/ content is included in every build merely
# by existing, and a link.xml pins assemblies against stripping. None have
# any business in an editor-only package. Folders ending in '~' are
# invisible to Unity's asset pipeline and are exempt.
foreach ($d in Get-ChildItem $pkg -Recurse -Directory |
         Where-Object { $_.Name -in @('Resources', 'StreamingAssets') -and $_.FullName -notmatch '~[\\/]' }) {
    $failures += "BUILD-BLEED FOLDER (ships in every player build): $($d.FullName)"
}
foreach ($f in Get-ChildItem $pkg -Recurse -Filter link.xml -File |
         Where-Object { $_.FullName -notmatch '~[\\/]' }) {
    $failures += "link.xml (pins assemblies into player builds): $($f.FullName)"
}

# Every TRACKED package file must have a TRACKED .meta beside it (and every
# directory implied by tracked files, its folder .meta). git ls-files, not
# the working tree: Unity generates metas locally in the dev project, which
# MASKS an uncommitted one — 1.1.1 shipped AteTooltip.cs without its meta,
# Unity ignored the file in consumers' immutable PackageCache, and every
# reference failed CS0103. Paths under '~' folders and dotfiles are exempt
# (invisible to the asset pipeline).
$tracked = git -C (Split-Path $pkg -Parent | Split-Path -Parent) ls-files -- "Packages/com.adkom.text-editor" |
    ForEach-Object { $_ -replace '^Packages/com\.adkom\.text-editor/', '' }
$trackedSet = @{}
foreach ($t in $tracked) { $trackedSet[$t] = $true }
$dirSet = @{}
foreach ($t in $tracked) {
    if ($t -match '(^|/)(\.|[^/]*~/)' ) { continue }              # ~ folders / dotfiles: exempt
    $parts = $t -split '/'
    for ($i = 1; $i -lt $parts.Count; $i++) {
        $dirSet[($parts[0..($i-1)] -join '/')] = $true
    }
    if ($t.EndsWith('.meta') -or ($parts[-1].StartsWith('.'))) { continue }
    if (-not $trackedSet.ContainsKey("$t.meta")) {
        $failures += "MISSING TRACKED META (Unity ignores the asset in consumer installs): $t"
    }
}
foreach ($d in $dirSet.Keys) {
    if ($d -match '~$' -or $d -match '~/' -or $d -match '(^|/)\.'){ continue }
    if (-not $trackedSet.ContainsKey("$d.meta")) {
        $failures += "MISSING TRACKED FOLDER META: $d"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "EDITOR-GUARD CHECK FAILED ($($failures.Count) issue(s)):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Editor-guard check passed: all .cs files guarded, all asmdefs Editor-only, no unconditional-ship content (Resources/StreamingAssets/link.xml), all tracked files carry tracked metas."
exit 0
