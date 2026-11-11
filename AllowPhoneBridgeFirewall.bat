@echo off
setlocal
echo ========================================================
echo   Dynamic Island - Phone Bridge Firewall Configuration
echo ========================================================
echo.

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Administrator privileges required to open LAN Port 8989.
    echo Prompting for elevation...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo Adding Windows Defender Firewall inbound rule for TCP Port 8989...
netsh advfirewall firewall delete rule name="Dynamic Island Phone Bridge" >nul 2>&1
netsh advfirewall firewall add rule name="Dynamic Island Phone Bridge" dir=in action=allow protocol=TCP localport=8989

echo.
echo [SUCCESS] Windows Defender Firewall now allows phone connections on port 8989!
echo You can now scan the QR code and connect from any phone on your local Wi-Fi.
echo.
timeout /t 5
