<#
.SYNOPSIS
    Compiles, self-tests, and publishes Dynamic Island.
.DESCRIPTION
    Runs Release build, executes the 37-test self-test suite, and publishes a self-contained win-x64 binary.
#>

$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " Dynamic Island Release & Verification Pipeline" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

Write-Host "`n[1/3] Building Release configuration..." -ForegroundColor Yellow
cmd.exe /c "dotnet build -c Release"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`n[2/3] Executing deterministic self-test suite (37 assertions)..." -ForegroundColor Yellow
$binPath = ".\bin\Release\net8.0-windows10.0.19041.0\DynamicIsland.exe"
cmd.exe /c "`"$binPath`" --self-test"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Self-tests failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`n[3/3] Publishing single-file win-x64 executable..." -ForegroundColor Yellow
cmd.exe /c "dotnet publish DynamicIsland.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Publish failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`nRelease build and verification completed successfully!" -ForegroundColor Green
Write-Host "Artifact available at: publish\DynamicIsland.exe" -ForegroundColor Green
