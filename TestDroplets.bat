@echo off
setlocal
cd /d "%~dp0"

echo Closing any previous Dynamic Island instance...
taskkill /F /IM DynamicIsland.exe >nul 2>&1
ping 127.0.0.1 -n 2 >nul

echo Registering Windows Firewall inbound rule for Phone Bridge (Port 8989)...
netsh advfirewall firewall add rule name="Dynamic Island Phone Bridge" dir=in action=allow protocol=TCP localport=8989 >nul 2>&1

echo Starting Dynamic Island with Snug Apple Fluid Dock & Continuous Zero-Drop HotZone...
if exist "%~dp0bin\Release\net8.0-windows10.0.19041.0\DynamicIsland.exe" (
    start "" "%~dp0bin\Release\net8.0-windows10.0.19041.0\DynamicIsland.exe"
    exit /b 0
)
if exist "%~dp0bin\Debug\net8.0-windows10.0.19041.0\DynamicIsland.exe" (
    start "" "%~dp0bin\Debug\net8.0-windows10.0.19041.0\DynamicIsland.exe"
    exit /b 0
)
if exist "%~dp0publish\DynamicIsland.exe" (
    start "" "%~dp0publish\DynamicIsland.exe"
    exit /b 0
)
start "" dotnet run -c Release
exit /b 0
