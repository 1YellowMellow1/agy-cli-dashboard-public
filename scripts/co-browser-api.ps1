<#
.SYNOPSIS
    Ultra-schneller CLI-Client für den Antigravity KI-Co-Browser HTTP-RPC Server.
.DESCRIPTION
    Kommuniziert direkt mit dem im Dashboard integrierten CoBrowserHttpServer (Port 9223).
    Unterstützt semantische DOM-Element-Maps, Event-Driven Auto-Waiting (MutationObserver),
    Batch-Formularbefüllung, Human Takeover & Screenshots in Millisekunden.
.EXAMPLE
    .\scripts\co-browser-api.ps1 -Action Status
    .\scripts\co-browser-api.ps1 -Action Map
    .\scripts\co-browser-api.ps1 -Action Click -Target 2
    .\scripts\co-browser-api.ps1 -Action Click -Selector "#submit-btn"
    .\scripts\co-browser-api.ps1 -Action Type -Target 1 -Text "Berlin" -Enter
    .\scripts\co-browser-api.ps1 -Action Fill -Data '{"#name": "Max Mustermann", "#city": "Berlin"}' -Submit "#save-btn"
    .\scripts\co-browser-api.ps1 -Action Screenshot -OutPath "screen.png"
    .\scripts\co-browser-api.ps1 -Action Handoff -Reason "Bitte Captcha lösen"
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("Status", "Open", "Close", "Map", "Content", "Network", "Console", "AXTree", "Click", "Type", "Fill", "Eval", "Navigate", "Scroll", "Upload", "Overlay", "Screenshot", "Handoff", "Ready", "Controlling", "Diagnostics")]
    [string]$Action = "Status",

    [Parameter()]
    [string]$Target = "", # Element-ID (Zahl wie 1, 2) oder CSS-Selektor (#btn, .submit)

    [Parameter()]
    [string]$Selector = "",

    [Parameter()]
    [string]$Text = "",

    [Parameter()]
    [string]$Url = "",

    [Parameter()]
    [Alias("Expression", "Code")]
    [string]$Script = "",

    [Parameter()]
    [string]$Data = "", # JSON für Fill-Form

    [Parameter()]
    [string]$Submit = "", # Submit-Button Selektor für Fill-Form

    [Parameter()]
    [string]$FilePath = "", # Lokaler Dateipfad für Upload

    [Parameter()]
    [string]$Direction = "down", # Scroll-Richtung: down, up, top, bottom

    [Parameter()]
    [int]$Pixels = 600, # Pixelanzahl beim Scrollen

    [Parameter()]
    [string]$OutPath = "",

    [Parameter()]
    [string]$Reason = "",

    [Parameter()]
    [switch]$Enter,

    [Parameter()]
    [switch]$NoClear,

    [Parameter()]
    [switch]$NoOpen,

    [Parameter()]
    [switch]$Hide,

    [Parameter()]
    [switch]$Overlay,

    [Parameter()]
    [switch]$ScanAll,

    [Parameter()]
    [switch]$Raw,

    [Parameter()]
    [int]$MaxLength = 60000,

    [Parameter()]
    [string]$Type = "",

    [Parameter()]
    [string]$Search = "",

    [Parameter()]
    [string]$RequestId = "",

    [Parameter()]
    [switch]$IncludeBody,

    [Parameter()]
    [switch]$Clear,

    [Parameter()]
    [int]$Limit = 25,

    [Parameter()]
    [int]$MaxDepth = 10,

    [Parameter()]
    [switch]$All,

    [Parameter()]
    [int]$TimeoutMs = 5000,

    [Parameter()]
    [switch]$WaitForLoad,

    [Parameter()]
    [switch]$SlowOnly,

    [Parameter()]
    [switch]$Reset,

    [Parameter()]
    [int]$Port = 0
)

try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# 1. Endpunkt-Discovery
$AuthToken = ""
$endpointFile = Join-Path $env:LOCALAPPDATA "AgyCliDashboard\co_browser_endpoint.json"
if (Test-Path $endpointFile) {
    try {
        $discovery = Get-Content $endpointFile -Raw | ConvertFrom-Json
        if ($Port -eq 0 -and $discovery.apiPort) {
            $Port = $discovery.apiPort
        }
        if ($discovery.authToken) {
            $AuthToken = $discovery.authToken
        }
    } catch { }
}
if ($Port -eq 0) {
    $Port = 9223 # Default Fallback
}

$BaseUrl = "http://127.0.0.1:$Port"

function Invoke-Api {
    param(
        [string]$Path,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $uri = "$BaseUrl$Path"
    try {
        $headers = @{}
        if (-not [string]::IsNullOrWhiteSpace($AuthToken)) {
            $headers["X-Agy-Token"] = $AuthToken
        }
        if (-not [string]::IsNullOrWhiteSpace($env:ANTIGRAVITY_CONVERSATION_ID)) {
            $headers["X-Conversation-Id"] = $env:ANTIGRAVITY_CONVERSATION_ID.Trim()
        }

        $params = @{
            Uri = $uri
            Method = $Method
            Headers = $headers
            TimeoutSec = 15
            ErrorAction = "Stop"
        }
        if ($Body) {
            $params["ContentType"] = "application/json; charset=utf-8"
            $params["Body"] = ($Body | ConvertTo-Json -Depth 10 -Compress)
        }
        return Invoke-RestMethod @params
    } catch {
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            try {
                return ($_.ErrorDetails.Message | ConvertFrom-Json)
            } catch { }
        }
        if ($_.Exception -and $_.Exception.Response) {
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream) {
                    $reader = [System.IO.StreamReader]::new($stream)
                    $errBody = $reader.ReadToEnd()
                    if ($errBody) {
                        return ($errBody | ConvertFrom-Json)
                    }
                }
            } catch { }
        }
        Write-Error "[CoBrowser API Error] Verbindung zu $uri fehlgeschlagen: $_"
        return $null
    }
}

# 2. Aktionen ausführen
switch ($Action) {
    "Status" {
        $res = Invoke-Api -Path "/api/browser/status"
        if ($res) {
            Write-Host "==========================================" -ForegroundColor Cyan
            Write-Host "  ANTIGRAVITY KI-CO-BROWSER STATUS" -ForegroundColor Cyan
            Write-Host "==========================================" -ForegroundColor Cyan
            Write-Host "Status:       $($res.status)" -ForegroundColor Yellow
            Write-Host "Titel:        $($res.title)" -ForegroundColor White
            Write-Host "URL:          $($res.url)" -ForegroundColor Gray
            Write-Host "API Port:     $($res.apiPort)" -ForegroundColor Green
            Write-Host "CDP Port:     $($res.cdpPort)" -ForegroundColor Green
            Write-Host "Takeover:     $($res.isTakeoverActive)" -ForegroundColor Magenta
            Write-Host "Initialisiert: $($res.isInitialized)" -ForegroundColor Gray
            $openState = if ($res.isOpen -eq $true) { "Ja" } elseif ($res.isOpen -eq $false) { "Nein" } else { "Unbekannt" }
            Write-Host "Sichtbar (UI): $openState" -ForegroundColor Cyan
        }
    }

    "Open" {
        $body = @{}
        if ($Url) { $body["url"] = $Url }
        $res = Invoke-Api -Path "/api/browser/open" -Method "POST" -Body $body
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] KI-Co-Browser im Split-Screen geöffnet ($($res.url))." -ForegroundColor Green
        } else {
            Write-Error "[Fehler] Öffnen des Co-Browsers fehlgeschlagen: $($res.error)"
        }
    }

    "Close" {
        $res = Invoke-Api -Path "/api/browser/close" -Method "POST" -Body @{}
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] KI-Co-Browser im Split-Screen geschlossen." -ForegroundColor Yellow
        } else {
            Write-Error "[Fehler] Schließen des Co-Browsers fehlgeschlagen: $($res.error)"
        }
    }

    "Map" {
        $queryParams = @()
        if ($Overlay.IsPresent) { $queryParams += "overlay=true" }
        if ($ScanAll.IsPresent) { $queryParams += "scanAll=true" }
        $qStr = if ($queryParams.Count -gt 0) { "?" + ($queryParams -join "&") } else { "" }

        $res = Invoke-Api -Path "/api/browser/interactive-map$qStr"
        if ($res) {
            Write-Host "`n[Interactive Map] $($res.title) ($($res.url))`n" -ForegroundColor Cyan
            if ($res.hasActiveDialog) {
                Write-Host ">>> AKTIVER MODAL-DIALOG GEFUNDEN: $($res.dialogTitle) <<<" -ForegroundColor Magenta
                Write-Host "    (Elemente im Dialog sind mit * markiert und magenta gefärbt)`n" -ForegroundColor DarkMagenta
            }
            Write-Host "ID  | TAG       | TEXT / BESCHREIBUNG                     | SELEKTOR" -ForegroundColor DarkGray
            Write-Host "----+-----------+------------------------------------------+-----------------------" -ForegroundColor DarkGray

            foreach ($el in $res.elements) {
                $dlgMarker = if ($el.inDialog) { "*" } else { " " }
                $idStr = "$($el.id)$dlgMarker".PadRight(4)
                $tagStr = "$($el.tag)".PadRight(9)
                $txt = ($el.text -replace "`r|`n", " ").Trim()
                if ($txt.Length -gt 40) { $txt = $txt.Substring(0, 37) + "..." }
                $txtStr = $txt.PadRight(40)
                $sel = $el.selector

                $color = "White"
                if ($el.inDialog) { $color = "Magenta" }
                elseif ($el.tag -eq "button" -or $el.type -eq "submit") { $color = "Green" }
                elseif ($el.tag -eq "input" -or $el.tag -eq "textarea") { $color = "Yellow" }
                elseif ($el.tag -eq "a") { $color = "Cyan" }

                Write-Host "$idStr| $tagStr | $txtStr | $sel" -ForegroundColor $color
            }
            Write-Host "`nInsgesamt $($res.elementCount) interaktive Elemente gefunden." -ForegroundColor DarkGray
            Write-Host "Tipp: Nutze 'co-browser-api.ps1 -Action Click -Target <ID>' oder '-Action Type -Target <ID> -Text ...'" -ForegroundColor DarkCyan
        }
    }

    "Content" {
        $queryParams = @()
        if ($Selector) { $queryParams += "selector=" + [Uri]::EscapeDataString($Selector) }
        elseif ($Target) { $queryParams += "selector=" + [Uri]::EscapeDataString($Target) }
        if ($Raw.IsPresent) { $queryParams += "clean=false" }
        if ($MaxLength -gt 0 -and $MaxLength -ne 60000) { $queryParams += "maxLength=$MaxLength" }
        $qStr = if ($queryParams.Count -gt 0) { "?" + ($queryParams -join "&") } else { "" }

        $res = Invoke-Api -Path "/api/browser/content$qStr"
        if (-not $res) { return }
        if (-not $res.success -or $res.error) {
            Write-Error "[Fehler] Text-Extraktion fehlgeschlagen: $($res.error)"
            return
        }
        Write-Host "`n[Content] $($res.title) ($($res.url))`n" -ForegroundColor Cyan
        if ($res.description) {
            Write-Host "Beschreibung: $($res.description)`n" -ForegroundColor DarkGray
        }
        Write-Host $res.content -ForegroundColor White
        Write-Host "`n(Zeichen: $($res.characterCount), Konvertierte Tabellen: $($res.tableCount))`n" -ForegroundColor DarkGray
    }

    "Network" {
        $queryParams = @()
        if ($Type) { $queryParams += "type=" + [Uri]::EscapeDataString($Type) }
        if ($Search) { $queryParams += "search=" + [Uri]::EscapeDataString($Search) }
        if ($RequestId) { $queryParams += "requestId=" + [Uri]::EscapeDataString($RequestId) }
        if ($IncludeBody.IsPresent) { $queryParams += "includeBody=true" }
        if ($Clear.IsPresent) { $queryParams += "clear=true" }
        if ($Limit -ne 25) { $queryParams += "limit=$Limit" }
        $qStr = if ($queryParams.Count -gt 0) { "?" + ($queryParams -join "&") } else { "" }

        $res = Invoke-Api -Path "/api/browser/network$qStr"
        if (-not $res) { return }
        if (-not $res.success -or $res.error) {
            Write-Error "[Fehler] Netzwerk-Abfrage fehlgeschlagen: $($res.error)"
            return
        }

        if ($Clear.IsPresent) {
            Write-Host "[OK] Netzwerk-Puffer geleert." -ForegroundColor Green
            return
        }

        Write-Host "`n=== NETZWERK-TRAFFIC ($($res.count) Treffer / $($res.totalBuffered) gepuffert) ===`n" -ForegroundColor Cyan
        foreach ($entry in $res.entries) {
            $st = if ($entry.failed) { "ERR " } else { "$($entry.statusCode)".PadRight(4) }
            $m = "$($entry.method)".PadRight(6)
            $t = "$($entry.resourceType)".PadRight(8)

            $color = if ($entry.failed -or $entry.statusCode -ge 400) { "Red" }
                     elseif ($entry.resourceType -eq "XHR" -or $entry.resourceType -eq "Fetch") { "Yellow" }
                     else { "White" }

            Write-Host "• [$m $st] ($t) $($entry.url)" -ForegroundColor $color
            if ($entry.mimeType) {
                Write-Host "  MIME: $($entry.mimeType) | ID: $($entry.requestId)" -ForegroundColor DarkGray
            }
            if ($entry.responseBody) {
                $snippet = if ($entry.responseBody.Length -gt 600) { $entry.responseBody.Substring(0, 597) + "..." } else { $entry.responseBody }
                Write-Host "  Payload:`n$snippet`n" -ForegroundColor DarkCyan
            }
        }
        Write-Host "`n(Tipp: Nutze -IncludeBody oder -Search <Begriff>, um JSON-APIs zu filtern)`n" -ForegroundColor DarkGray
    }

    "Console" {
        $queryParams = @()
        if ($Type) { $queryParams += "type=$Type" }
        if ($Search) { $queryParams += "search=$Search" }
        if ($Limit -gt 0) { $queryParams += "limit=$Limit" }
        if ($Clear.IsPresent) { $queryParams += "clear=true" }
        if (-not $All.IsPresent) { $queryParams += "currentUrlOnly=true" }
        $qStr = if ($queryParams.Count -gt 0) { "?" + ($queryParams -join "&") } else { "" }

        $res = Invoke-Api -Path "/api/browser/console$qStr"
        if (-not $res) { return }
        if (-not $res.success -or $res.error) {
            Write-Error "[Fehler] Konsole-Abfrage fehlgeschlagen: $($res.error)"
            return
        }

        if ($Clear.IsPresent) {
            Write-Host "[OK] Browser-Konsolenpuffer erfolgreich geleert." -ForegroundColor Green
            return
        }

        Write-Host "`n[Browser-Konsole] $($res.count) Meldungen (Fehler: $($res.errorCount), Warnungen: $($res.warningCount), Puffer gesamt: $($res.totalBuffered))`n" -ForegroundColor Cyan

        if ($res.entries.Count -eq 0) {
            Write-Host "  (Keine Konsolenmeldungen im Puffer)`n" -ForegroundColor DarkGray
            return
        }

        foreach ($entry in $res.entries) {
            $color = switch ($entry.type.ToLowerInvariant()) {
                "error" { "Red" }
                "exception" { "Red" }
                "warning" { "Yellow" }
                "warn" { "Yellow" }
                "info" { "Cyan" }
                default { "White" }
            }

            $loc = if ($entry.url) {
                $file = Split-Path -Leaf $entry.url
                " ($($file):$($entry.lineNumber))"
            } else { "" }

            Write-Host "• [$($entry.type.ToUpper())]$loc $($entry.message)" -ForegroundColor $color
        }
        Write-Host ""
    }

    "AXTree" {
        $queryParams = @()
        if ($All.IsPresent) { $queryParams += "interactiveOnly=false" }
        if ($MaxDepth -ne 10) { $queryParams += "maxDepth=$MaxDepth" }
        $qStr = if ($queryParams.Count -gt 0) { "?" + ($queryParams -join "&") } else { "" }

        $res = Invoke-Api -Path "/api/browser/axtree$qStr"
        if (-not $res) { return }
        if (-not $res.success -or $res.error) {
            Write-Error "[Fehler] AXTree-Abfrage fehlgeschlagen: $($res.error)"
            return
        }

        Write-Host "`n[Accessibility Tree] $($res.title) ($($res.url))`n" -ForegroundColor Cyan
        Write-Host $res.summary -ForegroundColor White
        Write-Host "`n($($res.nodeCount) semantische Knoten erfasst)`n" -ForegroundColor DarkGray
    }

    "Click" {
        $body = @{
            timeoutMs = $TimeoutMs
        }
        # Prüfen, ob Target eine reine Zahl ist (Element-ID)
        if ($Target -match "^\d+$") {
            $body["id"] = [int]$Target
            if ($Text) { $body["text"] = $Text }
            if ($Selector) { $body["selector"] = $Selector }
        } elseif ($Target) {
            $body["selector"] = $Target
            if ($Text) { $body["text"] = $Text }
        } elseif ($Selector) {
            $body["selector"] = $Selector
            if ($Text) { $body["text"] = $Text }
        } elseif ($Text) {
            $body["text"] = $Text
        } else {
            Write-Error "Bitte -Target (ID oder Selektor), -Selector oder -Text angeben!"
            return
        }

        $res = Invoke-Api -Path "/api/browser/click" -Method "POST" -Body $body
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] Element erfolgreich geklickt: $($body.id)$($body.selector)$($body.text)" -ForegroundColor Green
        } else {
            Write-Error "[Fehler] Klick fehlgeschlagen: $($res.error)"
        }
    }

    "Type" {
        $body = @{
            text = $Text
            clear = (-not $NoClear)
            pressEnter = $Enter.IsPresent
            timeoutMs = $TimeoutMs
        }
        if ($Target -match "^\d+$") {
            $body["id"] = [int]$Target
        } elseif ($Target) {
            $body["selector"] = $Target
        } elseif ($Selector) {
            $body["selector"] = $Selector
        } else {
            Write-Error "Bitte -Target (ID oder Selektor) oder -Selector angeben!"
            return
        }

        $res = Invoke-Api -Path "/api/browser/type" -Method "POST" -Body $body
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] Text erfolgreich eingegeben: '$Text'" -ForegroundColor Green
        } else {
            Write-Error "[Fehler] Eingabe fehlgeschlagen: $($res.error)"
        }
    }

    "Fill" {
        if (-not $Data) {
            Write-Error "Bitte -Data als JSON-String übergeben (z. B. '{\`"#name\`": \`"Max\`"}')"
            return
        }
        $fieldsObj = $Data | ConvertFrom-Json
        $body = @{
            fields = $fieldsObj
        }
        if ($Submit) {
            $body["submit"] = $Submit
        }

        $res = Invoke-Api -Path "/api/browser/fill-form" -Method "POST" -Body $body
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] Formular ausgefüllt: $($res.filledCount) Felder befüllt (Submit: $($res.submitted))" -ForegroundColor Green
        } else {
            Write-Error "[Fehler] Formular-Befüllung fehlgeschlagen: $($res.error)"
        }
    }

    "Eval" {
        if (-not $Script) {
            Write-Error "Bitte -Script angeben!"
            return
        }
        $res = Invoke-Api -Path "/api/browser/eval" -Method "POST" -Body @{ script = $Script }
        if (-not $res) { return }
        if ($res.success) {
            $output = if ($null -ne $res.raw) { $res.raw } else { $res.result }
            Write-Host $output
        } else {
            Write-Error "[Fehler] Script-Ausführung fehlgeschlagen: $($res.error)"
        }
    }

    "Navigate" {
        $body = @{}
        if ($Url) {
            $body["url"] = $Url
        } elseif ($Target -in @("reload", "back", "forward")) {
            $body["action"] = $Target
        } else {
            Write-Error "Bitte -Url oder -Target 'reload'/'back'/'forward' angeben!"
            return
        }

        if ($NoOpen.IsPresent) {
            $body["open"] = $false
        } else {
            $body["open"] = $true
        }

        if ($WaitForLoad.IsPresent) {
            $body["waitForLoad"] = $true
            if ($TimeoutMs -gt 0) {
                $body["timeoutMs"] = $TimeoutMs
            }
        }

        $res = Invoke-Api -Path "/api/browser/navigate" -Method "POST" -Body $body
        if (-not $res) { return }
        if ($res.success) {
            $statusExtra = if ($res.loaded) { " [Seite vollständig geladen]" } elseif ($res.timedOut) { " [Timeout beim Laden]" } else { "" }
            Write-Host "[OK] Navigation ausgeführt ($($body.url)$($body.action))$statusExtra" -ForegroundColor Green
        } else {
            Write-Error "[Fehler] Navigation fehlgeschlagen: $($res.error)"
        }
    }

    "Scroll" {
        $body = @{
            direction = $Direction
            pixels = $Pixels
        }
        if ($Target -match "^\d+$") {
            $body["id"] = [int]$Target
        } elseif ($Target) {
            $body["selector"] = $Target
        } elseif ($Selector) {
            $body["selector"] = $Selector
        }

        $res = Invoke-Api -Path "/api/browser/scroll" -Method "POST" -Body $body
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] Scroll ausgeführt ($($res.direction) $($res.pixels)px, scrollY: $($res.scrollY))" -ForegroundColor Green
        } else {
            Write-Error "[Fehler] Scroll fehlgeschlagen: $($res.error)"
        }
    }

    "Upload" {
        if (-not $FilePath) {
            Write-Error "Bitte -FilePath <Pfad> angeben!"
            return
        }
        $resolvedPath = if (Test-Path $FilePath) { (Resolve-Path $FilePath).Path } else { [System.IO.Path]::GetFullPath($FilePath) }
        $body = @{
            filePath = $resolvedPath
            selector = if ($Selector) { $Selector } else { "input[type='file']" }
        }
        if ($Target -match "^\d+$") {
            $body["id"] = [int]$Target
        }

        $res = Invoke-Api -Path "/api/browser/upload" -Method "POST" -Body $body
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] Datei erfolgreich headless hochgeladen: $($res.file)" -ForegroundColor Green
        } else {
            Write-Error "[Fehler] Datei-Upload fehlgeschlagen: $($res.error)"
        }
    }

    "Overlay" {
        $show = (-not $Hide.IsPresent)
        $res = Invoke-Api -Path "/api/browser/overlay" -Method "POST" -Body @{ show = $show }
        if (-not $res) { return }
        if ($res.success) {
            $stateText = if ($show) { "eingeblendet" } else { "ausgeblendet" }
            Write-Host "[OK] Nummern-Overlay $stateText." -ForegroundColor Cyan
        } else {
            Write-Error "[Fehler] Overlay fehlgeschlagen: $($res.error)"
        }
    }

    "Screenshot" {
        $savePath = $OutPath
        if (-not $savePath) {
            $savePath = Join-Path (Get-Location) "co-browser-screenshot.png"
        }
        $res = Invoke-Api -Path "/api/browser/screenshot?format=png"
        if (-not $res) { return }
        if ($res.success -and $res.base64) {
            $bytes = [Convert]::FromBase64String($res.base64)
            [IO.File]::WriteAllBytes($savePath, $bytes)
            Write-Host "[OK] Screenshot gespeichert ($($res.sizeBytes) Bytes): $savePath" -ForegroundColor Green
        } else {
            Write-Error "[Fehler] Screenshot konnte nicht erfasst werden."
        }
    }

    "Handoff" {
        $res = Invoke-Api -Path "/api/browser/handoff" -Method "POST" -Body @{
            action = "waiting_for_user"
            reason = $Reason
        }
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] An Benutzer übergeben ($Reason)" -ForegroundColor Yellow
        } else {
            Write-Error "[Fehler] Handoff fehlgeschlagen: $($res.error)"
        }
    }

    "Ready" {
        $res = Invoke-Api -Path "/api/browser/handoff" -Method "POST" -Body @{ action = "ready" }
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] Status auf 'Bereit' gesetzt." -ForegroundColor Green
        } else {
            Write-Error "[Fehler] Status-Wechsel fehlgeschlagen: $($res.error)"
        }
    }

    "Controlling" {
        $res = Invoke-Api -Path "/api/browser/handoff" -Method "POST" -Body @{
            action = "controlling"
            reason = $Reason
        }
        if (-not $res) { return }
        if ($res.success) {
            Write-Host "[OK] Status auf 'KI steuert' gesetzt." -ForegroundColor Cyan
        } else {
            Write-Error "[Fehler] Status-Wechsel fehlgeschlagen: $($res.error)"
        }
    }

    "Diagnostics" {
        $queryParams = @()
        if ($SlowOnly.IsPresent) { $queryParams += "slowOnly=true" }
        if ($Reset.IsPresent) { $queryParams += "reset=true" }
        if ($Limit -gt 0) { $queryParams += "limit=$Limit" }
        $qStr = if ($queryParams.Count -gt 0) { "?" + ($queryParams -join "&") } else { "" }
        $path = "/api/browser/diagnostics$qStr"
        $res = Invoke-Api -Path $path
        if ($res) {
            Write-Host "==========================================" -ForegroundColor Cyan
            Write-Host "  ANTIGRAVITY KI-CO-BROWSER DIAGNOSTICS" -ForegroundColor Cyan
            Write-Host "==========================================" -ForegroundColor Cyan
            if ($res.isReset) {
                Write-Host "[RESET] Flight-Log wurde ausgewertet und auf 0 zurückgesetzt." -ForegroundColor Green
                if ($res.previousLogPath) {
                    Write-Host "Backup archiviert unter: $($res.previousLogPath)" -ForegroundColor DarkGray
                }
                Write-Host ""
            }
            if ($res.summary) {
                $total = if ($null -ne $res.summary.totalActions) { $res.summary.totalActions } else { $res.summary.totalRecorded }
                $success = if ($null -ne $res.summary.successfulActions) { $res.summary.successfulActions } else { $res.summary.successful }
                $failed = if ($null -ne $res.summary.failedActions) { $res.summary.failedActions } else { $res.summary.failed }
                $slow = if ($null -ne $res.summary.slowActionsCount) { $res.summary.slowActionsCount } else { $res.summary.slowCount }
                Write-Host "Aktionen gesamt:    $total" -ForegroundColor White
                Write-Host "Erfolgreich:        $success" -ForegroundColor Green
                Write-Host "Fehlgeschlagen:     $failed" -ForegroundColor Red
                Write-Host "Langsam (>=1000ms): $slow" -ForegroundColor Yellow
                Write-Host "Durchschnittsdauer: $($res.summary.avgDurationMs) ms" -ForegroundColor Cyan
            }
            if ($res.fingerprint) {
                Write-Host ""
                Write-Host "--- Layout & Framework Fingerprint ---" -ForegroundColor DarkCyan
                Write-Host "Framework:        $($res.fingerprint.framework)" -ForegroundColor Yellow
                Write-Host "Scroll-Container: $($res.fingerprint.scrollContainer)" -ForegroundColor Gray
                $ov = if ($null -ne $res.fingerprint.bodyOverflowY) { $res.fingerprint.bodyOverflowY } else { $res.fingerprint.bodyOverflow }
                Write-Host "Body Overflow:    $ov" -ForegroundColor Gray
                Write-Host "DOM-Knoten:       $($res.fingerprint.totalDomNodes)" -ForegroundColor Gray
                if ($res.fingerprint.timestamp) {
                    Write-Host "Zuletzt erfasst:  $($res.fingerprint.timestamp)" -ForegroundColor DarkGray
                }
            }
            $list = if ($SlowOnly.IsPresent) { $res.slowActions } else { $res.recentHistory }
            $headerTitle = if ($SlowOnly.IsPresent) { "--- Langsame Aktionen (>= 1000ms) ---" } else { "--- Kürzliche Aktionen (Flight-Recorder) ---" }
            Write-Host ""
            Write-Host $headerTitle -ForegroundColor DarkCyan

            if ($list -and $list.Count -gt 0) {
                foreach ($item in $list) {
                    $color = if ($item.success) { "Green" } else { "Red" }
                    $durColor = if ($item.durationMs -ge 1000) { "Yellow" } else { "DarkGray" }
                    $code = if ($null -ne $item.statusCode) { $item.statusCode } else { $item.httpStatusCode }
                    Write-Host "[$($item.timestamp)] $($item.action) -> HTTP $code " -NoNewline -ForegroundColor $color
                    Write-Host "($($item.durationMs) ms)" -ForegroundColor $durColor
                    if ($item.conversationId) {
                        Write-Host "  Chat-ID: $($item.conversationId)" -ForegroundColor DarkMagenta
                    }
                    if ($item.target) {
                        Write-Host "  Ziel: $($item.target)" -ForegroundColor Gray
                    }
                    if ($item.url) {
                        Write-Host "  URL:  $($item.url)" -ForegroundColor DarkGray
                    }
                    $err = if ($null -ne $item.errorMessage) { $item.errorMessage } else { $item.error }
                    if ($err) {
                        Write-Host "  Fehler: $err" -ForegroundColor Red
                    }
                    $diag = if ($null -ne $item.diagnostic) { $item.diagnostic } else { $item.diagnostics }
                    if ($diag) {
                        $diagJson = ($diag | ConvertTo-Json -Compress)
                        Write-Host "  Diagnostik: $diagJson" -ForegroundColor Magenta
                    }
                }
            } else {
                Write-Host "Keine Einträge vorhanden." -ForegroundColor DarkGray
            }
        }
    }
}
