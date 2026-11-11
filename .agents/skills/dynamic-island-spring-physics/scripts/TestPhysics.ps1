<#
.SYNOPSIS
    Runs the deterministic spring physics test suite to verify analytical frame-rate invariance.
#>

$ErrorActionPreference = "Continue"

Write-Host "Running Dynamic Island Spring Physics Verification..." -ForegroundColor Cyan

$binPath = ".\bin\Release\net8.0-windows10.0.19041.0\DynamicIsland.exe"
if (-not (Test-Path $binPath)) {
    $binPath = ".\bin\Debug\net8.0-windows10.0.19041.0\DynamicIsland.exe"
}

if (-not (Test-Path $binPath)) {
    cmd.exe /c "dotnet build -c Release"
}

$rawOutput = cmd.exe /c "`"$binPath`" --self-test" 2>&1
$fullText = ($rawOutput | Out-String)

$springTests = $rawOutput | Where-Object { $_ -match "Spring:" }

foreach ($line in $springTests) {
    if ($line -match "\[PASS\]") {
        Write-Host $line -ForegroundColor Green
    } else {
        Write-Host $line -ForegroundColor Red
    }
}

if ($fullText -match "Self-Test Summary:\s+(\d+)\s+passed,\s+0\s+failed") {
    Write-Host "`nAll Spring physics tests passed successfully." -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nSpring physics verification encountered failures." -ForegroundColor Red
    exit 1
}
