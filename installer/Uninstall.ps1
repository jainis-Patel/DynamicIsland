<#
.SYNOPSIS
    Automated uninstaller script for Dynamic Island for Windows.
.PARAMETER RemoveUserData
    Also remove application settings and local cache from %LocalAppData%\DynamicIsland.
#>
[CmdletBinding()]
param(
    [switch]$RemoveUserData
)

$ErrorActionPreference = 'Stop'
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "    Dynamic Island Windows Uninstaller   " -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Cyan

$TargetDir = "$env:LOCALAPPDATA\Programs\DynamicIsland"
$TargetExe = Join-Path $TargetDir "DynamicIsland.exe"

# 1. Stop any running instance
Write-Host "`n[1/4] Closing Dynamic Island process..." -ForegroundColor Gray
if (Test-Path $TargetExe) {
    try {
        Start-Process -FilePath $TargetExe -ArgumentList "--stop" -Wait -WindowStyle Hidden -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600
    } catch { }
}
Stop-Process -Name "DynamicIsland" -Force -ErrorAction SilentlyContinue

# 2. Remove shortcuts
Write-Host "[2/4] Removing Start Menu & Desktop shortcuts..." -ForegroundColor Gray
$ProgramsFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$StartMenuLnk = Join-Path $ProgramsFolder "Dynamic Island.lnk"
if (Test-Path $StartMenuLnk) { Remove-Item -Path $StartMenuLnk -Force -ErrorAction SilentlyContinue }

$DesktopFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$DesktopLnk = Join-Path $DesktopFolder "Dynamic Island.lnk"
if (Test-Path $DesktopLnk) { Remove-Item -Path $DesktopLnk -Force -ErrorAction SilentlyContinue }

# 3. Clean registry
Write-Host "[3/4] Cleaning Windows Registry entries..." -ForegroundColor Gray
$RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Remove-ItemProperty -Path $RunKey -Name "DynamicIsland" -ErrorAction SilentlyContinue

$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DynamicIsland"
if (Test-Path $UninstallKey) { Remove-Item -Path $UninstallKey -Recurse -Force -ErrorAction SilentlyContinue }

# 4. Remove installation files
Write-Host "[4/4] Removing installed program files..." -ForegroundColor Gray
if (Test-Path $TargetDir) {
    Remove-Item -Path $TargetDir -Recurse -Force -ErrorAction SilentlyContinue
}

if ($RemoveUserData) {
    $DataDir = "$env:LOCALAPPDATA\DynamicIsland"
    if (Test-Path $DataDir) {
        Remove-Item -Path $DataDir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "      User data removed: $DataDir" -ForegroundColor Gray
    }
}

Write-Host "`n[OK] Dynamic Island uninstalled successfully!" -ForegroundColor Green
