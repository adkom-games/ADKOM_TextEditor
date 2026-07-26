# Release gate: verifies the localization catalogs are complete and consistent.
#   1. All PO files (en, ja, ko, zh-hans, zh-hant) contain the SAME msgid set.
#   2. Every string passed to L10n.Tr("...") (including "a" + "b" concatenations)
#      or used as a WithSc("label", ...) menu label exists in the catalogs.
# Exits 0 when clean, 1 with details otherwise.
# Run from anywhere: powershell -File Tools/check-l10n.ps1

$ErrorActionPreference = 'Stop'
$editor = Join-Path (Split-Path $PSScriptRoot -Parent) 'Packages\com.adkom.text-editor\Editor'
$locDir = Join-Path $editor 'Localization'
if (-not (Test-Path $locDir)) { Write-Error "Localization folder not found: $locDir"; exit 1 }

function Unescape([string]$s) {
    $s -replace '\\n', "`n" -replace '\\t', "`t" -replace '\\"', '"' -replace '\\\\', '\'
}

# --- Collect msgids per catalog ---
$catalogs = @{}
foreach ($po in Get-ChildItem $locDir -Filter *.po) {
    $ids = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in [regex]::Matches((Get-Content $po.FullName -Raw -Encoding UTF8), '(?m)^msgid "(.+)"\r?$')) {
        [void]$ids.Add((Unescape $m.Groups[1].Value))
    }
    $catalogs[$po.Name] = $ids
}
if ($catalogs.Count -eq 0) { Write-Error 'No .po files found.'; exit 1 }

$failures = @()

# --- 1. Catalogs must agree with each other ---
$reference = $catalogs['en.po']
if ($null -eq $reference) { Write-Error 'en.po missing (English identity catalog is required).'; exit 1 }
foreach ($name in $catalogs.Keys) {
    if ($name -eq 'en.po') { continue }
    $missing = @($reference | Where-Object { -not $catalogs[$name].Contains($_) })
    $extra = @($catalogs[$name] | Where-Object { -not $reference.Contains($_) })
    foreach ($k in $missing) { $failures += "MISSING in ${name}: $k" }
    foreach ($k in $extra) { $failures += "EXTRA in ${name} (not in en.po): $k" }
}

# --- 2. Every localized source string must be in the catalogs ---
# L10n.Tr( "..." [+ "..."]* )  — join concatenated literal parts.
$trPattern = 'L10n\.Tr\((\s*"(?:[^"\\]|\\.)*"(?:\s*\+\s*"(?:[^"\\]|\\.)*")*\s*)\)'
$withScPattern = 'WithSc\(\s*"((?:[^"\\]|\\.)*)"'
$litPattern = '"((?:[^"\\]|\\.)*)"'
$used = New-Object System.Collections.Generic.HashSet[string]
foreach ($cs in Get-ChildItem $editor -Recurse -Filter *.cs) {
    $text = Get-Content $cs.FullName -Raw -Encoding UTF8
    foreach ($m in [regex]::Matches($text, $trPattern)) {
        $joined = -join ([regex]::Matches($m.Groups[1].Value, $litPattern) | ForEach-Object { $_.Groups[1].Value })
        [void]$used.Add((Unescape $joined))
    }
    foreach ($m in [regex]::Matches($text, $withScPattern)) {
        [void]$used.Add((Unescape $m.Groups[1].Value))
    }
}
foreach ($s in $used) {
    if ($s.Length -gt 0 -and -not $reference.Contains($s)) {
        $failures += "UNTRANSLATED (not in catalogs): $s"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "L10N CHECK FAILED ($($failures.Count) issue(s)):" -ForegroundColor Red
    $failures | Sort-Object -Unique | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "L10n check passed: $($catalogs.Count) catalogs agree; all $($used.Count) localized source strings are catalogued."
exit 0
