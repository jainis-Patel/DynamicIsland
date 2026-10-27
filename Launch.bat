@echo off
cd /d "%~dp0publish"
if exist "DynamicIsland.exe" (
    start "" "DynamicIsland.exe"
) else (
    start "" dotnet "DynamicIsland.dll"
)
