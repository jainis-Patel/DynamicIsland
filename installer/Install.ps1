<#
.SYNOPSIS
    Automated installer script for Dynamic Island for Windows.
.DESCRIPTION
    Installs Dynamic Island to %LocalAppData%\Programs\DynamicIsland,
    configures Start Menu & Desktop shortcuts, registers auto-start,
    and sets up Windows "Installed Apps" integration without needing admin elevation.
.PARAMETER TargetDir
    Optional custom install folder. Defaults to %LocalAppData%\Programs\DynamicIsland.
.PARAMETER NoAutoStart
    Skip adding Dynamic Island to Windows startup.
.PARAMETER NoDesktopShortcut
    Skip creating a shortcut on the user's Desktop.
#>
[CmdletBinding()]
param(
    [string]$TargetDir = "$env:LOCALAPPDATA\Programs\DynamicIsland",
    [switch]$NoAutoStart,
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = 'Stop'
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    Dynamic Island Windows Installer   " -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan

# 1. Locate source executable
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptRoot
$SourceExe = Join-Path $RepoRoot "publish\DynamicIsland.exe"

if (-not (Test-Path $SourceExe)) {
    $SourceExe = Join-Path $RepoRoot "bin\Release\net8.0-windows10.0.19041.0\DynamicIsland.exe"
}

if (-not (Test-Path $SourceExe)) {
    Write-Error "Source DynamicIsland.exe not found. Please build or publish the project first."
    exit 1
}

# 2. Stop any existing running instance
Write-Host "`n[1/5] Stopping any running Dynamic Island process..." -ForegroundColor Gray
try {
    Start-Process -FilePath $SourceExe -ArgumentList "--stop" -Wait -WindowStyle Hidden -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
} catch { }

# 3. Copy files to target directory
Write-Host "[2/5] Installing binary to $TargetDir..." -ForegroundColor Gray
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
}

$DestExe = Join-Path $TargetDir "DynamicIsland.exe"
Copy-Item -Path $SourceExe -Destination $DestExe -Force
Write-Host "      Installed: $DestExe" -ForegroundColor Green

# 4. Create Shortcuts via WScript.Shell
Write-Host "[3/5] Creating Start Menu & Desktop shortcuts..." -ForegroundColor Gray
$WshShell = New-Object -ComObject WScript.Shell

$ProgramsFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$StartMenuLnk = Join-Path $ProgramsFolder "Dynamic Island.lnk"
$Shortcut = $WshShell.CreateShortcut($StartMenuLnk)
$Shortcut.TargetPath = $DestExe
$Shortcut.WorkingDirectory = $TargetDir
$Shortcut.Description = "Dynamic Island for Windows"
$Shortcut.Save()
Write-Host "      Start Menu shortcut: $StartMenuLnk" -ForegroundColor Green

if (-not $NoDesktopShortcut) {
    $DesktopFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $DesktopLnk = Join-Path $DesktopFolder "Dynamic Island.lnk"
    $DShortcut = $WshShell.CreateShortcut($DesktopLnk)
    $DShortcut.TargetPath = $DestExe
    $DShortcut.WorkingDirectory = $TargetDir
    $DShortcut.Description = "Dynamic Island for Windows"
    $DShortcut.Save()
    Write-Host "      Desktop shortcut: $DesktopLnk" -ForegroundColor Green
}

# 5. Configure Windows Startup Registry
if (-not $NoAutoStart) {
    Write-Host "[4/5] Enabling Windows Startup entry..." -ForegroundColor Gray
    $RunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $RunKey -Name "DynamicIsland" -Value "`"$DestExe`"" -Force
    Write-Host "      Auto-start registered in HKCU Run" -ForegroundColor Green
}

# 6. Register in Windows Settings -> Installed Apps
Write-Host "[5/5] Registering Windows Add/Remove Programs integration..." -ForegroundColor Gray
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DynamicIsland"
if (-not (Test-Path $UninstallKey)) {
    New-Item -Path $UninstallKey -Force | Out-Null
}

Set-ItemProperty -Path $UninstallKey -Name "DisplayName" -Value "Dynamic Island" -Force
Set-ItemProperty -Path $UninstallKey -Name "DisplayIcon" -Value "$DestExe,0" -Force
Set-ItemProperty -Path $UninstallKey -Name "DisplayVersion" -Value "0.3.0" -Force
Set-ItemProperty -Path $UninstallKey -Name "Publisher" -Value "DynamicIsland" -Force
Set-ItemProperty -Path $UninstallKey -Name "InstallLocation" -Value $TargetDir -Force
Set-ItemProperty -Path $UninstallKey -Name "UninstallString" -Value "`"$DestExe`" --uninstall" -Force
Set-ItemProperty -Path $UninstallKey -Name "QuietUninstallString" -Value "`"$DestExe`" --uninstall --silent" -Force
Set-ItemProperty -Path $UninstallKey -Name "NoModify" -Value 1 -Type DWord -Force
Set-ItemProperty -Path $UninstallKey -Name "NoRepair" -Value 1 -Type DWord -Force

Write-Host "`n✓ Dynamic Island installed successfully!" -ForegroundColor Green
Write-Host "Launching Dynamic Island..." -ForegroundColor Cyan
Start-Process -FilePath $DestExe
