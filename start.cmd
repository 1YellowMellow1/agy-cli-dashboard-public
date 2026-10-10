@echo off
setlocal
cd /d "%~dp0"

set "RELEASE_EXE=bin\Release\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe"
set "DEBUG_EXE=bin\Debug\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe"

if "%~1"=="--build" goto do_build
if "%~1"=="-b" goto do_build
if "%~1"=="--rebuild" goto do_build

if exist "%RELEASE_EXE%" (
    echo [AGY Dashboard] Starte Release-Build...
    echo (Tipp: Fuehre 'build.cmd' oder 'start.cmd --build' aus, um Code-Aenderungen zu kompilieren)
    start "" "%RELEASE_EXE%"
    exit /b 0
)

if exist "%DEBUG_EXE%" (
    echo [AGY Dashboard] Starte Debug-Build...
    echo (Tipp: Fuehre 'build.cmd' oder 'start.cmd --build' aus, um Code-Aenderungen zu kompilieren)
    start "" "%DEBUG_EXE%"
    exit /b 0
)

:do_build
echo ========================================================
echo   Antigravity CLI Dashboard - Build & Start
echo ========================================================
echo   Es wurde noch kein ausfuehrbarer Build gefunden.
echo   Fuehre automatischen Erst-Build durch (Debug)...
echo ========================================================
echo.

dotnet build -c Debug -m:1 /p:BuildInParallel=false
if errorlevel 1 (
    echo.
    echo [FEHLER] Build fehlgeschlagen.
    echo Bitte stelle sicher, dass das .NET 10 Windows SDK installiert ist.
    echo.
    pause
    exit /b 1
)

if exist "%DEBUG_EXE%" (
    echo.
    echo [AGY Dashboard] Build erfolgreich. Starte Anwendung...
    start "" "%DEBUG_EXE%"
) else (
    echo.
    echo [FEHLER] Ausfuehrbare Datei konnte nach dem Build nicht gefunden werden.
    pause
    exit /b 1
)
