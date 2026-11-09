# Verify all repository-bundled specialist fonts without network access.
# This preflight is intentionally independent from dotnet and MSVC.
param(
    [string]$FontDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'fonts')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    $fontDir = (Resolve-Path -LiteralPath $FontDirectory -ErrorAction Stop).Path
    $expectedFonts = @(
        'AtkinsonHyperlegibleMono-Variable.ttf',
        'AtkinsonHyperlegibleNext-Variable.ttf',
        'JetBrainsMono-Variable.ttf',
        'Recursive-Variable.ttf',
        'IBMPlexMono-Variable.ttf',
        'SourceCodePro-Variable.ttf',
        'GeistMono-Variable.ttf',
        'CascadiaMono-Variable.ttf'
    )
    $expectedLicenses = @(
        'Atkinson-OFL.txt', 'AtkinsonNext-OFL.txt', 'JetBrains-OFL.txt',
        'Recursive-OFL.txt', 'IBM-Plex-OFL.txt', 'Source-Code-Pro-OFL.txt',
        'Geist-Mono-OFL.txt', 'Cascadia-Mono-OFL.txt'
    )
    $fontList = @(Get-Content -LiteralPath (Join-Path $fontDir 'PORTABLE_FONT_FILES.txt') -Encoding ASCII |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $licenseList = @(Get-Content -LiteralPath (Join-Path $fontDir 'PORTABLE_FONT_LICENSES.txt') -Encoding ASCII |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if (($fontList -join '|') -cne ($expectedFonts -join '|')) {
        throw 'The font manifest must list exactly the eight approved variable TTFs in their canonical order.'
    }
    if (($licenseList -join '|') -cne ($expectedLicenses -join '|')) {
        throw 'The font license manifest does not match the eight bundled font families.'
    }
    $actualFonts = @(Get-ChildItem -LiteralPath $fontDir -File |
        Where-Object { $_.Extension -in @('.ttf', '.otf') } | ForEach-Object { $_.Name })
    if ($actualFonts.Count -ne 8) { throw "Expected exactly 8 bundled TTFs; found $($actualFonts.Count)." }
    foreach ($name in $actualFonts) {
        if ($expectedFonts -cnotcontains $name) { throw "Unlisted or unexpected font: $name" }
    }
    $shaFile = Join-Path $fontDir 'FONT_SHA256.txt'
    $hashLines = @(Get-Content -LiteralPath $shaFile -Encoding ASCII | Where-Object { $_.Trim().Length -gt 0 })
    if ($hashLines.Count -ne 8) { throw 'FONT_SHA256.txt must contain eight entries.' }
    for ($index = 0; $index -lt $expectedFonts.Count; $index++) {
        $name = $expectedFonts[$index]
        $filePath = Join-Path $fontDir $name
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) { throw "Missing bundled font: $name" }
        $match = [regex]::Match($hashLines[$index], '^([0-9a-f]{64})  ([A-Za-z0-9_-]+[.]ttf)$')
        if (-not $match.Success -or $match.Groups[2].Value -cne $name) {
            throw "Malformed or reordered font checksum entry: $name"
        }
        $bytes = [System.IO.File]::ReadAllBytes($filePath)
        if ($bytes.Length -lt 20000 -or $bytes.Length -gt 10000000) {
            throw "Unreasonable TTF size: $name ($($bytes.Length) bytes)"
        }
        if ($bytes[0] -ne 0 -or $bytes[1] -ne 1 -or $bytes[2] -ne 0 -or $bytes[3] -ne 0) {
            throw "Unexpected TrueType signature in $name"
        }
        $tableCount = [int]$bytes[4] * 256 + [int]$bytes[5]
        if ($tableCount -lt 1 -or $tableCount -gt 100 -or 12 + $tableCount * 16 -gt $bytes.Length) {
            throw "Invalid table directory in $name"
        }
        $hasFvar = $false
        for ($offset = 12; $offset -lt (12 + $tableCount * 16); $offset += 16) {
            if ([Text.Encoding]::ASCII.GetString($bytes, $offset, 4) -ceq 'fvar') {
                $hasFvar = $true
                break
            }
        }
        if (-not $hasFvar) { throw "Bundled font is not variable: $name" }
        $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -cne $match.Groups[1].Value) { throw "SHA-256 mismatch: $name" }
        Write-Host "[PASS] Bundled font $name" -ForegroundColor Green
    }
    $licenseDir = Join-Path $fontDir 'licenses'
    $actualLicenses = @(Get-ChildItem -LiteralPath $licenseDir -File | ForEach-Object { $_.Name })
    if ($actualLicenses.Count -ne 8) { throw 'Exactly eight font licenses must be bundled.' }
    foreach ($name in $expectedLicenses) {
        $licensePath = Join-Path $licenseDir $name
        if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) { throw "Missing OFL license: $name" }
        $content = [System.IO.File]::ReadAllText($licensePath)
        if ($content.IndexOf('SIL OPEN FONT LICENSE', [StringComparison]::OrdinalIgnoreCase) -lt 0 -or
            $content.IndexOf('PERMISSION & CONDITIONS', [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "Incomplete or incorrect OFL license: $name"
        }
        Write-Host "[PASS] OFL license $name" -ForegroundColor Green
    }
    foreach ($name in $actualLicenses) {
        if ($expectedLicenses -cnotcontains $name) { throw "Unexpected font license: $name" }
    }
    Write-Host '[PASS] All eight bundled fonts and licenses verified offline.' -ForegroundColor Green
} catch {
    [Console]::Error.WriteLine('[FAIL] Bundled font preflight: ' + $_.Exception.Message)
    exit 1
}
exit 0
