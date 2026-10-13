<#
.SYNOPSIS
    Co-Browser CDP (Chrome DevTools Protocol) Bridge für die Antigravity CLI.
.DESCRIPTION
    Ermöglicht der Antigravity CLI und Agenten die Steuerung des im Dashboard
    eingebetteten WinUI 3 Co-Browsers über den Remote-Debugging-Port (Standard 9222).
.EXAMPLE
    .\scripts\co-browser-cdp.ps1 -Action Status
    .\scripts\co-browser-cdp.ps1 -Action Navigate -Url "https://example.com"
    .\scripts\co-browser-cdp.ps1 -Action Type -Selector "#streetInput" -Value "Musterstraße 123"
    .\scripts\co-browser-cdp.ps1 -Action Click -Selector "#submitBtn"
    .\scripts\co-browser-cdp.ps1 -Action Handoff -Reason "Bitte PayPal-Zahlung bestätigen"
    .\scripts\co-browser-cdp.ps1 -Action Ready
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [ValidateSet("Status", "Navigate", "Eval", "Click", "Type", "Screenshot", "Handoff", "Ready")]
    [string]$Action,

    [Parameter(Mandatory = $false)]
    [string]$Url = "",

    [Parameter(Mandatory = $false)]
    [string]$Selector = "",

    [Parameter(Mandatory = $false)]
    [string]$Value = "",

    [Parameter(Mandatory = $false)]
    [string]$Script = "",

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "",

    [Parameter(Mandatory = $false)]
    [string]$Reason = "",

    [Parameter(Mandatory = $false)]
    [int]$Port = 9222
)

$ErrorActionPreference = "Stop"

# Signal- und Discovery-Pfade in AppData dynamisch ermitteln (Public vs Internal)
$appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$appSubDir = "AgyCliDashboard"
if (Test-Path (Join-Path $appData "AgyCliDashboard-Public\CoBrowserProfile")) {
    $appSubDir = "AgyCliDashboard-Public"
}
$signalPath = Join-Path $appData "$appSubDir\CoBrowserProfile\co_browser_signal.json"

# Port dynamisch aus Discovery-Datei lesen falls verfügbar und nicht explizit übergeben
$endpointPath = Join-Path $appData "$appSubDir\co_browser_endpoint.json"
if (-not $PSBoundParameters.ContainsKey('Port') -and (Test-Path $endpointPath)) {
    try {
        $epJson = Get-Content -Path $endpointPath -Raw | ConvertFrom-Json
        if ($epJson.cdpPort) {
            $Port = [int]$epJson.cdpPort
        }
    } catch { }
}

function Send-Signal {
    param([string]$ActionType, [string]$Detail)
    $dir = Split-Path -Parent $signalPath
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $payload = @{
        action = $ActionType
        detail = $Detail
        timestamp = (Get-Date).ToString("o")
    } | ConvertTo-Json -Compress
    Set-Content -Path $signalPath -Value $payload -Encoding UTF8
}

function Get-WsUrl {
    param([int]$CdpPort)
    $versionUrl = "http://localhost:$CdpPort/json/version"
    try {
        $resp = Invoke-RestMethod -Uri $versionUrl -TimeoutSec 3 -ErrorAction Stop
        return $resp.webSocketDebuggerUrl
    }
    catch {
        return $null
    }
}

function Get-PageTarget {
    param([int]$CdpPort)
    $listUrl = "http://localhost:$CdpPort/json/list"
    try {
        $pages = Invoke-RestMethod -Uri $listUrl -TimeoutSec 3 -ErrorAction Stop
        $page = $pages | Where-Object { $_.type -eq "page" } | Select-Object -First 1
        if ($null -ne $page) {
            return $page.webSocketDebuggerUrl
        }
        return ($pages | Select-Object -First 1).webSocketDebuggerUrl
    }
    catch {
        return $null
    }
}

function Invoke-CdpCommand {
    param(
        [string]$WsUrl,
        [string]$Method,
        [hashtable]$Params = @{}
    )

    $ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $cts = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(10))

    try {
        $connectTask = $ws.ConnectAsync([Uri]::new($WsUrl), $cts.Token)
        $connectTask.Wait()

        $id = [Guid]::NewGuid().ToString()
        $requestObj = @{
            id = 1
            method = $Method
            params = $Params
        }
        $json = $requestObj | ConvertTo-Json -Depth 10 -Compress
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        $segment = [ArraySegment[byte]]::new($bytes)

        $sendTask = $ws.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token)
        $sendTask.Wait()

        $ms = [System.IO.MemoryStream]::new()
        try {
            $buffer = [byte[]]::new(65536)
            do {
                $rcvSegment = [ArraySegment[byte]]::new($buffer)
                $rcvTask = $ws.ReceiveAsync($rcvSegment, $cts.Token)
                $rcvTask.Wait()
                if ($rcvTask.Result.Count -gt 0) {
                    $ms.Write($buffer, 0, $rcvTask.Result.Count)
                }
            } while (-not $rcvTask.Result.EndOfMessage)

            $respText = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
            return ($respText | ConvertFrom-Json)
        }
        finally {
            $ms.Dispose()
        }
    }
    finally {
        if ($ws.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
            $closeTask = $ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "Done", $cts.Token)
            $closeTask.Wait()
        }
        $ws.Dispose()
        $cts.Dispose()
    }
}

switch ($Action) {
    "Status" {
        $ws = Get-WsUrl -CdpPort $Port
        if ($ws) {
            Write-Host "🟢 Co-Browser ist aktiv auf Port $Port." -ForegroundColor Green
            Write-Host "WebSocket Debugger: $ws" -ForegroundColor Gray
            exit 0
        } else {
            Write-Host "🔴 Co-Browser ist auf Port $Port nicht erreichbar." -ForegroundColor Red
            exit 1
        }
    }

    "Handoff" {
        Send-Signal -ActionType "waiting_for_user" -Detail $Reason
        Write-Host "🟡 Handoff signalisiert: $Reason" -ForegroundColor Yellow
        exit 0
    }

    "Ready" {
        Send-Signal -ActionType "ready" -Detail ""
        Write-Host "⚪ Co-Browser Status auf Bereit zurückgesetzt." -ForegroundColor Gray
        exit 0
    }

    "Navigate" {
        if ([string]::IsNullOrWhiteSpace($Url)) {
            Write-Error "-Url ist erforderlich für Navigate."
            exit 1
        }
        Send-Signal -ActionType "controlling" -Detail "Navigiere zu $Url..."
        $targetWs = Get-PageTarget -CdpPort $Port
        if (-not $targetWs) {
            Write-Error "Keine offene Browser-Seite gefunden auf Port $Port."
            exit 1
        }
        $res = Invoke-CdpCommand -WsUrl $targetWs -Method "Page.navigate" -Params @{ url = $Url }
        Write-Host "🌐 Navigation angestoßen zu: $Url" -ForegroundColor Cyan
        exit 0
    }

    "Eval" {
        if ([string]::IsNullOrWhiteSpace($Script)) {
            Write-Error "-Script ist erforderlich für Eval."
            exit 1
        }
        $targetWs = Get-PageTarget -CdpPort $Port
        if (-not $targetWs) {
            Write-Error "Keine offene Browser-Seite gefunden auf Port $Port."
            exit 1
        }
        $res = Invoke-CdpCommand -WsUrl $targetWs -Method "Runtime.evaluate" -Params @{
            expression = $Script
            returnByValue = $true
        }
        $res | ConvertTo-Json -Depth 5
        exit 0
    }

    "Click" {
        if ([string]::IsNullOrWhiteSpace($Selector)) {
            Write-Error "-Selector ist erforderlich für Click."
            exit 1
        }
        Send-Signal -ActionType "controlling" -Detail "Klicke Element: $Selector..."
        $js = @"
(function() {
    var el = document.querySelector('$Selector');
    if (!el) return { success: false, error: 'Element nicht gefunden' };
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    el.click();
    return { success: true };
})()
"@
        $targetWs = Get-PageTarget -CdpPort $Port
        $res = Invoke-CdpCommand -WsUrl $targetWs -Method "Runtime.evaluate" -Params @{
            expression = $js
            returnByValue = $true
        }
        Write-Host "👆 Klick ausgeführt auf: $Selector" -ForegroundColor Green
        exit 0
    }

    "Type" {
        if ([string]::IsNullOrWhiteSpace($Selector)) {
            Write-Error "-Selector ist erforderlich für Type."
            exit 1
        }
        Send-Signal -ActionType "controlling" -Detail "Tippe in $Selector..."
        $escapedValue = $Value.Replace("'", "\'")
        $js = @"
(function() {
    var el = document.querySelector('$Selector');
    if (!el) return { success: false, error: 'Element nicht gefunden' };
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    el.focus();
    el.value = '$escapedValue';
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
    return { success: true };
})()
"@
        $targetWs = Get-PageTarget -CdpPort $Port
        $res = Invoke-CdpCommand -WsUrl $targetWs -Method "Runtime.evaluate" -Params @{
            expression = $js
            returnByValue = $true
        }
        Write-Host "⌨️ Text eingegeben in $Selector" -ForegroundColor Green
        exit 0
    }

    "Screenshot" {
        if ([string]::IsNullOrWhiteSpace($OutputPath)) {
            $OutputPath = Join-Path (Get-Location) "cobrowser_screenshot.png"
        }
        $targetWs = Get-PageTarget -CdpPort $Port
        $res = Invoke-CdpCommand -WsUrl $targetWs -Method "Page.captureScreenshot" -Params @{ format = "png" }
        if ($res.result.data) {
            $bytes = [Convert]::FromBase64String($res.result.data)
            [IO.File]::WriteAllBytes($OutputPath, $bytes)
            Write-Host "📸 Screenshot gespeichert unter: $OutputPath" -ForegroundColor Green
            exit 0
        } else {
            Write-Error "Screenshot-Aufnahme fehlgeschlagen."
            exit 1
        }
    }
}
