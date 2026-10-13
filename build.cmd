@echo off
setlocal
cd /d "%~dp0"

echo ========================================================
echo   Antigravity CLI Dashboard - Build (Debug)
echo ========================================================
echo.

dotnet build -c Debug -m:1 /p:BuildInParallel=false
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    echo Please ensure the .NET 10 Windows SDK is installed.
    pause
    exit /b 1
)

echo.
echo [SUCCESS] Build completed successfully!
echo Binary: bin\Debug\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe
echo.
pause
