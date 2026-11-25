param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$revision = '5684c0f51c0b81133db2efbe60a408b4155a3ff5'
$base = "https://raw.githubusercontent.com/hyphenation/tex-hyphen/$revision/hyph-utf8/tex/generic/hyph-utf8/patterns/"
$files = @{
 'txt/hyph-ru.pat.txt' = 'e110fb4be87a213fd045612e4bdb8ae9e1492cb7'
 'txt/hyph-ru.hyp.txt' = '5c32449ca1c9d00930a91399fb020447c774f4c5'
 'tex/hyph-ru.tex' = '207e39bc0834f2262bf131006149eedc8f992930'
 'txt/hyph-uk.pat.txt' = 'b476200658d182398021ec6e1c586e835bc3de0d'
 'tex/hyph-uk.tex' = '68b6f101e76702fa9289bec30cc45b53c3455af6'
 'txt/hyph-en-us.pat.txt' = 'f69e835d9dacea688fdc51956ea9243b9d042e51'
 'txt/hyph-en-us.hyp.txt' = 'b87163d4425b7b5b7e2d9f170e486131988706c4'
 'tex/hyph-en-us.tex' = 'bd9b757d89074e3e11b590311ce13ba6667544aa'
}
function Get-GitBlob([string]$Path) {
 $bytes = [IO.File]::ReadAllBytes($Path)
 $header = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
 $combined = New-Object byte[] ($header.Length + $bytes.Length)
 [Array]::Copy($header, 0, $combined, 0, $header.Length)
 [Array]::Copy($bytes, 0, $combined, $header.Length, $bytes.Length)
 $sha = [Security.Cryptography.SHA1]::Create()
 try { return ([BitConverter]::ToString($sha.ComputeHash($combined))).Replace('-', '').ToLowerInvariant() }
 finally { $sha.Dispose() }
}
[IO.Directory]::CreateDirectory($Destination) | Out-Null
foreach ($relative in $files.Keys) {
 $target = Join-Path $Destination ([IO.Path]::GetFileName($relative))
 if ((Test-Path -LiteralPath $target) -and (Get-GitBlob $target) -eq $files[$relative]) { continue }
 $temp = "$target.download"
 try {
  Invoke-WebRequest -UseBasicParsing -Uri ($base + $relative) -OutFile $temp -TimeoutSec 60
  if ((Get-GitBlob $temp) -ne $files[$relative]) { throw "Git blob checksum mismatch: $relative" }
  Move-Item -LiteralPath $temp -Destination $target -Force
 } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force } }
}
