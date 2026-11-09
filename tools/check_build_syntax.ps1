param([Parameter(Mandatory = $true)][string]$ScriptPath)
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 treats UTF-8 without BOM as the legacy system codepage.
# A Russian string inside a single-quoted expression can then break parsing,
# producing a long cascade of misleading syntax errors.
try {
    $scriptBytes = [System.IO.File]::ReadAllBytes($ScriptPath)
} catch {
    Write-Host "[FAIL] Cannot read PowerShell build script: $ScriptPath" -ForegroundColor Red
    exit 1
}
if ($scriptBytes.Length -lt 3 -or
    $scriptBytes[0] -ne 0xEF -or
    $scriptBytes[1] -ne 0xBB -or
    $scriptBytes[2] -ne 0xBF) {
    Write-Host '[FAIL] PowerShell build script encoding error: expected UTF-8 with BOM (EF BB BF).' -ForegroundColor Red
    Write-Host ('Save ' + $ScriptPath + ' as UTF-8 with BOM for Windows PowerShell 5.1.') -ForegroundColor Yellow
    exit 1
}
$tokens = $null
$parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
    $ScriptPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    foreach ($parseError in $parseErrors) {
        Write-Host ("[FAIL] PowerShell syntax error in {0} at line {1}: {2}" -f
            [System.IO.Path]::GetFileName($ScriptPath), $parseError.Extent.StartLineNumber, $parseError.Message) -ForegroundColor Red
    }
    exit 1
}
Write-Host ('[PASS] PowerShell syntax: ' + [System.IO.Path]::GetFileName($ScriptPath)) -ForegroundColor Green
exit 0
