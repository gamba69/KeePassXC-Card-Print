param([Parameter(Mandatory=$true)][string]$BuildDirectory,
      [Parameter(Mandatory=$true)][string]$PublishDirectory,
      [Parameter(Mandatory=$true)][string]$VsInstall,
      [Parameter(Mandatory=$true)][string]$VcVars,
      [Parameter(Mandatory=$true)][string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Join-Path $BuildDirectory 'vendor-video'
New-Item -ItemType Directory -Path $root -Force | Out-Null
# The SDK headers are compile-time dependencies only. A pinned commit identifies
# the complete Git tree; never download or run vendor installers during builds.
function Get-PinnedTree([string]$Name,[string]$Url,[string]$Commit,[string[]]$Sparse) {
    $folder = Join-Path $root $Name
    if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory -Path $folder | Out-Null
    & git -C $folder init -q
    if ($LASTEXITCODE -ne 0) { throw "Git initialization failed: $Name" }
    & git -C $folder remote add origin $Url
    if ($LASTEXITCODE -ne 0) { throw "Git remote setup failed: $Name" }
    if ($Sparse.Count -gt 0) {
        & git -C $folder config remote.origin.promisor true
        & git -C $folder config remote.origin.partialclonefilter blob:none
        & git -C $folder sparse-checkout set --cone @Sparse
        if ($LASTEXITCODE -ne 0) { throw "SDK sparse checkout setup failed: $Name" }
        & git -C $folder -c fetch.fsckObjects=true fetch -q --depth=1 --filter=blob:none origin $Commit
    } else {
        & git -C $folder -c fetch.fsckObjects=true fetch -q --depth=1 origin $Commit
    }
    if ($LASTEXITCODE -ne 0) { throw "Pinned SDK fetch failed: $Name" }
    & git -C $folder -c core.hooksPath=NUL checkout -q --detach $Commit
    if ($LASTEXITCODE -ne 0) { throw "Pinned SDK checkout failed: $Name" }
    $actual = (& git -C $folder rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $actual -cne $Commit) { throw "SDK commit mismatch: $Name" }
    return $folder
}
$nv = Get-PinnedTree 'nv-codec-headers' 'https://github.com/FFmpeg/nv-codec-headers.git' 'c69278340ab1d5559c7d7bf0edf615dc33ddbba7' @()
$intel = Get-PinnedTree 'libvpl' 'https://github.com/intel/libvpl.git' 'ca5bbbb057a6e84b103aca807612afb693ad046c' @()
$amd = Get-PinnedTree 'amf' 'https://github.com/GPUOpen-LibrariesAndSDKs/AMF.git' 'ba07d1b3c7c7ee0d846125cffc1d6d78a020c45a' @('amf/public/include')
$cmake = Join-Path $VsInstall 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path -LiteralPath $cmake)) { $cmake = (Get-Command cmake.exe -ErrorAction Stop).Source }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$instances = @(& $vswhere -all -products '*' -format json | Out-String | ConvertFrom-Json)
$instance = $instances | Where-Object { $_.installationPath -eq $VsInstall } | Select-Object -First 1
if ($null -eq $instance) { throw 'Unable to determine Visual Studio generator for oneVPL.' }
$generator = switch (([version]$instance.installationVersion).Major) { 17 { 'Visual Studio 17 2022' } 18 { 'Visual Studio 18 2026' } default { throw 'Unsupported Visual Studio generator.' } }
$compiled = Join-Path $root 'vpl-compiled'
if (Test-Path -LiteralPath $compiled) { Remove-Item -LiteralPath $compiled -Recurse -Force }
& $cmake -S $intel -B $compiled -G $generator -A x64 "-DCMAKE_GENERATOR_INSTANCE=$VsInstall" '-DBUILD_SHARED_LIBS=ON' '-DBUILD_DISPATCHER_ONLY=ON' '-DBUILD_TESTS=OFF' '-DUSE_MSVC_STATIC_RUNTIME=ON'
if ($LASTEXITCODE -ne 0) { throw 'oneVPL dispatcher configuration failed.' }
& $cmake --build $compiled --config Release --target VPL --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'oneVPL dispatcher build failed.' }
$vpl = @(Get-ChildItem -LiteralPath $compiled -Recurse -File -Filter 'libvpl.dll')
if ($vpl.Count -ne 1) { throw 'Expected exactly one built oneVPL dispatcher DLL.' }
$env:KPC_VENDOR_VCVARS = $VcVars
$env:KPC_VENDOR_NV = Join-Path $nv 'include'
$env:KPC_VENDOR_INTEL = Join-Path $intel 'api'
$env:KPC_VENDOR_AMD = Join-Path $amd 'amf\public\include'
$env:KPC_VENDOR_CPP = Join-Path $SourceRoot 'platforms\windows\native\KpcVendorVideo.cpp'
$env:KPC_VENDOR_DLL = Join-Path $root 'KpcVendorVideo.dll'
$cmd = Join-Path $root 'compile-vendor.cmd'
@'
@echo off
call "%KPC_VENDOR_VCVARS%" >NUL
if errorlevel 1 exit /b 1
cl /nologo /std:c++17 /EHsc /O2 /MT /LD /I"%KPC_VENDOR_NV%" /I"%KPC_VENDOR_INTEL%" /I"%KPC_VENDOR_AMD%" /Fe:"%KPC_VENDOR_DLL%" "%KPC_VENDOR_CPP%" /link d3d11.lib dxgi.lib ole32.lib uuid.lib
exit /b %errorlevel%
'@ | Set-Content -LiteralPath $cmd -Encoding Ascii
Push-Location $root
try {
    & cmd.exe /d /c $cmd
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $env:KPC_VENDOR_DLL)) { throw 'Direct hardware video bridge compilation failed.' }
} finally { Pop-Location }
Copy-Item -LiteralPath $env:KPC_VENDOR_DLL -Destination (Join-Path $PublishDirectory 'KpcVendorVideo.dll') -Force
Copy-Item -LiteralPath $vpl[0].FullName -Destination (Join-Path $PublishDirectory 'libvpl.dll') -Force
$notices = Join-Path $PublishDirectory 'licenses\vendor-video'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $intel 'LICENSE') -Destination (Join-Path $notices 'oneVPL-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $intel 'third-party-programs.txt') -Destination (Join-Path $notices 'oneVPL-third-party-programs.txt') -Force
Copy-Item -LiteralPath (Join-Path $amd 'LICENSE.txt') -Destination (Join-Path $notices 'AMF-LICENSE.txt') -Force
# NV codec header copyright and permission notices are in the header text.
foreach ($header in @('dynlink_cuda.h','dynlink_cuviddec.h','dynlink_nvcuvid.h')) {
    Copy-Item -LiteralPath (Join-Path $nv "include\ffnvcodec\$header") -Destination (Join-Path $notices "NVIDIA-$header.txt") -Force
}
Write-Host '[PASS] Direct NVDEC, Quick Sync / oneVPL and AMF bridge, dispatcher and licences packaged.' -ForegroundColor Green
