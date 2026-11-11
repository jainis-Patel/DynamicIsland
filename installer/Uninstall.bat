@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" %*
if %ERRORLEVEL% neq 0 (
    echo Uninstall encountered an issue (exit code %ERRORLEVEL%).
    pause
)
