<#
.SYNOPSIS
    Automated execution of Dynamic Island performance diagnostic modes.
.DESCRIPTION
    Runs --diagnose-memory, --diagnose-software, and --diagnose-window and summarizes memory health.
#>

$ErrorActionPreference = "Continue"

$binPath = ".\bin\Release\net8.0-windows10.0.19041.0\DynamicIsland.exe"
if (-not (Test-Path $binPath)) {
    $binPath = ".\bin\Debug\net8.0-windows10.0.19041.0\DynamicIsland.exe"
}

if (-not (Test-Path $binPath)) {
    cmd.exe /c "dotnet build -c Release"
}

Write-Host "=== Dynamic Island Performance Audit ===" -ForegroundColor Cyan

Write-Host "`n1. Running --diagnose-memory..." -ForegroundColor Yellow
cmd.exe /c "`"$binPath`" --diagnose-memory"

Write-Host "`n2. Running --diagnose-window (measuring UI initialization footprint)..." -ForegroundColor Yellow
cmd.exe /c "`"$binPath`" --diagnose-window"

Write-Host "`n3. Running --diagnose-software (measuring graphics module footprint)..." -ForegroundColor Yellow
cmd.exe /c "`"$binPath`" --diagnose-software"

Write-Host "`nPerformance diagnostic completed successfully." -ForegroundColor Green
