using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Globalization;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using AgyCliDashboard.Services;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private int _openRouterCreditTickCounter = 0;
    private int _healthCheckTickCounter = 0;
    private CanaryProbeResult? _lastCanaryResult;
    private static readonly HttpClient _canaryHttpClient = new() { Timeout = TimeSpan.FromSeconds(12) };
    private bool _isHealthCheckRunning = false;


    private static readonly SolidColorBrush HealthRedBrush = new(Windows.UI.Color.FromArgb(255, 239, 68, 68));
    private static readonly SolidColorBrush HealthSlateBrush = new(Windows.UI.Color.FromArgb(255, 100, 116, 139));
    private static readonly SolidColorBrush HealthGreenBrush = new(Windows.UI.Color.FromArgb(255, 16, 185, 129));
    private static readonly SolidColorBrush HealthAmberBrush = new(Windows.UI.Color.FromArgb(255, 245, 158, 11));
    private static readonly SolidColorBrush HealthOrangeBrush = new(Windows.UI.Color.FromArgb(255, 249, 115, 22));
    private static readonly SolidColorBrush HealthRoseBrush = new(Windows.UI.Color.FromArgb(255, 244, 63, 94));

    public sealed class TurnLatencyInfo
    {
        public double LatencySeconds { get; set; }
        public double? FirstTokenSeconds { get; set; }
        public double TotalModelSeconds { get; set; }
        public double TotalToolSeconds { get; set; }
        public int ModelStepCount { get; set; }
        public string? ActivePhase { get; set; }
        public DateTime UtcTimestamp { get; set; }
        public TimeSpan Age => DateTime.UtcNow >= UtcTimestamp ? DateTime.UtcNow - UtcTimestamp : TimeSpan.Zero;
        public bool HasError { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsRunning { get; set; }
    }

    public sealed class CanaryProbeResult
    {
        public bool Success { get; set; }
        public double LatencySeconds { get; set; }
        public int StatusCode { get; set; }
        public string Output { get; set; } = "";
        public bool IsDegraded { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool IsManual { get; set; }
    }

    public sealed class InferenceSample
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public double LatencySeconds { get; set; }
        public string Source { get; set; } = "Chat";
    }

    private static readonly List<InferenceSample> _inferenceHistory = new();
    private static readonly object _inferenceHistoryLock = new();
    private static bool _historySeeded = false;

    private static void RecordInferenceSample(double latencySec, DateTime timestamp, string source)
    {
        if (latencySec <= 0.05 || latencySec > 180) return;
        lock (_inferenceHistoryLock)
        {
            if (_inferenceHistory.Any(s => Math.Abs((s.TimestampUtc - timestamp).TotalSeconds) < 2))
                return;

            _inferenceHistory.Add(new InferenceSample
            {
                TimestampUtc = timestamp,
                LatencySeconds = latencySec,
                Source = source
            });

            DateTime cutoff = DateTime.UtcNow.AddHours(-24);
            _inferenceHistory.RemoveAll(s => s.TimestampUtc < cutoff);
            if (_inferenceHistory.Count > 100)
            {
                _inferenceHistory.RemoveRange(0, _inferenceHistory.Count - 100);
            }
        }
    }

    private static (double? Avg1h, double? AvgToday, string TrendSymbol, string TrendText) GetInferenceHistoryStats()
    {
        lock (_inferenceHistoryLock)
        {
            if (_inferenceHistory.Count == 0)
                return (null, null, "→", "Keine Historie");

            DateTime now = DateTime.UtcNow;
            var lastHourSamples = _inferenceHistory.Where(s => s.TimestampUtc >= now.AddHours(-1)).ToList();
            var todaySamples = _inferenceHistory.Where(s => s.TimestampUtc.Date == now.Date).ToList();

            double? avg1h = lastHourSamples.Count > 0 ? lastHourSamples.Average(s => s.LatencySeconds) : null;
            double? avgToday = todaySamples.Count > 0 ? todaySamples.Average(s => s.LatencySeconds) : _inferenceHistory.Average(s => s.LatencySeconds);

            var latest = _inferenceHistory.OrderBy(s => s.TimestampUtc).LastOrDefault();
            string trendSymbol = "→";
            string trendText = "Gleichbleibend normal";

            if (latest != null && avg1h.HasValue && lastHourSamples.Count >= 2)
            {
                if (latest.LatencySeconds > avg1h.Value * 1.35 && latest.LatencySeconds > 1.8)
                {
                    trendSymbol = "↑";
                    trendText = "Steigend (Erhöhte Serverlast / Rush Hour)";
                }
                else if (latest.LatencySeconds < avg1h.Value * 0.75)
                {
                    trendSymbol = "↓";
                    trendText = "Sinkend (Freie Inferenz-Kapazitäten)";
                }
            }

            return (avg1h, avgToday, trendSymbol, trendText);
        }
    }

    private static void EnsureInferenceHistorySeeded()
    {
        if (_historySeeded) return;
        _historySeeded = true;
        try
        {
            string brainBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
            if (!Directory.Exists(brainBase)) return;

            var recentLogs = new DirectoryInfo(brainBase)
                .GetDirectories()
                .Where(d => !d.Name.StartsWith("sync-knowledge-", StringComparison.OrdinalIgnoreCase))
                .Select(d => Path.Combine(d.FullName, ".system_generated", "logs", "transcript.jsonl"))
                .Where(File.Exists)
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(5)
                .ToList();

            foreach (var logFile in recentLogs)
            {
                try
                {
                    var lines = File.ReadLines(logFile.FullName).TakeLast(100).ToList();
                    DateTime? uTime = null;
                    DateTime? mTime = null;
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                        string source = root.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                        string createdAtStr = root.TryGetProperty("created_at", out var c) ? c.GetString() ?? "" : "";

                        if (!DateTime.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime dt))
                            continue;

                        if (type == "USER_INPUT" || source == "USER_EXPLICIT")
                        {
                            uTime = dt;
                            mTime = null;
                        }
                        else if (source == "MODEL" && uTime != null && mTime == null)
                        {
                            mTime = dt;
                            double ttft = (mTime.Value - uTime.Value).TotalSeconds;
                            if (ttft > 0.05 && ttft < 60)
                            {
                                RecordInferenceSample(ttft, uTime.Value, "Chat-Historie");
                            }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }


    private void InitializeStatusHealthTimer()
    {
        _statusHealthTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _statusHealthTimer.Tick += (s, e) =>
        {
            UpdateBuildStatus();
            _healthCheckTickCounter++;
            bool forceCanary = (_healthCheckTickCounter % 45 == 0); // Alle 15 Minuten
            _ = CheckNetworkAndApiHealthAsync(forceCanaryProbe: forceCanary, isManualClick: false);

            _openRouterCreditTickCounter++;
            if (_openRouterCreditTickCounter % 3 == 0) // Alle 60 Sekunden
            {
                _ = FetchOpenRouterCreditsAsync();
            }

            if (_openRouterCreditTickCounter % 45 == 0) // Alle 15 Minuten Remote-Update Check
            {
                _ = CheckForCliUpdatesAsync(showNoticeOnUpToDate: false, forceRefresh: false);
            }

            var sessions = _terminalSessions.ToArray();
            foreach (var ts in sessions)
            {
                if (ts?.Control != null)
                {
                    Helpers.TerminalBufferManager.EnforceBufferLimit(ts.Control);
                }
            }
        };
        _statusHealthTimer.Start();

        InitializeWorkingStateWatchdogTimer();

        UpdateBuildStatus();
        _ = CheckNetworkAndApiHealthAsync(forceCanaryProbe: true, isManualClick: false);
        _ = FetchOpenRouterCreditsAsync();
        _ = CheckLatestGeminiModelAsync(forceRemote: false);

        if (BdrApiLoadStatus != null)
        {
            BdrApiLoadStatus.PointerPressed += (s, e) =>
            {
                TelemetryLogger.Log("[Click Trace] BdrApiLoadStatus clicked -> manual health & canary check.");
                _ = CheckNetworkAndApiHealthAsync(forceCanaryProbe: true, isManualClick: true);
            };
        }
    }

    private DispatcherTimer? _workingStateWatchdogTimer;

    private void InitializeWorkingStateWatchdogTimer()
    {
        _workingStateWatchdogTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _workingStateWatchdogTimer.Tick += (s, e) =>
        {
            try
            {
                var sessions = _terminalSessions.ToArray();
                foreach (var ts in sessions)
                {
                    if (ts.Control?.ConPTYTerm == null || !ts.IsUsed) continue;

                    // 1. Wenn eine ConversationId vorhanden ist: Native SQLite-Prüfung als Single Source of Truth
                    if (!string.IsNullOrEmpty(ts.ConversationId))
                    {
                        var native = Services.DatabaseService.GetNativeConversationWorkingState(ts.ConversationId);
                        if (native.SchemaError != null)
                        {
                            NotifyNativeSchemaWarning(native.SchemaError);
                        }

                        if (native.Evaluated)
                        {
                            // Sofortiger Zero-CPU Presence-Lock Notanker: Wenn Lock-Datei existiert, aber entsperrt ist, ist der CLI-Prozess tot
                            if (ts.IsWorking && Services.DatabaseService.HasPresenceLockFile(ts.ConversationId) && !Services.DatabaseService.IsPresenceLocked(ts.ConversationId))
                            {
                                TelemetryLogger.Log($"[WorkingStateWatchdog] Immediate Presence-Lock release detected for session {ts.Id} ('{ts.Title}'). Resetting to idle.");
                                SetTerminalWorkingState(ts.Id, false);
                                continue;
                            }

                            // Notanker gegen verwaiste Zombie-Tabs (z. B. CLI-Absturz während aktiver Phase oder korrupte DB):
                            // Wenn das Terminal seit über 15 Sekunden kontinuierlich am Idle-Prompt (❯) verweilt
                            // und seit 15s kein Prompt gesendet wurde, übersteuert der Notanker den verwaisten DB-Zustand.
                            bool isZombieAtIdle = (DateTime.UtcNow - ts.LastPromptDispatchedUtc).TotalSeconds > 15 &&
                                                  (DateTime.UtcNow - ts.LastWorkingStateChangedUtc).TotalSeconds > 15 &&
                                                  Helpers.TerminalBufferManager.IsTerminalAtIdlePrompt(ts.Control.ConPTYTerm);

                            if (isZombieAtIdle)
                            {
                                if (ts.IsWorking)
                                {
                                    TelemetryLogger.Log($"[WorkingStateWatchdog] Emergency fallback: Idle prompt detected for >15s on session {ts.Id} ('{ts.Title}') despite native IsWorking=true. Resetting to idle.");
                                    SetTerminalWorkingState(ts.Id, false);
                                }
                                continue;
                            }

                            if (ts.IsWorking != native.IsWorking)
                            {
                                TelemetryLogger.Log($"[WorkingStateWatchdog] Native sync for session {ts.Id} ('{ts.Title}'): IsWorking -> {native.IsWorking} ({native.Details})");
                                SetTerminalWorkingState(ts.Id, native.IsWorking);
                            }

                            continue;
                        }
                    }

                    // 2. Fallback (wenn kein nativer SQLite-Zustand verfügbar oder ungebundene Session):
                    // a) Protobuf-Fallback prüfen
                    if (!string.IsNullOrEmpty(ts.ConversationId))
                    {
                        var protoSummaries = Services.JetboxProtoReader.ReadSummaries(Services.DatabaseService.JetboxPbPath);
                        var proto = protoSummaries?.FirstOrDefault(p => string.Equals(p.ConversationId, ts.ConversationId, StringComparison.OrdinalIgnoreCase));
                        if (proto != null && proto.HasActiveChildren)
                        {
                            if (!ts.IsWorking)
                            {
                                TelemetryLogger.Log($"[WorkingStateWatchdog] Protobuf fallback sync (active children) for session {ts.Id} ('{ts.Title}'): IsWorking -> true");
                                SetTerminalWorkingState(ts.Id, true);
                            }
                            continue; // Verhindert vorzeitiges Abwürgen durch den nachfolgenden ConPTY-Idle-Check
                        }
                    }

                    // b) ConPTY-Puffer-Prüfung als stabiler Ruhezustands-Anker
                    bool isRecentlyDispatched = (DateTime.UtcNow - ts.LastPromptDispatchedUtc).TotalMilliseconds <= 5000;
                    if (isRecentlyDispatched) continue;

                    bool isAtIdle = Helpers.TerminalBufferManager.IsTerminalAtIdlePrompt(ts.Control.ConPTYTerm);

                    if (ts.IsWorking && isAtIdle && (DateTime.UtcNow - ts.LastWorkingStateChangedUtc).TotalMilliseconds > 4000)
                    {
                        TelemetryLogger.Log($"[WorkingStateWatchdog] Detected idle prompt (Fallback) for session {ts.Id} ('{ts.Title}'). Resetting IsWorking to false.");
                        SetTerminalWorkingState(ts.Id, false);
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[WorkingStateWatchdog ERROR] {ex.Message}");
            }
        };
        _workingStateWatchdogTimer.Start();
    }

    private async Task CheckNetworkAndApiHealthAsync(bool forceCanaryProbe = false, bool isManualClick = false)
    {
        if (_isHealthCheckRunning) return;
        _isHealthCheckRunning = true;

        string? activeConvId = null;
        try
        {
            if (!string.IsNullOrEmpty(_activeTerminalId))
            {
                activeConvId = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId)?.ConversationId;
            }
        }
        catch { }

        DispatcherQueue?.TryEnqueue(() =>
        {
            if (SpinnerApiLoadStatus != null) { SpinnerApiLoadStatus.IsActive = true; SpinnerApiLoadStatus.Visibility = Visibility.Visible; }
            if (DotApiLoadStatus != null) DotApiLoadStatus.Visibility = Visibility.Collapsed;
        });

        EnsureInferenceHistorySeeded();

        try
        {
            bool isNetworkAvailable = NetworkInterface.GetIsNetworkAvailable();
            if (!isNetworkAvailable)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (IconConnectionStatus != null) IconConnectionStatus.Stroke = HealthRedBrush;
                    if (TxtConnectionStatus != null) { TxtConnectionStatus.Text = "Offline"; TxtConnectionStatus.Visibility = Visibility.Visible; }
                    if (DotApiLoadStatus != null) DotApiLoadStatus.Fill = HealthSlateBrush;
                    if (TxtApiLoadStatus != null) TxtApiLoadStatus.Text = "Offline";
                    if (BdrApiLoadStatus != null) ToolTipService.SetToolTip(BdrApiLoadStatus, "KI-Bedingungen: Offline\nKeine lokale Netzwerk- oder Internetverbindung verfügbar.");
                });
                return;
            }

            var sw = Stopwatch.StartNew();
            bool apiSuccess = false;
            long elapsedMs = 0;
            int statusCode = 0;

            await Task.Run(async () =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, "https://generativelanguage.googleapis.com");
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var response = await _healthHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    sw.Stop();
                    elapsedMs = sw.ElapsedMilliseconds;
                    statusCode = (int)response.StatusCode;
                    apiSuccess = response.IsSuccessStatusCode || (statusCode > 0 && statusCode < 500);
                }
                catch
                {
                    apiSuccess = false;
                }

                if (!apiSuccess)
                {
                    try
                    {
                        sw.Restart();
                        using var req2 = new HttpRequestMessage(HttpMethod.Head, "https://openrouter.ai/api/v1/models");
                        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        using var response = await _healthHttpClient.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
                        sw.Stop();
                        elapsedMs = sw.ElapsedMilliseconds;
                        statusCode = (int)response.StatusCode;
                        apiSuccess = response.IsSuccessStatusCode || (statusCode > 0 && statusCode < 500);
                    }
                    catch
                    {
                        sw.Stop();
                        elapsedMs = sw.ElapsedMilliseconds;
                        apiSuccess = false;
                    }
                }
            });

            if (apiSuccess && (forceCanaryProbe || (_lastCanaryResult == null && !string.IsNullOrEmpty(GetOpenRouterApiKey()))))
            {
                _lastCanaryResult = await RunInferenceCanaryProbeAsync(isManual: isManualClick);
            }

            TurnLatencyInfo? turnInfo = GetLatestTurnLatencyInfo(activeConvId);
            if (turnInfo != null && turnInfo.FirstTokenSeconds.HasValue)
            {
                RecordInferenceSample(turnInfo.FirstTokenSeconds.Value, turnInfo.UtcTimestamp, "Chat");
            }

            DispatcherQueue?.TryEnqueue(() =>
            {
                if (apiSuccess)
                {
                    if (IconConnectionStatus != null) IconConnectionStatus.Stroke = HealthGreenBrush;
                    if (TxtConnectionStatus != null) { TxtConnectionStatus.Text = ""; TxtConnectionStatus.Visibility = Visibility.Collapsed; }

                    var (badgeText, tooltipText, dotBrush) = EvaluateWorkConditions(apiSuccess: true, statusCode, elapsedMs, _lastCanaryResult, turnInfo);
                    if (DotApiLoadStatus != null) DotApiLoadStatus.Fill = dotBrush;
                    if (TxtApiLoadStatus != null) TxtApiLoadStatus.Text = badgeText;
                    if (BdrApiLoadStatus != null) ToolTipService.SetToolTip(BdrApiLoadStatus, tooltipText);
                }
                else
                {
                    bool generalInternet = NetworkInterface.GetIsNetworkAvailable();
                    if (generalInternet)
                    {
                        if (IconConnectionStatus != null) IconConnectionStatus.Stroke = HealthGreenBrush;
                        if (TxtConnectionStatus != null) { TxtConnectionStatus.Text = ""; TxtConnectionStatus.Visibility = Visibility.Collapsed; }
                        if (DotApiLoadStatus != null) DotApiLoadStatus.Fill = HealthRedBrush;
                        if (TxtApiLoadStatus != null) TxtApiLoadStatus.Text = "Keine API";
                        if (BdrApiLoadStatus != null) ToolTipService.SetToolTip(BdrApiLoadStatus, "KI-Bedingungen: Stark eingeschränkt\nInternetverbindung besteht, aber das KI-API-Gateway antwortet nicht.\nTipp: API-Status / Service-Ausfall prüfen.");
                    }
                    else
                    {
                        if (IconConnectionStatus != null) IconConnectionStatus.Stroke = HealthRedBrush;
                        if (TxtConnectionStatus != null) { TxtConnectionStatus.Text = "Offline"; TxtConnectionStatus.Visibility = Visibility.Visible; }
                        if (DotApiLoadStatus != null) DotApiLoadStatus.Fill = HealthSlateBrush;
                        if (TxtApiLoadStatus != null) TxtApiLoadStatus.Text = "Offline";
                        if (BdrApiLoadStatus != null) ToolTipService.SetToolTip(BdrApiLoadStatus, "KI-Bedingungen: Offline\nKeine Verbindung zum Netzwerk oder Internet vorhanden.");
                    }
                }
            });
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"CheckNetworkAndApiHealthAsync exception: {ex.Message}");
        }
        finally
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (SpinnerApiLoadStatus != null) { SpinnerApiLoadStatus.IsActive = false; SpinnerApiLoadStatus.Visibility = Visibility.Collapsed; }
                if (DotApiLoadStatus != null) DotApiLoadStatus.Visibility = Visibility.Visible;
            });
            _isHealthCheckRunning = false;
        }
    }

    private static (string BadgeText, string TooltipText, SolidColorBrush DotBrush) EvaluateWorkConditions(
        bool apiSuccess,
        int statusCode,
        long elapsedMs,
        CanaryProbeResult? canaryResult,
        TurnLatencyInfo? turnInfo)
    {
        if (!apiSuccess)
        {
            return (
                "KI: Offline",
                "KI-Bedingungen: Offline\nKeine Verbindung zum Google API Gateway oder Internet möglich.",
                HealthRedBrush
            );
        }

        if (statusCode == 429)
        {
            return (
                "429 Drossel",
                "KI-Bedingungen: Blockiert (HTTP 429)\n" +
                "Das API-Ratenlimit wurde erreicht. Anfragen werden derzeit abgewiesen.\n" +
                "Tipp: Kurz pausieren oder API-Key/Modell wechseln.",
                HealthRedBrush
            );
        }

        if (elapsedMs > 1500)
        {
            return (
                $"Instabil ({elapsedMs}ms)",
                $"Netzwerk: Stark verlangsamt\n" +
                $"Die Netzwerk-Latenz zum API-Gateway ist ungewöhnlich hoch ({elapsedMs}ms).\n" +
                $"Tipp: Lokale Internetverbindung oder VPN prüfen.",
                HealthRoseBrush
            );
        }

        var (avg1h, avgToday, trendSymbol, trendText) = GetInferenceHistoryStats();
        string trendSnippet = avg1h.HasValue
            ? $"\n\nInferenz-Trend (Google):\n• 1-Std.-Durchschnitt: {avg1h.Value:0.1}s (Trend: {trendSymbol} {trendText})" + (avgToday.HasValue ? $"\n• Tages-Durchschnitt: {avgToday.Value:0.1}s" : "")
            : "";

        // 0. Wenn ein abgeschlossener Turn der letzten 15 Minuten einen Fehler meldet:
        // HINWEIS: Ein aktiv rechnender Turn (IsRunning) hat Vorrang, da transiente Zwischenschritte
        // im ReAct-Loop normal sind und der Agent aktiv arbeitet.
        if (turnInfo != null && !turnInfo.IsRunning && turnInfo.HasError && turnInfo.Age.TotalMinutes <= 15)
        {
            string ageStr = FormatAge(turnInfo.Age);
            return (
                $"Fehler ({elapsedMs}ms)",
                $"KI-Bedingungen: Fehlerhafter Chat-Turn (vor {ageStr})\nLetzter Turn meldete Fehler: {turnInfo.ErrorMessage ?? "Unbekannt"}\nNetzwerk-Ping: {elapsedMs}ms\nTipp: Klicke auf dieses Badge für sofortigen Live-Inferenztest.",
                HealthAmberBrush
            );
        }

        // 1. Wenn aktuell ein Chat-Turn aktiv rechnet:
        if (turnInfo != null && turnInfo.IsRunning)
        {
            // Fall 1A: Prompt wurde gesendet, aber Google hat noch kein erstes Token geliefert (echte TTFT-Wartezeit)
            if (!turnInfo.FirstTokenSeconds.HasValue)
            {
                double waitSec = turnInfo.LatencySeconds;
                if (waitSec <= 1.5)
                {
                    return (
                        $"Rechnet ({waitSec:0.0}s)",
                        $"KI-Bedingungen: Inferenz läuft\nGoogle-Server verarbeitet den aktuellen Prompt (Wartezeit: {waitSec:0.1}s).\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                        HealthGreenBrush
                    );
                }
                else if (waitSec <= 3.5)
                {
                    return (
                        $"Rechnet ({waitSec:0.0}s)",
                        $"KI-Bedingungen: Denkphase / Reasoning\nGoogle-Modell benötigt {waitSec:0.1}s Bedenkzeit.\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                        HealthGreenBrush
                    );
                }
                else if (waitSec <= 8.0)
                {
                    return (
                        $"Zäh ({waitSec:0.0}s)",
                        $"KI-Bedingungen: Zäh / Rush Hour\nGoogle-Inferenz verzögert sich ({waitSec:0.1}s) – Anfrage steht in Warteschlange.\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                        HealthAmberBrush
                    );
                }
                else if (waitSec <= 20.0)
                {
                    return (
                        $"Träge ({waitSec:0.0}s)",
                        $"KI-Bedingungen: Hohe Wartezeit\nGoogle-Server benötigt {waitSec:0.1}s (Serverauslastung oder intensives Reasoning).\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                        HealthOrangeBrush
                    );
                }
                else
                {
                    return (
                        $"Stau ({waitSec:0.0}s)",
                        $"KI-Bedingungen: Server-Stau bei Google\nInferenz-Wartezeit übersteigt {waitSec:0.1}s!\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}\nTipp: Auf Flash-Modell wechseln.",
                        HealthRedBrush
                    );
                }
            }

            // Fall 1B: Erstes Modell-Token ist bereits eingetroffen! Agent führt Tools aus oder plant den nächsten Schritt.
            double ttft = turnInfo.FirstTokenSeconds.Value;
            double workflowSec = turnInfo.LatencySeconds;
            RecordInferenceSample(ttft, turnInfo.UtcTimestamp, "Chat");

            string phaseHint = turnInfo.ActivePhase == "TOOL" ? "Tools werden ausgeführt" : "Modell plant nächsten Schritt";
            string splitHint = (turnInfo.TotalModelSeconds > 0 || turnInfo.TotalToolSeconds > 0)
                ? $"\nAktuelle Verteilung: KI-Zeit {turnInfo.TotalModelSeconds:0.1}s · Tool-Zeit {turnInfo.TotalToolSeconds:0.1}s ({turnInfo.ModelStepCount} Schritte)"
                : "";

            if (ttft <= 1.2)
            {
                return (
                    $"Flott ({ttft:0.1}s) · {workflowSec:0}s",
                    $"KI-Bedingungen: Flott (Kein Server-Stau)\nReine KI-Reaktionszeit (TTFT): {ttft:0.1}s (Google antwortet schnell)\nLaufende Workflow-Dauer: {workflowSec:0}s ({phaseHint}){splitHint}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                    HealthGreenBrush
                );
            }
            else if (ttft <= 2.5)
            {
                return (
                    $"Gut ({ttft:0.1}s) · {workflowSec:0}s",
                    $"KI-Bedingungen: Normal\nReine KI-Reaktionszeit (TTFT): {ttft:0.1}s\nLaufende Workflow-Dauer: {workflowSec:0}s ({phaseHint}){splitHint}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                    HealthGreenBrush
                );
            }
            else if (ttft <= 5.0)
            {
                return (
                    $"Mäßig ({ttft:0.1}s) · {workflowSec:0}s",
                    $"KI-Bedingungen: Mäßig\nReine KI-Reaktionszeit (TTFT): {ttft:0.1}s (Leichte Google-Warteschlange)\nLaufende Workflow-Dauer: {workflowSec:0}s ({phaseHint}){splitHint}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                    HealthAmberBrush
                );
            }
            else
            {
                return (
                    $"Zäh ({ttft:0.1}s) · {workflowSec:0}s",
                    $"KI-Bedingungen: Zäh / Rush Hour\nReine KI-Reaktionszeit (TTFT): {ttft:0.1}s (Hohe Google-Serverauslastung)\nLaufende Workflow-Dauer: {workflowSec:0}s ({phaseHint}){splitHint}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                    HealthOrangeBrush
                );
            }
        }

        // 2. Frischer manueller Klick-Test (vom Benutzer innerhalb der letzten 2 Minuten explizit angestoßen):
        if (canaryResult != null && canaryResult.IsManual && canaryResult.Timestamp >= DateTime.UtcNow.AddMinutes(-2))
        {
            if (!canaryResult.Success)
            {
                return (
                    $"Fehler ({canaryResult.StatusCode})",
                    $"KI-Bedingungen: Manueller Test fehlgeschlagen\nFehler: {canaryResult.ErrorMessage}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                    HealthRedBrush
                );
            }

            if (canaryResult.IsDegraded)
            {
                return (
                    $"Degeneriert ({elapsedMs}ms)",
                    $"KI-Bedingungen: Manueller Test - Antwort-Degeneration\nAusgabe: '{canaryResult.Output}'\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                    HealthOrangeBrush
                );
            }

            double sec = canaryResult.LatencySeconds;
            RecordInferenceSample(sec, canaryResult.Timestamp, "Canary");

            string turnNotice = turnInfo != null ? $"\nLetzter Chat-Turn: {turnInfo.LatencySeconds:0.0}s (vor {FormatAge(turnInfo.Age)})" : "";
            return (
                $"Test {sec:0.1}s ({elapsedMs}ms)",
                $"KI-Bedingungen: Manueller Live-Inferenztest\nReaktionszeit Test-Probe: {sec:0.1}s (Ping: {elapsedMs}ms){turnNotice}{trendSnippet}\nTipp: Klicke auf dieses Badge für erneuten Test.",
                sec <= 1.5 ? HealthGreenBrush : (sec <= 4.0 ? HealthAmberBrush : HealthOrangeBrush)
            );
        }

        // 3. Echter Chat-Turn der letzten 15 Minuten bestimmt den Arbeitszustand:
        if (turnInfo != null && turnInfo.Age.TotalMinutes <= 15)
        {
            double totalSec = turnInfo.LatencySeconds;
            double? ttft = turnInfo.FirstTokenSeconds;
            string ageStr = FormatAge(turnInfo.Age);
            string splitHint = (turnInfo.TotalModelSeconds > 0 || turnInfo.TotalToolSeconds > 0)
                ? $"\nAufteilung: KI {turnInfo.TotalModelSeconds:0.1}s · Tools {turnInfo.TotalToolSeconds:0.1}s ({turnInfo.ModelStepCount} Schritte)"
                : "";

            if (turnInfo.HasError)
            {
                return (
                    $"Fehler ({elapsedMs}ms)",
                    $"KI-Bedingungen: Fehlerhafter Chat-Turn (vor {ageStr})\nLetzter Turn meldete Fehler: {turnInfo.ErrorMessage ?? "Unbekannt"}\nNetzwerk-Ping: {elapsedMs}ms\nTipp: Klicke auf dieses Badge für sofortigen Live-Inferenztest.",
                    HealthAmberBrush
                );
            }

            if (ttft.HasValue)
            {
                double val = ttft.Value;
                RecordInferenceSample(val, turnInfo.UtcTimestamp, "Chat");

                if (val <= 1.2)
                {
                    return (
                        $"Flott ({elapsedMs}ms)",
                        $"KI-Bedingungen: Flott (Chat vor {ageStr})\nReine KI-Reaktionszeit (TTFT): {val:0.1}s (Google antwortet blitzschnell)\nTurn-Gesamtdauer: {totalSec:0.0}s{splitHint}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}\nTipp: Klicke auf dieses Badge für sofortigen Live-Inferenztest.",
                        HealthGreenBrush
                    );
                }
                else if (val <= 2.5)
                {
                    return (
                        $"Gut ({elapsedMs}ms)",
                        $"KI-Bedingungen: Normal (Chat vor {ageStr})\nReine KI-Reaktionszeit (TTFT): {val:0.1}s (Normale Serverauslastung)\nTurn-Gesamtdauer: {totalSec:0.0}s{splitHint}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}\nTipp: Klicke auf dieses Badge für sofortigen Live-Inferenztest.",
                        HealthGreenBrush
                    );
                }
                else if (val <= 5.0)
                {
                    return (
                        $"Mäßig ({elapsedMs}ms)",
                        $"KI-Bedingungen: Mäßig (Chat vor {ageStr})\nReine KI-Reaktionszeit (TTFT): {val:0.1}s (Erhöhte Auslastung / Queue)\nTurn-Gesamtdauer: {totalSec:0.0}s{splitHint}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}\nTipp: Klicke auf dieses Badge für sofortigen Live-Inferenztest.",
                        HealthAmberBrush
                    );
                }
                else if (val <= 8.0)
                {
                    return (
                        $"Träge ({elapsedMs}ms)",
                        $"KI-Bedingungen: Träge / Rush Hour (Chat vor {ageStr})\nReine KI-Reaktionszeit (TTFT): {val:0.1}s (Google-Server stark ausgelastet)\nTurn-Gesamtdauer: {totalSec:0.0}s{splitHint}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}\nTipp: Bei anhaltender Trägheit Thinking-Budget senken oder Flash-Modell wählen.",
                        HealthOrangeBrush
                    );
                }
                else
                {
                    return (
                        $"Stau ({elapsedMs}ms)",
                        $"KI-Bedingungen: Server-Stau bei Google (Chat vor {ageStr})\nReine KI-Reaktionszeit (TTFT): {val:0.1}s (Anfragen stehen in Warteschlange)\nTurn-Gesamtdauer: {totalSec:0.0}s{splitHint}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}\nTipp: Klicke auf dieses Badge für sofortigen Live-Inferenztest.",
                        HealthRedBrush
                    );
                }
            }

            // Fallback wenn kein TTFT erfasst wurde
            return (
                $"Bereit ({elapsedMs}ms)",
                $"KI-Bedingungen: Chat vor {ageStr}\nTurn-Gesamtdauer: {totalSec:0.0}s\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                HealthGreenBrush
            );
        }

        // 4. Leerlauf (kein frischer Chat): Hintergrund-Canary oder Gateway-Ping
        if (canaryResult != null && canaryResult.Timestamp >= DateTime.UtcNow.AddMinutes(-15))
        {
            if (!canaryResult.Success)
            {
                if (canaryResult.StatusCode == 429)
                {
                    return (
                        "429 Drossel",
                        $"KI-Bedingungen: Drosselung (HTTP 429)\nLive-Inferenz meldet Ratenbegrenzung oder erschöpftes Kontingent.\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}\nTipp: Kurz pausieren oder Modell wechseln.",
                        HealthRedBrush
                    );
                }

                if (canaryResult.StatusCode == 401 || canaryResult.StatusCode == 402)
                {
                    return (
                        $"Key/Credits ({canaryResult.StatusCode})",
                        $"KI-Bedingungen: Authentifizierungs- oder Guthabenfehler (HTTP {canaryResult.StatusCode})\nBitte API-Schlüssel oder Kontoguthaben prüfen.\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                        HealthAmberBrush
                    );
                }

                return (
                    $"GPU-Stau ({canaryResult.StatusCode})",
                    $"KI-Bedingungen: Server überlastet\nLive-Inferenztest fehlgeschlagen: {canaryResult.ErrorMessage}\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}\nWarnung: Inferenz-Cluster antwortet nicht oder ist überlastet!",
                    HealthRedBrush
                );
            }

            if (canaryResult.IsDegraded)
            {
                return (
                    $"Degeneriert ({elapsedMs}ms)",
                    $"KI-Bedingungen: Antwort-Degeneration\nLive-Inferenztest hat unerwartete Ausgabe geliefert ('{canaryResult.Output}').\nWarnung: Modell liefert fehlerhafte Ergebnisse!\nNetzwerk-Ping: {elapsedMs}ms{trendSnippet}",
                    HealthOrangeBrush
                );
            }

            double sec = canaryResult.LatencySeconds;
            RecordInferenceSample(sec, canaryResult.Timestamp, "Canary");

            if (sec <= 1.5)
            {
                return (
                    $"Bereit ({elapsedMs}ms)",
                    $"KI-Bedingungen: Bereit (Canary-Inferenztest)\nInferenz-Reaktionszeit: {sec:0.1}s (Kein GPU-Stau · {elapsedMs}ms Ping)\nStatus: KI-Inferenz antwortet schnell – bereit für Eingaben!{trendSnippet}\nTipp: Klicke auf dieses Badge für erneuten Test.",
                    HealthGreenBrush
                );
            }
            else if (sec <= 3.5)
            {
                return (
                    $"Gut ({elapsedMs}ms)",
                    $"KI-Bedingungen: Gut (Canary-Inferenztest)\nInferenz-Reaktionszeit: {sec:0.1}s ({elapsedMs}ms Ping)\nStatus: Normale Inferenzgeschwindigkeit.{trendSnippet}\nTipp: Klicke auf dieses Badge für erneuten Test.",
                    HealthGreenBrush
                );
            }
            else if (sec <= 7.0)
            {
                return (
                    $"Mäßig ({elapsedMs}ms)",
                    $"KI-Bedingungen: Mäßig (Leichte Inferenz-Queue)\nInferenz-Reaktionszeit: {sec:0.1}s ({elapsedMs}ms Ping)\nStatus: Leichte Verzögerung in der Inferenz-Warteschlange.{trendSnippet}\nTipp: Klicke auf dieses Badge für erneuten Test.",
                    HealthAmberBrush
                );
            }
            else
            {
                return (
                    $"Träge ({elapsedMs}ms)",
                    $"KI-Bedingungen: Träge (Inferenz-Stau gemessen)\nInferenz-Reaktionszeit: {sec:0.1}s ({elapsedMs}ms Ping)\nWarnung: Hohe Serverauslastung bei Google!{trendSnippet}\nTipp: Bei Trägheit temporär auf Flash-Modell wechseln.",
                    HealthOrangeBrush
                );
            }
        }

        // 5. Wenn weder frischer Chat noch Canary vorliegt
        string turnHint = turnInfo != null ? $"\nLetzter Chat-Turn: {turnInfo.LatencySeconds:0.0}s (vor {FormatAge(turnInfo.Age)})." : "";
        return (
            $"Bereit ({elapsedMs}ms)",
            $"KI-Bedingungen: Bereit\nGoogle API Gateway erreichbar ({elapsedMs}ms Ping).{turnHint}{trendSnippet}\nTipp: Klicke auf dieses Badge für sofortigen Live-Inferenztest.",
            HealthGreenBrush
        );
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalSeconds < 60) return $"{Math.Max(1, (int)age.TotalSeconds)}s";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} Min.";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours} Std.";
        return $"{(int)age.TotalDays} d";
    }

    private async Task<CanaryProbeResult?> RunInferenceCanaryProbeAsync(bool isManual = false)
    {
        string apiKey = GetOpenRouterApiKey()?.Trim() ?? "";
        if (string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        if (apiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            apiKey = apiKey.Substring(7).Trim();
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");

            var payload = new
            {
                model = "google/gemini-2.5-flash",
                messages = new[]
                {
                    new { role = "user", content = "Say OK" }
                },
                max_tokens = 2,
                temperature = 0
            };

            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _canaryHttpClient.SendAsync(req, cts.Token);
            sw.Stop();

            double latencySec = sw.ElapsedMilliseconds / 1000.0;
            int statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                string outputText = "";
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];
                    if (choice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentProp))
                    {
                        outputText = contentProp.GetString()?.Trim() ?? "";
                    }
                }

                bool isDegraded = string.IsNullOrWhiteSpace(outputText) || !outputText.Contains("OK", StringComparison.OrdinalIgnoreCase);

                return new CanaryProbeResult
                {
                    Success = true,
                    LatencySeconds = latencySec,
                    StatusCode = statusCode,
                    Output = outputText,
                    IsDegraded = isDegraded,
                    Timestamp = DateTime.UtcNow,
                    IsManual = isManual
                };
            }
            else
            {
                return new CanaryProbeResult
                {
                    Success = false,
                    LatencySeconds = latencySec,
                    StatusCode = statusCode,
                    ErrorMessage = statusCode == 429 ? "HTTP 429 Drosselung" : $"HTTP {statusCode} Fehler",
                    Timestamp = DateTime.UtcNow,
                    IsManual = isManual
                };
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new CanaryProbeResult
            {
                Success = false,
                LatencySeconds = sw.ElapsedMilliseconds / 1000.0,
                StatusCode = 408,
                ErrorMessage = "Timeout (>10s) - GPU-Warteschlange blockiert",
                Timestamp = DateTime.UtcNow,
                IsManual = isManual
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CanaryProbeResult
            {
                Success = false,
                LatencySeconds = sw.ElapsedMilliseconds / 1000.0,
                StatusCode = 0,
                ErrorMessage = ex.Message,
                Timestamp = DateTime.UtcNow,
                IsManual = isManual
            };
        }
    }

    private TurnLatencyInfo? GetLatestTurnLatencyInfo(string? activeConvId = null)
    {
        try
        {
            string brainBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
            if (!Directory.Exists(brainBase)) return null;

            string? targetLogFile = null;

            // 1. Zuerst aktiven Chat prüfen
            if (!string.IsNullOrEmpty(activeConvId))
            {
                string candidate = Path.Combine(brainBase, activeConvId, ".system_generated", "logs", "transcript.jsonl");
                if (File.Exists(candidate)) targetLogFile = candidate;
            }

            // 2. Fallback: Neueste transcript.jsonl Datei zuerst über den gecachten Protobuf-Index (0ms) suchen
            if (targetLogFile == null)
            {
                var protoSummaries = Services.JetboxProtoReader.ReadSummaries(Services.DatabaseService.JetboxPbPath);
                if (protoSummaries != null && protoSummaries.Count > 0)
                {
                    foreach (var summary in protoSummaries.OrderByDescending(s => s.LastModified))
                    {
                        if (string.IsNullOrEmpty(summary.ConversationId) ||
                            summary.ConversationId.StartsWith("sync-knowledge-", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string candidate = Path.Combine(brainBase, summary.ConversationId, ".system_generated", "logs", "transcript.jsonl");
                        if (File.Exists(candidate))
                        {
                            targetLogFile = candidate;
                            break;
                        }
                    }
                }

                // Not-Fallback nur wenn Protobuf-Index leer oder nicht verfügbar ist
                if (targetLogFile == null)
                {
                    var newestLog = new DirectoryInfo(brainBase)
                        .GetDirectories()
                        .Where(d => !d.Name.StartsWith("sync-knowledge-", StringComparison.OrdinalIgnoreCase))
                        .Select(d => Path.Combine(d.FullName, ".system_generated", "logs", "transcript.jsonl"))
                        .Where(File.Exists)
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();

                    if (newestLog != null)
                    {
                        targetLogFile = newestLog.FullName;
                    }
                }
            }

            if (targetLogFile == null || !File.Exists(targetLogFile)) return null;

            var lines = new List<string>(500);
            using (var fs = new FileStream(targetLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length > 262144)
                {
                    fs.Seek(-262144, SeekOrigin.End);
                }
                using var reader = new StreamReader(fs, Encoding.UTF8);
                if (fs.Position > 0) reader.ReadLine(); // Mögliche halbe Zeile am Puffer-Anfang überspringen
                string? l;
                while ((l = reader.ReadLine()) != null)
                {
                    lines.Add(l);
                    if (lines.Count > 500) lines.RemoveAt(0);
                }
            }

            DateTime? lastUserTime = null;
            DateTime? firstModelTime = null;
            DateTime? lastModelTime = null;
            DateTime? turnCompletedTime = null;
            DateTime? lastStepTime = null;
            string currentPhase = "KI";
            double totalModelSec = 0;
            double totalToolSec = 0;
            int modelStepCount = 0;
            bool turnHadError = false;
            string? turnErrorMsg = null;
            bool lastTurnFinished = false;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    string type = root.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? "" : "";
                    string source = root.TryGetProperty("source", out var sProp) ? sProp.GetString() ?? "" : "";
                    string status = root.TryGetProperty("status", out var stProp) ? stProp.GetString() ?? "" : "";
                    string createdAtStr = root.TryGetProperty("created_at", out var cProp) ? cProp.GetString() ?? "" : "";

                    if (!DateTime.TryParse(createdAtStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime createdAt))
                    {
                        continue;
                    }

                    if (type == "USER_INPUT" || source == "USER_EXPLICIT")
                    {
                        lastUserTime = createdAt;
                        firstModelTime = null;
                        lastModelTime = null;
                        turnCompletedTime = null;
                        turnHadError = false;
                        turnErrorMsg = null;
                        lastTurnFinished = false;
                        lastStepTime = createdAt;
                        currentPhase = "KI";
                        totalModelSec = 0;
                        totalToolSec = 0;
                        modelStepCount = 0;
                    }
                    else if (lastUserTime != null && !lastTurnFinished)
                    {
                        double delta = (lastStepTime != null && createdAt >= lastStepTime.Value)
                            ? (createdAt - lastStepTime.Value).TotalSeconds
                            : 0;

                        if (type == "PLANNER_RESPONSE")
                        {
                            if (firstModelTime == null)
                            {
                                firstModelTime = createdAt;
                            }
                            lastModelTime = createdAt;

                            if (currentPhase == "KI")
                            {
                                totalModelSec += delta;
                                modelStepCount++;
                            }

                            if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase))
                            {
                                turnHadError = true;
                                if (root.TryGetProperty("content", out var cntProp))
                                {
                                    turnErrorMsg = cntProp.GetString();
                                }
                            }

                            bool hasToolCalls = root.TryGetProperty("tool_calls", out var tcProp) &&
                                                tcProp.ValueKind == JsonValueKind.Array &&
                                                tcProp.GetArrayLength() > 0;
                            bool hasContent = root.TryGetProperty("content", out var contentProp) &&
                                              !string.IsNullOrWhiteSpace(contentProp.GetString());

                            if (hasContent && !hasToolCalls)
                            {
                                turnCompletedTime = createdAt;
                                lastTurnFinished = true;
                                turnHadError = false;
                                turnErrorMsg = null;
                                currentPhase = "FERTIG";
                            }
                            else
                            {
                                currentPhase = "TOOL";
                            }
                            lastStepTime = createdAt;
                        }
                        else // Tool-Ergebnis (type == "GENERIC" oder source == "SYSTEM")
                        {
                            if (currentPhase == "TOOL")
                            {
                                totalToolSec += delta;
                            }
                            currentPhase = "KI";
                            lastStepTime = createdAt;

                            if (string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase) || type == "ERROR_MESSAGE")
                            {
                                turnHadError = true;
                                if (root.TryGetProperty("error", out var errProp))
                                    turnErrorMsg = errProp.GetString();
                                else if (root.TryGetProperty("content", out var cntProp))
                                    turnErrorMsg = cntProp.GetString();
                                lastModelTime = createdAt;
                            }
                            else if (string.Equals(status, "DONE", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
                            {
                                // Nachfolgender erfolgreicher Tool-Schritt heilt transiente Zwischenfehler im laufenden ReAct-Loop
                                turnHadError = false;
                                turnErrorMsg = null;
                            }
                        }
                    }
                }
                catch { }
            }

            if (lastUserTime != null)
            {
                DateTime nowUtc = DateTime.UtcNow;
                TimeSpan timeSinceUser = nowUtc >= lastUserTime.Value ? nowUtc - lastUserTime.Value : TimeSpan.Zero;
                double? ttft = firstModelTime != null && firstModelTime >= lastUserTime ? (firstModelTime.Value - lastUserTime.Value).TotalSeconds : null;

                // Fall 1: Turn läuft noch aktiv
                if (!lastTurnFinished && timeSinceUser.TotalMinutes <= 10)
                {
                    double activeElapsedSec = Math.Max(0.5, timeSinceUser.TotalSeconds);
                    return new TurnLatencyInfo
                    {
                        LatencySeconds = activeElapsedSec,
                        FirstTokenSeconds = ttft,
                        TotalModelSeconds = Math.Max(0, totalModelSec),
                        TotalToolSeconds = Math.Max(0, totalToolSec),
                        ModelStepCount = modelStepCount,
                        ActivePhase = currentPhase,
                        UtcTimestamp = lastUserTime.Value,
                        HasError = turnHadError,
                        ErrorMessage = turnErrorMsg,
                        IsRunning = true
                    };
                }

                // Fall 2: Turn ist fertig abgeschlossen
                DateTime endTimestamp = turnCompletedTime ?? lastModelTime ?? lastUserTime.Value;
                if (endTimestamp >= lastUserTime.Value)
                {
                    double totalDurationSec = (endTimestamp - lastUserTime.Value).TotalSeconds;
                    return new TurnLatencyInfo
                    {
                        LatencySeconds = Math.Max(0.1, totalDurationSec),
                        FirstTokenSeconds = ttft,
                        TotalModelSeconds = Math.Max(0, totalModelSec),
                        TotalToolSeconds = Math.Max(0, totalToolSec),
                        ModelStepCount = modelStepCount,
                        ActivePhase = "FERTIG",
                        UtcTimestamp = endTimestamp,
                        HasError = turnHadError,
                        ErrorMessage = turnErrorMsg,
                        IsRunning = false
                    };
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"GetLatestTurnLatencyInfo exception: {ex.Message}");
        }

        return null;
    }

    private string GetOpenRouterApiKey()
    {
        string openRouterKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "";
        if (string.IsNullOrEmpty(openRouterKey))
        {
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "settings.json");
            if (File.Exists(settingsPath))
            {
                try
                {
                    var settingsJson = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(settingsPath));
                    if (settingsJson.TryGetProperty("openRouterApiKey", out var keyProp))
                        openRouterKey = keyProp.GetString() ?? "";
                }
                catch { }
            }
        }
        return openRouterKey;
    }

    private void UpdateActiveProjectGitCardInfo()
    {
        string projectPath = GetCurrentProjectPath();
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            if (TxtActiveProjectLastUpdate != null) TxtActiveProjectLastUpdate.Text = "Update: --";
            if (DotGitRemote != null) DotGitRemote.Fill = (Brush)Application.Current.Resources["AgyTextSubtle"];
            return;
        }

        Task.Run(async () =>
        {
            string lastUploadDate = "--";
            string remoteUrl = "";
            bool hasRemote = false;

            try
            {
                var psiRemote = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "config --get remote.origin.url",
                    WorkingDirectory = projectPath
                };
                psiRemote.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                var remoteRes = await Helpers.ProcessRunner.RunAsync(psiRemote, TimeSpan.FromSeconds(5));
                if (!remoteRes.TimedOut && !string.IsNullOrWhiteSpace(remoteRes.StandardOutput))
                {
                    remoteUrl = remoteRes.StandardOutput.Trim();
                    hasRemote = !string.IsNullOrWhiteSpace(remoteUrl);
                }

                var psiDate = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "for-each-ref --sort=-committerdate --count=1 --format=\"%(committerdate:format:%d.%m.%Y %H:%M)\" refs/remotes/origin",
                    WorkingDirectory = projectPath
                };
                psiDate.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                var dateRes = await Helpers.ProcessRunner.RunAsync(psiDate, TimeSpan.FromSeconds(5));
                if (!dateRes.TimedOut && !string.IsNullOrWhiteSpace(dateRes.StandardOutput))
                {
                    lastUploadDate = dateRes.StandardOutput.Trim();
                }
                else if (hasRemote)
                {
                    lastUploadDate = "Nie gepusht";
                }
            }
            catch { }

            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!string.Equals(projectPath, GetCurrentProjectPath(), StringComparison.OrdinalIgnoreCase)) return;

                if (TxtActiveProjectLastUpdate != null)
                {
                    TxtActiveProjectLastUpdate.Text = $"Update: {lastUploadDate}";
                    ToolTipService.SetToolTip(TxtActiveProjectLastUpdate, hasRemote 
                        ? $"Letzter GitHub/Remote-Upload: {lastUploadDate}" 
                        : "Kein Git-Remote konfiguriert");
                }
                if (DotGitRemote != null)
                {
                    DotGitRemote.Fill = hasRemote 
                        ? (Brush)Application.Current.Resources["AgyStatusSuccess"] 
                        : (Brush)Application.Current.Resources["AgyTextSubtle"];
                    ToolTipService.SetToolTip(DotGitRemote, hasRemote ? $"Remote: {remoteUrl}" : "Kein Remote konfiguriert");
                }
            });
        });
    }

    private async void BtnCaptureTelemetry_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnCaptureTelemetry_Click)} triggered.");
        BtnCaptureTelemetry.IsEnabled = false;
        try
        {
            try 
            {
                IcoTelemetry.Source = CreateHighResSvg("ms-appx:///Assets/Icons/check.svg");
            } 
            catch { }

            string sourceLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "dashboard_telemetry.log");
            string destFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AGY_Telemetry");
            
            if (Directory.Exists(destFolder))
            {
                foreach (var file in Directory.GetFiles(destFolder, "*.log"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            else
            {
                Directory.CreateDirectory(destFolder);
            }
            
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string destLogFile = Path.Combine(destFolder, $"telemetry_{timestamp}.log");

            if (File.Exists(sourceLogPath))
            {
                File.Copy(sourceLogPath, destLogFile, true);
            }
            else
            {
                File.WriteAllText(destLogFile, "No telemetry log found.");
            }

            try
            {
                var dataPackage = new DataPackage();
                string prompt = $"[Hier sind die aktuellen Telemetrie-Daten aus dem AGY Dashboard:\n{destLogFile}]";
                dataPackage.SetText(prompt);
                Clipboard.SetContent(dataPackage);
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log("Clipboard copy failed: " + ex.Message);
            }

            await Task.Delay(1500);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"Error capturing telemetry: {ex.Message}");
            ShowMockupNotice("Fehler beim Erfassen der Telemetrie: " + ex.Message);
        }
        finally
        {
            IcoTelemetry.Source = CreateHighResSvg("ms-appx:///Assets/Icons/bug.svg");
            BtnCaptureTelemetry.IsEnabled = true;
        }
    }



    private OpenRouterModelItem? _latestGeminiModel = null;
    private DateTime _lastGeminiModelCheckTimeUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _geminiModelCheckLock = new(1, 1);

    public async Task CheckLatestGeminiModelAsync(bool forceRemote = false)
    {
        if (!await _geminiModelCheckLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            // 1. Sofortiges Feedback aus In-Memory oder Dateicache
            if (_cachedOpenRouterModels.Count == 0)
            {
                string cachePath = GetOpenRouterCacheFilePath();
                if (File.Exists(cachePath))
                {
                    try
                    {
                        string cachedJson = await File.ReadAllTextAsync(cachePath);
                        var parsed = ParseOpenRouterModelsJson(cachedJson);
                        if (parsed.Count > 0)
                        {
                            _cachedOpenRouterModels = parsed;
                            UpdateGeminiModelStatusUi(parsed);
                        }
                    }
                    catch { }
                }
            }
            else if (!forceRemote && _latestGeminiModel == null)
            {
                UpdateGeminiModelStatusUi(_cachedOpenRouterModels);
            }

            // 2. Prüfen, ob Remote-Abfrage fällig ist (bei forceRemote, fehlendem Cache oder älter als 10 Minuten)
            bool shouldFetchRemote = forceRemote || _cachedOpenRouterModels.Count == 0 || (DateTime.UtcNow - _lastGeminiModelCheckTimeUtc).TotalMinutes >= 10;
            if (shouldFetchRemote)
            {
                if (forceRemote)
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        if (TxtGeminiModelStatus != null) TxtGeminiModelStatus.Text = "Prüfe...";
                    });
                }

                var freshModels = await FetchOpenRouterModelsAsync(forceRefresh: forceRemote);
                if (freshModels.Count > 0)
                {
                    _cachedOpenRouterModels = freshModels;
                    _lastGeminiModelCheckTimeUtc = DateTime.UtcNow;
                    UpdateGeminiModelStatusUi(freshModels);
                }
            }
            else if (_cachedOpenRouterModels.Count > 0)
            {
                UpdateGeminiModelStatusUi(_cachedOpenRouterModels);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CheckLatestGeminiModelAsync] Fehler: {ex.Message}");
        }
        finally
        {
            _geminiModelCheckLock.Release();
        }
    }

    private void UpdateGeminiModelStatusUi(List<OpenRouterModelItem> models)
    {
        var geminiModels = models
            .Where(m => !string.IsNullOrEmpty(m.Id)
                && m.Id.StartsWith("google/", StringComparison.OrdinalIgnoreCase)
                && m.Id.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                && !m.Id.EndsWith(":batch", StringComparison.OrdinalIgnoreCase)
                && !m.Id.EndsWith(":free", StringComparison.OrdinalIgnoreCase)
                && !m.IsImageModel)
            .OrderByDescending(m => m.Created)
            .ThenByDescending(m => m.IntelligenceIndex)
            .ToList();

        if (geminiModels.Count == 0) return;

        var latest = geminiModels[0];
        _latestGeminiModel = latest;

        string shortName = latest.Name;
        if (shortName.StartsWith("Google: ", StringComparison.OrdinalIgnoreCase))
        {
            shortName = shortName.Substring(8).Trim();
        }

        DateTime? createdDt = latest.Created > 0
            ? DateTimeOffset.FromUnixTimeSeconds(latest.Created).ToLocalTime().DateTime
            : null;
        string createdStr = createdDt.HasValue ? createdDt.Value.ToString("dd.MM.yyyy") : "k. A.";
        string scoreStr = latest.IntelligenceIndex > 0 ? $"{latest.IntelligenceIndex:0.#}" : "k. A.";
        string ctxStr = latest.ContextFormatted;
        string checkTimeStr = DateTime.Now.ToString("HH:mm:ss");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"⭐ Aktuellstes & stärkstes Gemini-Modell: {shortName}");
        sb.AppendLine($"• Modell-ID: {latest.Id}");
        sb.AppendLine($"• Release: {createdStr}");
        sb.AppendLine($"• Benchmark-Score (Intelligence): {scoreStr}");
        if (!string.IsNullOrEmpty(ctxStr))
        {
            sb.AppendLine($"• Kontextfenster: {ctxStr}");
        }

        if (geminiModels.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine("Weitere aktuelle Versionen:");
            for (int i = 1; i < Math.Min(4, geminiModels.Count); i++)
            {
                var gm = geminiModels[i];
                string gmName = gm.Name.StartsWith("Google: ", StringComparison.OrdinalIgnoreCase) ? gm.Name.Substring(8).Trim() : gm.Name;
                DateTime gmDt = gm.Created > 0 ? DateTimeOffset.FromUnixTimeSeconds(gm.Created).ToLocalTime().DateTime : DateTime.MinValue;
                string gmDtStr = gmDt != DateTime.MinValue ? $" ({gmDt:dd.MM.yy})" : "";
                string gmScoreStr = gm.IntelligenceIndex > 0 ? $", Score {gm.IntelligenceIndex:0.#}" : "";
                sb.AppendLine($"  - {gmName}{gmDtStr}{gmScoreStr}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Zuletzt geprüft: {checkTimeStr} Uhr");
        sb.AppendLine("Linksklick: Neu prüfen | Rechtsklick: Modell-ID kopieren");

        string tooltip = sb.ToString().TrimEnd();

        string badgeName = shortName;
        if (badgeName.StartsWith("Gemini ", StringComparison.OrdinalIgnoreCase))
        {
            badgeName = badgeName.Substring(7).Trim();
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            if (TxtGeminiModelStatus != null)
            {
                TxtGeminiModelStatus.Text = badgeName;
            }
            if (BtnGeminiModelStatus != null)
            {
                ToolTipService.SetToolTip(BtnGeminiModelStatus, tooltip);
            }
        });
    }

    private async void BtnGeminiModelStatus_Click(object sender, RoutedEventArgs e)
    {
        await CheckLatestGeminiModelAsync(forceRemote: true);
        ShowNotice("Gemini Aktualisierung", "Aktuellste Gemini-Modelle wurden live abgefragt.", InfoBarSeverity.Informational);
    }

    private void BtnGeminiModelStatus_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (_latestGeminiModel != null && !string.IsNullOrEmpty(_latestGeminiModel.Id))
        {
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(_latestGeminiModel.Id);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
            ShowNotice("Gemini Modell", $"'{_latestGeminiModel.Id}' in die Zwischenablage kopiert.", InfoBarSeverity.Success);
        }
        else
        {
            _ = CheckLatestGeminiModelAsync(forceRemote: true);
        }
    }
}
