# AGY CLI Dashboard - Create Desktop Shortcut to Release Executable
$WshShell = New-Object -ComObject WScript.Shell
$DesktopPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), "AGY CLI Dashboard.lnk")

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir

$releaseExe = Join-Path $projectDir "bin\Release\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe"
$releaseDir = Join-Path $projectDir "bin\Release\net10.0-windows10.0.26100.0\win-x64"
$iconPath = Join-Path $projectDir "Assets\AppIcon.ico"

$Shortcut = $WshShell.CreateShortcut($DesktopPath)
$Shortcut.TargetPath = $releaseExe
$Shortcut.WorkingDirectory = $releaseDir
if (Test-Path -LiteralPath $iconPath) {
    $Shortcut.IconLocation = "$iconPath,0"
}
$Shortcut.Description = "AGY CLI Dashboard (Release Build)"
$Shortcut.Save()

Write-Host "Desktop-Verknuepfung fuer Release Build erfolgreich erstellt: $DesktopPath" -ForegroundColor Green
