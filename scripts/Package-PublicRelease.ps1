<#
.SYNOPSIS
    Package-PublicRelease.ps1 - Erstellt ein portables, self-contained GitHub Release-ZIP.
.DESCRIPTION
    Führt einen self-contained Release-Build aus, bündelt die App samt Launcher und
    Skripten, packt ein ZIP-Archiv und generiert SHA-256 Prüfsummen für GitHub Releases.
#>

[CmdletBinding()]
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $PSScriptRoot
$DistDir = Join-Path $ProjectDir "dist"
$ZipName = "Antigravity-CLI-Dashboard-v$Version-win-x64.zip"
$ZipPath = Join-Path $DistDir $ZipName
$ChecksumPath = Join-Path $DistDir "checksums-sha256.txt"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   AGY CLI Dashboard - Release Packager v$Version" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (Test-Path $DistDir) {
    Remove-Item -Path $DistDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -Path $DistDir -ItemType Directory -Force | Out-Null

$PublishDir = Join-Path $DistDir "publish"

Write-Host "[1/4] Erstelle Self-Contained Release-Build (win-x64)..." -ForegroundColor Green
dotnet publish "$ProjectDir\AgyCliDashboard.csproj" -c Release -r win-x64 --self-contained true -o $PublishDir /p:PublishSingleFile=false -m:1 /p:BuildInParallel=false
if ($LASTEXITCODE -ne 0) {
    Write-Error "Release-Publish fehlgeschlagen!"
}

Write-Host "[2/4] Bündele Launcher und Starter-Dateien..." -ForegroundColor Green
$LauncherPublishDir = Join-Path $DistDir "launcher_pub"
dotnet publish "$ProjectDir\AgyLauncher\AgyLauncher.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o $LauncherPublishDir -m:1 /p:BuildInParallel=false
if (Test-Path "$LauncherPublishDir\AgyLauncher.exe") {
    Copy-Item -Path "$LauncherPublishDir\AgyLauncher.exe" -Destination (Join-Path $PublishDir "AgyLauncher.exe") -Force
}

# Bequeme Start-Skripte und Dokumentation beilegen
$StartCmd = @"
@echo off
start "" "%~dp0Antigravity CLI Dashboard.exe"
"@
Set-Content -Path (Join-Path $PublishDir "start.cmd") -Value $StartCmd -Encoding ASCII
Set-Content -Path (Join-Path $PublishDir "Start-Dashboard.cmd") -Value $StartCmd -Encoding ASCII

if (Test-Path "$ProjectDir\LICENSE") {
    Copy-Item -Path "$ProjectDir\LICENSE" -Destination (Join-Path $PublishDir "LICENSE") -Force
}
if (Test-Path "$ProjectDir\README.md") {
    Copy-Item -Path "$ProjectDir\README.md" -Destination (Join-Path $PublishDir "README.md") -Force
}

Write-Host "[3/4] Packe ZIP-Archiv ($ZipName)..." -ForegroundColor Green
Compress-Archive -Path "$PublishDir\*" -DestinationPath $ZipPath -Force

Write-Host "[4/4] Berechne SHA-256 Prüfsumme..." -ForegroundColor Green
$hash = (Get-FileHash -Path $ZipPath -Algorithm SHA256).Hash
$checksumContent = "$hash  $ZipName`n"
Set-Content -Path $ChecksumPath -Value $checksumContent -Encoding ASCII

# Temporäres Publish-Verzeichnis bereinigen
Remove-Item -Path $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $LauncherPublishDir) {
    Remove-Item -Path $LauncherPublishDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host "   Release-Paket erfolgreich erstellt!" -ForegroundColor Green
Write-Host "   ZIP:      $ZipPath"
Write-Host "   SHA-256:  $hash"
Write-Host "==========================================================" -ForegroundColor Green
