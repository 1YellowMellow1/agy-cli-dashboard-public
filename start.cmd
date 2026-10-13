@echo off
setlocal
cd /d "%~dp0"

set "ARCH=win-x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "ARCH=win-arm64"
if /i "%PROCESSOR_ARCHITEW6432%"=="ARM64" set "ARCH=win-arm64"

set "RELEASE_EXE=bin\Release\net10.0-windows10.0.26100.0\%ARCH%\Antigravity CLI Dashboard.exe"
set "DEBUG_EXE=bin\Debug\net10.0-windows10.0.26100.0\%ARCH%\Antigravity CLI Dashboard.exe"

if not exist "%DEBUG_EXE%" if not exist "%RELEASE_EXE%" (
    if exist "bin\Debug\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe" (
        set "DEBUG_EXE=bin\Debug\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe"
    )
    if exist "bin\Release\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe" (
        set "RELEASE_EXE=bin\Release\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe"
    )
)

if "%~1"=="--build" goto do_build
if "%~1"=="-b" goto do_build
if "%~1"=="--rebuild" goto do_build

if exist "%RELEASE_EXE%" (
    echo [AGY Dashboard] Launching Release build...
    echo (Tip: Run 'build.cmd' or 'start.cmd --build' to compile code changes)
    start "" "%RELEASE_EXE%"
    exit /b 0
)

if exist "%DEBUG_EXE%" (
    echo [AGY Dashboard] Launching Debug build...
    echo (Tip: Run 'build.cmd' or 'start.cmd --build' to compile code changes)
    start "" "%DEBUG_EXE%"
    exit /b 0
)

:do_build
echo ========================================================
echo   Antigravity CLI Dashboard - Build & Launch
echo ========================================================
echo   No prebuilt binary found.
echo   Running automated first-time build (Debug)...
echo ========================================================
echo.

dotnet build -c Debug -m:1 /p:BuildInParallel=false
if errorlevel 1 (
    echo.
    echo [ERROR] Build failed.
    echo Please ensure the .NET 10 Windows SDK is installed.
    echo Download: https://dotnet.microsoft.com/download/dotnet/10.0
    echo.
    pause
    exit /b 1
)

if exist "%DEBUG_EXE%" (
    echo.
    echo [AGY Dashboard] Build succeeded. Launching application...
    start "" "%DEBUG_EXE%"
) else (
    echo.
    echo [ERROR] Executable binary not found after build.
    pause
    exit /b 1
)
