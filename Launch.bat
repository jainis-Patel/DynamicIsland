@echo off
setlocal
cd /d "%~dp0"

REM Prioritize the single-file release executable
if exist "%~dp0DynamicIsland.exe" (
    start "" "%~dp0DynamicIsland.exe" %*
    exit /b 0
)
if exist "%~dp0publish\DynamicIsland.exe" (
    start "" "%~dp0publish\DynamicIsland.exe" %*
    exit /b 0
)
if exist "%~dp0bin\Debug\net8.0-windows10.0.19041.0\DynamicIsland.exe" (
    start "" "%~dp0bin\Debug\net8.0-windows10.0.19041.0\DynamicIsland.exe" %*
    exit /b 0
)
if exist "%~dp0bin\Release\net8.0-windows10.0.19041.0\DynamicIsland.exe" (
    start "" "%~dp0bin\Release\net8.0-windows10.0.19041.0\DynamicIsland.exe" %*
    exit /b 0
)
if exist "%~dp0publish\DynamicIsland.exe" (
    start "" "%~dp0publish\DynamicIsland.exe" %*
    exit /b 0
)
if exist "%LOCALAPPDATA%\Programs\DynamicIsland\DynamicIsland.exe" (
    start "" "%LOCALAPPDATA%\Programs\DynamicIsland\DynamicIsland.exe" %*
    exit /b 0
)

REM Fallback to dotnet run
dotnet run -c Release -- %*
exit /b 0
