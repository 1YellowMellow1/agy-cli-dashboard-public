# AGY CLI Dashboard - Create Desktop Shortcut
$WshShell = New-Object -ComObject WScript.Shell
$DesktopPath = [System.IO.Path]::Combine([Environment]::GetFolderPath('Desktop'), "AGY CLI Dashboard.lnk")

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir

# Bevorzuge Release-Build, sonst Fallback auf Debug-Build oder start.cmd
$targetExe = Join-Path $projectDir "bin\Release\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe"
if (-not (Test-Path -LiteralPath $targetExe)) {
    $targetExe = Join-Path $projectDir "bin\Debug\net10.0-windows10.0.26100.0\win-x64\Antigravity CLI Dashboard.exe"
}
if (-not (Test-Path -LiteralPath $targetExe)) {
    $targetExe = Join-Path $projectDir "start.cmd"
}

$workingDir = Split-Path -Parent $targetExe
if (-not $workingDir) { $workingDir = $projectDir }
$iconPath = Join-Path $projectDir "Assets\AppIcon.ico"

$Shortcut = $WshShell.CreateShortcut($DesktopPath)
$Shortcut.TargetPath = $targetExe
$Shortcut.WorkingDirectory = $workingDir
if (Test-Path -LiteralPath $iconPath) {
    $Shortcut.IconLocation = "$iconPath,0"
}
$Shortcut.Description = "AGY CLI Dashboard"
$Shortcut.Save()

Write-Host "Desktop shortcut created successfully: $DesktopPath -> $targetExe" -ForegroundColor Green
