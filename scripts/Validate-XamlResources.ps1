param (
    [string]$ProjectDir = ""
)

if ([string]::IsNullOrWhiteSpace($ProjectDir)) {
    $ProjectDir = Split-Path -Parent $PSScriptRoot
}

$ErrorActionPreference = "Stop"
$hasErrors = $false

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " AGY CLI DASHBOARD - XAML RESOURCE VALIDATOR" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan

$appXamlPath = Join-Path $ProjectDir "App.xaml"
if (-not (Test-Path $appXamlPath)) {
    Write-Error "App.xaml nicht gefunden unter: $appXamlPath"
    exit 1
}

# 1. Sammle alle globalen Keys aus App.xaml
$appXamlContent = Get-Content $appXamlPath -Raw
$globalKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

$keyMatches = [regex]::Matches($appXamlContent, 'x:Key\s*=\s*"([^"]+)"')
foreach ($m in $keyMatches) {
    [void]$globalKeys.Add($m.Groups[1].Value)
}

# Auch alle per MergedDictionaries eingebundenen Theme-Dateien (Theme/*.xaml: Palette, Styles) liefern globale Keys
$themeDir = Join-Path $ProjectDir "Theme"
if (Test-Path $themeDir) {
    foreach ($themeFile in Get-ChildItem -Path $themeDir -Recurse -Filter *.xaml) {
        $themeContent = Get-Content $themeFile.FullName -Raw
        foreach ($tm in [regex]::Matches($themeContent, 'x:Key\s*=\s*"([^"]+)"')) {
            [void]$globalKeys.Add($tm.Groups[1].Value)
        }
    }
}

# Bekannte / integrierte WinUI 3 Theme Resources, die nicht in App.xaml stehen müssen
$knownBuiltInResources = @(
    "ControlElevationBrush",
    "ContentControlThemeFontFamily",
    "DefaultThresholdImageSize",
    "SystemControlPageBackgroundChromeLowBrush",
    "SystemControlForegroundBaseHighBrush",
    "SystemControlForegroundBaseMediumLowBrush",
    "SystemControlHighlightAccentBrush",
    "DefaultButtonStyle"
)
foreach ($builtin in $knownBuiltInResources) {
    [void]$globalKeys.Add($builtin)
}

Write-Host "Gefundene globale Ressourcen (App.xaml + Theme/*.xaml): $($globalKeys.Count)" -ForegroundColor Gray

# 2. Durchsuche alle .xaml Dateien nach {StaticResource ...}
$xamlFiles = Get-ChildItem -Path $ProjectDir -Recurse -Filter *.xaml | 
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

$checkedXamlCount = 0
$checkedStaticResourceCount = 0

foreach ($file in $xamlFiles) {
    $checkedXamlCount++
    $fileContent = Get-Content $file.FullName -Raw
    $localKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    
    # Lokale x:Key Definitionen erfassen
    $localMatches = [regex]::Matches($fileContent, 'x:Key\s*=\s*"([^"]+)"')
    foreach ($lm in $localMatches) {
        [void]$localKeys.Add($lm.Groups[1].Value)
    }

    # Alle {StaticResource ...} Verwendungen prüfen
    $staticResMatches = [regex]::Matches($fileContent, '\{StaticResource\s+([^}\s]+)\}')
    foreach ($sm in $staticResMatches) {
        $checkedStaticResourceCount++
        $resKey = $sm.Groups[1].Value
        
        if (-not $globalKeys.Contains($resKey) -and -not $localKeys.Contains($resKey)) {
            Write-Host "❌ [XAML FEHLER] Fehlende StaticResource '$resKey' in Datei: $($file.FullName)" -ForegroundColor Red
            $hasErrors = $true
        }
    }
}

# 3. Durchsuche alle .cs Dateien nach Application.Current.Resources["..."]
$csFiles = Get-ChildItem -Path $ProjectDir -Recurse -Filter *.cs | 
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

$checkedCsCount = 0
foreach ($file in $csFiles) {
    $checkedCsCount++
    $fileContent = Get-Content $file.FullName -Raw
    
    $csResMatches = [regex]::Matches($fileContent, 'Resources\[\s*"([^"]+)"\s*\]')
    foreach ($cm in $csResMatches) {
        $resKey = $cm.Groups[1].Value
        if (-not $globalKeys.Contains($resKey)) {
            Write-Host "❌ [C# FEHLER] Unbekannte Resource-Indexierung '$resKey' in Datei: $($file.FullName)" -ForegroundColor Red
            $hasErrors = $true
        }
    }
}

Write-Host "Geprüft: $checkedXamlCount XAML-Dateien ($checkedStaticResourceCount StaticResource-Referenzen), $checkedCsCount C#-Dateien." -ForegroundColor Gray

if ($hasErrors) {
    Write-Host "`n🚨 FEHLER: Fehlende XAML/C# Ressourcen entdeckt! Build wird abgebrochen." -ForegroundColor Red
    exit 1
} else {
    Write-Host "`n✅ Alle XAML StaticResources und C# Resource-Lookups sind vollständig und gültig." -ForegroundColor Green
    exit 0
}
