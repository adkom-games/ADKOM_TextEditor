# Release gate: every SHIPPED sample addon must carry an author signature
# (author.atesig) that matches its CURRENT content, signed by the expected
# ADKOM key.
#
# Why this gate exists: a signature covers an exact content hash, so editing
# a sample without re-signing does NOT degrade to "unsigned" - users see
# "SIGNATURE INVALID - content does not match the author's signature", i.e.
# ATE's own samples look tampered with. This check catches that locally and
# in CI, where verification needs only the PUBLIC key (the private key never
# leaves the maintainer's machine).
#
# Re-sign with: ATE -> Tools -> Addons -> Signing -> Sign Shipped Samples,
# then commit the .atesig files.
#
# Exits 0 when clean, 1 with a list of problems.
# Run from anywhere: powershell -File Tools/check-sample-signatures.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$samples = Join-Path $root 'Packages\com.adkom.text-editor\Addons~'
$expectedKeyFile = Join-Path $PSScriptRoot 'adkom-signing-key.pub'

if (-not (Test-Path $samples)) { Write-Error "Samples folder not found: $samples"; exit 1 }

# The identity hash: SHA-256 over the sorted sequence of
# (relative path + NUL + content) - must match AddonSecurity.HashFiles.
function Get-AddonHash([string]$key) {
    if (Test-Path $key -PathType Container) {
        $files = Get-ChildItem $key -Recurse -Filter *.cs -File | ForEach-Object { $_.FullName }
        $rootPath = (Resolve-Path $key).Path
    } else {
        $files = @((Resolve-Path $key).Path)
        $rootPath = (Resolve-Path $key).Path
    }
    $files = @($files | Sort-Object { $_.ToLowerInvariant() })
    $sb = New-Object System.Text.StringBuilder
    foreach ($f in $files) {
        $full = (Resolve-Path $f).Path
        # Mirror AddonSecurity.HashFiles exactly: relative to the addon key,
        # which is EMPTY for a single-file addon (the file is its own root).
        if ($full.ToLowerInvariant().StartsWith($rootPath.ToLowerInvariant())) {
            $rel = $full.Substring($rootPath.Length).TrimStart('\', '/')
        } else {
            $rel = Split-Path $full -Leaf
        }
        # Normalize line endings, exactly like AddonSecurity.HashFiles: git
        # rewrites CRLF/LF per platform, and identity must survive checkout.
        $content = [System.IO.File]::ReadAllText($full).Replace("`r`n", "`n").Replace("`r", "`n")
        [void]$sb.Append($rel.Replace('\', '/')).Append([char]0).Append($content).Append([char]0)
    }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($sb.ToString()))
    } finally { $sha.Dispose() }
    ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''
}

function Get-Fingerprint([string]$publicKey) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $h = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($publicKey)) }
    finally { $sha.Dispose() }
    $parts = for ($i = 0; $i -lt 8; $i++) { $h[$i].ToString('x2') }
    $s = -join $parts
    "$($s.Substring(0,4)) $($s.Substring(4,4)) $($s.Substring(8,4)) $($s.Substring(12,4))"
}

function Test-Signature([string]$publicKey, [string]$canonical, [string]$signatureB64) {
    $dot = $publicKey.IndexOf('.')
    if ($dot -le 0) { return $false }
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    try {
        $p = New-Object System.Security.Cryptography.RSAParameters
        $p.Modulus = [Convert]::FromBase64String($publicKey.Substring(0, $dot))
        $p.Exponent = [Convert]::FromBase64String($publicKey.Substring($dot + 1))
        $rsa.ImportParameters($p)
        return $rsa.VerifyData(
            [System.Text.Encoding]::UTF8.GetBytes($canonical),
            [Convert]::FromBase64String($signatureB64),
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    } catch { return $false }
    finally { $rsa.Dispose() }
}

$expectedFp = $null
if (Test-Path $expectedKeyFile) {
    $expectedKey = (Get-Content $expectedKeyFile -Raw).Trim()
    if ($expectedKey) { $expectedFp = Get-Fingerprint $expectedKey }
}

# Every shipped sample: top-level .cs files + first-level subfolders.
$keys = @()
$keys += Get-ChildItem $samples -Filter *.cs -File | ForEach-Object { $_.FullName }
$keys += Get-ChildItem $samples -Directory |
    Where-Object { -not $_.Name.StartsWith('.') } |
    Where-Object { (Get-ChildItem $_.FullName -Recurse -Filter *.cs -File).Count -gt 0 } |
    ForEach-Object { $_.FullName }

$problems = @()
foreach ($key in $keys) {
    $name = Split-Path $key -Leaf
    if (Test-Path $key -PathType Container) {
        $sig = Join-Path $key 'author.atesig'
    } else {
        $sig = "$key.author.atesig"
    }
    if (-not (Test-Path $sig)) {
        $problems += "$name : UNSIGNED (no $(Split-Path $sig -Leaf))"
        continue
    }
    try { $env = Get-Content $sig -Raw | ConvertFrom-Json }
    catch { $problems += "$name : signature file is not valid JSON"; continue }
    if ($env.type -ne 'author') { $problems += "$name : sidecar is '$($env.type)', expected 'author'"; continue }

    $hash = Get-AddonHash $key
    if ($env.contentHash -ne $hash) {
        $problems += "$name : signature is STALE - content changed since signing (re-sign before releasing)"
        continue
    }
    $canonical = @($env.type, $env.signerName, $env.signerKey, $env.date,
                   $env.contentHash, $env.authorFingerprint, $env.statement) -join "`n"
    if (-not (Test-Signature $env.signerKey $canonical $env.signature)) {
        $problems += "$name : signature does not verify"
        continue
    }
    if ($expectedFp) {
        $fp = Get-Fingerprint $env.signerKey
        if ($fp -ne $expectedFp) {
            $problems += "$name : signed by '$($env.signerName)' ($fp), expected the ADKOM key ($expectedFp)"
        }
    }
}

if ($problems.Count -gt 0) {
    Write-Host "SAMPLE SIGNATURE CHECK FAILED ($($problems.Count) issue(s)):" -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host "Re-sign with: ATE > Tools > Addons > Signing > Sign Shipped Samples, then commit the .atesig files." -ForegroundColor Yellow
    exit 1
}
$suffix = if ($expectedFp) { " by the ADKOM key ($expectedFp)" } else { " (no pinned key file; signer identity not enforced)" }
Write-Host "Sample signature check passed: $($keys.Count) sample(s) signed and current$suffix."
exit 0
