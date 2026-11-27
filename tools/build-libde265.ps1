param([Parameter(Mandatory=$true)][string]$BuildDirectory,
      [Parameter(Mandatory=$true)][string]$PublishDirectory,
      [Parameter(Mandatory=$true)][string]$VsInstall)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$version = '1.1.3'
$sha = '554228bd17788c99a7e63b37ab5634722190e6e2bf60c1dcb01cef328e133905'
$archiveName = "libde265-$version.tar.gz"
$root = Join-Path $BuildDirectory 'libde265-build'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$archive = Join-Path $root $archiveName
$url = "https://github.com/strukturag/libde265/releases/download/v$version/$archiveName"
Write-Host "Downloading pinned libde265 $version source ..."
Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sha) { throw 'libde265 source checksum mismatch.' }
& tar.exe -xf $archive -C $root
if ($LASTEXITCODE -ne 0) { throw 'libde265 source extraction failed.' }
$source = Join-Path $root "libde265-$version"
if (-not (Test-Path (Join-Path $source 'CMakeLists.txt'))) { throw 'libde265 source directory missing.' }
$cmake = Join-Path $VsInstall 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path $cmake)) { $cmake = (Get-Command cmake.exe -ErrorAction Stop).Source }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$instances = @(& $vswhere -all -products '*' -format json | Out-String | ConvertFrom-Json)
$instance = $instances | Where-Object { $_.installationPath -eq $VsInstall } | Select-Object -First 1
if ($null -eq $instance) { throw 'Unable to determine the Visual Studio generator for libde265.' }
$major = ([version]$instance.installationVersion).Major
$generator = switch ($major) { 17 { 'Visual Studio 17 2022' } 18 { 'Visual Studio 18 2026' } default { throw "Unsupported MSVC generator: $major" } }
$build = Join-Path $root 'compiled'
$install = Join-Path $root 'installed'
# Use the same MSVC installation as the native bridges; /MT avoids an extra runtime installer.
& $cmake -S $source -B $build -G $generator -A x64 "-DCMAKE_GENERATOR_INSTANCE=$VsInstall" '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW' '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded' '-DBUILD_SHARED_LIBS=ON' '-DENABLE_DECODER=OFF' '-DENABLE_ENCODER=OFF' '-DENABLE_SDL=OFF' '-DENABLE_SHERLOCK265=OFF' '-DENABLE_AVX512=OFF' '-DDE265_LOG_LEVEL=none' "-DCMAKE_INSTALL_PREFIX=$install"
if ($LASTEXITCODE -ne 0) { throw 'libde265 CMake configuration failed.' }
& $cmake --build $build --config Release --target de265 --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'libde265 compilation failed.' }
& $cmake --install $build --config Release
if ($LASTEXITCODE -ne 0) { throw 'libde265 installation failed.' }
$dll = Join-Path $install 'bin\libde265.dll'
if (-not (Test-Path $dll -PathType Leaf)) { throw 'libde265 shared DLL missing.' }
Copy-Item -LiteralPath $dll -Destination (Join-Path $PublishDirectory 'libde265.dll') -Force
# Include complete corresponding source, licence and exact build recipe in every release.
$notices = Join-Path $PublishDirectory 'licenses\libde265'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
Copy-Item -LiteralPath $archive -Destination $notices -Force
Copy-Item -LiteralPath (Join-Path $source 'COPYING') -Destination (Join-Path $notices 'COPYING.txt') -Force
Copy-Item -LiteralPath $PSCommandPath -Destination $notices -Force
Write-Host "[PASS] libde265 $version shared decoder, source and LGPL licence packaged." -ForegroundColor Green
