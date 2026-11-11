@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo   Dynamic Island - Single-File Release Setup Builder
echo ========================================================
echo.

echo [1/4] Compiling Release build...
call dotnet build -c Release
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Build failed.
    pause
    exit /b %ERRORLEVEL%
)

echo [2/4] Running 52-test automated verification suite...
call dotnet run -c Release -- --self-test
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Self-tests failed.
    pause
    exit /b %ERRORLEVEL%
)

echo [3/4] Publishing single-file native executable (win-x64)...
call dotnet publish DynamicIsland.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Publish failed.
    pause
    exit /b %ERRORLEVEL%
)

echo [4/4] Creating DynamicIsland-Setup.exe...
copy /y "publish\DynamicIsland.exe" "DynamicIsland-Setup.exe" >nul
copy /y "publish\DynamicIsland.exe" "publish\DynamicIsland-Setup.exe" >nul
copy /y "publish\DynamicIsland.exe" "installer\DynamicIsland-Setup.exe" >nul

echo.
echo ========================================================
echo   [SUCCESS] Setup executable created successfully!
echo   Location: %~dp0DynamicIsland-Setup.exe
echo ========================================================
echo.
exit /b 0
