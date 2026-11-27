param(
    [Parameter(Mandatory = $true)][string]$ArchivePath,
    [Parameter(Mandatory = $true)][string]$ChecksumPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
try {
    $archiveFile = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path
    $checksumFile = (Resolve-Path -LiteralPath $ChecksumPath -ErrorAction Stop).Path
    $archiveName = [System.IO.Path]::GetFileName($archiveFile)
    if ($archiveName -notmatch '^KeePassXCCardPrint-[0-9]+[.][0-9]+[.][0-9]+-win-x64[.]zip$') {
        throw "Unexpected release filename: $archiveName"
    }
    $hashLine = [System.IO.File]::ReadAllText($checksumFile,
        [System.Text.Encoding]::ASCII).Trim()
    $hashPattern = '^([0-9a-f]{64})  ' + [regex]::Escape($archiveName) + '$'
    $hashMatch = [regex]::Match($hashLine, $hashPattern)
    if (-not $hashMatch.Success) {
        throw 'The SHA-256 file must contain one sha256sum-compatible line for this archive.'
    }
    $expectedHash = $hashMatch.Groups[1].Value
    $actualHash = (Get-FileHash -LiteralPath $archiveFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedHash -cne $actualHash) { throw 'SHA-256 checksum mismatch.' }
    Write-Host '[PASS] Release SHA-256 matches the ZIP.' -ForegroundColor Green

    Add-Type -AssemblyName System.IO.Compression
    $stream = [System.IO.File]::OpenRead($archiveFile)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream,
            [System.IO.Compression.ZipArchiveMode]::Read, $false)
        try {
            $names = @($archive.Entries | ForEach-Object { $_.FullName })
            foreach ($required in @('KeePassXCCardPrint.exe', 'KpcXpsBridge.dll',
                'LICENSE', 'THIRD_PARTY_NOTICES.md', 'README.md', 'KpcVideoBridge.dll', 'libde265.dll',
                'KpcVendorVideo.dll', 'libvpl.dll', 'licenses/vendor-video/oneVPL-LICENSE.txt',
                'licenses/vendor-video/oneVPL-third-party-programs.txt', 'licenses/vendor-video/AMF-LICENSE.txt',
                'licenses/vendor-video/NVIDIA-dynlink_cuda.h.txt', 'licenses/vendor-video/NVIDIA-dynlink_cuviddec.h.txt', 'licenses/vendor-video/NVIDIA-dynlink_nvcuvid.h.txt',
                'licenses/libde265/COPYING.txt', 'licenses/libde265/libde265-1.1.3.tar.gz', 'licenses/libde265/build-libde265.ps1')) {
                if ($names -cnotcontains $required) { throw "Missing packaged file: $required" }
            }
            $fontEntries = @($names | Where-Object { $_ -match '^fonts/[^/]+[.]ttf$' })
            if ($fontEntries.Count -ne 8) {
                throw "Expected 8 portable TTF fonts, found $($fontEntries.Count)."
            }
            $fontLicenses = @($names | Where-Object { $_ -match '^fonts/licenses/[^/]+$' })
            if ($fontLicenses.Count -lt 8) {
                throw "Expected portable font licenses, found $($fontLicenses.Count)."
            }
            $unique = [System.Collections.Generic.HashSet[string]]::new(
                [System.StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $archive.Entries) {
                $name = $entry.FullName
                if (-not $unique.Add($name)) { throw "Duplicate ZIP entry: $name" }
                $absolutePath = $name.StartsWith('/') -or ($name -match '^[A-Za-z]:')
                $parentTraversal = $name -match '(?:^|/)[.][.](?:/|$)'
                $backslashPath = $name.Contains('\')
                if ($absolutePath -or $parentTraversal -or $backslashPath) {
                    throw "Unsafe ZIP entry path: $name"
                }
                $privateName = $name -match '(?i)(?:^|/)(?:[.]env(?:[.]|$)|credentials|secrets|id_rsa|id_ed25519)(?:/|$)'
                $privateExtension = $name -match '(?i)[.](?:kdbx?|keyx?|keyfile|pem|p12|pfx|ppk|asc|gpg|age|sqlite3?|db3?|dmp|mdmp|bak|backup|pdf)$'
                $generatedDirectory = $name -match '(?i)(?:^|/)(?:data|bin|obj|backups|print-tracking|templates|[.]git)/'
                $debugFile = $name -match '(?i)[.](?:pdb|lib|exp|obj|ilk|log)$'
                $privateConfig = $name -match '(?i)(?:^|/)(?:ui-shell|vault-state|mapping|domains|appearance|storage-mode)[.](?:json|txt)$'
                $testArtifact = $name -match '(?i)(?:^|/)(?:layout|vault|mapping|xps|updates)-selftest[.]txt$'
                if ($privateName -or $privateExtension -or $generatedDirectory -or $debugFile -or $privateConfig -or $testArtifact) {
                    throw "Forbidden private/debug entry: $name"
                }
                # Attempt full decompression of each entry, not just listing ZIP metadata.
                $entryStream = $entry.Open()
                try { $entryStream.CopyTo([System.IO.Stream]::Null) }
                finally { $entryStream.Dispose() }
            }
            Write-Host "[PASS] Release payload: $($names.Count) entries, 8 fonts, licenses and executable." -ForegroundColor Green
            Write-Host '[PASS] Release ZIP entries verified and decompressed without error.' -ForegroundColor Green
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
} catch {
    [Console]::Error.WriteLine('[FAIL] Release package verification: ' + $_.Exception.Message)
    exit 1
}
exit 0
