@echo off
setlocal
cd /d "%~dp0"
if exist "DynamicIsland-Setup.exe" (
    start "" "DynamicIsland-Setup.exe"
    exit /b 0
)
if exist "..\publish\DynamicIsland-Setup.exe" (
    start "" "..\publish\DynamicIsland-Setup.exe"
    exit /b 0
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
if %ERRORLEVEL% neq 0 (
    echo Installation failed with exit code %ERRORLEVEL%.
    pause
)
