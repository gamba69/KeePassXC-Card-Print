# Encoding: UTF-8 with BOM (required by Windows PowerShell 5.1 when script contains Cyrillic).
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

try {

$root = Split-Path -Parent $PSScriptRoot
# Check the checkout before restore, Native AOT compilation or release packaging.
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify_repository_privacy.ps1') -Root $root
if ($LASTEXITCODE -ne 0) { throw 'Repository privacy validation failed.' }
# All repository directories must have lowercase names. Check before any build
# so Windows' case-insensitive filesystem does not hide portability regressions.
# Generated directories and existing user configuration are excluded.
$sourceDirectoryRoots = @('.github', 'source', 'tools', 'fonts', 'docs')
$ignoredDirectoryNames = @('.git', '.vs', 'bin', 'obj', 'data', 'publish', 'dist')
foreach ($directoryName in $sourceDirectoryRoots) {
    $topDir = Join-Path $root $directoryName
    if (-not (Test-Path -LiteralPath $topDir -PathType Container)) {
        throw "MISSING SOURCE DIRECTORY: $topDir"
    }
    $directoriesToCheck = @((Get-Item -LiteralPath $topDir -Force))
    $directoriesToCheck += @(Get-ChildItem -LiteralPath $topDir -Directory -Recurse -Force |
        Where-Object {
            $relative = $_.FullName.Substring($topDir.Length).TrimStart('\', '/')
            $segments = @($relative -split '[\\/]')
            (@($segments | Where-Object { $ignoredDirectoryNames -ccontains $_ }).Count -eq 0)
        })
    foreach ($folder in $directoriesToCheck) {
        if ($folder.Name -cne $folder.Name.ToLowerInvariant()) {
            throw "DIRECTORY CASE VIOLATION: $($folder.FullName) (all source folders must be lowercase)"
        }
    }
}
Write-Host '[PASS] lowercase source-tree directory naming.' -ForegroundColor Green

$sourceRoot = Join-Path $root 'source\keepassxccardprint'
$nativeSource = Join-Path $sourceRoot 'platforms\windows\native\KpcXpsBridge.cpp'
# The native Windows bridge belongs to the Windows platform layer, not to the repository root.
if (Test-Path -LiteralPath (Join-Path $root 'native') -PathType Container) {
    throw 'NATIVE LAYER VIOLATION: Windows-specific code belongs under platforms/windows/native.'
}
if (-not (Test-Path -LiteralPath $nativeSource -PathType Leaf)) {
    throw "MISSING WINDOWS XPS BRIDGE: $nativeSource"
}
Write-Host '[PASS] native XPS bridge is isolated under platforms/windows/native.' -ForegroundColor Green
$project = Join-Path $root 'source\keepassxccardprint\KeePassXCCardPrint.csproj'
# Exactly two generated delivery locations at the repository root.
# MSBuild and MSVC intermediates remain under the ignored source/obj tree.
$publish = Join-Path $root 'publish'
$dist = Join-Path $root 'dist'
# Prevent regressions to per-build folders, mutable latest-build pointers,
# or release assets uploaded from the repository root.
$runSource = Get-Content -LiteralPath (Join-Path $root 'run.bat') -Raw -Encoding UTF8
$releaseWorkflow = Get-Content -LiteralPath (Join-Path $root '.github\workflows\draft-release.yml') -Raw -Encoding UTF8
if ($runSource -notmatch 'publish\\KeePassXCCardPrint[.]exe' -or
    $runSource -match 'latest-build|out\\' -or
    $releaseWorkflow -notmatch 'dist/KeePassXCCardPrint-' -or
    $releaseWorkflow -match '(?m)^\s+KeePassXCCardPrint-\$\{\{ needs[.]preflight[.]outputs[.]version \}\}-win-x64[.]zip') {
    throw 'OUTPUT LAYOUT CONTRACT: use publish/ for the application and dist/ for both release files.'
}
Write-Host '[PASS] build runner and GitHub release use publish/ and dist/.' -ForegroundColor Green

# Build contract checks for the current source tree.
$projectText = Get-Content -LiteralPath $project -Raw -Encoding UTF8
$programText = Get-Content -LiteralPath (Join-Path $sourceRoot 'platforms\windows\Program.cs') -Raw -Encoding UTF8
$xpsCanvasText = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\platforms\windows\printing\XpsCardCanvas.cs') -Raw -Encoding UTF8
$xpsPrinterText = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\platforms\windows\printing\WindowsXpsPrinter.cs') -Raw -Encoding UTF8
$printDialogText = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\PrintDialog.cs') -Raw -Encoding UTF8
$appConfirmationsText = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\AppConfirmations.cs') -Raw -Encoding UTF8
# Guard against the CS0201 regression: parenthesized method invocation in
# an event handler expression body is not a valid C# statement expression.
if ($printDialogText -match '_printer\.SelectionChanged\s*\+=\s*\([^)]*\)\s*=>\s*\(\s*UpdatePrinterChoice\(\)\s*\)\s*;' -or
    $printDialogText -notmatch '_printer\.SelectionChanged\s*\+=\s*\([^)]*\)\s*=>\s*UpdatePrinterChoice\(\)\s*;') {
    throw 'PRINT DIALOG HANDLER SYNTAX: invalid selection handler; CS0201 risk.'
}
Write-Host '[PASS] printer selection handler syntax.' -ForegroundColor Green
$versionMatch = [regex]::Match($projectText, '<Version>\s*([^<]+)\s*</Version>')
if (-not $versionMatch.Success) { throw 'Missing project version in KeePassXCCardPrint.csproj.' }
$sourceVersion = $versionMatch.Groups[1].Value.Trim()
Write-Host "BUILD SOURCE: $sourceVersion" -ForegroundColor Cyan
Write-Host "SOURCE ROOT: $root" -ForegroundColor Cyan
# Only the project version is authoritative; MSBuild generates AppBuildInfo.
$mainWindowText = Get-Content -LiteralPath (Join-Path $sourceRoot 'MainWindow.axaml') -Raw -Encoding UTF8
if ($sourceVersion -notmatch '^\d+\.\d+\.\d+$' -or
    $projectText -notmatch 'GenerateAppBuildInfo' -or
    $projectText -notmatch 'AppBuildInfo.g.cs' -or
    $programText -match 'const string Version' -or
    $mainWindowText -match 'Title="KeePassXC Card Print\s+\d+\.' -or
    $programText -notmatch 'AppStartupState.ResetWindowPlacement') {
    throw 'APPLICATION VERSION CONTRACT: only the project may declare a stable N.N.N version.'
}
Write-Host '[PASS] Single application version source and generated Native AOT metadata.' -ForegroundColor Green
if ($xpsCanvasText -notmatch 'resourcePrefix\.EndsWith\("/",\s*StringComparison\.Ordinal\)' -or
    $xpsCanvasText -match 'resourcePrefix\.EndsWith\(''/''\s*,\s*StringComparison\.Ordinal\)' -or
    $xpsPrinterText -match '\bcanvas\.DrawText\s*\(' -or
    $xpsPrinterText -notmatch '\bcanvas\.DrawGlyphRuns\s*\(' -or
    $printDialogText -match '\.Watermark\s*=' -or
    $printDialogText -notmatch '\.PlaceholderText\s*=') {
    throw 'XPS RENDERING CONTRACT: the native glyph-run and Avalonia text APIs are inconsistent.'
}
Write-Host '[PASS] XPS glyph-run API, path overload and Avalonia PlaceholderText.' -ForegroundColor Green
# The actual preview must clear both on scheduling and in the delayed worker.
# Explicit template-side samples have a separate scene; keeping that mode is
# not permission to keep the actual scene or render an unchecked entry.
$previewSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\viewmodels\CardPreviewViewModel.cs') -Raw -Encoding UTF8
$emptySelectionGuard = 'if\s*\(\s*!VaultRecords\.Any\(r\s*=>\s*r\.Selected\)\s*\)\s*\{\s*CloseCardPreview\(resetTemplatePreview:\s*false\);\s*return;\s*\}'
if ($previewSource -match 'chosen\.Length\s*==\s*0\s*&&\s*VaultRecords\.FirstOrDefault' -or
    [regex]::Matches($previewSource, $emptySelectionGuard).Count -ne 2 -or
    $previewSource -notmatch '_previewError="";\s*ResetTransition\(\)' -or
    $previewSource -notmatch '_previewScene=null;\s*Changed\(nameof\(PreviewScene\)\)' -or
    $previewSource -notmatch 'if\s*\(resetTemplatePreview\s*&&\s*TemplatePreviewForced\)\s*ResetTemplatePreview\(\);') {
    throw 'EMPTY SELECTION PREVIEW REGRESSION: unchecked entries must never appear in preview.'
}
# A forced-side preview may use a selected entry or an explicitly synthetic
# sample, but must never fall back to an arbitrary record in the open vault.
# A combined duplex card may also use a second explicitly selected entry.
# Both records must be snapshotted before the renderer runs; forced output
# must remain unavailable for printing and export.
$templatePreviewSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\viewmodels\TemplatePreviewViewModel.cs') -Raw -Encoding UTF8
$selectedRecord = 'VaultRecords\.FirstOrDefault\(r\s*=>\s*r\.Selected\s*&&\s*r\.Uuid\s*==\s*anchor\?\.Record\.Uuid\)\s*\?\?\s*VaultRecords\.FirstOrDefault\(r\s*=>\s*r\.Selected\)'
if ($templatePreviewSource -notmatch $selectedRecord -or
    [regex]::Matches($templatePreviewSource, 'VaultRecords\.FirstOrDefault\s*\(').Count -ne 3 -or
    $templatePreviewSource -notmatch 'var partner\s*=\s*partnerId is Guid id\s*&&\s*id\s*!=\s*chosen\?\.Uuid\s*\?\s*VaultRecords\.FirstOrDefault\(r\s*=>\s*r\.Selected\s*&&\s*r\.Uuid\s*==\s*id\)\s*:\s*null;' -or
    $templatePreviewSource -notmatch 'BuildTemplatePreviewAsync\(cancellation,\s*mode,\s*appearance,\s*mapping,\s*record,\s*partner is null\s*\?\s*null\s*:\s*SnapshotRecord\(partner\),\s*anchor\?\.Number\s*\?\?\s*1\)' -or
    $templatePreviewSource -notmatch 'CardLayoutEngine\.Build\(partner is null\s*\?\s*\[record\]\s*:\s*\[record,\s*partner\],\s*appearance,\s*mapping,\s*cancellationToken:token\)' -or
    $previewSource -notmatch 'public bool PrintReady\s*=>[^;]*!TemplatePreviewForced\s*&&' -or
    $templatePreviewSource -notmatch 'var record\s*=\s*chosen is null\s*\?\s*null\s*:\s*SnapshotRecord\(chosen\)' -or
    $templatePreviewSource -notmatch 'private void ScheduleTemplatePreview\(\)\s*\{\s*CancelTemplatePreview\(\);' -or
    $templatePreviewSource -notmatch '_templatePreviewScene\s*=\s*null;' -or
    $templatePreviewSource -notmatch 'if\s*\(card is null\)\s*\{\s*var example=new VaultRecord' -or
    $templatePreviewSource -notmatch 'if\s*\(token\.IsCancellationRequested\s*\|\|\s*!ReferenceEquals\(_templatePreviewCancellation,cancellation\)\)' -or
    $templatePreviewSource -match 'VaultRecords\.(?:Add|Insert|Clear)\s*\(') {
    throw 'TEMPLATE PREVIEW REGRESSION: forced preview must use selected records or isolated synthetic samples.'
}
Write-Host '[PASS] empty selection clears actual preview; forced preview uses selected or isolated synthetic records.' -ForegroundColor Green


# Windows-only NuGet references are isolated in the host-specific imported file.
$hostPackagesPath = Join-Path $sourceRoot 'platforms\windows\windows-packages.props'
if (-not (Test-Path -LiteralPath $hostPackagesPath -PathType Leaf)) {
    throw 'PLATFORM PACKAGE CONTRACT: Windows package declarations are missing.'
}
$hostPackages = Get-Content -LiteralPath $hostPackagesPath -Raw -Encoding UTF8
if ($projectText -notmatch 'Import Project="platforms/windows/windows-packages.props"' -or
    $projectText -match '<PackageReference Include="(?:Avalonia\.Win32|SkiaSharp\.NativeAssets\.Win32|HarfBuzzSharp\.NativeAssets\.Win32)"' -or
    $hostPackages -notmatch 'Include="Avalonia.Win32"' -or
    $hostPackages -notmatch 'Include="SkiaSharp.NativeAssets.Win32"' -or
    $hostPackages -notmatch 'Include="HarfBuzzSharp.NativeAssets.Win32"') {
    throw 'PLATFORM PACKAGE CONTRACT: Windows assets must be declared only in platforms/windows.'
}
Write-Host '[PASS] Windows-specific NuGet packages are isolated in platforms/windows.' -ForegroundColor Green
# This is a clean initial implementation: historical settings,
# storage aliases and migration helpers must never be silently reintroduced.
$firstReleaseContracts = @(
    @{ Path = 'services/UiSettings.cs'; Forbidden = 'session[.]json|legacy|Migrate' },
    @{ Path = 'services/CardAppearance.cs'; Forbidden = 'settings[.]json|appearance-stage[0-9]|WriteTemplate[(]|LegacyBackOverride' },
    @{ Path = 'services/VaultPreferences.cs'; Forbidden = 'session[.]json|mapping-stage[0-9]|Migrate' },
    @{ Path = 'services/DomainPrefixStore.cs'; Forbidden = 'domains-stage[0-9]|Migrate' },
    @{ Path = 'services/PrintTrackingLedger.cs'; Forbidden = 'ImportOld|legacy' },
    @{ Path = 'services/FontWeights.cs'; Forbidden = 'regular_plus1|regular_plus2' }
)
foreach ($contract in $firstReleaseContracts) {
    $entry = Join-Path $sourceRoot $contract.Path
    if (-not (Test-Path -LiteralPath $entry -PathType Leaf)) {
        throw "FIRST-RELEASE CONTRACT: missing $($contract.Path)"
    }
    $content = Get-Content -LiteralPath $entry -Raw -Encoding UTF8
    if ($content -match $contract.Forbidden) {
        throw "FIRST-RELEASE CONTRACT: old compatibility logic in $($contract.Path)"
    }
}
Write-Host '[PASS] First-release configuration formats contain no historical migration code.' -ForegroundColor Green

# Platform boundary: all sources outside platforms/windows are portable.
$sharedSources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]platforms[\\/]windows[\\/]' }
$forbidden = '\b(?:DllImport|LibraryImport|GeneratedDllImport)\s*\(|\b(?:Microsoft\.Win32|System\.Windows\.Forms|System\.Drawing\.Printing|Registry\.CurrentUser|WindowsRasterPrinter|WindowsXpsPrinter|WindowsConfigurationEnvironment|WindowsHost|WindowsPlatformServices|YubiKeyChallenge)\b|\bUseWin32\s*\(|\bGetModuleHandle\b'
foreach ($sharedFile in $sharedSources) {
    $sharedText = Get-Content -LiteralPath $sharedFile.FullName -Raw -Encoding UTF8
    if ($sharedText -match $forbidden) {
        throw "PLATFORM BOUNDARY VIOLATION: $($sharedFile.FullName)"
    }
}
if ($printDialogText -notmatch '_printing\.Submit\(request,token\)' -or
    $projectText -notmatch '<TargetFramework>net10\.0</TargetFramework>' -or
    -not (Test-Path -LiteralPath (Join-Path $sourceRoot 'platforms\windows\Program.cs')) -or
    (Test-Path -LiteralPath (Join-Path $sourceRoot 'Program.cs'))) {
    throw 'PLATFORM COMPOSITION CONTRACT: Windows entry point and native services must remain isolated.'
}
Write-Host '[PASS] All shared C# sources are free of direct native Windows imports.' -ForegroundColor Green

# CA1416 remains enabled and is treated as an error. Windows-only types
# must be annotated; construction happens only after the Windows host guard.
$winConfigSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'platforms\windows\storage\WindowsConfigurationEnvironment.cs') -Raw -Encoding UTF8
$winPlatformSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'platforms\windows\WindowsPlatformServices.cs') -Raw -Encoding UTF8
$winHostSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'platforms\windows\WindowsHost.cs') -Raw -Encoding UTF8
if ($winConfigSource -notmatch '\[SupportedOSPlatform\("windows"\)\]\s*internal\s+sealed\s+class\s+WindowsConfigurationEnvironment' -or
    $winPlatformSource -notmatch '\[SupportedOSPlatform\("windows"\)\]\s*internal\s+sealed\s+class\s+WindowsPlatformServices' -or
    $winHostSource -notmatch 'if\s*\(!OperatingSystem\.IsWindows\(\)\)' -or
    $projectText -notmatch '<WarningsAsErrors>[^<]*\bCA1416\b') {
    throw 'PLATFORM ANALYZER REGRESSION: Windows-only adapters need SupportedOSPlatform and a guarded composition root; CA1416 must remain enabled.'
}
Write-Host '[PASS] Windows platform annotations, runtime guard and CA1416-as-error.' -ForegroundColor Green

# About tab / Native AOT dependency-version manifest guard.
$aboutView = Get-Content -LiteralPath (Join-Path $sourceRoot 'AboutView.axaml') -Raw -Encoding UTF8
$aboutModel = Get-Content -LiteralPath (Join-Path $sourceRoot 'viewmodels\AboutViewModel.cs') -Raw -Encoding UTF8
$mainView = Get-Content -LiteralPath (Join-Path $sourceRoot 'MainWindow.axaml') -Raw -Encoding UTF8
if ($projectText -notmatch 'GenerateAboutDependencyVersions' -or
    $projectText -notmatch 'AboutDependencyVersions.g.cs' -or
    $mainView -notmatch '<views:AboutView DataContext="\{Binding About\}"' -or
    $aboutView -notmatch 'AboutGraphics' -and $aboutView -notmatch 'GraphicsTitle' -or
    $aboutModel -notmatch 'SafeDiagnosticSummary') {
    throw 'ABOUT DIAGNOSTICS REGRESSION: tab, automatic dependency versions or safe copy summary missing.'
}
Write-Host '[PASS] About tab, dependency manifest generation and safe diagnostics summary.' -ForegroundColor Green
# About navigation keeps a real fifth TabItem, separated to the right by the panel.
# The compact, responsive metadata page must retain both library columns and its metrics.
$tabLayoutSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'controls\RightAnchoredTabHeaderPanel.cs') -Raw -Encoding UTF8
if ($mainView -notmatch '<controls:RightAnchoredTabHeaderPanel\s*/>' -or
    $tabLayoutSource -notmatch 'class\s+RightAnchoredTabHeaderPanel\s*:\s*Panel' -or
    $mainView -notmatch '<views:AboutView DataContext=' -or
    $aboutView -notmatch 'LibrariesLeft' -or
    $aboutView -notmatch 'LibrariesRight' -or
    $aboutView -notmatch 'DetailsGrid' -or
    $aboutView -notmatch 'PerformanceTitle') {
    throw 'ABOUT LAYOUT REGRESSION: right-anchored tab, compact library columns or diagnostics missing.'
}
Write-Host '[PASS] separated About tab and responsive diagnostics layout.' -ForegroundColor Green

# Native XPS font-resource contract: C++ and C# must advance together.
$fontEmbeddingSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\platforms\windows\printing\XpsFontEmbedding.cs') -Raw -Encoding UTF8
$nativeBridgeSource = Get-Content -LiteralPath $nativeSource -Raw -Encoding UTF8
if ($xpsCanvasText -notmatch 'WriteStartElement\("Glyphs"' -or
    $xpsCanvasText -notmatch 'XpsFontEmbedding\.TryPrepare' -or
    $xpsPrinterText -notmatch 'FontCount' -or
    $nativeBridgeSource -notmatch 'GetFontResources' -or
    $nativeBridgeSource -notmatch 'KpcFreezeFont' -or
    $fontEmbeddingSource -notmatch 'HB_SUBSET' -and $fontEmbeddingSource -notmatch 'FreezeFontNative') {
    throw 'SOURCE MISMATCH: XPS font resource / Glyphs support missing from C#, C++ or Native AOT interop.'
}
Write-Host '[PASS] XPS embedded fonts, Glyphs, and native variable-font instancing interfaces.' -ForegroundColor Green
# The common glyph self-test must understand both embedded Glyphs and
# exact-outline fallback; do not regress to counting only transformed Paths.
$glyphTestSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\platforms\windows\tests\CardEngineSelfTests.cs') -Raw -Encoding UTF8
if ($glyphTestSource -match 'XPS emits one vector path per visible synthetic glyph' -or
    $glyphTestSource -notmatch 'XPS emits exactly one Glyphs or vector Path per synthetic glyph' -or
    $glyphTestSource -notmatch 'XPS embedded glyph.*expected Glyph ID' -or
    $glyphTestSource -notmatch 'xps\.Fonts\.TryGetValue') {
    throw 'XPS GLYPH TEST CONTRACT: mixed Glyphs/Path assertions are missing.'
}
Write-Host '[PASS] mixed XPS embedded Glyphs / vector outline self-test.' -ForegroundColor Green

# SkiaSharp 4.154.0 deprecates path mutation methods on SKPath.
# Smoke-test geometries must be constructed with SKPathBuilder and Detach().
$pathTestSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\platforms\windows\tests\CardEngineSelfTests.cs') -Raw -Encoding UTF8
if ($xpsPrinterText -notmatch 'geometryBuilder\.Detach\(' -or
    $pathTestSource -notmatch 'pathBuilder\.Detach\(' -or
    $xpsPrinterText -match '(?m)\bgeometry\.(?:MoveTo|LineTo|QuadTo|CubicTo|Close)\s*\(' -or
    $pathTestSource -match '(?m)\bpath\.(?:MoveTo|LineTo|QuadTo|CubicTo|Close)\s*\(') {
    throw 'OBSOLETE SKPATH USAGE: diagnostic geometry must use SKPathBuilder.'
}
Write-Host '[PASS] XPS diagnostic paths use SKPathBuilder (no obsolete SKPath mutation calls).' -ForegroundColor Green

# Printing UI and duplex contracts: secondary windows must always use
# the supplied light icon, regardless of UI theme. Keep duplex/DPI guards intact.
$printIconSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\PrintWindowIcons.cs') -Raw -Encoding UTF8
$rasterPrinterSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\platforms\windows\printing\WindowsRasterPrinter.cs') -Raw -Encoding UTF8
if ($printIconSource -notmatch 'CardPrint_white\.ico' -or
    $printIconSource -notmatch 'window\.Icon\s*=\s*white' -or
    $printIconSource -match 'CardPrint_black\.ico|ActualThemeVariantChanged|ActualThemeVariant\s*==' -or
    $printDialogText -notmatch 'PrintWindowIcons.Attach\(this\)' -or
    $printDialogText -notmatch 'ManualDuplexPlan\.Create\(pages\)' -or
    $printDialogText -notmatch 'await Send\(\[current.Front\],0\)' -or
    $printDialogText -notmatch 'await Send\(\[current.Back\],0\)' -or
    $rasterPrinterSource -notmatch 'DcEnumResolutions = 13') {
    throw 'PRINT UI CONTRACT FAILED: stable light window icon, sheet-by-sheet duplex or driver DPI disclosure is missing.'
}
Write-Host '[PASS] fixed light print window icons, per-sheet manual duplex, and printer DPI enumeration.' -ForegroundColor Green

# Print action controls should stay compact. Do not embed non-ASCII text in
# build-time regular expressions: Windows PowerShell 5.1 must parse this file.
# Load the main AXAML before checking it, rather than using an uninitialized variable.
$vaultSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\viewmodels\VaultViewModel.cs') -Raw -Encoding UTF8
$chromeXaml = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\MainWindow.axaml') -Raw -Encoding UTF8
$actionCount = [regex]::Matches($printDialogText, 'PrintDialogButtons\.Create\(').Count
if ($printDialogText -notmatch 'internal const double ActionHeight = 26' -or
    $printDialogText -notmatch 'MinWidth = 0' -or
    $actionCount -lt 5 -or
    $printDialogText -match 'Height\s*=\s*30,\s*MinHeight\s*=\s*30' -or
    $vaultSource -match '\[INVALID_CREDENTIALS\].*CredentialsRejected' -or
    $vaultSource -match 'failure\.Code\}\].*failure\.Message' -or
    $chromeXaml -notmatch 'TextWrapping="Wrap" TextTrimming="None"') {
    throw 'PRINT UI REGRESSION: check compact action buttons and full error text.'
}
Write-Host '[PASS] compact print actions and untruncated user-visible error status.' -ForegroundColor Green

# Printing dialog must reuse the main settings-card palette and remove options
# not advertised by the selected printer. A capability flag is NOT proof of
# native vector processing in the physical output device.
if ($printDialogText -notmatch 'panel.Classes.Add\("settings-card"\)' -or
    $printDialogText -notmatch '_printing\.IsVectorAvailable\(PrinterName\)' -or
    $rasterPrinterSource -notmatch 'CanAcceptDocumentJobs' -or
    $printDialogText -notmatch 'new\("sequential",' -or
    $printDialogText -notmatch '_duplex.IsVisible=both' -or
    $printDialogText -notmatch 'SingleSidedPrintPlan.Create\(pages\)' -or
    $appConfirmationsText -notmatch 'SizeToContent=SizeToContent.Height' -or
    $rasterPrinterSource -notmatch 'PrinterAttributeRawOnly') {
    throw 'PRINT DIALOG CONTRACT FAILED: grouped cards, sequential simplex printing or compact modal is missing.'
}
Write-Host '[PASS] grouped print panels, optional hardware duplex and sequential one-sided printing.' -ForegroundColor Green

# Security confirmation policy: file output must not silently expose KDBX data.
$mainWindowSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\MainWindow.axaml.cs') -Raw -Encoding UTF8
$compactTheme = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\styles\Compact.axaml') -Raw -Encoding UTF8
if ($mainWindowSource -notmatch 'ConfirmSecretFileAsync\(this,_settings.Language,false\)' -or
    $mainWindowSource -notmatch 'ConfirmExitAsync\(this,_settings.Language' -or
    $mainWindowSource -notmatch 'ResolveUnsavedTemplateAsync\(exiting:true\)' -or
    $mainWindowSource -match 'ResolveUnsavedMappingAsync' -or
    $mainWindowSource -notmatch 'HasUnsavedTemplateChanges' -or
    $mainWindowSource -notmatch 'SaveChangesToTemplate\(\)' -or
    $printDialogText -notmatch 'ConfirmSecretFileAsync\(this,_settings.Language,true\)' -or
    $printDialogText -notmatch 'MaySaveDocumentToFile\(printer\)' -or
    $appConfirmationsText -notmatch 'danger-action' -or
    $appConfirmationsText -notmatch 'WindowDecorations=WindowDecorations.Full' -or
    $compactTheme -notmatch 'Button.danger-action' -or
    $appConfirmationsText -notmatch 'Save file') {
    throw 'CONFIRMATION POLICY REGRESSION: check exit, PDF/PNG warning, virtual printers and danger theme.'
}
Write-Host '[PASS] exit and unencrypted-file confirmations and shared danger button.' -ForegroundColor Green

# Fluent System Icons Regular is the sole application-owned action icon standard.
$appAxaml = Get-Content -LiteralPath (Join-Path $sourceRoot 'App.axaml') -Raw -Encoding UTF8
$actionIconSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'ActionIconVectors.cs') -Raw -Encoding UTF8
$compactStyleSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'styles\Compact.axaml') -Raw -Encoding UTF8
$modalXamls = @('DomainsWindow.axaml', 'StorageSwitchDialog.axaml', 'UnsavedMappingDialog.axaml', 'SettingsResetDialog.axaml')
foreach ($modal in $modalXamls) {
    $modalXaml = Get-Content -LiteralPath (Join-Path $sourceRoot $modal) -Raw -Encoding UTF8
    if ($modalXaml -notmatch 'Click="OnCancel"(?:(?!</Button>)[\s\S])*?<fi:FluentIcon\b[^>]*Icon="DismissCircle"') {
        throw "CANCEL ICON MISMATCH: $modal must use the Fluent Regular DismissCircle glyph."
    }
}
if ($appAxaml -match 'KpCancelIconGeometry|KpFilterDismissGeometry' -or
    $actionIconSource -notmatch '"cancel"\s*=>\s*Icon.DismissCircle' -or
    $actionIconSource -notmatch 'IconVariant\s*=\s*IconVariant.Regular' -or
    $actionIconSource -notmatch 'FontSize\s*=\s*size' -or
    $compactStyleSource -notmatch 'Button:disabled /template/ ContentPresenter#PART_ContentPresenter') {
    throw 'THEME/FLUENT ICON MISMATCH: shared icon policy or disabled-button palette not present.'
}
Write-Host '[PASS] modal Fluent Cancel icons and theme-matched disabled buttons.' -ForegroundColor Green

$mainXaml = Get-Content -LiteralPath (Join-Path $sourceRoot 'MainWindow.axaml') -Raw -Encoding UTF8
if ($mainXaml -notmatch 'Click="OnOpenDatabase"(?:(?!</Button>)[\s\S])*?<fi:FluentIcon\b[^>]*Icon="FolderOpen"' -or
    $mainXaml -notmatch 'Click="OnCloseDatabase"(?:(?!</Button>)[\s\S])*?<fi:FluentIcon\b[^>]*Icon="LockClosed"') {
    throw 'FLUENT ACTION ICON: missing database open or close glyph.'
}
foreach ($uiFile in (Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.axaml' | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
    $uiText = Get-Content -LiteralPath $uiFile.FullName -Raw -Encoding UTF8
    if ($uiText -match '<(?:\w+:)?(?:PathIcon|SymbolIcon|StreamGeometry)\b') {
        throw "NON-FLUENT UI ICON: $($uiFile.Name) contains a legacy icon control or resource."
    }
    # The only standalone Path is a preview of actual Fluent vector card artwork, not an action glyph.
    foreach ($pathTag in [regex]::Matches($uiText, '<Path\b[^>]*>')) {
        if ($pathTag.Value -notmatch 'Data="\{Binding IconGeometry\}"') {
            throw "NON-FLUENT UI ICON: unexpected vector Path in $($uiFile.Name)."
        }
    }
    foreach ($glyph in [regex]::Matches($uiText, '<fi:FluentIcon\b[^>]*>')) {
        $tag = $glyph.Value
        $w = [regex]::Match($tag, '\bWidth="(\d+)"').Groups[1].Value
        $h = [regex]::Match($tag, '\bHeight="(\d+)"').Groups[1].Value
        $f = [regex]::Match($tag, '\bFontSize="(\d+)"').Groups[1].Value
        if ($tag -notmatch 'IconVariant="Regular"' -or $tag -notmatch 'IconSize="Size20"' -or
            -not $w -or $w -ne $h -or $w -ne $f) {
            throw "FLUENT GLYPH SIZE/STYLE: $($uiFile.Name): $tag"
        }
    }
}
foreach ($codeFile in (Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' })) {
    $codeText = Get-Content -LiteralPath $codeFile.FullName -Raw -Encoding UTF8
    if ($codeText -match '\b(?:PathIcon|SymbolIcon|CancelGeometry)\b') {
        throw "NON-FLUENT UI ICON: $($codeFile.Name) contains legacy icon code."
    }
}
$aboutGlyphs = @([regex]::Matches($aboutView, '<fi:FluentIcon\b[^>]*>') | Where-Object { $_.Value -notmatch 'Classes="section-heading-icon"' })
if ($aboutGlyphs.Count -ne 4 -or @($aboutGlyphs | Where-Object { $_.Value -notmatch 'FontSize="18"' }).Count -ne 0) {
    throw 'ABOUT ICON SIZE: all four action glyphs must be 18 DIP.'
}
# Check every static section title, including both already-iconified source headings.
function Assert-SectionHeadingIcon($page, [string]$attribute, [string]$caption, [string]$glyphName) {
    $heading = $page.SelectSingleNode("//*[local-name()='TextBlock' and @Classes='section-heading' and @$attribute='$caption']")
    if ($null -eq $heading) { throw "UI check: missing section heading $caption." }
    $glyphNode = $heading.ParentNode.SelectSingleNode("./*[local-name()='FluentIcon' and @Classes='section-heading-icon' and @Icon='$glyphName']")
    if ($null -eq $glyphNode -or $glyphNode.GetAttribute('Width') -ne '16' -or
        $glyphNode.GetAttribute('Height') -ne '16' -or $glyphNode.GetAttribute('FontSize') -ne '16' -or
        $glyphNode.GetAttribute('Foreground') -ne '{DynamicResource KpTextBrush}' -or
        $glyphNode.GetAttribute('IconVariant') -ne 'Regular' -or $glyphNode.GetAttribute('IconSize') -ne 'Size20') {
        throw "UI check: missing or inconsistent Fluent heading glyph for $caption."
    }
}
[xml]$headingMain = $mainXaml
[xml]$headingSettings = Get-Content -LiteralPath (Join-Path $sourceRoot 'SettingsView.axaml') -Raw -Encoding UTF8
[xml]$headingAbout = $aboutView
Assert-SectionHeadingIcon $headingMain 'Text' '{Binding SourceTitle}' 'Database'
Assert-SectionHeadingIcon $headingMain 'Text' '{Binding Records}' 'TextBulletList'
Assert-SectionHeadingIcon $headingMain 'Text' '{Binding UiTemplate}' 'DocumentSettings'
Assert-SectionHeadingIcon $headingMain 'Text' '{Binding MappingFieldsHeading}' 'Table'
Assert-SectionHeadingIcon $headingMain 'Text' '{Binding MappingFieldHeading}' 'Settings'
Assert-SectionHeadingIcon $headingMain 'Text' '{Binding MappingRestrictionsCaption}' 'Shield'
Assert-SectionHeadingIcon $headingMain 'Text' '{Binding Label}' '{Binding Glyph}'
Assert-SectionHeadingIcon $headingSettings 'Text' 'Хранение конфигурации' 'Cloud'
Assert-SectionHeadingIcon $headingSettings 'Text' 'Обновления программы' 'ArrowDownload'
Assert-SectionHeadingIcon $headingSettings 'Text' 'Правила переноса слов' 'TextWrap'
Assert-SectionHeadingIcon $headingSettings 'Text' 'Обслуживание' 'Wrench'
Assert-SectionHeadingIcon $headingAbout 'Text' '{Binding ComponentsTitle}' 'Apps'
Assert-SectionHeadingIcon $headingAbout 'Text' '{Binding GraphicsTitle}' 'Desktop'
Assert-SectionHeadingIcon $headingAbout 'Text' '{Binding PrintingTitle}' 'Print'
Assert-SectionHeadingIcon $headingAbout 'Text' 'Обновления приложения' 'ArrowDownload'
Assert-SectionHeadingIcon $headingAbout 'Text' '{Binding PerformanceTitle}' 'Gauge'
$headingStyle = ([xml]$compactStyleSource).SelectSingleNode("//*[local-name()='Style' and @Selector='Grid.section-heading-row']/*[local-name()='Setter' and @Property='ColumnSpacing' and @Value='6']")
if ($null -eq $headingStyle -or @($headingAbout.SelectNodes("//*[local-name()='FluentIcon' and @Classes='section-heading-icon']")).Count -ne 5) {
    throw 'UI check: common heading spacing or About heading glyph count is incorrect.'
}
Write-Host '[PASS] all main tab section headings have themed Fluent icons.' -ForegroundColor Green
Write-Host '[PASS] project-wide Fluent Regular action icons, explicit matching glyph sizes and About 18-DIP controls.' -ForegroundColor Green

# The unified template editor has five mapping stepper pairs and one shared
# appearance stepper pair. Verify each role as well as all timing attributes.
[xml]$stepperUi = $mainXaml
$repeatButtons = @($stepperUi.SelectNodes("//*[local-name()='RepeatButton']"))
if ($repeatButtons.Count -ne 12) {
    throw 'NUMERIC STEPPER COUNT MISMATCH: expected five mapping pairs and one shared appearance pair (12 controls).'
}
foreach ($stepper in $repeatButtons) {
    if ($stepper.GetAttribute('ClickMode') -ne 'Press' -or
        $stepper.GetAttribute('Delay') -ne '520' -or
        $stepper.GetAttribute('Interval') -ne '300') {
        throw 'NUMERIC STEPPER TIMING MISMATCH: all RepeatButtons must use Press, Delay=520 and Interval=300.'
    }
}
foreach ($field in @('Order', 'SizePct', 'TrackingPt', 'GroupSize', 'GroupSpaces')) {
    foreach ($direction in @('+1', '-1')) {
        $tag = "${field}:$direction"
        if (@($repeatButtons | Where-Object { $_.GetAttribute('Click') -eq 'OnMappingNumericSpin' -and $_.GetAttribute('Tag') -eq $tag }).Count -ne 1) {
            throw "NUMERIC STEPPER MAPPING MISMATCH: expected exactly one control for $tag."
        }
    }
}
foreach ($handler in @('OnAppearanceIncrease', 'OnAppearanceDecrease')) {
    if (@($repeatButtons | Where-Object { $_.GetAttribute('Click') -eq $handler }).Count -ne 1) {
        throw "NUMERIC STEPPER APPEARANCE MISMATCH: expected exactly one shared control for $handler."
    }
}
Write-Host '[PASS] five mapping stepper pairs and one shared appearance pair; repeat timing (520ms / 300ms).' -ForegroundColor Green


# presentation and QR contracts. Avoid obsolete return-to-top UI.
$qrSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'rendering\CardSkiaRenderer.cs') -Raw -Encoding UTF8
$qrLayout = Get-Content -LiteralPath (Join-Path $sourceRoot 'rendering\CardLayoutEngine.cs') -Raw -Encoding UTF8
$previewState = Get-Content -LiteralPath (Join-Path $sourceRoot 'viewmodels\CardPreviewViewModel.cs') -Raw -Encoding UTF8
if ($qrSource -notmatch 'InlineQrLabelInkTopMm' -or
    $qrLayout -notmatch 'result.RemoveRange\(beforeFieldLabel' -or
    $qrLayout -notmatch 'InlineQrLabelInkTopMm' -or
    $printDialogText -notmatch 'calibrationActions.Children.Add\(_test\)' -or
    $chromeXaml -notmatch 'Classes="preview-state"' -or
    $previewState -notmatch 'PreviewNoCardsLabel' -or
    $printDialogText -notmatch 'ActionIconVectors.Create') {
    throw 'PRINT UI CONTRACT: QR caption, calibration test, vector button icons or preview overlays are missing.'
}
Write-Host '[PASS] QR caption alignment, calibration test action and themed preview states.' -ForegroundColor Green

if ($rasterPrinterSource -notmatch 'CurrentDriverResolution' -or
    $rasterPrinterSource -notmatch 'IsKnownManualDuplexOnly' -or
    $rasterPrinterSource -notmatch 'PrinterPortHandle.Open' -or
    $printDialogText -notmatch 'Рабочее разрешение драйвера') {
    throw 'PRINTER INFO CONTRACT: verify current driver DPI, port name and manual-only printer detection.'
}
Write-Host '[PASS] printer driver DPI and manual-duplex compatibility checks.' -ForegroundColor Green
# The export flyout trigger itself must be disabled with printing; disabling
# only its menu entries is misleading when no database/records are available.
$previewXaml = Get-Content -LiteralPath (Join-Path $sourceRoot 'MainWindow.axaml') -Raw -Encoding UTF8
$previewVm = Get-Content -LiteralPath (Join-Path $sourceRoot 'viewmodels\CardPreviewViewModel.cs') -Raw -Encoding UTF8
$vaultVm = Get-Content -LiteralPath (Join-Path $sourceRoot 'viewmodels\VaultViewModel.cs') -Raw -Encoding UTF8
if ($previewXaml -notmatch 'Classes="preview-tool" IsEnabled="\{Binding PrintReady\}" ToolTip.Tip="Экспорт') {
    throw 'PRINT/EXPORT CONTRACT: export toolbar trigger must use PrintReady.'
}
if ($previewVm -notmatch '_snapshot is not null && !Busy && _selectedCount>0' -or
    $vaultVm -notmatch 'Changed\(nameof\(PrintReady\)\)') {
    throw 'PRINT/EXPORT CONTRACT: open vault, selected records and readiness notification are required.'
}
Write-Host '[PASS] print/export both require an open vault and selected cards.' -ForegroundColor Green






if ($fontEmbeddingSource -match 'stream\.GetData\s*\(' -or
    $fontEmbeddingSource -notmatch 'stream\.Read\s*\(' -or
    $fontEmbeddingSource -notmatch 'received\s*<\s*expectedLength') {
    throw 'STALE XPS FONT READER: font streams must use checked SKStream.Read, not GetData.'
}
Write-Host '[PASS] length-checked OpenType stream extraction (no SKStream.GetData).' -ForegroundColor Green



# Fail before expensive AOT publishing if URL/recovery changes were merged twice.
$modelSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\models\VaultRecord.cs') -Raw -Encoding UTF8
$mappingSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\services\VaultPreferences.cs') -Raw -Encoding UTF8
$readerSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\services\KdbxVaultReader.cs') -Raw -Encoding UTF8
$layoutSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\rendering\CardLayoutEngine.cs') -Raw -Encoding UTF8
$normalizerSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\services\FieldMappingDefaults.cs') -Raw -Encoding UTF8
if ([regex]::Matches($modelSource, 'public\s+[^\r\n;]+\s+AdditionalUrls\s*\{').Count -ne 1 -or
    [regex]::Matches($mappingSource, 'public\s+string\s+UrlSource\s*\{').Count -ne 1 -or
    [regex]::Matches($readerSource, 'AdditionalUrls\s*=').Count -ne 1 -or
    [regex]::Matches($normalizerSource, 'Contains\(r\.UrlSource\)').Count -ne 1 -or
    [regex]::Matches($layoutSource, 'RecoveryGrouped:grouped').Count -ne 1) {
    throw 'Source check: duplicate URL declarations/assignments or missing grouped recovery layout.'
}
Write-Host '[PASS] unique URL declarations and measured recovery group layout.' -ForegroundColor Green

# Fail fast if an appearance field was added without updating the source
# registry count or the Native AOT contract tests. This runs before publish.
$appearanceModel = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\services\CardAppearance.cs') -Raw -Encoding UTF8
$appearanceDescriptor = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\services\AppearanceDescriptor.cs') -Raw -Encoding UTF8
$appearanceTests = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\platforms\windows\tests\CardEngineSelfTests.cs') -Raw -Encoding UTF8
$modelKeys = @([regex]::Matches($appearanceModel, 'JsonPropertyName\("([^"]+)"\)') | ForEach-Object { $_.Groups[1].Value })
$editorKeys = @([regex]::Matches($appearanceDescriptor, '(?m)^\s+Add\("[^"]+",\s*"[^"]+",\s*"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
$propertyCount = $modelKeys.Count
if ($propertyCount -eq 0 -or $propertyCount -ne $editorKeys.Count -or
    @($modelKeys | Sort-Object -Unique).Count -ne $propertyCount -or
    @($editorKeys | Sort-Object -Unique).Count -ne $propertyCount -or
    @(Compare-Object -ReferenceObject @($modelKeys | Sort-Object) -DifferenceObject @($editorKeys | Sort-Object)).Count -gt 0) {
    throw "Appearance preflight: JSON model and editor descriptor keys disagree (model=$($modelKeys.Count), editor=$($editorKeys.Count))."
}
if ($appearanceDescriptor -notmatch ('result\.Count\s*!=\s*' + $propertyCount + '\b') -or
    $appearanceTests -notmatch ('appearanceFields\.Count\s*==\s*' + $propertyCount + '\b') -or
    $appearanceTests -notmatch ('keyNames\.Count\s*==\s*' + $propertyCount + '\b')) {
    throw "Appearance preflight: inconsistent expected property count (actual=$propertyCount)."
}
Write-Host "Verified $propertyCount unique appearance JSON properties and matching editor/test contracts." -ForegroundColor Green

# Custom chrome regression: exact Icons8 icon and no blank gray ComboBox popup bands.
$chromeXaml = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\MainWindow.axaml') -Raw -Encoding UTF8
# Keep the Cloud Sync header proportions while retaining Card Print's storage selector.
$headerColumns = [regex]::Match($chromeXaml,
    'ColumnDefinitions="Auto,(?<themeWidth>\d+),Auto,(?<languageWidth>\d+),Auto,(?<storageWidth>\d+)"')
if (-not $headerColumns.Success -or
    [int]$headerColumns.Groups['themeWidth'].Value -ne 132 -or
    [int]$headerColumns.Groups['languageWidth'].Value -ne 142 -or
    [int]$headerColumns.Groups['storageWidth'].Value -ne 132 -or
    $chromeXaml -notmatch 'MinHeight="56"') {
    throw 'UI check: header must match Cloud Sync selector sizes and 76-DIP height.'
}
Write-Host '[PASS] Cloud Sync header proportions and Card Print storage selector.' -ForegroundColor Green

# Quick record presets must stay view-only, mutually exclusive and theme aware.
$recordFilterVm = Get-Content -LiteralPath (Join-Path $sourceRoot 'viewmodels\VaultViewModel.cs') -Raw -Encoding UTF8
$recordFilterLedger = Get-Content -LiteralPath (Join-Path $sourceRoot 'services\PrintTrackingLedger.cs') -Raw -Encoding UTF8
$recordFilterStyle = Get-Content -LiteralPath (Join-Path $sourceRoot 'styles\Compact.axaml') -Raw -Encoding UTF8
$recordWindowCode = Get-Content -LiteralPath (Join-Path $sourceRoot 'MainWindow.axaml.cs') -Raw -Encoding UTF8
if ([regex]::Matches($chromeXaml, 'Classes="record-quick-filter"').Count -ne 3 -or
    [regex]::Matches($chromeXaml, 'Classes="record-quick-action"').Count -ne 2 -or
    $chromeXaml -notmatch 'Icon="FilterDismiss"' -or
    $chromeXaml -notmatch 'FilterRemainingActive, Mode=OneWay' -or
    $recordFilterVm -notmatch 'RecordQuickFilter.Remaining => record.Selected && !fullyPrinted' -or
    $recordFilterVm -notmatch 'RecordQuickFilter.Reprint => fullyPrinted' -or
    $recordFilterVm -notmatch 'record.Visible = matchesText && matchesMode' -or
    $recordFilterLedger -notmatch 'IsFullyPrinted\(Guid id\)' -or
    $recordFilterStyle -notmatch 'ToggleButton.record-quick-filter:checked /template/' -or
    $recordWindowCode -notmatch 'OnRefreshRecords' -or
    $recordWindowCode -notmatch 'preserveCurrentView: true') {
    throw 'RECORD FILTER CONTRACT: compact presets, typed ledger, refresh or theme styles missing.'
}
Write-Host '[PASS] compact record filters, mutually exclusive presets, and reauthenticated reload.' -ForegroundColor Green

$chromeStyle = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\styles\Compact.axaml') -Raw -Encoding UTF8
# Visual hierarchy regression guard: every semantic heading shares one
# central 14-DIP typography style; brand title and field labels are separate.
[xml]$typographyXml = $chromeStyle
$sectionTitleStyle = $typographyXml.SelectSingleNode("//*[local-name()='Style' and @Selector='TextBlock.section-heading']")
if ($null -eq $sectionTitleStyle -or
    $null -eq $sectionTitleStyle.SelectSingleNode("./*[local-name()='Setter' and @Property='FontSize' and @Value='14']") -or
    $null -eq $sectionTitleStyle.SelectSingleNode("./*[local-name()='Setter' and @Property='FontWeight' and @Value='SemiBold']")) {
    throw 'UI typography check: one 14-DIP semibold section-heading style must govern all section titles.'
}
if ($chromeXaml -match 'Classes="heading"' -or
    $chromeXaml -match 'Classes="group-heading"' -or
    $chromeXaml -match '<TextBlock[^>]*FontSize="1[3456]"[^>]*FontWeight="SemiBold"') {
    throw 'UI typography check: old heading classes or hand-sized section titles have returned.'
}
# Mapping no longer has a separate title: all three editor tabs show the same
# print-template toolbar, and its section heading is UiTemplate.
foreach ($headingCaption in @('SourceTitle','Records','MappingFieldsHeading','MappingFieldHeading','MappingRestrictionsCaption')) {
    if ($chromeXaml -notmatch ('Classes="section-heading"[^>]*Text="\{Binding ' + $headingCaption + '\}"') -and
        $chromeXaml -notmatch ('Text="\{Binding ' + $headingCaption + '\}"[^>]*Classes="section-heading"')) {
        throw "UI typography check: semantic section style missing for $headingCaption."
    }
}
[xml]$semanticXaml = $chromeXaml
$templateHeadings = $semanticXaml.SelectNodes("//*[local-name()='Border' and @Classes='template-toolbar']//*[local-name()='TextBlock' and @Classes='section-heading' and @Text='{Binding UiTemplate}']")
if ($null -eq $templateHeadings -or $templateHeadings.Count -ne 1) {
    throw 'UI typography check: Template must have one styled template heading.'
}
Write-Host '[PASS] consistent 14-DIP semantic section headings in the unified template editor.' -ForegroundColor Green

if ($chromeXaml -notmatch 'Classes="app-header"' -or
    $chromeXaml -notmatch 'Classes="app-footer"' -or
    $chromeXaml -notmatch 'x:Name="AppHeaderIcon"' -or
    $chromeXaml -notmatch 'ItemsSource="\{Binding LanguageOptions\}"' -or
    $chromeXaml -notmatch 'Margin="0" RowDefinitions="Auto,\*,Auto" RowSpacing="0"') {
    throw 'UI check: expected edge-to-edge header/footer and compact right-aligned selectors.'
}
# The original bands came from FluentControlResources.xaml:
# ComboBoxDropdownContentMargin = 0,4,0,4. Those margins are assigned as
# LOCAL DynamicResource values inside ComboBox.xaml and cannot be overridden
# with setters on /template/ ItemsPresenter. Check the actual resource keys.
$appXaml = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\App.axaml') -Raw -Encoding UTF8
[xml]$appXml = $appXaml
$popupMargin = @($appXml.SelectNodes("//*[local-name()='Thickness' and @*[local-name()='Key']='ComboBoxDropdownContentMargin']"))
if ($popupMargin.Count -ne 1 -or $popupMargin[0].InnerText.Trim() -ne '0') {
    throw 'UI check: ComboBoxDropdownContentMargin must be overridden to zero (Fluent default exposes gray bands).'
}
foreach ($brush in @('ComboBoxDropDownBackground', 'ComboBoxDropDownBorderBrush')) {
    $definitions = @($appXml.SelectNodes("//*[local-name()='SolidColorBrush' and @*[local-name()='Key']='$brush']"))
    if ($definitions.Count -ne 2) { throw "UI check: $brush must be overridden for both light and dark themes." }
}
if ($chromeStyle -notmatch 'ContentPresenter#PART_ContentPresenter' -or
    $chromeStyle -notmatch 'Border.app-footer') {
    throw 'UI check: ComboBox item colors or footer styles are missing.'
}
if ($chromeStyle -match 'ItemsPresenter#PART_ItemsPresenter' -or
    $chromeStyle -match 'Border#PopupBorder') {
    throw 'UI check: obsolete, ineffective popup style setters must be removed; override Fluent resources instead.'
}
Write-Host '[PASS] resource-level fix for 4-DIP ComboBox popup bands in both themes.' -ForegroundColor Green
# Database footer text must be contiguous. A leading '*' column in the nested
# footer grid would push metadata to the far right, separating it from the name.
# Since 9.2 the outer grid has a dedicated, normally hidden cloud activity cell.
$footerMarkup = [regex]::Match($chromeXaml, '(?s)<Border Grid.Row="2" Classes="app-footer">(?<body>.*?)</Border>').Groups['body'].Value
if ([string]::IsNullOrWhiteSpace($footerMarkup) -or
    $footerMarkup -notmatch 'Grid.Column="0" ColumnDefinitions="Auto,\*"' -or
    $footerMarkup -notmatch 'Grid.Column="1" Classes="app-footer-text"' -or
    $footerMarkup -notmatch '<Grid ColumnDefinitions="\*,Auto,Auto" ColumnSpacing="12"' -or
    $footerMarkup -notmatch '<StackPanel x:Name="CloudFooter" Grid.Column="1"[^>]*IsVisible="False"' -or
    $footerMarkup -notmatch '<TextBlock Grid.Column="2" Classes="app-footer-meta" Text="\{Binding AppVersionPlatform\}"\s+HorizontalAlignment="Right"' -or
    $footerMarkup -match 'ColumnDefinitions="\*,Auto" ColumnSpacing="0"') {
    throw 'UI footer layout check: database name and metadata must be contiguous (nested Auto,*), with cloud activity in column 1 and version right-aligned in column 2.'
}
Write-Host '[PASS] contiguous database metadata, dedicated cloud activity and right-aligned app version.' -ForegroundColor Green


foreach ($asset in @('CardPrint_black_32.png', 'CardPrint_white_32.png',
                      'CardPrint_black_40.png', 'CardPrint_white_40.png',
                      'CardPrint_black_48.png', 'CardPrint_white_48.png',
                      'CardPrint_black_64.png', 'CardPrint_white_64.png',
                      'CardPrint_black_96.png', 'CardPrint_white_96.png',
                      'CardPrint_black.ico', 'CardPrint_white.ico')) {
    if (-not (Test-Path -LiteralPath (Join-Path $root "source\keepassxccardprint\assets\$asset"))) {
        throw "UI check: supplied Card Print icon resource missing: $asset"
    }
}
# Regression checks for the tightly sized preview toolbar and footer spacing.
# The zoom percentage belongs to the trailing preview-header cell, never
# to the movable (top/side) toolbar. Existing footer spacing is preserved.
if ($chromeXaml -notmatch 'Grid.Column="4" x:Name="PreviewZoomLabel"' -or
    $chromeXaml -notmatch 'PreviewZoomCaption\}' -or
    [regex]::Matches($chromeXaml, 'x:Name="PreviewZoomLabel"').Count -ne 1 -or
    $chromeXaml.IndexOf('x:Name="PreviewZoomLabel"') -gt $chromeXaml.IndexOf('x:Name="PreviewToolbar"') -or
    $chromeXaml -notmatch 'x:Name="MainPanels" Margin="8,8,8,8"') {
    throw 'UI check: zoom percentage must be at far right of the preview header with footer spacing preserved.'
}
$fluentGlyphs = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\rendering\FluentCardGlyphs.cs') -Raw -Encoding UTF8
$fluentDrawer = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\rendering\CardVectorIcons.cs') -Raw -Encoding UTF8
$fluentPicker = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\services\AppearanceDescriptor.cs') -Raw -Encoding UTF8
foreach ($emblem in @('shield','lock','key','card','vault')) {
    if ($fluentGlyphs -notmatch ('"' + $emblem + '" => "M')) {
        throw "UI check: Fluent back emblem missing: $emblem"
    }
}
if ($fluentDrawer -notmatch 'SKPath.ParseSvgPathData' -or
    $fluentPicker -notmatch 'Geometry.Parse\(FluentCardGlyphs.PathData' -or
    $chromeXaml -notmatch 'Data="\{Binding IconGeometry\}"' -or
    $chromeXaml -match 'Source="\{Binding IconPreview\}"') {
    throw 'UI check: back-card icons must share authentic Fluent paths in Skia and themed Avalonia picker.'
}
$footerModel = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\services\CardAppearance.cs') -Raw -Encoding UTF8
$footerLayout = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\rendering\CardLayoutEngine.cs') -Raw -Encoding UTF8
$footerRenderer = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\rendering\CardSkiaRenderer.cs') -Raw -Encoding UTF8
if ($footerModel -notmatch 'FooterBottomInsetMm' -or
    $footerLayout -notmatch 'FooterBottomInsetMm' -or
    $footerRenderer -notmatch 'FooterBottomInsetMm') {
    throw 'UI check: footer offset must use the same geometry for layout and drawing.'
}
Write-Host '[PASS] full-width header/footer, themed Icons8 assets and clean popup styling.' -ForegroundColor Green

# Verify phone formatting is opt-in, scoped to USERNAME, and display-only.
$phonePath = Join-Path $root 'source\keepassxccardprint\services\PhoneLoginFormatter.cs'
$phoneText = Get-Content -LiteralPath $phonePath -Raw -Encoding UTF8
$mappingUi = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\MainWindow.axaml') -Raw -Encoding UTF8
$mappingModel = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\viewmodels\MappingViewModel.cs') -Raw -Encoding UTF8
$previewSource = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\viewmodels\CardPreviewViewModel.cs') -Raw -Encoding UTF8
if (-not $phoneText.Contains('public static string Format(string input, bool enabled)') -or
    -not $mappingUi.Contains('IsChecked="{Binding PhoneFormat, Mode=TwoWay}"') -or
    -not $mappingModel.Contains('PhoneFormat=IsUsernameField') -or
    -not $previewSource.Contains('Task.Run(async () =>') -or
    -not $previewSource.Contains('CancellationTokenSource') -or
    -not $previewSource.Contains('_previewRenderGate.WaitAsync')) {
    throw 'Preview/mapping check: async renderer or optional phone formatting is missing.'
}
Write-Host '[PASS] background preview worker and opt-in phone display formatting.' -ForegroundColor Green

# The singleton guard is only for a normal UI startup. Update/reset workers
# and executable self-tests must remain independently runnable.
$programEntry = Get-Content -LiteralPath (Join-Path $sourceRoot 'platforms\windows\Program.cs') -Raw -Encoding UTF8
$appEntry = Get-Content -LiteralPath (Join-Path $sourceRoot 'App.axaml.cs') -Raw -Encoding UTF8
$singletonSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'platforms\windows\WindowsSingleInstance.cs') -Raw -Encoding UTF8
# Verify the window assignment and attachment use the same local variable.
if ($programEntry -notmatch 'WindowsSingleInstance\.EnterOrActivate\(\)' -or
    $programEntry.IndexOf('WindowsSingleInstance.EnterOrActivate()') -lt $programEntry.IndexOf('--self-test-layout') -or
    $programEntry.IndexOf('WindowsSingleInstance.EnterOrActivate()') -gt $programEntry.IndexOf('ConfigurationMaintenance.CleanOnStartup()') -or
    $appEntry -notmatch 'desktop\.MainWindow\s*=\s*mainWindow\s*;' -or
    $appEntry -notmatch 'WindowsSingleInstance\.Attach\(mainWindow\)' -or
    $singletonSource -notmatch 'new Mutex\(true, MutexName, out var createdNew\)' -or
    $singletonSource -notmatch 'EventWaitHandle\.OpenExisting\(ActivateEventName\)' -or
    $singletonSource -notmatch 'WindowState\.Minimized' -or
    $singletonSource -notmatch 'SetForegroundWindow\(hwnd\)') {
    throw 'STARTUP REGRESSION: one interactive window with existing-window activation is required.'
}
Write-Host '[PASS] one interactive Card Print window; worker and self-test startup preserved.' -ForegroundColor Green

# Fail early if either second-level tab strip disappears or leaves the
# template manager. Both strips must remain navigable for built-in presets.
$mainXaml = Join-Path $root 'source\keepassxccardprint\MainWindow.axaml'
[xml]$ui = Get-Content -LiteralPath $mainXaml -Raw -Encoding UTF8
# Native AOT contract: tab headers must be direct, compiled ShellViewModel bindings.
# A DataTemplate with x:CompileBindings=False generates ReflectionBindingExtension,
# causing IL2026/IL3050 errors during ilc even when normal compilation succeeds.
if ($chromeXaml -match 'x:CompileBindings\s*=\s*"False"' -or
    $chromeXaml -match 'ReflectionBinding') {
    throw 'Native AOT check: reflection bindings are forbidden in MainWindow.axaml.'
}
foreach ($captionProperty in @('TabSource', 'TabTemplate', 'TabSettings', 'TabAbout')) {
    $tabHeader = $ui.SelectSingleNode("//*[local-name()='TabItem.Header']//*[local-name()='TextBlock' and @Text='{Binding $captionProperty}']")
    if ($null -eq $tabHeader) {
        throw "Native AOT check: missing compiled tab header binding: $captionProperty"
    }
}
Write-Host '[PASS] all four tab headers use compiled ShellViewModel bindings (Native AOT).' -ForegroundColor Green
# All three selectors must show identical 14-DIP text in the caption, selection box
# and expanded popup; never revert to the 12-DIP Fluent ComboBoxItem default.
$headerControls = @($ui.SelectNodes("//*[local-name()='Border' and @Classes='app-header']//*[local-name()='ComboBox' and @Classes='header-select']"))
if ($headerControls.Count -ne 3) {
    throw 'UI check: expected exactly three header selectors.'
}
foreach ($selector in $headerControls) {
    $templateText = $selector.SelectSingleNode("./*[local-name()='ComboBox.ItemTemplate']//*[local-name()='TextBlock']")
    $popupFont = $selector.SelectSingleNode("./*[local-name()='ComboBox.Styles']/*[local-name()='Style']/*[local-name()='Setter' and @Property='FontSize']")
    if ($null -eq $templateText -or $templateText.GetAttribute('FontSize') -ne '14' -or
        $null -eq $popupFont -or $popupFont.GetAttribute('Value') -ne '14') {
        throw 'UI check: header selector and popup font sizes must both be 14 DIP.'
    }
}
if ($compactTheme -notmatch '(?s)<Style Selector="TextBlock.chrome-label">.*?<Setter Property="FontSize" Value="14"') {
    throw 'UI check: header captions must match the selector font size (14 DIP).'
}
Write-Host '[PASS] identical 14-DIP header captions, selected text and dropdown items.' -ForegroundColor Green
# The header and About brandmarks use optically padded high-DPI UI art.
# External EXE/window/taskbar icons stay light for every theme; in-app art must switch.
# Retain the general Avalonia XAML guard against unsupported WPF attributes.
$badJoins = @($ui.SelectNodes("//*[local-name()='Path' and @StrokeLineJoin]"))
if ($badJoins.Count -gt 0) {
    throw 'UI check: Path.StrokeLineJoin is not supported by Avalonia 12; use StrokeJoin.'
}
$header = $ui.SelectSingleNode("//*[local-name()='Border' and @Classes='app-header']")
$headerIcon = if ($null -ne $header) {
    $header.SelectSingleNode(".//*[local-name()='Image' and @*[local-name()='Name' and .='AppHeaderIcon']]")
} else { $null }
if ($null -eq $headerIcon -or
    $headerIcon.GetAttribute('Source') -ne 'avares://KeePassXCCardPrint/assets/CardPrint_black_48.png' -or
    $headerIcon.GetAttribute('Width') -ne '48' -or
    $headerIcon.GetAttribute('Height') -ne '48') {
    throw 'UI check: expected the 48-DIP Card Print image in the header.'
}
if ($null -ne $header.SelectSingleNode(".//*[local-name()='Path']")) {
    throw 'UI artwork contract: unrelated shield/checkmark paths must not replace Card Print artwork.'
}
$iconCode = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\MainWindow.axaml.cs') -Raw -Encoding UTF8
foreach ($expected in @('UpdateAppIcon()', 'BrandArtwork.Apply(logo,',
                         'CardPrint_white.ico', 'RenderScaling, 48')) {
    if (-not $iconCode.Contains($expected)) {
        throw "UI check: fixed light Card Print icon support is missing: $expected"
    }
}
if ($iconCode -match 'CardPrint_black\.ico|_titleIconBlack' -or
    $mainWindowText -notmatch 'Icon="avares://KeePassXCCardPrint/assets/CardPrint_white.ico"' -or
    $iconCode -notmatch 'ActualThemeVariant\s*==\s*ThemeVariant\.Dark') {
    throw 'UI check: main window/taskbar icon must stay light, while the in-app header icon must react to theme.'
}
Write-Host '[PASS] supplied Card Print header image, titlebar icon and Avalonia 12 Path attributes.' -ForegroundColor Green
# Keep application branding consistent on the executable, About page and every dialog.
$aboutBrand = Get-Content -LiteralPath (Join-Path $sourceRoot 'AboutView.axaml') -Raw -Encoding UTF8
$aboutBrandCode = Get-Content -LiteralPath (Join-Path $sourceRoot 'AboutView.axaml.cs') -Raw -Encoding UTF8
if ($projectText -notmatch '<ApplicationIcon>assets\\CardPrint_white\.ico</ApplicationIcon>' -or
    $aboutBrand -notmatch 'x:Name="AboutAppIcon" Width="32" Height="32"' -or
    $aboutBrand -notmatch 'RowDefinitions="Auto,Auto"' -or
    $aboutBrand -notmatch 'Grid.Column="1" Grid.Row="1" SourceText="\{Binding Purpose\}"' -or
    $aboutBrand -notmatch '<controls:MetadataWrapPanel' -or
    $aboutBrand -notmatch '<controls:UiProseTextBlock Grid.Column="1" Grid.Row="1"' -or
    $aboutBrand -match '<Border[^>]*>\s*<Image[^>]*x:Name="AboutAppIcon"' -or
    $aboutBrandCode -notmatch 'BrandArtwork\.Apply\(AboutAppIcon' -or
    $aboutBrandCode -notmatch 'scaling, 32' -or
    $printIconSource -notmatch 'CardPrint_white\.ico' -or
    $printIconSource -match 'CardPrint_black\.ico') {
    throw 'APP BRANDING REGRESSION: light executable/window icons, theme-specific in-app artwork and no About-icon backdrop must remain intact.'
}
Write-Host '[PASS] consistent CardPrint branding in executable, About page and dialogs.' -ForegroundColor Green
# Main tabs share the page background; only their cards use the surface brush.
[xml]$aboutPageSurface = $aboutBrand
[xml]$settingsPageSurface = Get-Content -LiteralPath (Join-Path $sourceRoot 'SettingsView.axaml') -Raw -Encoding UTF8
foreach ($pageSurface in @($aboutPageSurface, $settingsPageSurface)) {
    if ($pageSurface.DocumentElement.GetAttribute('Background') -ne '{DynamicResource KpTabWindowBrush}' -or
        $null -eq $pageSurface.DocumentElement.SelectSingleNode("./*[local-name()='ScrollViewer']/*[local-name()='StackPanel' and @Margin='6' and @Spacing='8']")) {
        throw 'UI check: Settings and About must share the main tab background and compact page spacing.'
    }
}
$identityCard = $aboutPageSurface.DocumentElement.SelectSingleNode("./*[local-name()='ScrollViewer']/*[local-name()='StackPanel']/*[local-name()='Border' and @Classes='settings-card' and @*[local-name()='Name']='AboutIdentityCard']")
if ($null -eq $identityCard -or
    $null -eq $identityCard.SelectSingleNode(".//*[local-name()='Image' and @*[local-name()='Name']='AboutAppIcon']") -or
    $null -eq $identityCard.SelectSingleNode(".//*[local-name()='TextBlock' and @Text='{Binding AppTitle}']") -or
    $null -eq $identityCard.SelectSingleNode(".//*[local-name()='MetadataWrapPanel']//*[local-name()='TextBlock' and @*[local-name()='Name']='AboutRepositoryLink']") -or
    $null -eq $identityCard.SelectSingleNode(".//*[local-name()='UiProseTextBlock' and @SourceText='{Binding Purpose}']")) {
    throw 'UI check: About identity card must contain the artwork, title, wrapped metadata/link and dictionary-aware description.'
}
Write-Host '[PASS] unified tab backgrounds and complete About identity card.' -ForegroundColor Green
$brandSelector = Get-Content -LiteralPath (Join-Path $sourceRoot 'services\BrandArtwork.cs') -Raw -Encoding UTF8
if ($brandSelector -notmatch 'nativePixels / scale' -or
    $brandSelector -notmatch 'new Bitmap\(stream\)' -or
    $brandSelector -notmatch 'NativeSizes' -or
    $brandSelector -match '\.(?:Resize|Resample|Blur|GrayBorder)\s*\(' -or
    $chromeXaml -notmatch 'x:Name="AppHeaderIcon" Width="48" Height="48"' -or
    $aboutBrand -notmatch 'x:Name="AboutAppIcon" Width="32" Height="32"') {
    throw 'APP BRANDING REGRESSION: internal icons must use native source pixels at current DPI.'
}
Write-Host '[PASS] unscaled, DPI-selected source frames for both in-app brandmarks.' -ForegroundColor Green



# Live crossfade regression: one direct Skia control must explicitly blend
# both scenes in SKPaint, not animate Opacity on a separate native draw op.
$skiaPreview = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\controls\SkiaPreviewControl.cs') -Raw -Encoding UTF8
$previewNode = @($ui.SelectNodes("//*[local-name()='SkiaPreviewControl']"))
if ($previewNode.Count -ne 1 -or
    $previewNode[0].GetAttribute('PreviousScene') -ne '{Binding PreviewPreviousScene}' -or
    $previewNode[0].GetAttribute('PreviousOpacity') -ne '{Binding PreviewOldOpacity}' -or
    $previewNode[0].HasAttribute('Opacity') -or
    $previewNode[0].GetAttribute('HighlightSection') -ne '{Binding TemplateHighlightSection}' -or
    $previewNode[0].GetAttribute('HighlightGroup') -ne '{Binding TemplateHighlightGroup}' -or
    $previewNode[0].GetAttribute('HoverRegion') -ne '{Binding TemplateHoverRegion}' -or
    $previewNode[0].GetAttribute('SelectedRegion') -ne '{Binding TemplateSelectedRegion}' -or
    -not $skiaPreview.Contains('AffectsRender<SkiaPreviewControl>(SceneProperty,PreviousSceneProperty,PreviousOpacityProperty,MonochromeProperty,HighlightSectionProperty,HighlightGroupProperty,HoverRegionProperty,SelectedRegionProperty)') -or
    -not $skiaPreview.Contains('canvas.DrawPicture(scene.Picture,paint)') -or
    -not $skiaPreview.Contains('_previousOpacity*255')) {
    throw 'UI check: custom Skia preview must blend current/previous pictures using real paint alpha on every fade frame.'
}
Write-Host '[PASS] live Skia crossfade and editor highlights use the single preview control.' -ForegroundColor Green
# Fluent UI System Icons must replace custom toolbar paths while leaving the
# original brand/titlebar icon and record-specific KeePass icon images intact.
if ($chromeXaml -notmatch 'xmlns:fi="using:FluentIcons.Avalonia"' -or
    $chromeXaml -notmatch 'PreviewDuplexAvailable' -or
    $chromeXaml -notmatch 'Monochrome="{Binding PreviewMonochrome}"' -or
    $chromeXaml -notmatch 'Slider Grid.Column="1" Minimum="50" Maximum="500"' -or
    $chromeXaml -match 'PreviewAnimationDurationOptions' -or
    $skiaPreview -notmatch 'SKColorFilter.CreateColorMatrix' -or
    $chromeXaml -match 'StrokeLineJoin') {
    throw 'UI check: Fluent toolbar, mono preview, duplex states or animation slider is missing.'
}
Write-Host '[PASS] Fluent toolbar, duplex controls, 50-500ms slider and preview grayscale.' -ForegroundColor Green


$editorTab = $ui.SelectSingleNode("//*[local-name()='TabItem' and *[local-name()='TabItem.Header']//*[local-name()='TextBlock' and @Text='{Binding TabTemplate}']]")
if ($null -eq $editorTab) { throw 'UI check: missing unified template tab.' }
$strip = $editorTab.SelectSingleNode(".//*[local-name()='ListBox' and @ItemsSource='{Binding TemplateEditorSections}']")
$groups = $editorTab.SelectSingleNode(".//*[local-name()='ItemsControl' and @ItemsSource='{Binding TemplateEditorGroups}']")
if ($null -eq $strip -or $strip.GetAttribute('SelectedItem') -notmatch 'SelectedTemplateEditorSection' -or
    $null -eq $groups -or $null -ne $strip.SelectSingleNode("ancestor::*[@IsEnabled='{Binding CanEditAppearance}']") -or
    @($ui.SelectNodes("//*[local-name()='SkiaPreviewControl']")).Count -ne 1 -or
    @($ui.SelectNodes("//*[local-name()='Border' and @Classes='template-toolbar']")).Count -ne 1) {
    throw 'UI check: one preview, one template toolbar and browsable section navigation are required.'
}
$fieldEditor = $groups.SelectSingleNode(".//*[local-name()='Grid' and @Width='{Binding TemplateFieldWidth}']")
$autoMode = $groups.SelectSingleNode(".//*[local-name()='CheckBox' and @IsChecked='{Binding AutomaticValue,Mode=TwoWay}']")
$navGlyph = $strip.SelectSingleNode(".//*[local-name()='FluentIcon' and @Icon='{Binding Glyph}']")
if ($null -eq $fieldEditor -or $null -eq $autoMode -or $null -eq $navGlyph -or
    $null -eq $groups.SelectSingleNode(".//*[local-name()='WrapPanel']") -or
    $navGlyph.GetAttribute('IconVariant') -ne 'Regular' -or
    $null -eq $groups.SelectSingleNode(".//*[local-name()='UiProseTextBlock' and @SourceText='{Binding Help}' and @Classes='hint' and @TextWrapping='Wrap' and @IsVisible='{Binding IsPrintPermissionGroup}']")) {
    throw 'UI check: compact field groups, automatic sizes, mapping restriction help and Fluent navigation are required.'
}
# Natural label height, fixed compact editor height and bottom alignment in
# WrapPanel's per-line layout slot keep controls aligned without reserving a
# second caption line for every field. No fixed ItemHeight on the WrapPanel.
$labelBand = $fieldEditor.SelectSingleNode("./*[local-name()='TextBlock' and @Text='{Binding EditorLabel}']")
$valueBand = $fieldEditor.SelectSingleNode("./*[local-name()='StackPanel' and @Grid.Row='1']")
$boolEditor = $fieldEditor.SelectSingleNode("./*[local-name()='CheckBox' and @IsChecked='{Binding Checked,Mode=TwoWay}']")
$choiceEditor = $fieldEditor.SelectSingleNode("./*[local-name()='ComboBox' and @SelectedItem='{Binding SelectedChoice,Mode=TwoWay}']")
$fieldWrap = $fieldEditor.SelectSingleNode("ancestor::*[local-name()='ItemsControl'][1]/*[local-name()='ItemsControl.ItemsPanel']//*[local-name()='WrapPanel']")
if ($fieldEditor.GetAttribute('RowDefinitions') -ne 'Auto,26' -or
    $fieldEditor.GetAttribute('VerticalAlignment') -ne 'Bottom' -or
    $fieldEditor.GetAttribute('RowSpacing') -ne '2' -or
    $null -eq $fieldWrap -or $fieldWrap.GetAttribute('Orientation') -ne 'Horizontal' -or $fieldWrap.HasAttribute('ItemHeight') -or
    $null -eq $labelBand -or $labelBand.GetAttribute('Grid.Row') -ne '0' -or
    $labelBand.GetAttribute('MaxLines') -ne '2' -or $labelBand.GetAttribute('VerticalAlignment') -ne 'Bottom' -or
    $labelBand.HasAttribute('IsVisible') -or $labelBand.HasAttribute('Height') -or $labelBand.HasAttribute('MinHeight') -or
    $null -eq $valueBand -or $valueBand.GetAttribute('VerticalAlignment') -ne 'Center' -or
    $valueBand.GetAttribute('Orientation') -ne 'Horizontal' -or $valueBand.GetAttribute('Height') -ne '26' -or
    $null -eq $valueBand.SelectSingleNode("./*[local-name()='Grid' and @IsVisible='{Binding ManualTextVisible}']") -or
    $null -eq $valueBand.SelectSingleNode("./*[local-name()='CheckBox' and @IsChecked='{Binding AutomaticValue,Mode=TwoWay}']") -or
    $autoMode.GetAttribute('Padding') -ne '6,0,0,0' -or
    $null -eq $boolEditor -or $boolEditor.GetAttribute('Grid.Row') -ne '1' -or
    $boolEditor.GetAttribute('VerticalAlignment') -ne 'Center' -or $boolEditor.GetAttribute('Height') -ne '26' -or
    $null -eq $choiceEditor -or $choiceEditor.GetAttribute('Grid.Row') -ne '1' -or
    $choiceEditor.GetAttribute('VerticalAlignment') -ne 'Center' -or $choiceEditor.GetAttribute('Height') -ne '26') {
    throw 'UI check: template fields must use natural caption heights and bottom-aligned compact editor rows.'
}
$choiceGlyph = $choiceEditor.SelectSingleNode(".//*[local-name()='Path' and @Data='{Binding IconGeometry}']")
if ($null -eq $choiceGlyph -or $choiceGlyph.GetAttribute('Width') -ne '16' -or
    $choiceGlyph.GetAttribute('Height') -ne '16' -or
    $choiceGlyph.GetAttribute('VerticalAlignment') -ne 'Center' -or
    $choiceGlyph.GetAttribute('Stretch') -ne 'Uniform') {
    throw 'UI check: reverse emblem picker must use centered 16-DIP glyphs inside the compact 26-DIP ComboBox.'
}
$colorEditor = $fieldEditor.SelectSingleNode("./*[local-name()='Button' and @Click='OnTemplateColorPick']")
$palettePicker = $groups.SelectSingleNode(".//*[local-name()='Button' and @Click='OnTemplatePalettePick']")
$previewPage = $ui.SelectSingleNode("//*[local-name()='Grid' and @*[local-name()='Name']='PreviewPageHost']")
$previewCaption = $ui.SelectSingleNode("//*[local-name()='Border' and @*[local-name()='Name']='TemplateHighlightCaptionOverlay']")
if ($null -eq $colorEditor -or $colorEditor.GetAttribute('Grid.Row') -ne '1' -or
    $colorEditor.GetAttribute('Height') -ne '26' -or $colorEditor.GetAttribute('IsEnabled') -ne '{Binding IsEditable}' -or
    $null -eq $colorEditor.SelectSingleNode(".//*[local-name()='TextBlock' and @Text='{Binding ColorCaption}']") -or
    $null -eq $palettePicker -or $palettePicker.GetAttribute('IsVisible') -ne '{Binding IsPalette}' -or
    $null -eq $previewPage -or $previewPage.GetAttribute('PointerPressed') -ne 'OnTemplatePreviewPointerPressed' -or
    $previewPage.GetAttribute('PointerMoved') -ne 'OnTemplatePreviewPointerMoved' -or
    $previewPage.GetAttribute('PointerReleased') -ne 'OnTemplatePreviewPointerReleased' -or
    $previewPage.GetAttribute('PointerExited') -ne 'OnTemplatePreviewPointerExited' -or
    $null -eq $previewCaption -or $previewCaption.GetAttribute('IsHitTestVisible') -ne 'False' -or
    $previewCaption.GetAttribute('IsVisible') -ne '{Binding TemplateHighlightCaptionVisible}') {
    throw 'UI check: shared palette, individual source picker or Ctrl preview interaction is missing.'
}
$editorCode = Get-Content -LiteralPath (Join-Path $sourceRoot 'MainWindow.TemplateEditor.cs') -Raw -Encoding UTF8
if ($editorCode -notmatch 'KeyModifiers\.Control' -or $editorCode -notmatch 'PreviewHitRegion\.CanCommit\(' -or
    $editorCode -notmatch 'OnTemplatePreviewKeyUp' -or $editorCode -notmatch 'SetTemplatePreviewHover\(null\)') {
    throw 'UI check: preview selection must require Ctrl, and releasing Ctrl must clear hover.'
}
$templatePreviewCode = Get-Content -LiteralPath (Join-Path $sourceRoot 'viewmodels\TemplatePreviewViewModel.cs') -Raw -Encoding UTF8
if ($editorCode -match '!_vm\.TemplateEditorActive' -or
    $editorCode -match '_vm\.TemplateEditorActive\s*&&' -or
    $templatePreviewCode -notmatch 'TemplateHoverRegion\s*=>\s*_templateHoverRegion\s*;' -or
    $editorCode -notmatch 'Key\.Escape' -or
    $editorCode -notmatch 'OnTemplatePreviewViewportPressed' -or
    $editorCode -notmatch 'Equals\(_vm\.TemplateSelectedRegion,hit\)' -or
    $templatePreviewCode -notmatch 'public void ClearTemplatePreviewSelection\(') {
    throw 'UI check: Ctrl preview navigation must work on every tab and fixed highlights must be dismissible.'
}
Write-Host '[PASS] unified editor, palette sources, single preview and Ctrl selection.' -ForegroundColor Green
# Guard against accidental reintroduction of two-stage field mapping edits.
# The compact mapping toolbar uses 4 columns: title, scope, status, Save.
$mappingTab = $ui.SelectSingleNode("//*[local-name()='TabItem' and *[local-name()='TabItem.Header']//*[local-name()='TextBlock' and @Text='{Binding TabTemplate}']]")
if ($null -eq $mappingTab) { throw 'UI check: missing template tab.' }
$toolbar = $mappingTab.SelectSingleNode(".//*[local-name()='Border' and @Classes='template-toolbar' and @Grid.Row='0']")
$mappingSave = $toolbar.SelectSingleNode(".//*[local-name()='Button' and @Click='OnSaveAppearance']")
$mappingCreate = $toolbar.SelectSingleNode(".//*[local-name()='Button' and @Click='OnSaveCardTemplate']")
$mappingRename = $toolbar.SelectSingleNode(".//*[local-name()='Button' and @Click='OnRenameCardTemplate']")
$mappingDelete = $toolbar.SelectSingleNode(".//*[local-name()='Button' and @Click='OnDeleteCardTemplate']")
$mappingSelect = $toolbar.SelectSingleNode(".//*[local-name()='ComboBox' and @ItemsSource='{Binding TemplateNames}']")
$mappingReset = $mappingTab.SelectSingleNode(".//*[local-name()='Button' and @Click='OnResetMapping']")
$mappingApply = $mappingTab.SelectSingleNode(".//*[local-name()='Button' and @Click='OnApplyMapping']")
$domainButton = $mappingTab.SelectSingleNode(".//*[local-name()='Button' and @Click='OnEditDomains']")
$fieldListHeading = $mappingTab.SelectSingleNode(".//*[local-name()='TextBlock' and @Text='{Binding MappingFieldsHeading}']")
if ($null -eq $toolbar -or $null -eq $mappingSave -or $null -eq $mappingCreate -or
    $null -eq $mappingRename -or $null -eq $mappingDelete -or
    $null -eq $mappingSelect -or $null -eq $fieldListHeading -or $null -eq $domainButton -or
    $mappingSelect.GetAttribute('SelectionChanged') -ne 'OnCardTemplateSelectionChanged' -or
    $mappingSave.GetAttribute('IsEnabled') -ne '{Binding CanSaveTemplateChanges}' -or
    $mappingCreate.GetAttribute('Grid.Column') -ne '2' -or
    $mappingRename.GetAttribute('Grid.Column') -ne '3' -or
    $mappingDelete.GetAttribute('Grid.Column') -ne '4' -or
    $mappingRename.GetAttribute('IsEnabled') -ne '{Binding CanManagePersonalTemplate}' -or
    $mappingDelete.GetAttribute('IsEnabled') -ne '{Binding CanManagePersonalTemplate}' -or
    $mappingSave.GetAttribute('Grid.Column') -ne '6' -or
    $mappingSave.ParentNode.GetAttribute('ColumnDefinitions') -ne 'Auto,Auto,Auto,Auto,Auto,*,Auto' -or
    $mappingSelect.GetAttribute('Grid.Column') -ne '1' -or
    $mappingSelect.ParentNode -ne $mappingSave.ParentNode -or
    $mappingCreate.ParentNode -ne $mappingSave.ParentNode -or
    $mappingRename.ParentNode -ne $mappingSave.ParentNode -or
    $mappingDelete.ParentNode -ne $mappingSave.ParentNode -or
    $mappingSave.ParentNode.ParentNode.LocalName -ne 'ScrollViewer' -or
    $mappingSave.ParentNode.ParentNode.GetAttribute('HorizontalScrollBarVisibility') -ne 'Auto' -or
    $mappingSave.ParentNode.ParentNode.GetAttribute('VerticalScrollBarVisibility') -ne 'Disabled' -or
    [regex]::Matches($chromeXaml, 'Click="OnSaveMapping"').Count -ne 0 -or
    [regex]::Matches($chromeXaml, 'Click="OnSaveAppearance"').Count -ne 1) {
    throw 'UI check: Template must have one complete-template toolbar.'
}
if ($null -eq $mappingReset -or $null -ne $mappingApply -or
    $mappingReset.GetAttribute('IsEnabled') -ne '{Binding MappingCanEdit}' -or
    $domainButton.GetAttribute('Grid.Column') -ne '1' -or
    $fieldListHeading.ParentNode.GetAttribute('Classes') -ne 'section-heading-row' -or
    $fieldListHeading.ParentNode.ParentNode.GetAttribute('Grid.Row') -ne '0' -or
    $domainButton.ParentNode -ne $fieldListHeading.ParentNode.ParentNode -or
    $null -eq $fieldListHeading.ParentNode.ParentNode.ParentNode.SelectSingleNode("./*[local-name()='WrapPanel' and @Grid.Row='1']")) {
    throw 'UI check: domain action and view checkboxes must be in the field list header, editor respects read-only templates.'
}
Write-Host '[PASS] one print-template toolbar in all editors; built-in field mapping is read-only.' -ForegroundColor Green
# Both mapping tables expose Section, not the derived OutputGroup. Keep headers
# and item templates in the same order, including the last link cell.
foreach ($mode in @('MappingCompactVisible', 'MappingDetailedVisible')) {
    $table = $mappingTab.SelectSingleNode(".//*[local-name()='Border' and @IsVisible='{Binding $mode}']/*[local-name()='ScrollViewer']/*[local-name()='Grid']")
    if ($null -eq $table) { throw "UI check: missing mapping table $mode." }
    $header = $table.SelectSingleNode("./*[local-name()='Border' and @Classes='table-header']/*[local-name()='Grid']")
    $row = $table.SelectSingleNode("./*[local-name()='ListBox' and @ItemsSource='{Binding FieldRows}']/*[local-name()='ListBox.ItemTemplate']/*[local-name()='DataTemplate']/*[local-name()='Grid']")
    if ($null -eq $header -or $null -eq $row -or
        $header.GetAttribute('ColumnDefinitions') -ne $row.GetAttribute('ColumnDefinitions')) {
        throw "UI check: mapping header and row widths differ in $mode."
    }
    if ($mode -eq 'MappingCompactVisible') {
        $expectedHeaders = @('Field','Section','Priority','Visibility','MappingLinkText')
        $expectedValues = @('PositionText','Field','Section','Order','Visibility','LinkText')
    } else {
        $expectedHeaders = @('Field','Section','Priority','Format','Visibility','Label','LongValue','Grouping','MappingPaginationText','MappingLinkText')
        $expectedValues = @('PositionText','Field','Section','Order','Role','Visibility','Heading','Fit','Grouping','Pagination','LinkText')
    }
    $headCells = @($header.SelectNodes('./*'))
    $rowCells = @($row.SelectNodes('./*'))
    $count = $expectedValues.Count
    if ($headCells.Count -ne $count -or $rowCells.Count -ne $count -or
        ($header.GetAttribute('ColumnDefinitions') -split ',').Count -ne $count -or
        $headCells[0].GetAttribute('Text') -ne '№') {
        throw "UI check: unexpected mapping column count in $mode."
    }
    for ($i = 0; $i -lt $count; $i++) {
        if ([int]$headCells[$i].GetAttribute('Grid.Column') -ne $i -or
            [int]$rowCells[$i].GetAttribute('Grid.Column') -ne $i) {
            throw "UI check: mapping column index mismatch in $mode."
        }
        if ($i -gt 0 -and $headCells[$i].GetAttribute('Text') -ne ('{Binding ' + $expectedHeaders[$i - 1] + '}')) {
            throw "UI check: mapping header binding mismatch in $mode."
        }
        $valueBinding = '{Binding ' + $expectedValues[$i] + '}'
        if ($i -eq ($count - 1)) {
            if ($null -eq $rowCells[$i].SelectSingleNode("./*[local-name()='TextBlock' and @Text='$valueBinding']")) {
                throw "UI check: missing mapping link cell in $mode."
            }
        } elseif ($rowCells[$i].GetAttribute('Text') -ne $valueBinding) {
            throw "UI check: mapping row binding mismatch in $mode."
        }
    }
}
Write-Host '[PASS] mapping tables retain Section and aligned columns without Output group.' -ForegroundColor Green
# Template data and mapping must be persisted together, never via mapping.json.
$templatesSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'services\CardAppearance.cs') -Raw -Encoding UTF8
$mapSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'viewmodels\MappingViewModel.cs') -Raw -Encoding UTF8
if ($templatesSource -notmatch 'UpdatePersonalTemplate\(string path, CardAppearance model, Dictionary<string, FieldRule> mapping\)' -or
    $templatesSource -notmatch 'VaultJsonContext.Default.DictionaryStringFieldRule' -or
    $mapSource -match 'FieldMappingStore.Save\(' -or
    $mapSource -match 'public bool SaveMapping\(' -or
    $mapSource -notmatch 'MappingEditor\.SetEditable\(CanEditAppearance\)' -or
    $mapSource -notmatch 'if\(!CanEditAppearance \|\| SelectedMappingRow is null\)return;' -or
    $mainWindowText -match 'Click="OnSaveMapping"') {
    throw 'UI check: mapping must be saved only as part of the complete print template.'
}
Write-Host '[PASS] unified template persistence and no standalone mapping Save.' -ForegroundColor Green
# Source and mapping must be bounded siblings, not vertically scrolling pages.
foreach ($pageName in @('SourcePage', 'MappingPage')) {
    $page = $ui.SelectSingleNode("//*[local-name()='Grid' and @*[local-name()='Name']='$pageName']")
    if ($null -eq $page -or $page.GetAttribute('ClipToBounds') -ne 'True' -or
        $null -ne $page.SelectSingleNode("ancestor::*[local-name()='ScrollViewer']")) {
        throw "UI check: $pageName must be a bounded, non-scrolling grid."
    }
}
foreach ($oldName in @('SourceTabScroll','MappingTabScroll')) {
    if ($null -ne $ui.SelectSingleNode("//*[@*[local-name()='Name']='$oldName']")) {
        throw "UI check: obsolete outer scrolling viewport $oldName."
    }
}
if ($null -eq $ui.SelectSingleNode("//*[local-name()='GridSplitter' and @*[local-name()='Name']='MappingSplitter' and @ResizeDirection='Rows']") -or
    $null -eq $ui.SelectSingleNode("//*[local-name()='ToggleButton' and @*[local-name()='Name']='SourceConnectionToggle' and @Click='OnSourceConnectionToggle']") -or
    $null -eq $ui.SelectSingleNode("//*[local-name()='ToggleButton' and @*[local-name()='Name']='SourceConnectionToggle' and *[local-name()='StackPanel']/*[local-name()='FluentIcon' and @Icon='Database'] and *[local-name()='StackPanel']/*[local-name()='TextBlock' and @Text='{Binding SourceTitle}']]") -or
    $null -eq $ui.SelectSingleNode("//*[local-name()='Grid' and *[local-name()='TextBlock' and @Text='{Binding Records}'] and *[local-name()='FluentIcon' and @Icon='TextBulletList']]") -or
    $null -eq $ui.SelectSingleNode("//*[local-name()='ScrollViewer' and @*[local-name()='Name']='SourceConnectionScroll' and @HorizontalScrollBarVisibility='Disabled']/*[local-name()='Grid' and @Margin='0,4,4,0']") -or
    $null -eq $ui.SelectSingleNode("//*[local-name()='ScrollViewer' and @*[local-name()='Name']='MappingFieldScroll' and @VerticalScrollBarVisibility='Auto']")) {
    throw 'UI check: adaptive source and mapping child scroll areas / splitter are missing.'
}
[xml]$aboutScrollUi = Get-Content -LiteralPath (Join-Path $sourceRoot 'AboutView.axaml') -Raw -Encoding UTF8
if ($null -eq $aboutScrollUi.DocumentElement.SelectSingleNode("./*[local-name()='ScrollViewer' and @VerticalScrollBarVisibility='Auto']")) {
    throw 'UI check: About tab must expose its vertical scroll viewport.'
}
# Early layout contract: the first two tabs use the same rounded settings
# cards as Appearance/Duplex. Verify control ownership and not only card count.
$sourceTab = $ui.SelectSingleNode("//*[local-name()='TabItem' and *[local-name()='TabItem.Header']//*[local-name()='TextBlock' and @Text='{Binding TabSource}']]")
if ($null -eq $sourceTab) { throw 'UI check: Source tab is missing.' }
$sourceCards = @($sourceTab.SelectNodes("./*[local-name()='Grid' and @*[local-name()='Name']='SourcePage']/*[local-name()='Border' and @Classes='settings-card']"))
if ($sourceCards.Count -ne 2 -or $sourceCards[0].GetAttribute('Grid.Row') -ne '0' -or
    $sourceCards[1].GetAttribute('Grid.Row') -ne '1') {
    throw 'UI check: Source must contain two rounded cards (connection and records).'
}
foreach ($handler in @('OnBrowseDatabase', 'OnBrowseKeyFile', 'OnOpenDatabase', 'OnCloseDatabase', 'OnRefreshYubi')) {
    if ($null -eq $sourceCards[0].SelectSingleNode(".//*[local-name()='Button' and @Click='$handler']")) {
        throw "UI check: missing Source action in connection card: $handler"
    }
}
foreach ($handler in @('OnSelectAll', 'OnSelectNone')) {
    if ($null -eq $sourceCards[1].SelectSingleNode(".//*[local-name()='Button' and @Click='$handler']")) {
        throw "UI check: missing records action: $handler"
    }
}
if ($null -eq $sourceCards[1].SelectSingleNode(".//*[local-name()='ItemsControl' and @ItemsSource='{Binding VaultRecords}']")) {
    throw 'UI check: records list must be inside its rounded card.'
}
$mappingCards = @($mappingTab.SelectNodes(".//*[local-name()='Grid' and @*[local-name()='Name']='MappingPage']/*[local-name()='Border' and @Classes='settings-card']"))
if ($mappingCards.Count -ne 2 -or $mappingCards[0].GetAttribute('Grid.Row') -ne '0' -or
    $mappingCards[1].GetAttribute('Grid.Row') -ne '2') {
    throw 'UI check: Mapping must contain its shared template toolbar plus field list and editor cards.'
}
if ($null -eq $toolbar.SelectSingleNode(".//*[local-name()='Button' and @Click='OnSaveAppearance']") -or
    $null -eq $mappingCards[0].SelectSingleNode(".//*[local-name()='ListBox' and @ItemsSource='{Binding FieldRows}']") -or
    $null -eq $mappingCards[1].SelectSingleNode(".//*[local-name()='Button' and @Click='OnResetMapping']")) {
    throw 'UI check: Mapping panel content or complete-template Save / Reset ownership is incorrect.'
}
Write-Host '[PASS] rounded Source and Mapping panels and complete-template actions.' -ForegroundColor Green

# Startup UX contract: opening must not restore the previous editing tab,
# and a previously configured database must make the password box ready.
$tabControl = $ui.SelectSingleNode("//*[local-name()='TabControl' and @*[local-name()='Name']='MainTabs']")
$passwordInput = $sourceTab.SelectSingleNode(".//*[local-name()='TextBox' and @*[local-name()='Name']='PasswordBox']")
if ($null -eq $tabControl -or $tabControl.GetAttribute('SelectedIndex') -ne '{Binding ActiveTab, Mode=TwoWay}' -or
    $null -eq $passwordInput -or $passwordInput.GetAttribute('KeyDown') -ne 'OnPasswordKeyDown') {
    throw 'UI check: main tab selection or password entry wiring is missing.'
}
$mainCode = Get-Content -LiteralPath (Join-Path $root 'source\keepassxccardprint\MainWindow.axaml.cs') -Raw -Encoding UTF8
foreach ($expected in @('_settings.ActiveTab = 0;', '!string.IsNullOrWhiteSpace(_vm.DatabasePath)',
                           'DispatcherPriority.Loaded', 'passwordBox.Focus();')) {
    if (-not $mainCode.Contains($expected)) {
        throw "UI check: missing startup navigation / password focus behavior: $expected"
    }
}
Write-Host '[PASS] startup tab reset and deferred focus on restored password box.' -ForegroundColor Green
# Avalonia 12 focus events carry FocusChangedEventArgs. Catch obsolete
# event signatures before the managed build to avoid another long AOT cycle.
if ($mainCode.Contains('GotFocusEventArgs') -or
    $mainCode -notmatch 'OnPasswordGotFocus\(object\? sender, FocusChangedEventArgs e\)' -or
    $mainCode -notmatch 'OnPasswordLostFocus\(object\? sender, FocusChangedEventArgs e\)' -or
    $passwordInput.GetAttribute('GotFocus') -ne 'OnPasswordGotFocus' -or
    $passwordInput.GetAttribute('LostFocus') -ne 'OnPasswordLostFocus') {
    throw 'UI check: Avalonia 12 password focus handlers must use FocusChangedEventArgs.'
}
Write-Host '[PASS] Avalonia 12 password focus event signatures.' -ForegroundColor Green

# Avalonia 12 moved IClipboard.SetTextAsync from an instance member into an
# extension method. Catch a missing namespace import before the managed build.
$aboutViewSource = Get-Content -LiteralPath (Join-Path $sourceRoot 'AboutView.axaml.cs') -Raw -Encoding UTF8
if ($aboutViewSource -match '\bclipboard\.SetTextAsync\s*\(' -and
    $aboutViewSource -notmatch '(?m)^using Avalonia\.Input\.Platform;\s*$') {
    throw 'AVALONIA 12 CLIPBOARD: AboutView must import Avalonia.Input.Platform for SetTextAsync.'
}
Write-Host '[PASS] Avalonia 12 clipboard extension import.' -ForegroundColor Green






# The repository must select .NET 10 even when the Windows runner also has
# .NET 11. setup-dotnet installs an SDK but does not pin SDK resolution.
$globalJsonPath = Join-Path $root 'global.json'
if (-not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
    throw 'SDK SELECTION CONTRACT: missing root global.json; Windows runners may select .NET 11.'
}
try {
    $sdkPolicy = Get-Content -LiteralPath $globalJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $minimumSdk = [Version]::Parse([string]$sdkPolicy.sdk.version)
}
catch {
    throw 'SDK SELECTION CONTRACT: invalid global.json SDK policy.'
}
if ($minimumSdk.Major -ne 10 -or $minimumSdk.Minor -ne 0 -or
    $minimumSdk -lt [Version]'10.0.114' -or
    [string]$sdkPolicy.sdk.rollForward -cne 'latestFeature' -or
    $sdkPolicy.sdk.allowPrerelease -ne $false) {
    throw 'SDK SELECTION CONTRACT: pin patched .NET 10.0 with latestFeature and no prereleases.'
}
Write-Host '[PASS] patched .NET 10 SDK selection policy (global.json).' -ForegroundColor Green
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is required to build.'
}
$sdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sdk.StartsWith('10.')) {
    throw "Expected the .NET 10 SDK; found: $sdk"
}

# Microsoft advisory GHSA-p3jp-9v83-76w9 / CVE-2026-111497:
# NativeAOT's bundled Brotli decoder is vulnerable before ILCompiler 10.0.14.
# Microsoft released patched .NET 10 SDKs 10.0.114 and 10.0.403.
# Reject vulnerable SDKs before resolving packages or compiling Native AOT.
# The NuGet graph below remains the final authority for other SDK feature bands.
if ($sdk -match '^10\.0\.(\d+)$') {
    $sdkBuild = [int]$Matches[1]
    if (($sdkBuild -ge 100 -and $sdkBuild -lt 114) -or
        ($sdkBuild -ge 400 -and $sdkBuild -lt 403)) {
        throw "SECURITY: .NET SDK $sdk is outdated (CVE-2026-111497). Install .NET 10 SDK 10.0.114 or 10.0.403 (or newer patched version) from https://dotnet.microsoft.com/download/dotnet/10.0 then rerun build.bat."
    }
}

# Eight specialist variable TTFs are committed to fonts/ (Recursive serves two choices).
# A clean checkout must build offline with respect to fonts; never download them.
$fontDirectory = Join-Path $root 'fonts'
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'verify_fonts.ps1') -FontDirectory $fontDirectory
if ($LASTEXITCODE -ne 0) { throw 'Bundled font verification failed; source checkout is incomplete or modified.' }

# Verify what will ACTUALLY be linked, rather than assuming that an installed
# runtime or an SDK version string guarantees a patched Native AOT compiler.
Write-Host "Security preflight: resolving Native AOT package with .NET SDK $sdk ..."
& dotnet restore $project -r win-x64 -p:PublishAot=true -p:SelfContained=true -p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore / security preflight failed.' }
$assetsPath = Join-Path (Split-Path -Parent $project) 'obj\project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
    throw 'Security preflight: NuGet restore did not create project.assets.json.'
}
$assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$resolved = @($assets.libraries.PSObject.Properties.Name)
$ilcPackages = @($resolved | Where-Object { $_ -match '^Microsoft\.DotNet\.ILCompiler/' })
if ($ilcPackages.Count -ne 1) {
    throw ('SECURITY: unable to verify selected Native AOT compiler (found: ' +
        ($ilcPackages -join ', ') + '). Refusing to publish unverified binaries.')
}
$ilcVersionString = $ilcPackages[0].Split('/')[1]
try { $ilcVersion = [Version]::Parse($ilcVersionString) }
catch { throw "SECURITY: unrecognized Native AOT compiler version: $ilcVersionString" }
if ($ilcVersion.Major -ne 10 -or $ilcVersion -lt [Version]'10.0.14') {
    throw "SECURITY: selected Microsoft.DotNet.ILCompiler $ilcVersionString is vulnerable or unsupported (CVE-2026-111497). Update .NET 10 SDK to 10.0.114 or 10.0.403 (or newer patched) and rebuild: https://dotnet.microsoft.com/download/dotnet/10.0"
}
Write-Host "Security OK: Microsoft.DotNet.ILCompiler $ilcVersionString (patched; SDK $sdk)." -ForegroundColor Green

# Each build replaces the single publish directory and its distribution files.
# Never delete local portable settings, including settings accidentally created
# inside publish by running the executable outside its development tree.
if (Test-Path -LiteralPath (Join-Path $publish 'data')) {
    throw 'SAFETY: publish/data contains local application state. Move/back it up outside publish before rebuilding.'
}
foreach ($generated in @($publish, $dist)) {
    if (Test-Path -LiteralPath $generated) {
        $existing = Get-Item -LiteralPath $generated -Force
        if (($existing.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "SAFETY: refusing to clean a reparse point: $generated"
        }
        if (-not $existing.PSIsContainer) {
            throw "OUTPUT: expected a directory, found a file: $generated"
        }
        Remove-Item -LiteralPath $generated -Force -Recurse
    }
    New-Item -Path $generated -ItemType Directory -Force | Out-Null
}
Write-Host '[PASS] clean publish/ and dist/ output directories.' -ForegroundColor Green


# Compile the Windows XPS Object Model bridge with the installed Windows SDK.
# This is genuine XPS OM / Print Document Package API; it is not Skia XPS,
# GDI bitmaps, RAW WritePrinter or a third-party PDF conversion utility.
$nativeBuildDir = Join-Path $sourceRoot 'obj\windows-native'
$nativeDll = Join-Path $nativeBuildDir 'KpcXpsBridge.dll'
# Ensure a failed C++ compile cannot reuse a DLL from the preceding build.
if (Test-Path -LiteralPath $nativeBuildDir) {
    Remove-Item -LiteralPath $nativeBuildDir -Force -Recurse
}
New-Item -ItemType Directory -Path $nativeBuildDir -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw 'Native XPS build requires Visual Studio Build Tools with MSVC and Windows SDK (vswhere missing).'
}
$vsInstall = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
if (-not $vsInstall) { throw 'MSVC C++ compiler was not found. Install Visual Studio C++ build tools.' }
$vcvars = Join-Path $vsInstall 'VC\Auxiliary\Build\vcvars64.bat'
if (-not (Test-Path -LiteralPath $vcvars)) { throw 'MSVC vcvars64.bat is unavailable.' }
$compileCmd = Join-Path $nativeBuildDir 'compile-xps.cmd'
# Paths are passed through Unicode environment variables: .cmd remains ASCII,
# even when the user's portable source folder contains Ukrainian characters.
$env:KPC_XPS_VCVARS = $vcvars
$env:KPC_XPS_SOURCE = $nativeSource
$env:KPC_XPS_DLL = $nativeDll
@'
@echo off
call "%KPC_XPS_VCVARS%" >NUL
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /O2 /MT /LD /Fe:"%KPC_XPS_DLL%" "%KPC_XPS_SOURCE%" /link ole32.lib prntvpt.lib uuid.lib
exit /b %errorlevel%
'@ | Set-Content -LiteralPath $compileCmd -Encoding Ascii
Write-Host 'Compiling native Microsoft XPS OM bridge ...'
Push-Location $nativeBuildDir
try {
    & cmd.exe /d /c $compileCmd
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $nativeDll)) {
        throw 'Native XPS OM bridge compilation failed. See MSVC errors above.'
    }
} finally { Pop-Location }
Write-Host '[PASS] Native XPS OM bridge compiled.' -ForegroundColor Green

# Catch runtime self-test failures BEFORE the expensive Native AOT link.
# Build a normal self-contained Windows executable from the same sources and
# run the same deterministic synthetic layout contracts. No vault is opened.
Write-Host 'Pre-AOT managed layout smoke test ...'
& dotnet build $project -c Release -r win-x64 --self-contained true --no-restore -p:PublishAot=false -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { throw 'Pre-AOT managed build failed.' }
$managedDir = Join-Path $root 'source\keepassxccardprint\bin\Release\net10.0\win-x64'
$managedExe = Join-Path $managedDir 'KeePassXCCardPrint.exe'
if (-not (Test-Path -LiteralPath $managedExe)) { throw 'Pre-AOT managed executable was not produced.' }
$managedFonts = Join-Path $managedDir 'fonts'
New-Item -Path $managedFonts -ItemType Directory -Force | Out-Null
foreach ($name in (Get-Content -LiteralPath (Join-Path $fontDirectory 'PORTABLE_FONT_FILES.txt') -Encoding ASCII)) {
    if (-not [string]::IsNullOrWhiteSpace($name)) {
        Copy-Item -LiteralPath (Join-Path $fontDirectory $name) -Destination $managedFonts -Force
    }
}
$pretestLog = Join-Path $managedDir 'layout-pre-aot-selftest.txt'
if (Test-Path -LiteralPath $pretestLog) { Remove-Item -LiteralPath $pretestLog -Force }
$pretestArgs = '--self-test-layout "' + $pretestLog + '"'
$pretest = Start-Process -FilePath $managedExe -ArgumentList $pretestArgs -PassThru -Wait
if (Test-Path -LiteralPath $pretestLog) { Write-Host (Get-Content -LiteralPath $pretestLog -Raw -Encoding UTF8) }
if ($pretest.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $pretestLog)) {
    throw "Pre-AOT managed layout self-tests failed (exit $($pretest.ExitCode))."
}
Remove-Item -LiteralPath $pretestLog -Force
Write-Host '[PASS] Pre-AOT managed layout self-tests passed.' -ForegroundColor Green
$managedUpdateTestLog = Join-Path $managedDir 'updates-pre-aot-selftest.txt'
$managedUpdateArgs = '--self-test-updates "' + $managedUpdateTestLog + '"'
$managedUpdate = Start-Process -FilePath $managedExe -ArgumentList $managedUpdateArgs -PassThru -Wait
if (Test-Path -LiteralPath $managedUpdateTestLog) {
    Write-Host (Get-Content -LiteralPath $managedUpdateTestLog -Raw -Encoding UTF8)
}
if ($managedUpdate.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $managedUpdateTestLog)) {
    throw "Pre-AOT updater self-tests failed (exit $($managedUpdate.ExitCode))."
}
Remove-Item -LiteralPath $managedUpdateTestLog -Force
Write-Host '[PASS] Pre-AOT updater self-tests passed.' -ForegroundColor Green

Write-Host "Native AOT publish: $publish"
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishAot=true --no-restore -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Native AOT publish failed.' }
# The library is standalone and AOT-safe: only Windows SDK / MSVC runtime.
Copy-Item -LiteralPath $nativeDll -Destination (Join-Path $publish 'KpcXpsBridge.dll') -Force
if (-not (Test-Path -LiteralPath (Join-Path $publish 'KpcXpsBridge.dll'))) {
    throw 'Native XPS printer bridge is missing from the published distribution.'
}

# Verify selected Windows runtime versions before attempting to use the published binary.
# Avalonia.Skia 12.1.x permits SkiaSharp >= 3.x; this application deliberately
# resolves SkiaSharp 4.x and must not accidentally package a 3.x native DLL.
$assetsPath = Join-Path (Split-Path -Parent $project) 'obj\project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath)) { throw 'NuGet restore assets were not produced.' }
$assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
$resolved = @($assets.libraries.PSObject.Properties.Name)
foreach ($required in @(
    'Avalonia/12.1.5', 'Avalonia.Desktop/12.1.5', 'Avalonia.Win32/12.1.5',
    'Avalonia.Skia/12.1.5', 'Avalonia.HarfBuzz/12.1.5', 'Avalonia.Themes.Fluent/12.1.5',
    'SkiaSharp/4.154.0', 'SkiaSharp.HarfBuzz/4.154.0',
    'SkiaSharp.NativeAssets.Win32/4.154.0',
    'HarfBuzzSharp/14.2.1.400', 'HarfBuzzSharp.NativeAssets.Win32/14.2.1.400'
)) {
    if ($resolved -notcontains $required) { throw "Unexpected NuGet resolution: missing $required" }
}
# Avalonia.Skia 12.1.5 declares Linux and WebAssembly native assets >= 3.x,
# while the explicitly selected Windows runtime uses SkiaSharp 4.x. A restore
# can therefore legitimately contain SkiaSharp.NativeAssets.Linux/3.119.4 or
# SkiaSharp.NativeAssets.WebAssembly/3.119.4 in project.assets.json. Those are
# not Windows DLLs and must not make a win-x64 build fail.
# Check the exact Windows-relevant packages, NOT all packages with a Skia prefix.
$windowsPackages = [ordered]@{
    'SkiaSharp' = '4.154.0'
    'SkiaSharp.HarfBuzz' = '4.154.0'
    'SkiaSharp.NativeAssets.Win32' = '4.154.0'
    'HarfBuzzSharp' = '14.2.1.400'
    'HarfBuzzSharp.NativeAssets.Win32' = '14.2.1.400'
}
foreach ($name in $windowsPackages.Keys) {
    $prefix = $name + '/'
    $matching = @($resolved | Where-Object { $_.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) })
    $expected = $prefix + $windowsPackages[$name]
    if ($matching.Count -ne 1 -or $matching[0] -cne $expected) {
        throw "Incorrect Windows graphics dependency: $name. Expected $expected; resolved: $($matching -join ', ')"
    }
}
# Report foreign-platform dependencies for debugging, but never confuse them
# with the actual win-x64 native payload.
$otherPlatform = @($resolved | Where-Object {
    $_ -match '^(?:SkiaSharp|HarfBuzzSharp)\.NativeAssets\.(?:Linux|WebAssembly)/'
})
if ($otherPlatform.Count -gt 0) {
    Write-Host ('INFO: non-Windows native packages in restore graph (not win-x64 payload): ' +
        ($otherPlatform -join ', '))
}
Write-Host '[PASS] Windows renderer: SkiaSharp 4.154.0 + HarfBuzzSharp 14.2.1.400 (Avalonia 12.1.5).' -ForegroundColor Green
$exe = Join-Path $publish 'KeePassXCCardPrint.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'Native AOT publish did not produce KeePassXCCardPrint.exe.'
}

# Bundle portable fallback fonts beside each AOT EXE, without Windows UI font copies.
$publishedFonts = Join-Path $publish 'fonts'
New-Item -ItemType Directory -Path $publishedFonts -Force | Out-Null
$fontManifest = Join-Path $fontDirectory 'PORTABLE_FONT_FILES.txt'
if (-not (Test-Path -LiteralPath $fontManifest)) { throw 'Missing verified portable-font manifest.' }
$fontNames = @(Get-Content -LiteralPath $fontManifest -Encoding ASCII | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($fontNames.Count -ne 8) { throw "Unexpected number of bundled font faces: $($fontNames.Count)" }
foreach ($name in $fontNames) {
    if ($name -notmatch '^[A-Za-z0-9_-]+\.ttf$') { throw "Invalid font manifest entry: $name" }
    $source = Join-Path $fontDirectory $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing font: $name" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $publishedFonts $name) -Force
}
# Publish exactly the licenses corresponding to current bundled fonts.
# Copy only the licenses declared by the current font manifest.
$licenseManifest = Join-Path $fontDirectory 'PORTABLE_FONT_LICENSES.txt'
if (-not (Test-Path -LiteralPath $licenseManifest)) { throw 'Missing current font-license manifest.' }
$licenseNames = @(Get-Content -LiteralPath $licenseManifest -Encoding ASCII |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($licenseNames.Count -ne 8) { throw "Unexpected number of font licenses: $($licenseNames.Count)" }
$publishedLicenses = Join-Path $publishedFonts 'licenses'
New-Item -ItemType Directory -Path $publishedLicenses -Force | Out-Null
foreach ($name in $licenseNames) {
    if ($name -notmatch '^[A-Za-z0-9_-]+\.txt$') { throw "Invalid license manifest entry: $name" }
    $source = Join-Path (Join-Path $fontDirectory 'licenses') $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing font license: $name" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $publishedLicenses $name) -Force
}

# Run the actual tests inside the published Native AOT binary, after portable fonts are installed.
Write-Host 'Running Skia layout contract tests in the Native AOT EXE ...'
$testLog = Join-Path $publish 'layout-selftest.txt'
$testArgs = '--self-test-layout "' + $testLog + '"'
$smoke = Start-Process -FilePath $exe -ArgumentList $testArgs -PassThru -Wait
if (Test-Path -LiteralPath $testLog) { Write-Host (Get-Content -LiteralPath $testLog -Raw -Encoding UTF8) }
if ($smoke.ExitCode -ne 0) { throw "Native AOT layout self-tests failed (exit $($smoke.ExitCode))." }
if (-not (Test-Path -LiteralPath $testLog)) { throw 'Native AOT did not write its self-test result.' }
Remove-Item -LiteralPath $testLog -Force
Write-Host '[PASS] Native AOT layout self-tests passed.'
Write-Host 'Running KDBX error classification tests in the Native AOT EXE ...'
$vaultTestLog = Join-Path $publish 'vault-selftest.txt'
$vaultTestArgs = '--self-test-vault-errors "' + $vaultTestLog + '"'
$vaultSmoke = Start-Process -FilePath $exe -ArgumentList $vaultTestArgs -PassThru -Wait
if (Test-Path -LiteralPath $vaultTestLog) {
    Write-Host (Get-Content -LiteralPath $vaultTestLog -Raw -Encoding UTF8)
}
if ($vaultSmoke.ExitCode -ne 0) {
    throw "Native AOT KDBX error tests failed (exit $($vaultSmoke.ExitCode))."
}
if (-not (Test-Path -LiteralPath $vaultTestLog)) {
    throw 'Native AOT did not write its KDBX self-test result.'
}
Remove-Item -LiteralPath $vaultTestLog -Force
Write-Host '[PASS] Native AOT KDBX error tests passed.' -ForegroundColor Green
Write-Host 'Running field mapping validation tests in the Native AOT EXE ...'
$mappingTestLog = Join-Path $publish 'mapping-selftest.txt'
$mappingTestArgs = '--self-test-mapping "' + $mappingTestLog + '"'
$mappingSmoke = Start-Process -FilePath $exe -ArgumentList $mappingTestArgs -PassThru -Wait
if (Test-Path -LiteralPath $mappingTestLog) {
    Write-Host (Get-Content -LiteralPath $mappingTestLog -Raw -Encoding UTF8)
}
if ($mappingSmoke.ExitCode -ne 0) {
    throw "Native AOT mapping tests failed (exit $($mappingSmoke.ExitCode))."
}
if (-not (Test-Path -LiteralPath $mappingTestLog)) {
    throw 'Native AOT did not write its mapping self-test result.'
}
Remove-Item -LiteralPath $mappingTestLog -Force
Write-Host '[PASS] Native AOT mapping validation tests passed.' -ForegroundColor Green
Write-Host 'Running updater filesystem/rollback self-tests in the Native AOT EXE ...'
$updateTestLog = Join-Path $publish 'updates-selftest.txt'
$updateTestArgs = '--self-test-updates "' + $updateTestLog + '"'
$updateSmoke = Start-Process -FilePath $exe -ArgumentList $updateTestArgs -PassThru -Wait
if (Test-Path -LiteralPath $updateTestLog) {
    Write-Host (Get-Content -LiteralPath $updateTestLog -Raw -Encoding UTF8)
}
if ($updateSmoke.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $updateTestLog)) {
    throw "Native AOT updater self-tests failed (exit $($updateSmoke.ExitCode))."
}
Remove-Item -LiteralPath $updateTestLog -Force
Write-Host '[PASS] Native AOT updater self-tests passed.' -ForegroundColor Green


# Validate the actual Windows XPS OM COM parser in the published Native AOT
# binary with synthetic vector-only geometry. This never opens a printer/vault.
Write-Host 'Native AOT XPS Object Model smoke test (no spooler job) ...'
$xpsTestLog = Join-Path $publish 'xps-selftest.txt'
$xpsArgs = '--self-test-xps "' + $xpsTestLog + '"'
$xpsSmoke = Start-Process -FilePath $exe -ArgumentList $xpsArgs -PassThru -Wait
if (Test-Path -LiteralPath $xpsTestLog) {
    Write-Host (Get-Content -LiteralPath $xpsTestLog -Raw -Encoding UTF8)
}
if ($xpsSmoke.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $xpsTestLog)) {
    throw "Native AOT XPS OM validation failed (exit $($xpsSmoke.ExitCode))."
}
Remove-Item -LiteralPath $xpsTestLog -Force
Write-Host '[PASS] Native AOT Windows XPS OM validation passed.' -ForegroundColor Green

# Add redistribution notices to the final package (no source tree or user data).
foreach ($notice in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    $noticeSource = Join-Path $root $notice
    if (-not (Test-Path -LiteralPath $noticeSource -PathType Leaf)) {
        throw "Missing distribution document: $notice"
    }
    Copy-Item -LiteralPath $noticeSource -Destination (Join-Path $publish $notice) -Force
}

# Package only the finished, tested publish directory. Never ship personal
# configuration, test reports, compiler symbols, or the local build cache.
$privateNames = @('ui-shell.json', 'vault-state.json', 'mapping.json', 'domains.json',
    'appearance.json', 'appearance-template-source.json', 'storage-mode.txt')
$privateExtensions = @('.pdb', '.ilk', '.exp', '.lib', '.obj', '.log')
$files = @(Get-ChildItem -LiteralPath $publish -File -Recurse -Force |
    Where-Object {
        $_.Extension -notin $privateExtensions -and
        $_.Name -notin $privateNames -and
        $_.Name -notmatch '^(?:layout|vault|mapping|xps|updates)-selftest[.]txt$' -and
        $_.FullName -notmatch '[\\/]data[\\/]|[\\/]print-tracking[\\/]|[\\/]templates[\\/]|[\\/]backups[\\/]'
    } | Sort-Object FullName)
if ($files.Count -eq 0) { throw 'Portable payload is empty.' }

# Stable filename: the same version/tag always produces the same asset names.
# ZIP and checksum are the only files in dist/ after a successful build.
$zip = Join-Path $dist ('KeePassXCCardPrint-' + $sourceVersion + '-win-x64.zip')
$checksum = $zip + '.sha256'
Add-Type -AssemblyName System.IO.Compression

# Write the complete archive to a temporary file first. Never replace the
# previous successful package until the new archive has been fully assembled.
$done = $false
for ($attempt = 1; $attempt -le 4 -and -not $done; $attempt++) {
    $temp = $zip + '.tmp'
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
    try {
        $output = [System.IO.File]::Open($temp, [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $archive = [System.IO.Compression.ZipArchive]::new($output,
                [System.IO.Compression.ZipArchiveMode]::Create, $true)
            try {
                foreach ($file in $files) {
                    $entryName = $file.FullName.Substring($publish.Length + 1).Replace('\', '/')
                    $entry = $archive.CreateEntry($entryName,
                        [System.IO.Compression.CompressionLevel]::Optimal)
                    $sourceStream = [System.IO.File]::Open($file.FullName, [System.IO.FileMode]::Open,
                        [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
                    try {
                        $entryStream = $entry.Open()
                        try { $sourceStream.CopyTo($entryStream) }
                        finally { $entryStream.Dispose() }
                    } finally { $sourceStream.Dispose() }
                }
            } finally { $archive.Dispose() }
        } finally { $output.Dispose() }
        if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
        Move-Item -LiteralPath $temp -Destination $zip
        $done = $true
    } catch {
        if ($attempt -eq 4) { throw }
        Write-Warning "ZIP attempt $attempt failed: $($_.Exception.Message)"
        Start-Sleep -Milliseconds (500 * $attempt)
    } finally {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
    }
}

# Standard sha256sum-compatible line: <lowercase sha256><two spaces><filename>.
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumContent = $hash + '  ' + [System.IO.Path]::GetFileName($zip) + "`n"
[System.IO.File]::WriteAllText($checksum, $checksumContent,
    [System.Text.Encoding]::ASCII)
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `
    (Join-Path $PSScriptRoot 'verify_release_package.ps1') -ArchivePath $zip -ChecksumPath $checksum
if ($LASTEXITCODE -ne 0) { throw 'Release ZIP/checksum validation failed.' }
Write-Host '[PASS] Portable ZIP and SHA-256 verified.' -ForegroundColor Green

Write-Host ''
Write-Host '[PASS] Native AOT build completed.'
Write-Host "EXE: $exe"
Write-Host "ZIP: $zip"
Write-Host "SHA256: $checksum"
Write-Host 'Launch with run.bat.'

} catch {
    [Console]::Error.WriteLine('[FAIL] BUILD: ' + $_.Exception.Message)
    exit 1
}
