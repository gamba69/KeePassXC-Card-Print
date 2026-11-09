# Repository privacy and secret scanning
# Windows PowerShell 5.1 compatible. Runs before any restore, compilation or packaging.
param([string]$Root = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

try {
    $rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
    $skipDirectories = @('.git', '.vs', '.idea', 'bin', 'obj', 'publish', 'dist',
                         'data', 'out', 'backups', 'templates', 'print-tracking',
                         'logs', 'screenshots', 'captures', 'debug-dumps')
    $blockedExact = @('ui-shell.json', 'vault-state.json', 'appearance.json',
                      'appearance-template-source.json', 'mapping.json', 'domains.json',
                      'storage-mode.txt', '.env', 'id_rsa', 'id_ed25519', 'known_hosts',
                      'credentials', 'credentials.json', 'secrets.json', 'secrets.yml',
                      'secrets.yaml', 'desktop.ini', 'thumbs.db')
    $blockedExtension = '(?i)\.(?:kdbx?|keyx?|keyfile|pem|p12|pfx|ppk|asc|gpg|age|env|sqlite3?|db3?|dmp|mdmp|log|bak|backup|tmp|pdf|zip|7z|rar)$'
    $textExtensions = @('.cs', '.csproj', '.axaml', '.cpp', '.h', '.hpp', '.props',
                        '.targets', '.ps1', '.bat', '.md', '.txt', '.yml', '.yaml',
                        '.json', '.xml', '.config', '.toml', '.sh', '.gitattributes',
                        '.gitignore')
    $findings = New-Object 'System.Collections.Generic.List[string]'
    $patterns = @(
        @{ Name = 'private-key block'; Expression = '-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP )?PRIVATE KEY-----' },
        @{ Name = 'GitHub token'; Expression = '(?:gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{40,})' },
        @{ Name = 'AWS access key'; Expression = '(?:AKIA|ASIA)[0-9A-Z]{16}' },
        @{ Name = 'Google API key'; Expression = 'AIza[0-9A-Za-z_-]{25,}' },
        @{ Name = 'OpenAI secret'; Expression = 'sk-(?:proj-)?[A-Za-z0-9_-]{20,}' },
        @{ Name = 'Slack token'; Expression = 'xox[baprs]-[A-Za-z0-9-]{12,}' },
        @{ Name = 'JWT'; Expression = 'eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}' },
        @{ Name = 'URL with embedded credentials'; Expression = '(?i)\b(?:https?|ftp|webdav)://[^\s"''<>:@/]+:[^\s"''<>@/]+@' },
        @{ Name = 'personal Windows path'; Expression = '(?i)\b[A-Z]:[\\/](?:Users|Documents and Settings)[\\/][^\\/\s"'']+' },
        @{ Name = 'personal Unix path'; Expression = '(?i)/(?:Users|home)/[A-Za-z0-9_.-]+/' },
        @{ Name = 'literal credential'; Expression = '(?i)\b(?:password|passphrase|masterpassword|clientsecret|apikey|refreshtoken|accesstoken)\s*[:=]\s*["'']([^"''\r\n]{6,})["'']' },
        @{ Name = 'email address'; Expression = '(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b' }
    )
    $count = 0
    $queue = New-Object 'System.Collections.Generic.Queue[string]'
    $queue.Enqueue($rootPath)
    while ($queue.Count -gt 0) {
        $folder = $queue.Dequeue()
        foreach ($entry in [System.IO.Directory]::EnumerateFileSystemEntries($folder)) {
            $name = [System.IO.Path]::GetFileName($entry)
            $attributes = [System.IO.File]::GetAttributes($entry)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                $findings.Add('Reparse-point entry (not allowed): ' + $name)
                continue
            }
            if (($attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
                if ($skipDirectories -icontains $name) { continue }
                $queue.Enqueue($entry)
                continue
            }
            $relative = $entry.Substring($rootPath.Length).TrimStart([char[]]@('\', '/'))
            $count++
            $blockedByName = $blockedExact -icontains $name
            $blockedByExtension = $name -match $blockedExtension
            $blockedAsEnvironment = $name -match '(?i)^\.env(?:\..+)?$'
            $blockedAsScreenshot = $name -match '(?i)(?:^|[-_.])(?:screenshot|screen[-_]?capture|printscreen)(?:[-_.0-9]|$)'
            if ($blockedByName -or $blockedByExtension -or $blockedAsEnvironment -or $blockedAsScreenshot) {
                $findings.Add('Sensitive or generated filename: ' + $relative)
                continue
            }
            if ($textExtensions -notcontains ([System.IO.Path]::GetExtension($entry))) {
                continue
            }
            $contents = [System.IO.File]::ReadAllText($entry)
            foreach ($pattern in $patterns) {
                $foundMatches = [regex]::Matches($contents, $pattern.Expression)
                foreach ($detectedMatch in $foundMatches) {
                    # Do not name this variable $match: PowerShell treats it as the
                    # case-insensitive automatic $Matches variable used by -match.
                    $syntheticCredential = $false
                    if ($pattern.Name -eq 'literal credential') {
                        $syntheticCredential = $detectedMatch.Groups[1].Value -match '(?i)^(?:TEST|DUMMY|SYNTHETIC|EXAMPLE)[_-]'
                    }
                    if ($syntheticCredential) { continue }
                    # Reserved public documentation domains are intentionally synthetic.
                    if ($pattern.Name -eq 'email address') {
                        $reservedDocumentationEmail = $detectedMatch.Value -match '(?i)@(?:[A-Za-z0-9-]+\.)*(?:invalid|test|example)$'
                        if ($reservedDocumentationEmail) { continue }
                    }
                    $before = $contents.Substring(0, $detectedMatch.Index)
                    $line = ($before.ToCharArray() | Where-Object { $_ -eq "`n" } | Measure-Object).Count + 1
                    $findings.Add($relative + ':' + $line + ' — ' + $pattern.Name)
                }
            }
        }
    }
    # Git's index is authoritative for what can be published. Even force-added
    # ignored paths must be blocked when Git is available in a checkout.
    if (Test-Path -LiteralPath (Join-Path $rootPath '.git')) {
        $gitTool = Get-Command git -ErrorAction SilentlyContinue
        if ($null -ne $gitTool) {
            $trackedFiles = @(& git -C $rootPath -c core.quotePath=false ls-files --cached)
            if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Git-tracked source paths.' }
            foreach ($trackedFile in $trackedFiles) {
                $segments = @($trackedFile -split '/')
                $baseName = [System.IO.Path]::GetFileName($trackedFile)
                $privateDirectoryTracked = @($segments | Where-Object { $skipDirectories -icontains $_ }).Count -gt 0
                $privateNameTracked = $blockedExact -icontains $baseName
                $privateExtensionTracked = $baseName -match $blockedExtension
                $environmentTracked = $baseName -match '(?i)^\.env(?:\..+)?$'
                $screenshotTracked = $baseName -match '(?i)(?:^|[-_.])(?:screenshot|screen[-_]?capture|printscreen)(?:[-_.0-9]|$)'
                if ($privateDirectoryTracked -or $privateNameTracked -or $privateExtensionTracked -or $environmentTracked -or $screenshotTracked) {
                    $findings.Add('Git tracks a private/generated path: ' + $trackedFile)
                }
            }
        }
    }
    if ($findings.Count -gt 0) {
        foreach ($finding in $findings) { Write-Host ('[FAIL] PRIVACY ' + $finding) -ForegroundColor Red }
        Write-Host '[FAIL] Privacy guard rejected repository contents. It never prints matched values.' -ForegroundColor Red
        exit 1
    }
    Write-Host ('[PASS] Repository privacy: ' + $count + ' source files checked; no blocked files or high-confidence secrets.') -ForegroundColor Green
    exit 0
} catch {
    Write-Host ('[FAIL] Repository privacy check could not complete: ' + $_.Exception.GetType().Name) -ForegroundColor Red
    exit 1
}
