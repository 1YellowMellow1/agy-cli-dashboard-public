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
    echo [FEHLER] Build fehlgeschlagen.
    pause
    exit /b 1
)

echo.
echo [ERFOLG] Build erfolgreich abgeschlossen!
echo Ziel: bin\Debug\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe
echo.
pause
