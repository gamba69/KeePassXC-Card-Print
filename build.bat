@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "PAUSE_AT_END=1"
set "RESULT=0"

if /I "%~1"=="--no-pause" goto :no_pause
if /I "%~1"=="/no-pause" goto :no_pause
if /I "%~1"=="-nopause" goto :no_pause
goto :arguments

:no_pause
set "PAUSE_AT_END=0"
shift

:arguments
if "%~1"=="" goto :start
echo [FAIL] BUILD. Unknown argument: %~1
echo Usage: build.bat [--no-pause]
set "RESULT=2"
goto :finish

:start
cd /d "%~dp0"
if errorlevel 1 goto :directory_failed
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0tools\check_build_syntax.ps1" "%~dp0tools\build.ps1"
if errorlevel 1 goto :syntax_failed
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0tools\check_build_syntax.ps1" "%~dp0tools\verify_fonts.ps1"
if errorlevel 1 goto :syntax_failed
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0tools\check_build_syntax.ps1" "%~dp0tools\verify_release_package.ps1"
if errorlevel 1 goto :syntax_failed
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0tools\check_build_syntax.ps1" "%~dp0tools\verify_repository_privacy.ps1"
if errorlevel 1 goto :syntax_failed
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "%~dp0tools\build.ps1"
set "RESULT=%ERRORLEVEL%"
goto :finish

:directory_failed
echo [FAIL] BUILD. Cannot switch to the source directory.
set "RESULT=1"
goto :finish

:syntax_failed
echo [FAIL] BUILD. PowerShell script validation failed.
set "RESULT=1"

:finish
echo.
if not "%RESULT%"=="0" goto :failed
echo [PASS] BUILD completed successfully.
goto :pause_or_exit

:failed
echo [FAIL] BUILD. Exit code: %RESULT%

:pause_or_exit
echo.
if "%PAUSE_AT_END%"=="1" pause
exit /b %RESULT%
