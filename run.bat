@echo off
setlocal DisableDelayedExpansion
set "ROOT=%~dp0"
set "APP=%ROOT%publish\KeePassXCCardPrint.exe"
if not exist "%APP%" goto :missing
start "" "%APP%" %*
exit /b 0
:missing
echo Published executable not found. Run build.bat first.
pause
exit /b 1
