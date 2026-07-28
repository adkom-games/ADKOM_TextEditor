# Release gate: verifies nothing in the package can leak into user builds.
#   1. Every .cs file's first non-blank line is "#if UNITY_EDITOR" and its
#      last non-blank line is "#endif" (belt) — so even a file that ends up
#      outside an Editor assembly compiles to nothing in a player build.
#   2. Every .asmdef declares includePlatforms exactly ["Editor"] (braces).
# Exits 0 when clean, 1 with a list of offending files otherwise.
# Run from anywhere: powershell -File Tools/check-editor-guards.ps1

$ErrorActionPreference = 'Stop'
$pkg = Join-Path (Split-Path $PSScriptRoot -Parent) 'Packages\com.adkom.text-editor'
if (-not (Test-Path $pkg)) { Write-Error "Package folder not found: $pkg"; exit 1 }

$failures = @()

foreach ($f in Get-ChildItem $pkg -Recurse -Filter *.cs -File) {
    # Samples~ is invisible to Unity's asset pipeline (the ~ suffix), so it
    # can never compile into a build — and sample ADDONS must NOT be guarded:
    # ATE's Roslyn addon compiler defines no UNITY_EDITOR symbol, so a guard
    # would compile them to nothing.
    if ($f.FullName -match '[\\/]Samples~[\\/]') { continue } # CI runs on Linux: match both separators
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

if ($failures.Count -gt 0) {
    Write-Host "EDITOR-GUARD CHECK FAILED ($($failures.Count) issue(s)):" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Editor-guard check passed: all .cs files guarded, all asmdefs Editor-only."
exit 0
