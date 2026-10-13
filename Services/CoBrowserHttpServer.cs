using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Windows.Foundation;

namespace AgyCliDashboard.Services;

/// <summary>
/// Datenklasse für den Co-Browser Flight-Recorder (In-Memory Ringpuffer der letzten Aktionen und Latenzen).
/// </summary>
public sealed class CoBrowserFlightRecord
{
    public string Timestamp { get; set; } = "";
    public string Action { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public long DurationMs { get; set; } = 0;
    public int StatusCode { get; set; } = 200;
    public bool Success { get; set; } = true;
    public string? Target { get; set; } = null;
    public string? ErrorMessage { get; set; } = null;
    public object? Diagnostic { get; set; } = null;
    public List<string>? RecentErrors { get; set; } = null;
    public string? ConversationId { get; set; } = null;
}

/// <summary>
/// Datenklasse für abgefangene Browser-Konsolenmeldungen (console.log, warn, error, unhandled exceptions) im Co-Browser.
/// </summary>
public sealed class ConsoleMessageEntry
{
    public string Type { get; set; } = "log"; // log, warning, error, info, debug, exception
    public string Message { get; set; } = "";
    public string Url { get; set; } = "";
    public string PageUrl { get; set; } = "";
    public int LineNumber { get; set; } = 0;
    public int ColumnNumber { get; set; } = 0;
    public string Timestamp { get; set; } = "";
    public string? StackTrace { get; set; } = null;
}

/// <summary>
/// Datenklasse für abgefangene HTTP/XHR/Fetch Netzwerkanfragen im Co-Browser.
/// </summary>
public sealed class NetworkRequestEntry
{
    public string RequestId { get; set; } = "";
    public string Url { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string ResourceType { get; set; } = "";
    public int StatusCode { get; set; } = 0;
    public string StatusText { get; set; } = "";
    public string MimeType { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public long ContentLength { get; set; } = 0;
    public bool Failed { get; set; } = false;
    public string ErrorText { get; set; } = "";
    public string? ResponseBody { get; set; } = null;
}

/// <summary>
/// Ultra-schneller lokaler HTTP-RPC Server für die direkte Automatisierung der WinUI 3 CoreWebView2 Instanz.
/// Ersetzt träge externe WebSocket-Handshakes und starre Delays durch In-Process Script-Execution,
/// semantische Interactive-Maps und reaktive MutationObserver-Waiting-Primitiven.
/// </summary>
public sealed class CoBrowserHttpServer : IDisposable
{
    private HttpListener? _listener;
    private readonly WebView2 _browserControl;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action<string> _setStatusAction;
    private readonly Action<string> _setWaitingForUserAction;
    private readonly Action _setReadyAction;
    private readonly Func<string> _getStatusFunc;
    private readonly Func<bool> _isTakeoverActiveFunc;
    private readonly Action<string?, string?>? _requestOpenAction;
    private readonly Action? _requestCloseAction;
    private readonly Func<bool>? _isBrowserOpenFunc;
    private readonly Action<string>? _onConversationActiveAction;
    private readonly Func<double>? _getZoomFunc;
    private readonly Action<double>? _setZoomAction;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;

    private readonly object _networkLock = new();
    private readonly List<NetworkRequestEntry> _networkEntries = new();
    private readonly Dictionary<string, NetworkRequestEntry> _pendingRequests = new();
    private volatile bool _networkInterceptionEnabled = false;

    private CoreWebView2DevToolsProtocolEventReceiver? _reqReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _respReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _failReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _finishReceiver;

    private readonly object _consoleLock = new();
    private readonly List<ConsoleMessageEntry> _consoleEntries = new();
    private volatile bool _consoleInterceptionEnabled = false;

    private CoreWebView2DevToolsProtocolEventReceiver? _consoleReceiver;
    private CoreWebView2DevToolsProtocolEventReceiver? _exceptionReceiver;
    private volatile string _currentSourceUrl = "";

    private readonly object _flightLock = new();
    private readonly List<CoBrowserFlightRecord> _flightRecords = new();
    private const int MaxFlightRecords = 50;
    private volatile object? _lastPageFingerprint = null;

    private static readonly JsonDocumentOptions LenientJsonOptions = new() { MaxDepth = 256 };
    // In Community Edition: Keine persistenten Dateipfade
    // In Community Edition: Keine Rotationsdateien
    private static readonly string LastConversationIdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "last_conversation_id.txt");
    private static readonly string BrainDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
    private volatile string _lastActiveConversationId = "";
    private long _lastBrainLookupTicks = 0;

    private void UpdateActiveConversationId(string convId)
    {
        if (string.IsNullOrWhiteSpace(convId)) return;
        string trimmed = convId.Trim();
        if (_lastActiveConversationId != trimmed)
        {
            _lastActiveConversationId = trimmed;
            try
            {
                var dir = Path.GetDirectoryName(LastConversationIdPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(LastConversationIdPath, trimmed, Encoding.UTF8);
            }
            catch { }
        }
    }

    private string? GetEffectiveConversationId(string? explicitConvId)
    {
        if (!string.IsNullOrWhiteSpace(explicitConvId))
        {
            UpdateActiveConversationId(explicitConvId);
            return explicitConvId.Trim();
        }

        // Da MCP-Server-Subprozesse keine ANTIGRAVITY_CONVERSATION_ID erben, prüfen wir alle 5s
        // das zuletzt geschriebene Transkript im brain-Ordner, um kein veraltetes _lastActiveConversationId mitzuschleppen.
        long nowTicks = Environment.TickCount64;
        if (nowTicks - Interlocked.Read(ref _lastBrainLookupTicks) > 5000)
        {
            Interlocked.Exchange(ref _lastBrainLookupTicks, nowTicks);
            try
            {
                if (Directory.Exists(BrainDirectoryPath))
                {
                    string? newestConvId = null;
                    DateTime newestWrite = DateTime.UtcNow.AddMinutes(-30);
                    foreach (var dir in Directory.EnumerateDirectories(BrainDirectoryPath))
                    {
                        string transcriptPath = Path.Combine(dir, ".system_generated", "logs", "transcript.jsonl");
                        if (File.Exists(transcriptPath))
                        {
                            DateTime lw = File.GetLastWriteTimeUtc(transcriptPath);
                            if (lw > newestWrite)
                            {
                                newestWrite = lw;
                                newestConvId = Path.GetFileName(dir);
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(newestConvId))
                    {
                        UpdateActiveConversationId(newestConvId);
                        return newestConvId;
                    }
                }
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(_lastActiveConversationId))
        {
            return _lastActiveConversationId;
        }
        try
        {
            if (File.Exists(LastConversationIdPath))
            {
                var fi = new FileInfo(LastConversationIdPath);
                if (fi.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-48))
                {
                    string saved = File.ReadAllText(LastConversationIdPath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(saved))
                    {
                        _lastActiveConversationId = saved;
                        return saved;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private void RecordFlight(string action, string method, string url, long durationMs, int statusCode, bool success, string? target = null, string? error = null, object? diagnostic = null, string? convId = null)
    {
        string? effectiveConvId = GetEffectiveConversationId(convId);

        List<string>? recentErrors = null;
        if (!success)
        {
            lock (_consoleLock)
            {
                var errs = _consoleEntries
                    .Where(c => c.Type.Equals("error", StringComparison.OrdinalIgnoreCase) || c.Type.Equals("exception", StringComparison.OrdinalIgnoreCase))
                    .TakeLast(2)
                    .Select(c => $"[{c.Type}] {c.Message}")
                    .ToList();
                if (errs.Count > 0) recentErrors = errs;
            }
        }

        var record = new CoBrowserFlightRecord
        {
            Timestamp = DateTime.UtcNow.ToString("o"),
            Action = action,
            Method = method,
            Url = string.IsNullOrWhiteSpace(url) ? _currentSourceUrl : url,
            DurationMs = durationMs,
            StatusCode = statusCode,
            Success = success,
            Target = target,
            ErrorMessage = error,
            Diagnostic = diagnostic,
            RecentErrors = recentErrors,
            ConversationId = effectiveConvId
        };

        lock (_flightLock)
        {
            if (_flightRecords.Count >= MaxFlightRecords)
            {
                _flightRecords.RemoveAt(0);
            }
            _flightRecords.Add(record);

            // In Community Edition: Reiner In-Memory Flight-Recorder ohne persistentes Disk-Logging
        }
    }

    private List<CoBrowserFlightRecord> GetFlightRecordsSnapshot(bool preferPersistent = true)
    {
        lock (_flightLock)
        {
            return _flightRecords.ToList();
        }
    }

    private void ResetFlightRecords()
    {
        lock (_flightLock)
        {
            _flightRecords.Clear();
        }
    }

    private static readonly object _tokenLock = new();
    private static string? _sharedProcessAuthToken;
    public int Port { get; private set; } = 9223;
    public int CdpPort { get; set; } = 9222;
    public string AuthToken { get; private set; } = "";
    public bool IsRunning => _listener?.IsListening ?? false;

    public CoBrowserHttpServer(
        WebView2 browserControl,
        DispatcherQueue dispatcherQueue,
        Action<string> setStatusAction,
        Action<string> setWaitingForUserAction,
        Action setReadyAction,
        Func<string> getStatusFunc,
        Func<bool>? isTakeoverActiveFunc = null,
        Action<string?, string?>? requestOpenAction = null,
        Action? requestCloseAction = null,
        Func<bool>? isBrowserOpenFunc = null,
        Action<string>? onConversationActiveAction = null,
        Func<double>? getZoomFunc = null,
        Action<double>? setZoomAction = null,
        int startingPort = 9223)
    {
        _browserControl = browserControl;
        _dispatcherQueue = dispatcherQueue;
        _setStatusAction = setStatusAction;
        _setWaitingForUserAction = setWaitingForUserAction;
        _setReadyAction = setReadyAction;
        _getStatusFunc = getStatusFunc;
        _isTakeoverActiveFunc = isTakeoverActiveFunc ?? (() => false);
        _requestOpenAction = requestOpenAction;
        _requestCloseAction = requestCloseAction;
        _isBrowserOpenFunc = isBrowserOpenFunc;
        _onConversationActiveAction = onConversationActiveAction;
        _getZoomFunc = getZoomFunc;
        _setZoomAction = setZoomAction;
        Port = FindAvailablePort(startingPort);
        try { GetEffectiveConversationId(null); } catch { }
    }

    public void Start()
    {
        if (_listener != null && _listener.IsListening) return;

        try
        {
            if (string.IsNullOrEmpty(AuthToken))
            {
                lock (_tokenLock)
                {
                    if (string.IsNullOrEmpty(_sharedProcessAuthToken))
                    {
                        _sharedProcessAuthToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    }
                    AuthToken = _sharedProcessAuthToken;
                }
            }

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();

            // Port-Discovery Datei für CLI-Tools schreiben
            SaveEndpointDiscovery();

            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            System.Diagnostics.Debug.WriteLine($"[CoBrowserHttpServer] Läuft auf http://127.0.0.1:{Port}/");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoBrowserHttpServer Start Error] {ex.Message}");
        }
    }

    public void Stop()
    {
        try
        {
            CleanupConsoleInterception();
            CleanupNetworkInterception();
            DeleteEndpointDiscovery();

            _cts?.Cancel();
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
            _listener = null;
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private static string GetAppDataSubDir()
    {
        return AppDomain.CurrentDomain.BaseDirectory.Contains("-public", StringComparison.OrdinalIgnoreCase)
            ? "AgyCliDashboard-Public"
            : "AgyCliDashboard";
    }

    public void DeleteEndpointDiscovery()
    {
        // Niemals während der normalen App-Laufzeit löschen, da sonstige Ansichten (z. B. Embedded Co-Browser)
        // oder laufende MCP-Sessions sonst unerwartet den Zugriff verlieren.
        // Nur löschen, wenn der gesamte Prozess wirklich herunterfährt.
        if (!Environment.HasShutdownStarted)
        {
            return;
        }

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string path = Path.Combine(localAppData, GetAppDataSubDir(), "co_browser_endpoint.json");
            if (File.Exists(path))
            {
                try
                {
                    string content = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("apiPort", out var portProp) && portProp.GetInt32() != Port)
                    {
                        return; // Gehört einer anderen Instanz
                    }
                }
                catch { }
                File.Delete(path);
            }
        }
        catch { }
    }

    public void SaveEndpointDiscovery()
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(localAppData, GetAppDataSubDir());
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "co_browser_endpoint.json");

            var info = new
            {
                apiPort = Port,
                cdpPort = CdpPort,
                baseUrl = $"http://127.0.0.1:{Port}",
                authToken = AuthToken,
                updatedAt = DateTime.UtcNow.ToString("o")
            };
            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmpPath, path, overwrite: true);
        }
        catch { }
    }

    private int FindAvailablePort(int startingPort)
    {
        for (int port = startingPort; port < startingPort + 50; port++)
        {
            try
            {
                var tcp = new TcpListener(IPAddress.Loopback, port);
                tcp.Start();
                tcp.Stop();
                return port;
            }
            catch (SocketException) { }
        }
        return startingPort;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CoBrowserHttpServer Loop] {ex.Message}");
            }
        }
    }

    private static string ExtractConversationId(HttpListenerRequest req, JsonElement? root = null)
    {
        string? headerConv = req.Headers["X-Conversation-Id"];
        string? queryConv = req.QueryString["convId"] ?? req.QueryString["conversationId"];
        string convId = !string.IsNullOrWhiteSpace(headerConv) ? headerConv.Trim() : (!string.IsNullOrWhiteSpace(queryConv) ? queryConv.Trim() : "");
        if (string.IsNullOrEmpty(convId) && root.HasValue && root.Value.ValueKind == JsonValueKind.Object)
        {
            if (root.Value.TryGetProperty("conversationId", out var cProp) || root.Value.TryGetProperty("convId", out cProp))
            {
                convId = cProp.ValueKind == JsonValueKind.String ? (cProp.GetString()?.Trim() ?? "") : cProp.ToString().Trim();
            }
        }
        return convId;
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        // CORS-Schutz: Nur lokale Origins erlauben (strikter Host-Vergleich)
        string origin = req.Headers["Origin"] ?? "";
        if (!string.IsNullOrEmpty(origin))
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
                (originUri.IsLoopback || originUri.DnsSafeHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
            {
                resp.Headers.Add("Access-Control-Allow-Origin", origin);
                resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Conversation-Id, X-Agy-Token");
            }
            else
            {
                await SendJsonResponseAsync(resp, 403, new { success = false, error = "Forbidden: Cross-origin requests from non-loopback origins are rejected" });
                return;
            }
        }

        if (req.HttpMethod == "OPTIONS")
        {
            resp.StatusCode = 200;
            resp.Close();
            return;
        }

        // Token-Authentifizierung zur Verhinderung von unautorisiertem Zugriff und DNS-Rebinding / CSRF
        string? clientToken = req.Headers["X-Agy-Token"];
        if (string.IsNullOrWhiteSpace(clientToken))
        {
            clientToken = req.QueryString["token"];
        }

        bool isAuthorized = !string.IsNullOrEmpty(AuthToken) &&
            !string.IsNullOrEmpty(clientToken) &&
            clientToken.Length == AuthToken.Length &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(clientToken), Encoding.UTF8.GetBytes(AuthToken));

        if (!isAuthorized)
        {
            await SendJsonResponseAsync(resp, 401, new { success = false, error = "Unauthorized: Missing or invalid X-Agy-Token header" });
            return;
        }

        string path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";

        string convId = ExtractConversationId(req);
        if (!string.IsNullOrEmpty(convId))
        {
            UpdateActiveConversationId(convId);
            try
            {
                _onConversationActiveAction?.Invoke(convId);
            }
            catch { }
        }

        try
        {
            switch (path)
            {
                case "":
                case "/":
                case "/api":
                case "/api/browser":
                    await SendJsonResponseAsync(resp, 200, new
                    {
                        success = true,
                        service = "AgyCliDashboard Co-Browser HTTP Server",
                        apiPort = Port,
                        cdpPort = CdpPort,
                        endpoints = new[]
                        {
                            "/api/browser/status (Aliases: /api/status, /status, /health)",
                            "/api/browser/navigate (Alias: /api/navigate)",
                            "/api/browser/content (Alias: /api/content)",
                            "/api/browser/eval (Alias: /api/eval)",
                            "/api/browser/click (Alias: /api/click)",
                            "/api/browser/type (Alias: /api/type)",
                            "/api/browser/fill-form (Alias: /api/fill-form)",
                            "/api/browser/open (Alias: /api/open)",
                            "/api/browser/close (Alias: /api/close)",
                            "/api/browser/screenshot (Alias: /api/screenshot)",
                            "/api/browser/interactive-map",
                            "/api/browser/network (Alias: /api/network)",
                            "/api/browser/console (Alias: /api/console)",
                            "/api/browser/axtree",
                            "/api/browser/overlay",
                            "/api/browser/scroll",
                            "/api/browser/upload",
                            "/api/browser/handoff",
                            "/api/browser/diagnostics (Aliases: /api/diagnostics, /diagnostics)"
                        }
                    });
                    break;

                case "/api/browser/status":
                case "/api/status":
                case "/status":
                case "/health":
                    await HandleGetStatusAsync(req, resp);
                    break;

                case "/api/browser/diagnostics":
                case "/api/diagnostics":
                case "/diagnostics":
                case "/api/browser/diagnostics/reset":
                case "/api/diagnostics/reset":
                    await HandleGetDiagnosticsAsync(req, resp);
                    break;

                case "/api/browser/zoom":
                case "/api/zoom":
                    await HandleZoomAsync(req, resp);
                    break;

                case "/api/browser/open":
                case "/api/open":
                    await HandlePostOpenAsync(req, resp);
                    break;

                case "/api/browser/close":
                case "/api/close":
                    await HandlePostCloseAsync(req, resp);
                    break;

                case "/api/browser/navigate":
                case "/api/navigate":
                    await HandlePostNavigateAsync(req, resp);
                    break;

                case "/api/browser/eval":
                case "/api/eval":
                    await HandlePostEvalAsync(req, resp);
                    break;

                case "/api/browser/click":
                case "/api/click":
                    await HandlePostClickAsync(req, resp);
                    break;

                case "/api/browser/type":
                case "/api/type":
                    await HandlePostTypeAsync(req, resp);
                    break;

                case "/api/browser/fill-form":
                case "/api/fill-form":
                    await HandlePostFillFormAsync(req, resp);
                    break;

                case "/api/browser/interactive-map":
                    await HandleGetInteractiveMapAsync(req, resp);
                    break;

                case "/api/browser/overlay":
                    await HandlePostOverlayAsync(req, resp);
                    break;

                case "/api/browser/scroll":
                    await HandlePostScrollAsync(req, resp);
                    break;

                case "/api/browser/upload":
                    await HandlePostUploadAsync(req, resp);
                    break;

                case "/api/browser/screenshot":
                case "/api/screenshot":
                    await HandleGetScreenshotAsync(req, resp);
                    break;

                case "/api/browser/content":
                case "/api/content":
                    await HandleGetContentAsync(req, resp);
                    break;

                case "/api/browser/network":
                case "/api/network":
                    await HandleGetNetworkAsync(req, resp);
                    break;

                case "/api/browser/console":
                case "/api/console":
                    await HandleGetConsoleAsync(req, resp);
                    break;

                case "/api/browser/axtree":
                    await HandleGetAXTreeAsync(req, resp);
                    break;

                case "/api/browser/handoff":
                    await HandlePostHandoffAsync(req, resp);
                    break;

                default:
                    await SendJsonResponseAsync(resp, 404, new { success = false, error = $"Endpoint '{path}' not found" });
                    break;
            }
        }
        catch (Exception ex)
        {
            await SendJsonResponseAsync(resp, 500, new { success = false, error = ex.Message });
        }
    }

    #region Endpunkt-Handler

    private const string DeepDomHelperJs = @"
function querySelectorDeep(sel, root, maxDepth) {
    if (!root) root = document;
    if (maxDepth === undefined) maxDepth = 6;
    if (maxDepth <= 0) return null;
    try {
        var el = root.querySelector(sel);
        if (el) return el;
    } catch (_) {}
    try {
        var all = root.querySelectorAll('*');
        for (var s = 0; s < all.length; s++) {
            if (all[s].shadowRoot) {
                var shadowFound = querySelectorDeep(sel, all[s].shadowRoot, maxDepth - 1);
                if (shadowFound) return shadowFound;
            }
        }
    } catch (_) {}
    var frames = root.querySelectorAll('iframe, frame');
    for (var i = 0; i < frames.length; i++) {
        try {
            var doc = frames[i].contentDocument || (frames[i].contentWindow ? frames[i].contentWindow.document : null);
            if (doc) {
                var found = querySelectorDeep(sel, doc, maxDepth - 1);
                if (found) return found;
            }
        } catch (_) {}
    }
    return null;
}

function querySelectorAllDeep(sel, root, maxResults, maxDepth) {
    if (!root) root = document;
    if (maxResults === undefined) maxResults = 1500;
    if (maxDepth === undefined) maxDepth = 6;
    var results = [];
    if (maxDepth <= 0) return results;

    function walk(node, depth) {
        if (!node || results.length >= maxResults || depth > maxDepth) return;
        try {
            var found = node.querySelectorAll(sel);
            for (var i = 0; i < found.length && results.length < maxResults; i++) {
                results.push(found[i]);
            }
        } catch (_) {}
        if (results.length >= maxResults) return;

        try {
            var all = node.querySelectorAll('*');
            for (var s = 0; s < all.length && results.length < maxResults; s++) {
                if (all[s].shadowRoot) {
                    walk(all[s].shadowRoot, depth + 1);
                }
            }
        } catch (_) {}

        if (results.length >= maxResults) return;
        var frames = node.querySelectorAll('iframe, frame');
        for (var f = 0; f < frames.length && results.length < maxResults; f++) {
            try {
                var doc = frames[f].contentDocument || (frames[f].contentWindow ? frames[f].contentWindow.document : null);
                if (doc) walk(doc, depth + 1);
            } catch (_) {}
        }
    }

    walk(root, 0);
    return results;
}
";

    private bool IsTakeoverBlocked(HttpListenerRequest req, JsonElement? bodyRoot = null)
    {
        if (!_isTakeoverActiveFunc()) return false;

        string? forceHeader = req.Headers["X-Agy-Force"];
        if (string.Equals(forceHeader, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(forceHeader, "1", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (bodyRoot.HasValue && bodyRoot.Value.ValueKind == JsonValueKind.Object)
        {
            if (bodyRoot.Value.TryGetProperty("force", out var fProp))
            {
                if (fProp.ValueKind == JsonValueKind.True) return false;
                if (fProp.ValueKind == JsonValueKind.String &&
                    (string.Equals(fProp.GetString(), "true", StringComparison.OrdinalIgnoreCase) || fProp.GetString() == "1"))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private async Task HandleGetStatusAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        bool includeDiagnostics = req.QueryString["diagnostics"] == "true" || req.QueryString["diag"] == "true";
        var status = await RunOnDispatcherAsync<object>(async () =>
        {
            string url = _browserControl.Source?.ToString() ?? "";
            string title = "";
            bool isCoreAvailable = _browserControl.CoreWebView2 != null;
            if (isCoreAvailable)
            {
                title = _browserControl.CoreWebView2!.DocumentTitle;
            }
            return new
            {
                success = true,
                url = url,
                title = title,
                status = _getStatusFunc(),
                isTakeoverActive = _isTakeoverActiveFunc(),
                isInitialized = isCoreAvailable,
                isOpen = _isBrowserOpenFunc?.Invoke() ?? false,
                zoomFactor = _getZoomFunc?.Invoke() ?? 0.8,
                zoomPercent = (int)Math.Round((_getZoomFunc?.Invoke() ?? 0.8) * 100),
                cdpPort = CdpPort,
                apiPort = Port
            };
        });

        if (includeDiagnostics)
        {
            List<CoBrowserFlightRecord> records = GetFlightRecordsSnapshot(preferPersistent: false);

            int totalActions = records.Count;
            int successfulActions = records.Count(r => r.Success);
            int failedActions = records.Count(r => !r.Success);
            int evalFallbacks = records.Count(r => r.Action.Equals("eval", StringComparison.OrdinalIgnoreCase));
            double avgDurationMs = totalActions > 0 ? Math.Round(records.Average(r => r.DurationMs), 1) : 0;
            var slowActions = records.Where(r => r.DurationMs >= 1000).Select(r => new
            {
                r.Timestamp,
                r.Action,
                r.DurationMs,
                r.Url,
                r.Target,
                r.Success,
                r.ErrorMessage,
                r.ConversationId
            }).ToList();

            var combined = new
            {
                success = true,
                status = status,
                diagnostics = new
                {
                    summary = new
                    {
                        totalActions = totalActions,
                        successfulActions = successfulActions,
                        failedActions = failedActions,
                        evalFallbacks = evalFallbacks,
                        avgDurationMs = avgDurationMs,
                        slowActionsCount = slowActions.Count
                    },
                    slowActions = slowActions,
                    recentHistory = records.TakeLast(10).ToList(),
                    fingerprint = _lastPageFingerprint
                }
            };
            await SendJsonResponseAsync(resp, 200, combined);
            return;
        }

        await SendJsonResponseAsync(resp, 200, status);
    }

    private async Task HandleGetDiagnosticsAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "";
        bool slowOnly = req.QueryString["slowOnly"] == "true" || req.QueryString["slow_only"] == "true";
        bool reset = req.QueryString["reset"] == "true" || req.QueryString["clear"] == "true" || path.EndsWith("/reset") || req.HttpMethod.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
        int limit = 100;
        if (int.TryParse(req.QueryString["limit"], out int l) && l > 0)
        {
            limit = Math.Min(l, 1000);
        }

        // Lese Snapshot der gesammelten Daten (bevorzugt aus persistentem JSONL-Log)
        List<CoBrowserFlightRecord> records = GetFlightRecordsSnapshot(preferPersistent: true);

        if (reset)
        {
            ResetFlightRecords();
        }

        int totalActions = records.Count;
        int successfulActions = records.Count(r => r.Success);
        int failedActions = records.Count(r => !r.Success);
        int evalFallbacks = records.Count(r => r.Action.Equals("eval", StringComparison.OrdinalIgnoreCase));
        double avgDurationMs = totalActions > 0 ? Math.Round(records.Average(r => r.DurationMs), 1) : 0;
        var slowActions = records.Where(r => r.DurationMs >= 1000).TakeLast(limit).Select(r => new
        {
            r.Timestamp,
            r.Action,
            r.DurationMs,
            r.Url,
            r.Target,
            r.Success,
            r.ErrorMessage,
            r.Diagnostic,
            r.ConversationId
        }).ToList();

        var historyList = slowOnly
            ? records.Where(r => !r.Success || r.DurationMs >= 1000).TakeLast(limit).ToList()
            : records.TakeLast(limit).ToList();

        var result = new
        {
            success = true,
            isReset = reset,
            persistentLogPath = (string?)null,
            previousLogPath = (string?)null,
            summary = new
            {
                totalActions = totalActions,
                successfulActions = successfulActions,
                failedActions = failedActions,
                evalFallbacks = evalFallbacks,
                avgDurationMs = avgDurationMs,
                slowActionsCount = slowActions.Count
            },
            slowActions = slowActions,
            recentHistory = historyList,
            fingerprint = _lastPageFingerprint,
            serverUptime = DateTime.UtcNow.ToString("o")
        };

        await SendJsonResponseAsync(resp, 200, result);
    }

    private async Task HandleZoomAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        if (req.HttpMethod == "GET")
        {
            double factor = _getZoomFunc?.Invoke() ?? 0.8;
            int percent = (int)Math.Round(factor * 100);
            await SendJsonResponseAsync(resp, 200, new
            {
                success = true,
                zoomFactor = factor,
                percent = percent
            });
            return;
        }

        if (req.HttpMethod == "POST")
        {
            string body = await ReadBodyAsStringAsync(req);
            double target = 0.8;
            bool ok = false;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("zoomFactor", out var zf) && zf.TryGetDouble(out var d))
                    {
                        target = d;
                        ok = true;
                    }
                    else if (doc.RootElement.TryGetProperty("percent", out var zp) && zp.TryGetInt32(out var p))
                    {
                        target = p / 100.0;
                        ok = true;
                    }
                    else if (doc.RootElement.TryGetProperty("action", out var act))
                    {
                        string a = act.GetString()?.ToLowerInvariant() ?? "";
                        double cur = _getZoomFunc?.Invoke() ?? 0.8;
                        if (a == "in") { target = cur + 0.1; ok = true; }
                        else if (a == "out") { target = cur - 0.1; ok = true; }
                        else if (a == "reset") { target = 0.8; ok = true; }
                    }
                }
                catch { }
            }

            if (ok && _setZoomAction != null)
            {
                _dispatcherQueue.TryEnqueue(() => _setZoomAction(target));
            }

            int resPercent = (int)Math.Round(target * 100);
            await SendJsonResponseAsync(resp, 200, new
            {
                success = true,
                zoomFactor = target,
                percent = resPercent
            });
            return;
        }

        await SendJsonResponseAsync(resp, 405, new { success = false, message = "Method Not Allowed" });
    }

    private async Task HandlePostOpenAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string body = await ReadBodyAsStringAsync(req);
        string? targetUrl = null;
        JsonDocument? doc = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("url", out var u))
                {
                    targetUrl = u.GetString();
                }
            }
            catch { }
        }

        string convId = ExtractConversationId(req, doc?.RootElement);
        doc?.Dispose();

        if (!string.IsNullOrEmpty(convId))
        {
            UpdateActiveConversationId(convId);
            try { _onConversationActiveAction?.Invoke(convId); } catch { }
        }

        var result = await RunOnDispatcherAsync<object>(async () =>
        {
            _requestOpenAction?.Invoke(targetUrl, convId);
            return new
            {
                success = true,
                isOpen = true,
                url = targetUrl ?? _browserControl.Source?.ToString() ?? ""
            };
        });

        await SendJsonResponseAsync(resp, 200, result);
    }

    private async Task HandlePostCloseAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var result = await RunOnDispatcherAsync<object>(async () =>
        {
            _requestCloseAction?.Invoke();
            return new
            {
                success = true,
                isOpen = false
            };
        });

        await SendJsonResponseAsync(resp, 200, result);
    }

    private static string NormalizeTargetUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return "";
        string target = rawUrl.Trim();

        // 0. Versehentlich doppelt geprefixte URLs bereinigen (z. B. "https://file:///C:/..." oder "https://C:\...")
        if (target.StartsWith("https://file://", StringComparison.OrdinalIgnoreCase))
        {
            target = target.Substring(8);
        }
        else if (target.StartsWith("http://file://", StringComparison.OrdinalIgnoreCase))
        {
            target = target.Substring(7);
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(target, @"^https?://[a-zA-Z]:[/\\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            int colonSlashIdx = target.IndexOf("://", StringComparison.Ordinal);
            if (colonSlashIdx >= 0)
            {
                target = target.Substring(colonSlashIdx + 3);
            }
        }

        // 1. Bereits vorhandenes gültiges URI-Schema prüfen
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        // 2. Lokale Windows-Pfade (z. B. C:\... oder C:/... oder \\server\share)
        if (Path.IsPathRooted(target) || System.Text.RegularExpressions.Regex.IsMatch(target, @"^[a-zA-Z]:[/\\]"))
        {
            try
            {
                return new Uri(target).AbsoluteUri;
            }
            catch
            {
                string normalized = target.Replace('\\', '/');
                if (!normalized.StartsWith("/")) normalized = "/" + normalized;
                return "file://" + normalized;
            }
        }

        // 3. Relativer Dateipfad, der auf der lokalen Festplatte existiert oder mit ./ bzw. .\ beginnt
        if (target.StartsWith("./", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(@".\", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(target))
        {
            try
            {
                return new Uri(Path.GetFullPath(target)).AbsoluteUri;
            }
            catch { }
        }

        // 4. Standard Web-URL
        return "https://" + target;
    }

    private async Task HandlePostNavigateAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();

        string body = await ReadBodyAsStringAsync(req);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;

        if (IsTakeoverBlocked(req, root))
        {
            await SendJsonResponseAsync(resp, 409, new { success = false, error = "takeover_active", message = "Menschlicher Takeover ist aktiv. Navigation abgelehnt. Verwende 'force': true zum Übersteuern." });
            return;
        }

        string convId = ExtractConversationId(req, root);
        if (!string.IsNullOrEmpty(convId))
        {
            UpdateActiveConversationId(convId);
            try { _onConversationActiveAction?.Invoke(convId); } catch { }
        }

        string? targetUrl = root.TryGetProperty("url", out var u) ? u.GetString() : null;
        string? action = root.TryGetProperty("action", out var a) ? a.GetString() : null;

        bool autoOpen = true;
        if (root.TryGetProperty("open", out var oProp))
        {
            autoOpen = oProp.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => !oProp.GetString()!.Equals("false", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        bool waitForLoad = false;
        if (root.TryGetProperty("waitForLoad", out var wProp))
        {
            waitForLoad = wProp.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => wProp.GetString()!.Equals("true", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        int timeoutMs = 15000;
        if (root.TryGetProperty("timeoutMs", out var tmProp) && tmProp.TryGetInt32(out int tm) && tm > 0)
        {
            timeoutMs = tm;
        }

        if (!string.IsNullOrWhiteSpace(targetUrl) && waitForLoad)
        {
            string target = NormalizeTargetUrl(targetUrl);

            var tcs = new TaskCompletionSource<(bool isSuccess, CoreWebView2WebErrorStatus errorStatus)>(TaskCreationOptions.RunContinuationsAsynchronously);
            TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs>? navHandler = null;

            void DetachHandlers()
            {
                try
                {
                    if (_browserControl.CoreWebView2 != null && navHandler != null)
                    {
                        _browserControl.CoreWebView2.NavigationCompleted -= navHandler;
                        navHandler = null;
                    }
                }
                catch { }
            }

            await RunOnDispatcherAsync(async () =>
            {
                if (autoOpen)
                {
                    _requestOpenAction?.Invoke(null, convId);
                }

                if (_browserControl.CoreWebView2 != null)
                {
                    navHandler = (s, e) =>
                    {
                        if (!e.IsSuccess && e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                        {
                            // Redirect oder nachfolgende Navigation aktiv - abwarten
                            return;
                        }

                        DetachHandlers();
                        tcs.TrySetResult((e.IsSuccess, e.WebErrorStatus));
                    };

                    _browserControl.CoreWebView2.NavigationCompleted += navHandler;
                    _browserControl.CoreWebView2.Navigate(target);
                }
                else
                {
                    _browserControl.Source = new Uri(target);
                }
                return true;
            });

            bool timedOut = false;
            bool isSuccess = false;
            CoreWebView2WebErrorStatus webStatus = CoreWebView2WebErrorStatus.Unknown;

            try
            {
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
                timedOut = completed != tcs.Task;
                if (!timedOut && tcs.Task.IsCompletedSuccessfully)
                {
                    var navResult = tcs.Task.Result;
                    isSuccess = navResult.isSuccess;
                    webStatus = navResult.errorStatus;
                }

                if (!timedOut && !isSuccess &&
                    (webStatus == CoreWebView2WebErrorStatus.ConnectionAborted ||
                     webStatus == CoreWebView2WebErrorStatus.OperationCanceled ||
                     webStatus == CoreWebView2WebErrorStatus.ConnectionReset))
                {
                    await Task.Delay(180);
                }

                if (timedOut || (!isSuccess &&
                    (webStatus == CoreWebView2WebErrorStatus.ConnectionAborted ||
                     webStatus == CoreWebView2WebErrorStatus.OperationCanceled ||
                     webStatus == CoreWebView2WebErrorStatus.ConnectionReset)))
                {
                    bool actuallyLoaded = false;
                    await RunOnDispatcherAsync(async () =>
                    {
                        DetachHandlers();
                        try
                        {
                            string currentSrc = _browserControl.CoreWebView2?.Source ?? _browserControl.Source?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(currentSrc) && !currentSrc.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
                            {
                                if (_browserControl.CoreWebView2 != null)
                                {
                                    string state = await _browserControl.CoreWebView2.ExecuteScriptAsync("document.readyState");
                                    if (state != null && (state.Contains("complete") || state.Contains("interactive")))
                                    {
                                        actuallyLoaded = true;
                                    }
                                }
                            }
                        }
                        catch { }
                        return true;
                    });

                    if (actuallyLoaded)
                    {
                        timedOut = false;
                        isSuccess = true;
                        webStatus = CoreWebView2WebErrorStatus.Unknown;
                    }
                }
            }
            finally
            {
                _dispatcherQueue.TryEnqueue(DetachHandlers);
            }

            string? errorMessage = timedOut
                ? "Timeout beim Laden"
                : (isSuccess ? null : $"Navigation fehlgeschlagen ({webStatus})");

            RecordFlight("navigate", "POST", target, sw.ElapsedMilliseconds, 200, isSuccess, target, errorMessage,
                isSuccess ? null : new { webErrorStatus = webStatus.ToString() }, convId);

            await SendJsonResponseAsync(resp, 200, new
            {
                success = isSuccess,
                action = "navigate",
                url = target,
                loaded = isSuccess,
                timedOut = timedOut,
                webErrorStatus = isSuccess ? null : webStatus.ToString(),
                error = errorMessage
            });
            return;
        }

        var result = await RunOnDispatcherAsync<object>(async () =>
        {
            if (autoOpen)
            {
                _requestOpenAction?.Invoke(null, convId);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                if (action.Equals("reload", StringComparison.OrdinalIgnoreCase))
                {
                    _browserControl.Reload();
                    return new { success = true, action = "reload" };
                }
                if (action.Equals("back", StringComparison.OrdinalIgnoreCase) && _browserControl.CanGoBack)
                {
                    _browserControl.GoBack();
                    return new { success = true, action = "back" };
                }
                if (action.Equals("forward", StringComparison.OrdinalIgnoreCase) && _browserControl.CanGoForward)
                {
                    _browserControl.GoForward();
                    return new { success = true, action = "forward" };
                }
            }

            if (!string.IsNullOrWhiteSpace(targetUrl))
            {
                string target = NormalizeTargetUrl(targetUrl);
                if (_browserControl.CoreWebView2 != null)
                {
                    _browserControl.CoreWebView2.Navigate(target);
                }
                else
                {
                    _browserControl.Source = new Uri(target);
                }
                return new { success = true, action = "navigate", url = target, loaded = false };
            }

            return new { success = false, action = "unknown" };
        });

        RecordFlight(action ?? "navigate", "POST", targetUrl ?? action ?? "unknown", sw.ElapsedMilliseconds, 200, true, targetUrl ?? action, convId: convId);
        await SendJsonResponseAsync(resp, 200, result);
    }

    private async Task HandlePostEvalAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();
        if (req.HttpMethod != "POST")
        {
            await SendJsonResponseAsync(resp, 405, new { success = false, error = "Method Not Allowed: POST is required for eval" });
            return;
        }

        string body = await ReadBodyAsStringAsync(req);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);

        string convId = ExtractConversationId(req, doc.RootElement);
        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);

        if (IsTakeoverBlocked(req, doc.RootElement))
        {
            await SendJsonResponseAsync(resp, 409, new { success = false, error = "takeover_active", message = "Menschlicher Takeover ist aktiv. Eval abgelehnt. Verwende 'force': true zum Übersteuern." });
            return;
        }

        string? script = null;
        if (doc.RootElement.ValueKind == JsonValueKind.Object)
        {
            if (doc.RootElement.TryGetProperty("script", out var s) && !string.IsNullOrWhiteSpace(s.GetString()))
            {
                script = s.GetString();
            }
            else if (doc.RootElement.TryGetProperty("expression", out var expr) && !string.IsNullOrWhiteSpace(expr.GetString()))
            {
                script = expr.GetString();
            }
            else if (doc.RootElement.TryGetProperty("Expression", out var exprUpper) && !string.IsNullOrWhiteSpace(exprUpper.GetString()))
            {
                script = exprUpper.GetString();
            }
            else if (doc.RootElement.TryGetProperty("code", out var code) && !string.IsNullOrWhiteSpace(code.GetString()))
            {
                script = code.GetString();
            }
        }
        else if (doc.RootElement.ValueKind == JsonValueKind.String)
        {
            script = doc.RootElement.GetString();
        }

        if (string.IsNullOrWhiteSpace(script))
        {
            RecordFlight("eval", "POST", _currentSourceUrl, sw.ElapsedMilliseconds, 400, false, null, "script or expression must be provided", null, convId);
            await SendJsonResponseAsync(resp, 400, new { success = false, error = "script or expression must be provided in request body" });
            return;
        }

        var result = await RunOnDispatcherAsync<object>(async () =>
        {
            if (_browserControl.CoreWebView2 == null)
            {
                return new { success = false, error = "CoreWebView2 is not initialized" };
            }
            try
            {
                string rawJson = await ExecuteScriptWithCdpAsync(script);
                return ParseJsResult(rawJson);
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message };
            }
        });

        bool isSuccess = false;
        string? errorMsg = null;
        try
        {
            if (result != null)
            {
                var succProp = result.GetType().GetProperty("success");
                if (succProp?.GetValue(result) is bool b)
                {
                    isSuccess = b;
                }
                var errProp = result.GetType().GetProperty("error");
                errorMsg = errProp?.GetValue(result)?.ToString();
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    isSuccess = false;
                }

                var rProp = result.GetType().GetProperty("raw");
                string? rawJson = rProp?.GetValue(result) as string;
                if (!string.IsNullOrEmpty(rawJson))
                {
                    using var jDoc = JsonDocument.Parse(rawJson);
                    if (jDoc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (jDoc.RootElement.TryGetProperty("success", out var sProp))
                            isSuccess = sProp.GetBoolean();
                        if (jDoc.RootElement.TryGetProperty("error", out var eProp))
                            errorMsg = eProp.GetString();
                    }
                }
            }
        }
        catch { }

        RecordFlight("eval", "POST", _currentSourceUrl, sw.ElapsedMilliseconds, 200, isSuccess, script?.Length > 400 ? script.Substring(0, 400) + "..." : script, errorMsg, null, convId);
        await SendJsonResponseAsync(resp, 200, result);
    }

    private async Task HandlePostClickAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();

        string body = await ReadBodyAsStringAsync(req);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;

        string convId = ExtractConversationId(req, root);
        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);

        if (IsTakeoverBlocked(req, root))
        {
            await SendJsonResponseAsync(resp, 409, new { success = false, error = "takeover_active", message = "Menschlicher Takeover ist aktiv. Aktion abgelehnt. Verwende 'force': true zum Übersteuern." });
            return;
        }

        string selector = root.TryGetProperty("selector", out var s) ? s.GetString() ?? "" : "";
        string text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        string tag = root.TryGetProperty("tag", out var tg) ? tg.GetString() ?? "" : "";
        int id = root.TryGetProperty("id", out var i)
            ? i.GetInt32()
            : (root.TryGetProperty("elementId", out var ei) ? ei.GetInt32() : 0);
        int timeoutMs = root.TryGetProperty("timeoutMs", out var tm) ? tm.GetInt32() : 5000;

        string idSelector = id > 0 ? $"[data-agy-id='{id}']" : "";

        if (string.IsNullOrWhiteSpace(idSelector) && string.IsNullOrWhiteSpace(selector) && string.IsNullOrWhiteSpace(text))
        {
            await SendJsonResponseAsync(resp, 400, new { success = false, error = "selector, text or id must be provided" });
            return;
        }

        // Script mit Event-Driven Auto-Waiting, Iframe-Deep-Traversal, anatomischer DOM-Inspektion & nativem Click-Dispatching
        string script = $@"
(async function() {{
    try {{
        {DeepDomHelperJs}

        function waitFor(sel, timeout) {{
            return new Promise(function(resolve) {{
                function checkNow() {{
                    var found = querySelectorDeep(sel);
                    if (found && (found.offsetWidth > 0 || found.offsetHeight > 0 || found.getClientRects().length > 0)) return found;
                    return null;
                }}
                var initial = checkNow();
                if (initial) return resolve(initial);
                var done = false;
                var obs = null;
                var poll = null;
                function finish(res) {{
                    if (done) return;
                    done = true;
                    if (obs) obs.disconnect();
                    if (poll) clearInterval(poll);
                    clearTimeout(timer);
                    resolve(res);
                }}
                var timer = setTimeout(function() {{ finish(null); }}, timeout);
                poll = setInterval(function() {{
                    var t = checkNow();
                    if (t) finish(t);
                }}, 100);
                obs = new MutationObserver(function() {{
                    var t = checkNow();
                    if (t) finish(t);
                }});
                obs.observe(document.documentElement || document.body, {{ childList: true, subtree: true, attributes: true }});
            }});
        }}

        var idSel = {JsonSerializer.Serialize(idSelector)};
        var sel = {JsonSerializer.Serialize(selector)};
        var text = {JsonSerializer.Serialize(text)};
        var tag = {JsonSerializer.Serialize(tag)};
        var target = null;

        // 1. Zuerst via idSelector [data-agy-id='X'] versuchen (kurzer Timeout, da Webseiten dieses Attribut nicht selbst nachladen)
        if (idSel) {{
            target = await waitFor(idSel, Math.min({timeoutMs}, 350));
        }}

        // 2. Fallback auf regulären CSS-Selektor
        if (!target && sel && sel !== idSel) {{
            target = await waitFor(sel, Math.min({timeoutMs}, 1500));
        }}

        // 3. Fallback auf Textsuche inkl. aria-label/title/placeholder (falls SPA neu gerendert hat und data-agy-id verloren ging)
        if (!target && text) {{
            var textLower = text.toLowerCase().trim();
            function isElVisible(el) {{
                if (!el) return false;
                try {{
                    var win = el.ownerDocument ? (el.ownerDocument.defaultView || window) : window;
                    var r = el.getBoundingClientRect();
                    var cs = win.getComputedStyle(el);
                    return (r.width > 0 || r.height > 0 || el.offsetWidth > 0 || el.offsetHeight > 0) &&
                           cs.display !== 'none' && cs.visibility !== 'hidden';
                }} catch(_) {{ return true; }}
            }}

            function getCandidateTexts(el) {{
                var list = [];
                var t1 = (el.innerText || el.value || el.textContent || '').trim().toLowerCase();
                if (t1) list.push(t1);
                var aria = (el.getAttribute && el.getAttribute('aria-label') || '').trim().toLowerCase();
                if (aria && list.indexOf(aria) === -1) list.push(aria);
                var ttl = (el.title || (el.getAttribute && el.getAttribute('placeholder')) || '').trim().toLowerCase();
                if (ttl && list.indexOf(ttl) === -1) list.push(ttl);
                return list;
            }}

            function matchEl(candidates) {{
                if (!candidates || candidates.length === 0) return null;
                var exact = candidates.filter(function(el) {{
                    var texts = getCandidateTexts(el);
                    return texts.indexOf(textLower) !== -1;
                }});
                var exactVis = exact.find(isElVisible);
                if (exactVis) return exactVis;
                if (exact.length > 0) return exact[0];

                var partial = candidates.filter(function(el) {{
                    var texts = getCandidateTexts(el);
                    for (var ti = 0; ti < texts.length; ti++) {{
                        var t = texts[ti];
                        if (t.length > 0 && (t.includes(textLower) || textLower.includes(t))) return true;
                    }}
                    return false;
                }});
                var partialVis = partial.find(isElVisible);
                if (partialVis) return partialVis;
                return partial.length > 0 ? partial[0] : null;
            }}

            if (tag) {{
                target = matchEl(querySelectorAllDeep(tag));
            }}
            if (!target) {{
                var primaryFilter = 'button, a, input[type=button], input[type=submit], [role=button], [role=link], label, [role=radio], [role=option], [role=checkbox], [role=switch], [role=tab], [role=menuitem]';
                target = matchEl(querySelectorAllDeep(primaryFilter));
            }}
            if (!target) {{
                var secondaryFilter = 'li, span, div[tabindex], div[role]';
                target = matchEl(querySelectorAllDeep(secondaryFilter));
            }}
            if (!target) {{
                var tertiaryFilter = '[class*=""tab""], [class*=""btn""], [class*=""button""], [class*=""clickable""], [class*=""item""], [class*=""card""], [class*=""nav""], [onclick], [data-tab], [data-action], [data-click]';
                target = matchEl(querySelectorAllDeep(tertiaryFilter));
            }}
            if (!target) {{
                var genericFilter = 'div, p, span, h1, h2, h3, h4, h5, h6, b, strong, em, td, th';
                target = matchEl(querySelectorAllDeep(genericFilter));
            }}

            if (target) {{
                var innerClickable = target.querySelector('button, a, input, [role=button], [role=link]');
                if (innerClickable && getCandidateTexts(innerClickable).indexOf(textLower) !== -1) {{
                    target = innerClickable;
                }}
            }}
        }}

        if (!target) {{
            var anyMatch = (idSel ? querySelectorDeep(idSel) : null) || (sel ? querySelectorDeep(sel) : null);
            var diag = null;
            if (anyMatch) {{
                var aWin = anyMatch.ownerDocument ? (anyMatch.ownerDocument.defaultView || window) : window;
                var aSty = aWin.getComputedStyle(anyMatch);
                diag = {{
                    elementExistsInDom: true,
                    display: aSty.display,
                    visibility: aSty.visibility,
                    opacity: aSty.opacity,
                    inDom: true
                }};
            }}
            return JSON.stringify({{
                success: false,
                error: 'Element nicht gefunden' + (idSel ? ' (ID ' + idSel + ')' : '') + (text ? ' mit Text: ' + text : '') + (sel ? ' Selektor: ' + sel : ''),
                diagnostic: diag
            }});
        }}

        try {{
            target.scrollIntoView({{ behavior: 'instant', block: 'center', inline: 'center' }});
        }} catch(_) {{ }}

        var docForPoint = target.ownerDocument || document;
        var winForPoint = docForPoint.defaultView || window;

        var rect = target.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0 && target.children && target.children.length > 0) {{
            for (var ci = 0; ci < target.children.length; ci++) {{
                var cr = target.children[ci].getBoundingClientRect();
                if (cr.width > 0 && cr.height > 0) {{ rect = cr; break; }}
            }}
        }}

        var cx = Math.max(0, Math.min(winForPoint.innerWidth - 1, rect.x + (rect.width > 0 ? rect.width / 2 : 5)));
        var cy = Math.max(0, Math.min(winForPoint.innerHeight - 1, rect.y + (rect.height > 0 ? rect.height / 2 : 5)));

        var topEl = docForPoint.elementFromPoint(cx, cy);

        // Shadow-DOM Durchdringung: Wenn topEl ein Web Component mit ShadowRoot ist, tiefer bohren
        try {{
            while (topEl && topEl.shadowRoot && typeof topEl.shadowRoot.elementFromPoint === 'function') {{
                var innerTop = topEl.shadowRoot.elementFromPoint(cx, cy);
                if (!innerTop || innerTop === topEl) break;
                topEl = innerTop;
            }}
        }} catch (_) {{}}

        // Prüfen, ob target mit topEl verwandt ist (contains oder gemeinsamer Host/Shadow-Ancestor)
        var isRelated = false;
        if (topEl === target) {{
            isRelated = true;
        }} else if (topEl && target) {{
            if (target.contains(topEl) || topEl.contains(target)) {{
                isRelated = true;
            }} else {{
                var targetRoot = target.getRootNode ? target.getRootNode() : null;
                var topRoot = topEl.getRootNode ? topEl.getRootNode() : null;
                if (targetRoot && targetRoot.host === topEl) isRelated = true;
                if (topRoot && topRoot.host === target) isRelated = true;
                if (targetRoot && topRoot && targetRoot.host && targetRoot.host === topRoot.host) isRelated = true;
            }}
        }}

        // Wenn target ein äußerer Preload-/Link-Wrapper ist (z. B. <a href*='preload/sharebox'> auf LinkedIn)
        // und topEl ein spezifischeres interaktives Kind-Element mit aria-label oder role=button ist,
        // das eigentliche Kind-Element als Klick-Ziel verwenden, damit SPA-Event-Handler zuverlässig feuern.
        var clickTarget = target;
        if (topEl && target !== topEl && target.contains(topEl)) {{
            var curInner = topEl;
            while (curInner && curInner !== target) {{
                if (curInner.tagName === 'BUTTON' || curInner.getAttribute('role') === 'button' || curInner.getAttribute('aria-label')) {{
                    clickTarget = curInner;
                    break;
                }}
                curInner = curInner.parentElement;
            }}
        }}

        var isCovered = Boolean(topEl && !isRelated);
        var sty = winForPoint.getComputedStyle(target);
        var isPointerEventsNone = (sty.pointerEvents === 'none');
        var isVisible = (rect.width > 0 && rect.height > 0 && sty.visibility !== 'hidden' && sty.display !== 'none');

        var framework = 'native';
        if (target.__reactFiber$ || target._reactRootContainer || window.React) framework = 'react';
        else if (target.__vue__ || target.__vue_app__ || window.Vue) framework = 'vue';
        else if (target.closest('[ng-version]') || window.ng) framework = 'angular';

        var coveredByDesc = null;
        if (isCovered && topEl) {{
            var cTag = topEl.tagName ? topEl.tagName.toLowerCase() : '';
            var cId = topEl.id ? '#' + topEl.id : '';
            var cClass = (topEl.className && typeof topEl.className === 'string') ? '.' + topEl.className.trim().split(/\s+/).slice(0, 2).join('.') : '';
            coveredByDesc = cTag + cId + cClass;
        }}

        var diagnostic = {{
            isCovered: isCovered,
            coveredBy: coveredByDesc,
            pointerEvents: sty.pointerEvents,
            framework: framework,
            rect: {{ width: Math.round(rect.width), height: Math.round(rect.height), x: Math.round(rect.x), y: Math.round(rect.y) }},
            isVisible: isVisible,
            disabled: Boolean(target.disabled)
        }};

        var mouseOpts = {{ bubbles: true, cancelable: true, view: winForPoint, clientX: cx, clientY: cy, button: 0, buttons: 1 }};
        var pointerOpts = {{ bubbles: true, cancelable: true, view: winForPoint, clientX: cx, clientY: cy, button: 0, buttons: 1, pointerId: 1, pointerType: 'mouse', isPrimary: true, width: 1, height: 1 }};

        function dispatch(name, isPointer) {{
            try {{
                if (isPointer && typeof PointerEvent !== 'undefined') {{
                    clickTarget.dispatchEvent(new PointerEvent(name, pointerOpts));
                }} else {{
                    clickTarget.dispatchEvent(new MouseEvent(name, mouseOpts));
                }}
            }} catch(e) {{}}
        }}

        dispatch('pointerenter', true);
        dispatch('mouseenter', false);
        dispatch('pointerover', true);
        dispatch('mouseover', false);
        dispatch('pointerdown', true);
        dispatch('mousedown', false);
        if (typeof clickTarget.focus === 'function') clickTarget.focus();
        pointerOpts.buttons = 0;
        mouseOpts.buttons = 0;
        dispatch('pointerup', true);
        dispatch('mouseup', false);

        // Genau 1 Klick ausführen: clickTarget.click() triggert nativen Klick inklusive Bubble & Default-Action.
        // Nur wenn click() nicht existiert, Fallback auf synthetisches MouseEvent.
        if (typeof clickTarget.click === 'function') {{
            try {{ clickTarget.click(); }} catch(e) {{
                clickTarget.dispatchEvent(new MouseEvent('click', mouseOpts));
            }}
        }} else {{
            clickTarget.dispatchEvent(new MouseEvent('click', mouseOpts));
        }}

        // Kurze Beruhigung für React 18/19 & Vue SPA Microtasks
        await new Promise(function(r) {{ setTimeout(r, 60); }});

        var isChecked = target.checked;
        if (isChecked === undefined && target.getAttribute) {{
            var ariaCheck = target.getAttribute('aria-checked') || target.getAttribute('aria-selected');
            if (ariaCheck !== null) isChecked = ariaCheck === 'true';
        }}

        var resPayload = {{
            success: true,
            tag: target.tagName,
            id: target.id,
            text: (target.innerText || target.value || target.getAttribute('aria-label') || '').trim(),
            checked: isChecked,
            isCovered: isCovered,
            coveredBy: coveredByDesc,
            diagnostic: diagnostic
        }};
        if (isCovered && coveredByDesc) {{
            resPayload.warning = 'Element wird physisch von ' + coveredByDesc + ' überdeckt (z. B. offener Dialog/Backdrop im Vordergrund).';
        }}
        return JSON.stringify(resPayload);
    }} catch (err) {{
        return JSON.stringify({{ success: false, error: err.message || String(err) }});
    }}
}})()";

        var result = await RunOnDispatcherAsync<object>(async () =>
        {
            if (_browserControl.CoreWebView2 == null) return new { success = false, error = "WebView2 not initialized" };
            try
            {
                string raw = await ExecuteScriptWithCdpAsync(script);
                return ParseJsResult(raw);
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message };
            }
        });

        string targetDesc = !string.IsNullOrEmpty(idSelector) ? idSelector : (!string.IsNullOrEmpty(selector) ? selector : text);
        bool isSuccess = false;
        string? errorMsg = null;
        object? diagObj = null;

        try
        {
            if (result != null)
            {
                var succProp = result.GetType().GetProperty("success");
                if (succProp?.GetValue(result) is bool b)
                {
                    isSuccess = b;
                }
                var errProp = result.GetType().GetProperty("error");
                errorMsg = errProp?.GetValue(result)?.ToString();
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    isSuccess = false;
                }

                var rProp = result.GetType().GetProperty("raw");
                string? rawJson = rProp?.GetValue(result) as string;
                if (!string.IsNullOrEmpty(rawJson))
                {
                    using var jDoc = JsonDocument.Parse(rawJson, LenientJsonOptions);
                    if (jDoc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (jDoc.RootElement.TryGetProperty("success", out var sProp))
                            isSuccess = sProp.GetBoolean();
                        if (jDoc.RootElement.TryGetProperty("error", out var eProp))
                            errorMsg = eProp.GetString();
                        if (jDoc.RootElement.TryGetProperty("diagnostic", out var dProp) && dProp.ValueKind == JsonValueKind.Object)
                            diagObj = JsonSerializer.Deserialize<object>(dProp.GetRawText());
                    }
                }
            }
        }
        catch { }

        RecordFlight("click", "POST", _currentSourceUrl, sw.ElapsedMilliseconds, 200, isSuccess, targetDesc, errorMsg, diagObj, convId);
        await SendJsonResponseAsync(resp, 200, result);
    }

    private async Task HandlePostTypeAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();

        string body = await ReadBodyAsStringAsync(req);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;

        string convId = ExtractConversationId(req, root);
        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);

        if (IsTakeoverBlocked(req, root))
        {
            await SendJsonResponseAsync(resp, 409, new { success = false, error = "takeover_active", message = "Menschlicher Takeover ist aktiv. Aktion abgelehnt. Verwende 'force': true zum Übersteuern." });
            return;
        }

        string selector = root.TryGetProperty("selector", out var s) ? s.GetString() ?? "" : "";
        int id = root.TryGetProperty("id", out var i)
            ? i.GetInt32()
            : (root.TryGetProperty("elementId", out var ei) ? ei.GetInt32() : 0);
        string text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        bool clear = !root.TryGetProperty("clear", out var c) || c.GetBoolean();
        bool pressEnter = root.TryGetProperty("pressEnter", out var pe) && pe.GetBoolean();
        int timeoutMs = root.TryGetProperty("timeoutMs", out var tm) ? tm.GetInt32() : 5000;

        string idSelector = id > 0 ? $"[data-agy-id='{id}']" : "";

        if (string.IsNullOrWhiteSpace(idSelector) && string.IsNullOrWhiteSpace(selector))
        {
            await SendJsonResponseAsync(resp, 400, new { success = false, error = "selector or id must be provided" });
            return;
        }

        string effectiveSelector = !string.IsNullOrWhiteSpace(idSelector) ? idSelector : selector;

        // 1. Element im DOM finden, ins Sichtfeld scrollen und fokussieren (mit Iframe-Deep-Traversal & Rich-Text-Support)
        string prepareScript = $@"
(async function() {{
    try {{
        {DeepDomHelperJs}

        function waitFor(sel, timeout) {{
            return new Promise(function(resolve) {{
                function checkNow() {{
                    var found = querySelectorDeep(sel);
                    if (found && (found.offsetWidth > 0 || found.offsetHeight > 0 || found.getClientRects().length > 0)) return found;
                    return null;
                }}
                var initial = checkNow();
                if (initial) return resolve(initial);
                var done = false;
                var obs = null;
                var poll = null;
                function finish(res) {{
                    if (done) return;
                    done = true;
                    if (obs) obs.disconnect();
                    if (poll) clearInterval(poll);
                    clearTimeout(timer);
                    resolve(res);
                }}
                var timer = setTimeout(function() {{ finish(null); }}, timeout);
                poll = setInterval(function() {{
                    var t = checkNow();
                    if (t) finish(t);
                }}, 100);
                obs = new MutationObserver(function() {{
                    var t = checkNow();
                    if (t) finish(t);
                }});
                obs.observe(document.documentElement || document.body, {{ childList: true, subtree: true, attributes: true }});
            }});
        }}

        var idSel = {JsonSerializer.Serialize(idSelector)};
        var sel = {JsonSerializer.Serialize(selector)};
        var el = null;

        if (idSel) {{
            el = await waitFor(idSel, Math.min({timeoutMs}, 350));
        }}
        if (!el && sel && sel !== idSel) {{
            el = await waitFor(sel, Math.min({timeoutMs}, 1500));
        }}

        if (!el) {{
            var anyEl = (idSel ? querySelectorDeep(idSel) : null) || (sel ? querySelectorDeep(sel) : null);
            var diag = null;
            if (anyEl) {{
                var aWin = anyEl.ownerDocument ? (anyEl.ownerDocument.defaultView || window) : window;
                var aSty = aWin.getComputedStyle(anyEl);
                diag = {{
                    elementExistsInDom: true,
                    display: aSty.display,
                    visibility: aSty.visibility,
                    disabled: Boolean(anyEl.disabled),
                    readOnly: Boolean(anyEl.readOnly)
                }};
            }}
            return JSON.stringify({{ success: false, error: 'Target not found: ' + (idSel || sel), diagnostic: diag }});
        }}

        el.scrollIntoView({{ behavior: 'instant', block: 'center', inline: 'center' }});

        // Wenn el selbst kein Textfeld/Contenteditable ist, aber ein inneres Element (oder ShadowRoot) eines enthält
        var isEditable = el.isContentEditable || el.getAttribute('contenteditable') === 'true';
        if (!isEditable && el.tagName !== 'INPUT' && el.tagName !== 'TEXTAREA') {{
            var innerEdit = el.querySelector ? el.querySelector('[contenteditable=""true""], textarea, input:not([type=""hidden""]), [role=""textbox""]') : null;
            if (!innerEdit && el.shadowRoot) {{
                innerEdit = el.shadowRoot.querySelector('[contenteditable=""true""], textarea, input:not([type=""hidden""]), [role=""textbox""]');
            }}
            if (innerEdit) {{
                el = innerEdit;
                isEditable = el.isContentEditable || el.getAttribute('contenteditable') === 'true';
            }}
        }}

        var elDoc = el.ownerDocument || document;
        var elWin = elDoc.defaultView || window;
        if (elWin !== window) {{
            try {{
                if (elWin.frameElement && typeof elWin.frameElement.focus === 'function') elWin.frameElement.focus();
                if (typeof elWin.focus === 'function') elWin.focus();
            }} catch(_) {{}}
        }}

        if (typeof el.focus === 'function') el.focus();

        if (isEditable) {{
            var range = elDoc.createRange();
            range.selectNodeContents(el);
            var selObj = elWin.getSelection();
            if (selObj) {{
                selObj.removeAllRanges();
                selObj.addRange(range);
            }}
        }} else if (typeof el.select === 'function') {{
            el.select();
        }}

        return JSON.stringify({{ success: true, tag: el.tagName, id: el.id, isContentEditable: isEditable }});
    }} catch (err) {{
        return JSON.stringify({{ success: false, error: err.message || String(err) }});
    }}
}})();";

        var result = await RunOnDispatcherAsync<(bool success, object data)>(async () =>
        {
            if (_browserControl.CoreWebView2 == null) return (false, new { success = false, error = "WebView2 not initialized" });
            try
            {
                string prepRaw = await ExecuteScriptWithCdpAsync(prepareScript);
                string unescaped = prepRaw;
                if (prepRaw.StartsWith("\"") && prepRaw.EndsWith("\"") && prepRaw.Length >= 2)
                {
                    try { unescaped = JsonSerializer.Deserialize<string>(prepRaw) ?? prepRaw; } catch { }
                }

                try
                {
                    using var prepDoc = JsonDocument.Parse(unescaped, LenientJsonOptions);
                    if (prepDoc.RootElement.TryGetProperty("success", out var succ) && !succ.GetBoolean())
                    {
                        string errMsg = prepDoc.RootElement.TryGetProperty("error", out var errProp) ? (errProp.GetString() ?? "Target not found") : "Target not found";
                        object? diagVal = null;
                        if (prepDoc.RootElement.TryGetProperty("diagnostic", out var dProp) && dProp.ValueKind == JsonValueKind.Object)
                        {
                            diagVal = JsonSerializer.Deserialize<object>(dProp.GetRawText());
                        }
                        return (false, new { success = false, error = errMsg, diagnostic = diagVal });
                    }
                }
                catch (Exception pEx)
                {
                    return (false, new { success = false, error = $"DOM-Vorbereitung fehlgeschlagen: {pEx.Message}" });
                }

                // 2. Clear via CDP KeyEvents (Ctrl+A gefolgt von Backspace & ContentEditable execCommand inkl. Iframe-Traversal)
                if (clear)
                {
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"rawKeyDown\",\"windowsVirtualKeyCode\":65,\"modifiers\":2}");
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyUp\",\"windowsVirtualKeyCode\":65,\"modifiers\":2}");
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"rawKeyDown\",\"windowsVirtualKeyCode\":8}");
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyUp\",\"windowsVirtualKeyCode\":8}");

                    string deleteContentScript = @"
(function() {
    var act = document.activeElement;
    while (act && (act.tagName === 'IFRAME' || act.tagName === 'FRAME')) {
        try {
            var fDoc = act.contentDocument || (act.contentWindow ? act.contentWindow.document : null);
            if (!fDoc || !fDoc.activeElement || fDoc.activeElement === act) break;
            act = fDoc.activeElement;
        } catch(_) { break; }
    }
    if (act && (act.isContentEditable || act.getAttribute('contenteditable') === 'true')) {
        try { (act.ownerDocument || document).execCommand('delete'); } catch(_) {}
    }
})();";
                    await ExecuteScriptWithCdpAsync(deleteContentScript);
                }

                // 3. Echte OS-Tastenanschläge via CDP Input.insertText injizieren
                if (!string.IsNullOrEmpty(text))
                {
                    string insertParams = JsonSerializer.Serialize(new { text = text });
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.insertText", insertParams);
                }

                // 4. Enter via CDP KeyEvents
                if (pressEnter)
                {
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"rawKeyDown\",\"windowsVirtualKeyCode\":13,\"text\":\"\\r\",\"unmodifiedText\":\"\\r\"}");
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", "{\"type\":\"keyUp\",\"windowsVirtualKeyCode\":13}");
                }

                // 5. Input- & Change-Events im DOM als Fallback dispatchen & React/Controlled-Sync sichern
                string notifyScript = $@"
(function() {{
    {DeepDomHelperJs}
    var sel = {JsonSerializer.Serialize(effectiveSelector)};
    var el = sel ? querySelectorDeep(sel) : null;
    if (el) {{
        if (!el.isContentEditable) {{
            try {{
                var proto = el.constructor ? el.constructor.prototype : null;
                var desc = proto ? Object.getOwnPropertyDescriptor(proto, 'value') : null;
                if (!desc) {{
                    desc = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value') ||
                           Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value');
                }}
                if (desc && desc.set && el.value !== undefined) {{
                    desc.set.call(el, el.value);
                }}
            }} catch (_) {{ }}
        }}
        el.dispatchEvent(new Event('input', {{ bubbles: true }}));
        el.dispatchEvent(new Event('change', {{ bubbles: true }}));
    }}
}})();";
                await ExecuteScriptWithCdpAsync(notifyScript);

                return (true, new { success = true, selector = effectiveSelector, length = text.Length, cdp = true });
            }
            catch (Exception ex)
            {
                return (false, new { success = false, error = ex.Message });
            }
        });

        string? typeErr = null;
        object? typeDiag = null;
        if (!result.success && result.data != null)
        {
            var eProp = result.data.GetType().GetProperty("error");
            typeErr = eProp?.GetValue(result.data)?.ToString();
            var dProp = result.data.GetType().GetProperty("diagnostic");
            typeDiag = dProp?.GetValue(result.data);
        }

        RecordFlight("type", "POST", _currentSourceUrl, sw.ElapsedMilliseconds, result.success ? 200 : 400, result.success, effectiveSelector, typeErr, typeDiag, convId);
        await SendJsonResponseAsync(resp, result.success ? 200 : 400, result.data);
    }

    private async Task HandlePostFillFormAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();

        string body = await ReadBodyAsStringAsync(req);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;

        string convId = ExtractConversationId(req, root);
        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);

        if (IsTakeoverBlocked(req, root))
        {
            await SendJsonResponseAsync(resp, 409, new { success = false, error = "takeover_active", message = "Menschlicher Takeover ist aktiv. Aktion abgelehnt. Verwende 'force': true zum Übersteuern." });
            return;
        }

        if (!root.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
        {
            await SendJsonResponseAsync(resp, 400, new { success = false, error = "fields object must be provided" });
            return;
        }

        string? submitSelector = root.TryGetProperty("submit", out var s) ? s.GetString() : null;
        string fieldsJson = fields.GetRawText();

        string script = $@"
(async function() {{
    try {{
        {DeepDomHelperJs}

        var map = {fieldsJson};
        var submitSel = {JsonSerializer.Serialize(submitSelector)};
        var filledCount = 0;
        var failedFields = [];

        function setNativeValue(el, val) {{
            try {{
                var tag = el.tagName ? el.tagName.toLowerCase() : '';
                var proto = (tag === 'textarea') ? window.HTMLTextAreaElement.prototype :
                            (tag === 'select') ? window.HTMLSelectElement.prototype :
                            window.HTMLInputElement.prototype;
                var desc = Object.getOwnPropertyDescriptor(proto, 'value');
                if (el._valueTracker) {{
                    try {{ el._valueTracker.setValue(String(val) + '_force_react_sync_' + Math.random()); }} catch (_) {{}}
                }}
                if (desc && desc.set) {{
                    desc.set.call(el, val);
                    return;
                }}
            }} catch (_) {{ }}
            try {{ el.value = val; }} catch (_) {{ }}
        }}

        function setNativeChecked(el, checked) {{
            var proto = el.constructor ? el.constructor.prototype : null;
            var desc = proto ? Object.getOwnPropertyDescriptor(proto, 'checked') : null;
            if (!desc) {{
                desc = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'checked');
            }}
            if (desc && desc.set) {{
                desc.set.call(el, checked);
            }} else {{
                el.checked = checked;
            }}
        }}

        function resolveElement(sel) {{
            if (!sel) return null;
            sel = String(sel).trim();
            if ((sel.startsWith('""') && sel.endsWith('""') && sel.length >= 2) ||
                (sel.startsWith('\'') && sel.endsWith('\'') && sel.length >= 2)) {{
                sel = sel.slice(1, -1).trim();
            }}
            if (/^\d+$/.test(sel)) {{
                var elById = querySelectorDeep('[data-agy-id=""' + sel + '""]');
                if (elById) return elById;
            }}
            if (/^#\d+$/.test(sel)) {{
                var elById2 = querySelectorDeep('[data-agy-id=""' + sel.slice(1) + '""]');
                if (elById2) return elById2;
            }}
            try {{
                var direct = querySelectorDeep(sel);
                if (direct) return direct;
            }} catch (_) {{}}

            var safeSel = sel.replace(/[""\\]/g, '\\$&');
            var attributeSelectors = [
                '[name=""' + safeSel + '""]',
                '[id=""' + safeSel + '""]',
                '[placeholder=""' + safeSel + '"" i]',
                '[aria-label=""' + safeSel + '"" i]',
                '[data-testid=""' + safeSel + '""]'
            ];
            if (!/\s/.test(sel)) {{
                attributeSelectors.unshift('#' + safeSel);
            }}
            for (var ai = 0; ai < attributeSelectors.length; ai++) {{
                try {{
                    var elAttr = querySelectorDeep(attributeSelectors[ai]);
                    if (elAttr) return elAttr;
                }} catch (_) {{}}
            }}
            return null;
        }}

        for (var key in map) {{
            if (!Object.prototype.hasOwnProperty.call(map, key)) continue;
            try {{
                var val = map[key];
                var el = resolveElement(key);
                if (!el) {{
                    failedFields.push(key);
                    continue;
                }}

                el.scrollIntoView({{ behavior: 'instant', block: 'center' }});
                if (typeof el.focus === 'function') el.focus();

                if (el.isContentEditable || el.getAttribute('contenteditable') === 'true' || (el.classList && (el.classList.contains('ProseMirror') || el.classList.contains('ql-editor')))) {{
                    var strVal = String(val);
                    var doc = el.ownerDocument || document;
                    var win = doc.defaultView || window;
                    var range = doc.createRange();
                    range.selectNodeContents(el);
                    var selObj = win.getSelection();
                    if (selObj) {{
                        selObj.removeAllRanges();
                        selObj.addRange(range);
                    }}

                    var handled = false;
                    try {{
                        var dt = new DataTransfer();
                        dt.setData('text/plain', strVal);
                        var pasteEvt = new ClipboardEvent('paste', {{
                            clipboardData: dt,
                            bubbles: true,
                            cancelable: true
                        }});
                        handled = !el.dispatchEvent(pasteEvt);
                    }} catch (_) {{}}

                    if (!handled) {{
                        try {{
                            handled = doc.execCommand('insertText', false, strVal);
                        }} catch (_) {{}}
                    }}

                    if (!handled) {{
                        try {{
                            doc.execCommand('delete', false, null);
                            handled = doc.execCommand('insertText', false, strVal);
                        }} catch (_) {{}}
                    }}

                    if (!handled) {{
                        el.innerText = strVal;
                    }}

                    try {{
                        el.dispatchEvent(new InputEvent('input', {{ bubbles: true, inputType: 'insertText', data: strVal }}));
                    }} catch (_) {{
                        el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    }}
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                    filledCount++;
                    continue;
                }}

                var tag = el.tagName.toLowerCase();
                var inputType = (el.getAttribute('type') || '').toLowerCase();

                if (tag === 'input' && inputType === 'radio') {{
                    if (typeof val === 'boolean') {{
                        setNativeChecked(el, val);
                        if (val && typeof el.click === 'function') el.click();
                    }} else if (el.name) {{
                        var targetRadio = (document.getElementsByName && document.getElementsByName(el.name) ? Array.from(document.getElementsByName(el.name)).find(function(r) {{ return r.value === String(val); }}) : null) || el;
                        setNativeChecked(targetRadio, true);
                        if (typeof targetRadio.click === 'function') targetRadio.click();
                    }} else {{
                        setNativeChecked(el, true);
                        if (typeof el.click === 'function') el.click();
                    }}
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                }} else if (tag === 'input' && inputType === 'checkbox') {{
                    var isChecked = (typeof val === 'boolean') ? val : (String(val).toLowerCase() === 'true' || String(val) === '1');
                    setNativeChecked(el, isChecked);
                    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                    if (el.checked !== isChecked) {{
                        try {{
                            var lbl = el.closest('label') || (el.id ? document.querySelector('label[for=""' + el.id + '""]') : null);
                            if (lbl && typeof lbl.click === 'function') {{
                                lbl.click();
                            }} else if (typeof el.click === 'function') {{
                                el.click();
                            }}
                        }} catch (_) {{ }}
                    }}
                }} else if (typeof val === 'boolean') {{
                    setNativeChecked(el, val);
                    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                }} else if (tag === 'select') {{
                    var targetStr = String(val).trim().toLowerCase();
                    var matchedIdx = -1;
                    for (var oi = 0; oi < el.options.length; oi++) {{
                        if (el.options[oi].value === String(val)) {{ matchedIdx = oi; break; }}
                    }}
                    if (matchedIdx === -1) {{
                        for (var oi = 0; oi < el.options.length; oi++) {{
                            if (el.options[oi].value.toLowerCase() === targetStr) {{ matchedIdx = oi; break; }}
                        }}
                    }}
                    if (matchedIdx === -1) {{
                        for (var oi = 0; oi < el.options.length; oi++) {{
                            if (el.options[oi].text.trim().toLowerCase() === targetStr) {{ matchedIdx = oi; break; }}
                        }}
                    }}
                    if (matchedIdx === -1) {{
                        for (var oi = 0; oi < el.options.length; oi++) {{
                            var optText = el.options[oi].text.trim().toLowerCase();
                            if (optText.indexOf(targetStr) !== -1 || targetStr.indexOf(optText) !== -1) {{ matchedIdx = oi; break; }}
                        }}
                    }}
                    if (matchedIdx !== -1) {{
                        el.selectedIndex = matchedIdx;
                    }} else {{
                        setNativeValue(el, String(val));
                    }}
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                }} else {{
                    setNativeValue(el, String(val));
                    el.dispatchEvent(new Event('input', {{ bubbles: true, cancelable: true }}));
                    el.dispatchEvent(new Event('change', {{ bubbles: true, cancelable: true }}));
                    try {{
                        el.dispatchEvent(new KeyboardEvent('keydown', {{ bubbles: true, key: 'End' }}));
                        el.dispatchEvent(new KeyboardEvent('keyup', {{ bubbles: true, key: 'End' }}));
                    }} catch (_) {{}}
                }}
                filledCount++;
            }} catch (fieldErr) {{
                failedFields.push({{ key: key, error: fieldErr.message || String(fieldErr) }});
            }}
        }}

        function isElVisible(el) {{
            if (!el) return false;
            try {{
                var r = el.getBoundingClientRect();
                var cs = window.getComputedStyle(el);
                return (r.width > 0 || r.height > 0 || el.offsetWidth > 0 || el.offsetHeight > 0) &&
                       cs.display !== 'none' && cs.visibility !== 'hidden';
            }} catch(_) {{ return true; }}
        }}

        var submitted = false;
        if (submitSel) {{
            await new Promise(function(r) {{ setTimeout(r, 60); }});
            try {{
                var btn = resolveElement(submitSel);
                if (!btn && typeof submitSel === 'string') {{
                    var sTrim = submitSel.trim();
                    var isCss = /^[#.\[]/.test(sTrim);
                    if (!isCss) {{
                        var sLower = sTrim.toLowerCase();
                        var candidates = Array.from(document.querySelectorAll('button, input[type=submit], [role=button], a[role=button]'));
                        var exactMatches = candidates.filter(function(b) {{
                            var bt = (b.innerText || b.value || '').trim().toLowerCase();
                            return bt === sLower;
                        }});
                        btn = exactMatches.find(isElVisible) || (exactMatches.length > 0 ? exactMatches[0] : null);

                        if (!btn && sLower.length >= 3) {{
                            var partialMatches = candidates.filter(function(b) {{
                                var bt = (b.innerText || b.value || '').trim().toLowerCase();
                                return bt.length >= 3 && bt.indexOf(sLower) !== -1;
                            }});
                            btn = partialMatches.find(isElVisible) || (partialMatches.length > 0 ? partialMatches[0] : null);
                        }}
                    }}
                    if (!btn) {{
                        var allSubmits = Array.from(document.querySelectorAll('button[type=submit], input[type=submit]'));
                        btn = allSubmits.find(isElVisible) || (allSubmits.length > 0 ? allSubmits[0] : null);
                    }}
                }}
                if (btn) {{
                    try {{ btn.scrollIntoView({{ behavior: 'instant', block: 'center' }}); }} catch(_) {{ }}
                    if (typeof btn.click === 'function') {{
                        btn.click();
                    }} else {{
                        btn.dispatchEvent(new MouseEvent('click', {{ bubbles: true, cancelable: true, view: window }}));
                    }}
                    submitted = true;
                }}
            }} catch (_) {{ }}
        }}

        return JSON.stringify({{ success: true, filledCount: filledCount, submitted: submitted, failedFields: failedFields }});
    }} catch (err) {{
        return JSON.stringify({{ success: false, error: err.message || String(err) }});
    }}
}})()";

        var result = await RunOnDispatcherAsync<object>(async () =>
        {
            if (_browserControl.CoreWebView2 == null) return new { success = false, error = "WebView2 not initialized" };
            try
            {
                string raw = await ExecuteScriptWithCdpAsync(script);
                return ParseJsResult(raw);
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message };
            }
        });

        bool isSuccess = false;
        string? errorMsg = null;
        object? diagObj = null;

        try
        {
            if (result != null)
            {
                var succProp = result.GetType().GetProperty("success");
                if (succProp?.GetValue(result) is bool b)
                {
                    isSuccess = b;
                }
                var errProp = result.GetType().GetProperty("error");
                errorMsg = errProp?.GetValue(result)?.ToString();
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    isSuccess = false;
                }

                var rProp = result.GetType().GetProperty("raw");
                string? rawJson = rProp?.GetValue(result) as string;
                if (!string.IsNullOrEmpty(rawJson))
                {
                    using var jDoc = JsonDocument.Parse(rawJson);
                    if (jDoc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (jDoc.RootElement.TryGetProperty("success", out var sProp))
                            isSuccess = sProp.GetBoolean();
                        if (jDoc.RootElement.TryGetProperty("error", out var eProp))
                            errorMsg = eProp.GetString();
                        if (jDoc.RootElement.TryGetProperty("failedFields", out var fProp) && fProp.ValueKind == JsonValueKind.Array && fProp.GetArrayLength() > 0)
                            diagObj = new { failedFields = JsonSerializer.Deserialize<object>(fProp.GetRawText()) };
                    }
                }
            }
        }
        catch { }

        RecordFlight("fill-form", "POST", _currentSourceUrl, sw.ElapsedMilliseconds, 200, isSuccess, $"fields={fields.EnumerateObject().Count()}", errorMsg, diagObj, convId);
        await SendJsonResponseAsync(resp, 200, result);
    }

    public bool IsOverlayActive { get; set; } = false;

    private async Task HandleGetInteractiveMapAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();
        string convId = ExtractConversationId(req);
        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);
        bool overlay = req.QueryString["overlay"] == "true" || IsOverlayActive;
        bool scanAll = req.QueryString["scanAll"] == "true" || req.QueryString["scan_all"] == "true";

        if (scanAll && IsTakeoverBlocked(req))
        {
            await SendJsonResponseAsync(resp, 409, new { success = false, error = "takeover_active", message = "Menschlicher Takeover ist aktiv. Scan-All abgelehnt." });
            return;
        }

        string script = $@"
(async function() {{
    try {{
        {DeepDomHelperJs}

        function getActiveScrollContainer() {{
            var modal = querySelectorDeep('[role=""dialog""], [aria-modal=""true""], .modal, .dialog');
            if (modal && (modal.offsetWidth > 0 || modal.offsetHeight > 0 || modal.getClientRects().length > 0)) {{
                if (modal.scrollHeight > modal.clientHeight + 20) return modal;
                var scrollables = modal.querySelectorAll('*');
                for (var i = 0; i < scrollables.length; i++) {{
                    var s = scrollables[i];
                    if (s.scrollHeight > s.clientHeight + 20) {{
                        var sty = window.getComputedStyle(s);
                        if (sty.overflowY === 'auto' || sty.overflowY === 'scroll') {{
                            return s;
                        }}
                    }}
                }}
            }}
            var cur = document.activeElement;
            while (cur && cur !== document.body && cur !== document.documentElement) {{
                if (cur.scrollHeight > cur.clientHeight + 20) {{
                    var sty = window.getComputedStyle(cur);
                    if (sty.overflowY === 'auto' || sty.overflowY === 'scroll') {{
                        return cur;
                    }}
                }}
                cur = cur.parentElement;
            }}
            var bestEl = null;
            var bestHeight = 0;
            var candidates = querySelectorAllDeep('main, [role=""main""], #workspace, [class*=""scroll""], [class*=""container""]');
            for (var i = 0; i < candidates.length; i++) {{
                var el = candidates[i];
                if (el.scrollHeight > el.clientHeight + 50) {{
                    var sty = (el.ownerDocument ? el.ownerDocument.defaultView || window : window).getComputedStyle(el);
                    if ((sty.overflowY === 'auto' || sty.overflowY === 'scroll') && el.scrollHeight > bestHeight) {{
                        bestHeight = el.scrollHeight;
                        bestEl = el;
                    }}
                }}
            }}
            if (bestEl) return bestEl;

            var generic = querySelectorAllDeep('div[style*=""overflow""], section[style*=""overflow""], [class*=""scroll""]', document, 50, 3);
            for (var j = 0; j < generic.length; j++) {{
                var el2 = generic[j];
                if (el2.scrollHeight > el2.clientHeight + 100) {{
                    var sty2 = (el2.ownerDocument ? el2.ownerDocument.defaultView || window : window).getComputedStyle(el2);
                    if ((sty2.overflowY === 'auto' || sty2.overflowY === 'scroll') && el2.scrollHeight > bestHeight) {{
                        bestHeight = el2.scrollHeight;
                        bestEl = el2;
                    }}
                }}
            }}
            return bestEl;
        }}

        var shouldScanAll = {scanAll.ToString().ToLowerInvariant()};
        if (shouldScanAll) {{
            var container = getActiveScrollContainer();
            if (container) {{
                var total = container.scrollHeight;
                var cur = 0;
                var maxSteps = 25;
                var step = 0;
                while (cur < total && step < maxSteps) {{
                    step++;
                    container.scrollTop += 800;
                    cur += 800;
                    await new Promise(function(r) {{ setTimeout(r, 120); }});
                    total = container.scrollHeight;
                }}
                container.scrollTop = 0;
                await new Promise(function(r) {{ setTimeout(r, 100); }});
            }} else {{
                var scrollEl = document.scrollingElement || document.documentElement || document.body;
                var total = scrollEl ? scrollEl.scrollHeight : 0;
                var cur = 0;
                var maxSteps = 25;
                var step = 0;
                while (cur < total && step < maxSteps) {{
                    step++;
                    window.scrollBy({{ top: 800, behavior: 'instant' }});
                    cur += 800;
                    await new Promise(function(r) {{ setTimeout(r, 120); }});
                    scrollEl = document.scrollingElement || document.documentElement || document.body;
                    total = scrollEl ? scrollEl.scrollHeight : cur;
                }}
                window.scrollTo({{ top: 0, behavior: 'instant' }});
                await new Promise(function(r) {{ setTimeout(r, 100); }});
            }}
        }}

        function isDialogVisible(d) {{
            if (!d) return false;
            if (d.hasAttribute('hidden') || d.getAttribute('aria-hidden') === 'true') return false;
            if (d.classList && (d.classList.contains('vjs-hidden') || d.classList.contains('hidden') || d.classList.contains('d-none') || d.classList.contains('ng-hide'))) return false;
            var win = d.ownerDocument ? (d.ownerDocument.defaultView || window) : window;
            var st = win.getComputedStyle(d);
            if (st.display === 'none' || st.visibility === 'hidden' || (parseFloat(st.opacity) === 0 && !d.classList.contains('show'))) return false;
            var r = d.getBoundingClientRect();
            if (r.width < 20 || r.height < 20) return false;
            if (r.right <= 0 || r.bottom <= 0 || r.left >= win.innerWidth || r.top >= win.innerHeight) return false;
            return true;
        }}

        function isInsideDialog(el, dlg) {{
            if (!dlg || !el) return false;
            var cur = el;
            while (cur) {{
                if (dlg.contains(cur)) return true;
                var doc = cur.ownerDocument;
                var w = doc ? doc.defaultView : null;
                cur = (w && w !== window) ? w.frameElement : null;
            }}
            return false;
        }}

        var dialogCandidates = Array.from(querySelectorAllDeep('[role=""dialog""], [role=""alertdialog""], dialog[open], mat-dialog-container, .modal.show, .modal-dialog, .modal[aria-modal=""true""]', document, 20, 4));
        var activeDialog = dialogCandidates.find(isDialogVisible) || null;
        var hasActiveDialog = !!activeDialog;
        var dialogTitle = null;
        if (activeDialog) {{
            var titleEl = activeDialog.querySelector('h1, h2, h3, h4, [role=""heading""], .modal-title, [class*=""title""], [class*=""header""]');
            var rawTitle = activeDialog.getAttribute('aria-label') || (titleEl ? (titleEl.innerText || titleEl.textContent) : '') || '';
            dialogTitle = rawTitle.trim();
            if (dialogTitle.length > 80) dialogTitle = dialogTitle.substring(0, 80) + '...';
        }}

        // Bestehende data-agy-id Attribute restlos bereinigen, um Stale IDs zu verhindern
        var existingAgyIds = querySelectorAllDeep('[data-agy-id]', document, 1500, 6);
        for (var k = 0; k < existingAgyIds.length; k++) {{
            existingAgyIds[k].removeAttribute('data-agy-id');
        }}

        var elements = [];
        var candidates = querySelectorAllDeep(
            'button, a[href], input:not([type=""hidden""]), select, textarea, [role=""button""], [role=""link""], [role=""checkbox""], [role=""radio""], [role=""option""], [role=""switch""], [role=""menuitem""], [role=""tab""], [class*=""tab""], [class*=""btn""], [data-tab], [onclick], label[for], label:has(input), [contenteditable=""true""]',
            document, 1500, 6
        );
        var idCounter = 1;
        for (var i = 0; i < candidates.length; i++) {{
            if (elements.length >= 800) break;
            var el = candidates[i];
            if (el.disabled) continue;
            var win = el.ownerDocument ? (el.ownerDocument.defaultView || window) : window;
            var style = win.getComputedStyle(el);
            var isFileInput = (el.tagName === 'INPUT' && el.type === 'file');
            var isHiddenInput = isFileInput || ((el.type === 'checkbox' || el.type === 'radio') && style.opacity === '0');
            if (!isFileInput && (style.display === 'none' || style.visibility === 'hidden' || (style.opacity === '0' && !isHiddenInput))) continue;
            
            var rect = el.getBoundingClientRect();
            if (rect.width === 0 && rect.height === 0) {{
                if (isHiddenInput) {{
                    var lbl = (el.labels && el.labels.length > 0) ? el.labels[0] : (el.id ? (el.ownerDocument || document).querySelector('label[for=""' + el.id + '""]') : null);
                    if (lbl) rect = lbl.getBoundingClientRect();
                    if (rect.width === 0 && rect.height === 0 && el.parentElement) {{
                        rect = el.parentElement.getBoundingClientRect();
                    }}
                }}
            }}
            if (!isFileInput && (rect.width <= 1 && rect.height <= 1)) continue;
            if (!isFileInput && !shouldScanAll && (rect.right <= 0 || rect.bottom <= 0 || rect.x < -5000 || rect.y < -5000)) continue;

            // Koordinaten relativ zum Hauptfenster berechnen (falls Element in einem iframe liegt)
            var offsetX = 0;
            var offsetY = 0;
            var curWin = win;
            while (curWin && curWin !== window) {{
                try {{
                    var frameEl = curWin.frameElement;
                    if (frameEl) {{
                        var fRect = frameEl.getBoundingClientRect();
                        offsetX += fRect.left;
                        offsetY += fRect.top;
                        curWin = curWin.parent;
                    }} else {{
                        break;
                    }}
                }} catch(e) {{ break; }}
            }}
            
            var id = idCounter++;
            el.setAttribute('data-agy-id', id);
            
            var rawVal = (el.type === 'password') ? (el.value ? '***' : '') : el.value;
            var innerTxt = (el.innerText || '').trim();
            var ariaLbl = (el.getAttribute('aria-label') || '').trim();
            var text = '';

            // Bei Material-Icon-Buttons (z. B. innerText='add', 'settings', 'help_outline', 'more_vert') hat aria-label Vorrang!
            if (ariaLbl && (!innerTxt || /^[a-z0-9_]+$/.test(innerTxt) || innerTxt.length <= 2)) {{
                text = ariaLbl;
            }} else {{
                text = (innerTxt || rawVal || el.placeholder || ariaLbl || el.title || el.alt || '').trim();
            }}
            
            // Bei Radio/Checkbox/File: Falls kein lesbarer Text oder nur technisches 'on'/'true', Text aus assoziiertem Label oder accept beziehen
            if ((el.type === 'checkbox' || el.type === 'radio' || isFileInput) && (!text || text === 'on' || text === 'true' || text === '1')) {{
                var assocLabel = (el.labels && el.labels.length > 0) ? el.labels[0] : (el.id ? (el.ownerDocument || document).querySelector('label[for=""' + el.id + '""]') : null);
                if (assocLabel && assocLabel.innerText) {{
                    text = assocLabel.innerText.trim();
                }} else if (isFileInput && el.getAttribute('accept')) {{
                    text = 'File-Upload (' + el.getAttribute('accept') + ')';
                }}
            }}

            if (text.length > 80) text = text.substring(0, 80) + '...';
            
            var inDialog = isInsideDialog(el, activeDialog);
            var safeEscape = function(val) {{ return (window.CSS && CSS.escape) ? CSS.escape(val) : val.replace(/[""\\]/g, '\\$&'); }};
            var stableSel = el.id
                ? '#' + safeEscape(el.id)
                : (el.getAttribute('data-tab')
                    ? '[data-tab=""' + safeEscape(el.getAttribute('data-tab')) + '""]'
                    : (el.name
                        ? el.tagName.toLowerCase() + '[name=""' + safeEscape(el.name) + '""]'
                        : (ariaLbl ? el.tagName.toLowerCase() + '[aria-label=""' + safeEscape(ariaLbl) + '""]' : null)));
            var item = {{
                id: id,
                tag: el.tagName.toLowerCase(),
                type: el.getAttribute('type') || (el.tagName.toLowerCase() === 'textarea' ? 'textarea' : null),
                selector: '[data-agy-id=""' + id + '""]',
                fallbackSelector: stableSel,
                text: text,
                rect: {{ x: Math.round(rect.x + offsetX), y: Math.round(rect.y + offsetY), w: Math.round(rect.width), h: Math.round(rect.height) }}
            }};
            if (ariaLbl && ariaLbl !== text) item.ariaLabel = ariaLbl;
            if (isFileInput && el.getAttribute('accept')) item.accept = el.getAttribute('accept');
            if (inDialog) item.inDialog = true;
            if (el.id) item.domId = el.id;
            if (el.name) item.name = el.name;

            // ARIA / Custom Role
            var role = el.getAttribute('role');
            if (role) item.role = role;
            var ariaCheck = el.getAttribute('aria-checked') || el.getAttribute('aria-selected');
            if (ariaCheck !== null) item.checked = ariaCheck === 'true';

            // Checked State für native Checkbox / Radio
            if (el.checked !== undefined && (el.type === 'checkbox' || el.type === 'radio')) item.checked = el.checked;

            // Active / Selected / Expanded State für Tabs, Toggles, Menüs
            var cls = (typeof el.className === 'string') ? el.className.toLowerCase() : '';
            var isActive = cls.indexOf('active') !== -1 || cls.indexOf('selected') !== -1 || cls.indexOf('current') !== -1 || el.getAttribute('aria-selected') === 'true' || el.getAttribute('aria-current') === 'page' || el.getAttribute('aria-current') === 'true' || el.getAttribute('data-active') === 'true';
            if (isActive) item.active = true;

            var ariaExpanded = el.getAttribute('aria-expanded');
            if (ariaExpanded !== null) item.expanded = ariaExpanded === 'true';

            if (el.disabled || el.getAttribute('aria-disabled') === 'true') item.disabled = true;

            // Falls es sich um ein Label handelt: assoziiertes Input verknüpfen
            if (el.tagName.toLowerCase() === 'label') {{
                var assocInput = el.htmlFor ? document.getElementById(el.htmlFor) : el.querySelector('input');
                if (assocInput) {{
                    if (assocInput.type) item.type = assocInput.type;
                    if (assocInput.name && !item.name) item.name = assocInput.name;
                    if (assocInput.checked !== undefined) item.checked = assocInput.checked;
                    if (assocInput.disabled) item.disabled = true;
                }}
            }}

            elements.push(item);
        }}

        // Etwaige visuelle Badges immer restlos entfernen (reine unsichtbare DOM-Arbeitsweise der KI)
        var old = document.getElementById('agy-cobrowser-badges-root');
        if (old) old.remove();

        var activeScrollEl = getActiveScrollContainer();
        var scrollDesc = activeScrollEl ? (activeScrollEl.id ? '#' + activeScrollEl.id : (activeScrollEl.tagName.toLowerCase() + (activeScrollEl.className && typeof activeScrollEl.className === 'string' ? '.' + activeScrollEl.className.trim().split(/\s+/).slice(0, 2).join('.') : ''))) : 'window';

        var detectedFramework = 'vanilla';
        try {{
            if (window.React || document.querySelector('[data-reactroot], [data-reacthost]')) {{
                detectedFramework = 'react';
            }} else if (window.Vue || document.querySelector('[data-v-]')) {{
                detectedFramework = 'vue';
            }} else if (window.ng || document.querySelector('[ng-version]')) {{
                detectedFramework = 'angular';
            }} else if (document.body) {{
                for (var bKey in document.body) {{
                    if (bKey.startsWith('__reactFiber') || bKey.startsWith('__reactContainer')) {{
                        detectedFramework = 'react';
                        break;
                    }}
                }}
            }}
        }} catch (_) {{ }}

        var bodyOv = 'visible';
        try {{ bodyOv = window.getComputedStyle(document.body).overflowY; }} catch (_) {{ }}

        var fingerprint = {{
            framework: detectedFramework,
            scrollContainer: scrollDesc,
            bodyOverflowY: bodyOv,
            totalDomNodes: document.querySelectorAll('*').length,
            timestamp: new Date().toISOString()
        }};

        return JSON.stringify({{
            url: window.location.href,
            title: document.title,
            hasActiveDialog: hasActiveDialog,
            dialogTitle: dialogTitle,
            elementCount: elements.length,
            overlay: false,
            elements: elements,
            fingerprint: fingerprint
        }});
    }} catch (err) {{
        return JSON.stringify({{ error: err.message || String(err) }});
    }}
}})()";

        var result = await RunOnDispatcherAsync(async () =>
        {
            if (_browserControl.CoreWebView2 == null) return "{\"error\": \"WebView2 not ready\"}";
            try
            {
                string raw = await ExecuteScriptWithCdpAsync(script);
                if (!string.IsNullOrEmpty(raw) && raw.StartsWith("\"") && raw.EndsWith("\""))
                {
                    return JsonSerializer.Deserialize<string>(raw) ?? raw;
                }
                return raw;
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        });

        int elemCount = 0;
        try
        {
            using var fpDoc = JsonDocument.Parse(result);
            if (fpDoc.RootElement.TryGetProperty("fingerprint", out var fpProp) && fpProp.ValueKind == JsonValueKind.Object)
            {
                _lastPageFingerprint = JsonSerializer.Deserialize<object>(fpProp.GetRawText());
            }
            if (fpDoc.RootElement.TryGetProperty("elementCount", out var ecProp))
            {
                elemCount = ecProp.GetInt32();
            }
        }
        catch { }

        RecordFlight("interactive-map", "GET", _currentSourceUrl, sw.ElapsedMilliseconds, 200, true, $"elements={elemCount}", null, null, convId);

        resp.ContentType = "application/json; charset=utf-8";
        resp.StatusCode = 200;
        byte[] bytes = Encoding.UTF8.GetBytes(result);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    private async Task HandleGetContentAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();
        string convId = ExtractConversationId(req);
        string? selector = req.QueryString["selector"];
        bool clean = req.QueryString["clean"] != "false";
        int maxLength = 60000;
        if (int.TryParse(req.QueryString["maxLength"] ?? req.QueryString["max_length"], out var ml) && ml > 0)
        {
            maxLength = ml;
        }

        if (req.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string body = await ReadBodyAsStringAsync(req);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    var root = doc.RootElement;
                    if (string.IsNullOrEmpty(convId))
                    {
                        convId = ExtractConversationId(req, root);
                    }
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (string.IsNullOrWhiteSpace(selector) && root.TryGetProperty("selector", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                        {
                            selector = sProp.GetString();
                        }
                        if (root.TryGetProperty("clean", out var cProp))
                        {
                            clean = cProp.ValueKind switch
                            {
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.String => !cProp.GetString()!.Equals("false", StringComparison.OrdinalIgnoreCase),
                                _ => clean
                            };
                        }
                        if (root.TryGetProperty("maxLength", out var mProp) && mProp.TryGetInt32(out int mVal) && mVal > 0)
                        {
                            maxLength = mVal;
                        }
                        else if (root.TryGetProperty("max_length", out var mProp2) && mProp2.TryGetInt32(out int mVal2) && mVal2 > 0)
                        {
                            maxLength = mVal2;
                        }
                    }
                }
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);

        string script = $@"
(function() {{
    try {{
        {DeepDomHelperJs}

        var sel = {JsonSerializer.Serialize(selector)};
        var clean = {clean.ToString().ToLowerInvariant()};
        var maxLength = {maxLength};

        var target = null;
        if (sel) {{
            var all = querySelectorAllDeep(sel);
            if (all.length > 1) {{
                target = document.createElement('div');
                all.forEach(function(el) {{ target.appendChild(el.cloneNode(true)); }});
            }} else if (all.length === 1) {{
                target = all[0];
            }}
        }}
        if (!target) {{
            target = querySelectorDeep('article') || querySelectorDeep('main') || querySelectorDeep('[role=""main""]') || document.body;
        }}
        if (!target) return JSON.stringify({{ success: false, error: 'Target element not found' }});

        // Vor dem Clonen: Inhalte lesbarer Iframes erfassen
        var liveIframes = target.querySelectorAll('iframe, frame');
        var iframeReplacements = [];
        for (var f = 0; f < liveIframes.length; f++) {{
            try {{
                var fDoc = liveIframes[f].contentDocument || liveIframes[f].contentWindow.document;
                if (fDoc && fDoc.body && (fDoc.body.innerText || '').trim().length > 0) {{
                    iframeReplacements.push({{
                        index: f,
                        html: fDoc.body.innerHTML
                    }});
                }}
            }} catch(e) {{ }}
        }}

        var clone = target.cloneNode(true);

        // Cloned iframes durch DIVs mit dem extrahierten Iframe-Body ersetzen
        var cloneIframes = clone.querySelectorAll('iframe, frame');
        for (var rep = 0; rep < iframeReplacements.length; rep++) {{
            var r = iframeReplacements[rep];
            if (r.index < cloneIframes.length) {{
                var holder = document.createElement('div');
                holder.className = 'agy-embedded-iframe-content';
                holder.innerHTML = r.html;
                var oldFrame = cloneIframes[r.index];
                if (oldFrame && oldFrame.parentNode) {{
                    oldFrame.parentNode.replaceChild(holder, oldFrame);
                }}
            }}
        }}

        // 1. Boilerplate & störende Elemente bereinigen (form und button bewusst erhalten für Interaktivität!)
        if (clean) {{
            var junk = [
                'script', 'style', 'noscript', 'nav', 'footer', 'header', 'svg', 'iframe',
                '[role=""navigation""]', '[role=""banner""]', '[role=""contentinfo""]',
                '.cookie', '.banner', '.ad', '.advertisement',
                '#agy-cobrowser-badges-root'
            ];
            junk.forEach(function(s) {{
                var els = clone.querySelectorAll(s);
                els.forEach(function(el) {{ el.remove(); }});
            }});

            // Nur rein dekorative, textlose aria-hidden Elemente oder Deko-Icons entfernen (echten Text für Accessible Apps erhalten!)
            var ariaHiddens = clone.querySelectorAll('[aria-hidden=""true""]');
            ariaHiddens.forEach(function(el) {{
                var txt = (el.innerText || el.textContent || '').trim();
                if (txt === '' || el.tagName.toLowerCase() === 'svg' || el.tagName.toLowerCase() === 'i') {{
                    el.remove();
                }}
            }});
        }}

        // 2. Tabellen in Key-Value-Karten umwandeln (100% Terminal-freundlich, KEINE Pipe-Tabellen!)
        var tables = clone.querySelectorAll('table');
        var convertedTablesCount = 0;
        tables.forEach(function(tbl) {{
            convertedTablesCount++;
            var headers = [];
            var ths = tbl.querySelectorAll('th');
            if (ths.length > 0) {{
                ths.forEach(function(th) {{ headers.push((th.innerText || '').trim()); }});
            }}

            var rows = tbl.querySelectorAll('tr');
            var cardText = '\n';
            var rowIndex = 0;

            rows.forEach(function(tr) {{
                var tds = tr.querySelectorAll('td');
                if (tds.length === 0) {{
                    if (headers.length === 0) {{
                        tr.querySelectorAll('th').forEach(function(th) {{ headers.push((th.innerText || '').trim()); }});
                    }}
                    return;
                }}

                rowIndex++;
                cardText += '• [Eintrag ' + rowIndex + ']\n';
                tds.forEach(function(td, colIdx) {{
                    var label = headers[colIdx] || ('Feld ' + (colIdx + 1));
                    var val = (td.innerText || '').trim().replace(/\s+/g, ' ');
                    cardText += '  ' + label + ': ' + val + '\n';
                }});
            }});

            var replacement = document.createElement('div');
            replacement.textContent = cardText + '\n';
            if (tbl.parentNode) {{
                tbl.parentNode.replaceChild(replacement, tbl);
            }}
        }});

        // 2b. Formular-Felder mit Live-Werten aus dem Original-DOM anreichern (Werte & Checkboxen visualisieren)
        var origFields = Array.from(target.querySelectorAll('input, select, textarea'));
        var cloneFields = Array.from(clone.querySelectorAll('input, select, textarea'));
        for (var f = 0; f < cloneFields.length; f++) {{
            var cloneField = cloneFields[f];
            var liveField = origFields[f] || cloneField;
            var type = (liveField.getAttribute('type') || liveField.tagName.toLowerCase()).toLowerCase();
            if (type === 'hidden' || type === 'password') continue;
            var valSpan = document.createElement('span');
            if (type === 'checkbox' || type === 'radio') {{
                valSpan.textContent = liveField.checked ? ' [x] ' : ' [ ] ';
            }} else if (liveField.tagName.toLowerCase() === 'select') {{
                var selectedOpt = liveField.options && liveField.selectedIndex >= 0 ? liveField.options[liveField.selectedIndex] : null;
                var val = selectedOpt ? (selectedOpt.text || selectedOpt.value) : (liveField.value || '');
                valSpan.textContent = val ? ' [' + val.trim() + '] ' : ' [---] ';
            }} else {{
                var v = (liveField.value || liveField.placeholder || '').trim();
                valSpan.textContent = v ? ' [' + v + '] ' : ' [___] ';
            }}
            if (cloneField.parentNode) {{
                cloneField.parentNode.replaceChild(valSpan, cloneField);
            }}
        }}

        // 3. Überschriften in Markdown überführen
        ['h1', 'h2', 'h3', 'h4', 'h5', 'h6'].forEach(function(tag, idx) {{
            var prefix = '#'.repeat(idx + 1) + ' ';
            clone.querySelectorAll(tag).forEach(function(h) {{
                var t = (h.innerText || '').trim();
                var div = document.createElement('div');
                div.textContent = '\n\n' + prefix + t + '\n\n';
                if (h.parentNode) h.parentNode.replaceChild(div, h);
            }});
        }});

        // 4. Listenpunkte
        clone.querySelectorAll('li').forEach(function(li) {{
            var t = (li.innerText || '').trim();
            var div = document.createElement('div');
            div.textContent = '\n- ' + t;
            if (li.parentNode) li.parentNode.replaceChild(div, li);
        }});

        // 5. Links
        clone.querySelectorAll('a[href]').forEach(function(a) {{
            var href = a.getAttribute('href');
            var text = (a.innerText || '').trim();
            if (text && href && !href.startsWith('javascript:') && !href.startsWith('#')) {{
                var span = document.createElement('span');
                span.textContent = ' [' + text + '](' + href + ') ';
                if (a.parentNode) a.parentNode.replaceChild(span, a);
            }}
        }});

        // 6. Bereinigten Fließtext normalisieren
        var raw = clone.innerText || clone.textContent || '';
        var cleaned = raw
            .replace(/\r\n/g, '\n')
            .replace(/\n{{3,}}/g, '\n\n')
            .replace(/[ \t]{{2,}}/g, ' ')
            .trim();

        if (cleaned.length > maxLength) {{
            cleaned = cleaned.substring(0, maxLength) + '\n\n... [Gekürzt auf ' + maxLength + ' Zeichen]';
        }}

        var metaDesc = '';
        var descEl = document.querySelector('meta[name=""description""]') || document.querySelector('meta[property=""og:description""]');
        if (descEl) metaDesc = descEl.getAttribute('content') || '';

        return JSON.stringify({{
            success: true,
            title: document.title || '',
            url: window.location.href,
            description: metaDesc,
            characterCount: cleaned.length,
            tableCount: convertedTablesCount,
            content: cleaned
        }});
    }} catch (err) {{
        return JSON.stringify({{ success: false, error: err.message || String(err) }});
    }}
}})()";

        var result = await RunOnDispatcherAsync(async () =>
        {
            if (_browserControl.CoreWebView2 == null) return "{\"error\": \"WebView2 not ready\"}";
            try
            {
                string raw = await ExecuteScriptWithCdpAsync(script);
                if (!string.IsNullOrEmpty(raw) && raw.StartsWith("\"") && raw.EndsWith("\""))
                {
                    return JsonSerializer.Deserialize<string>(raw) ?? raw;
                }
                return raw;
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        });

        bool isSuccess = false;
        string? errorMsg = null;
        int charCount = 0;
        try
        {
            using var jDoc = JsonDocument.Parse(result);
            if (jDoc.RootElement.TryGetProperty("success", out var sProp))
                isSuccess = sProp.GetBoolean();
            if (jDoc.RootElement.TryGetProperty("error", out var eProp))
                errorMsg = eProp.GetString();
            if (jDoc.RootElement.TryGetProperty("characterCount", out var cProp))
                charCount = cProp.GetInt32();
        }
        catch { }

        RecordFlight("read_content", req.HttpMethod, _currentSourceUrl, sw.ElapsedMilliseconds, 200, isSuccess, !string.IsNullOrEmpty(selector) ? selector : $"{charCount} chars", errorMsg, null, convId);

        resp.ContentType = "application/json; charset=utf-8";
        resp.StatusCode = 200;
        byte[] bytes = Encoding.UTF8.GetBytes(result);
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        resp.Close();
    }

    private async Task HandlePostOverlayAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        IsOverlayActive = false;
        string script = "(function() { var old = document.getElementById('agy-cobrowser-badges-root'); if (old) old.remove(); return JSON.stringify({ success: true, overlay: false }); })()";

        var result = await RunOnDispatcherAsync<object>(async () =>
        {
            if (_browserControl.CoreWebView2 == null) return new { success = false, error = "WebView2 not initialized" };
            try
            {
                string raw = await ExecuteScriptWithCdpAsync(script);
                return ParseJsResult(raw);
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message };
            }
        });

        await SendJsonResponseAsync(resp, 200, result);
    }

    private async Task HandlePostScrollAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();
        string body = await ReadBodyAsStringAsync(req);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;

        string convId = ExtractConversationId(req, root);
        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);

        if (IsTakeoverBlocked(req, root))
        {
            await SendJsonResponseAsync(resp, 409, new { success = false, error = "takeover_active", message = "Menschlicher Takeover ist aktiv. Scrollen abgelehnt." });
            return;
        }

        string direction = root.TryGetProperty("direction", out var d) ? d.GetString()?.ToLowerInvariant() ?? "down" : "down";
        int pixels = root.TryGetProperty("pixels", out var p) ? p.GetInt32() : 600;
        int? targetY = null;
        if (root.TryGetProperty("y", out var yProp) && yProp.TryGetInt32(out int yVal))
        {
            targetY = yVal;
        }
        else if (root.TryGetProperty("targetY", out var tyProp) && tyProp.TryGetInt32(out int tyVal))
        {
            targetY = tyVal;
        }
        int offsetY = 0;
        if (root.TryGetProperty("offsetY", out var offProp) && offProp.TryGetInt32(out int offVal))
        {
            offsetY = offVal;
        }
        else if (root.TryGetProperty("offset", out var offProp2) && offProp2.TryGetInt32(out int offVal2))
        {
            offsetY = offVal2;
        }
        else if (root.TryGetProperty("yOffset", out var offProp3) && offProp3.TryGetInt32(out int offVal3))
        {
            offsetY = offVal3;
        }
        string selector = root.TryGetProperty("selector", out var s) ? s.GetString() ?? "" : "";
        int id = root.TryGetProperty("id", out var i)
            ? i.GetInt32()
            : (root.TryGetProperty("elementId", out var ei) ? ei.GetInt32() : 0);
        if (id > 0) selector = $"[data-agy-id='{id}']";

        string script = $@"
(function() {{
    {DeepDomHelperJs}

    var sel = {JsonSerializer.Serialize(selector)};
    var dir = {JsonSerializer.Serialize(direction)};
    var px = {pixels};
    var targetY = {(targetY.HasValue ? targetY.Value.ToString() : "null")};
    var offsetY = {offsetY};

    if (sel) {{
        var el = querySelectorDeep(sel);
        if (!el) return JSON.stringify({{ success: false, error: 'Element not found: ' + sel }});
        if (offsetY !== 0) {{
            var rect = el.getBoundingClientRect();
            var targetScrollY = rect.top + window.pageYOffset + offsetY;
            window.scrollTo({{ top: Math.max(0, targetScrollY), behavior: 'instant' }});
        }} else {{
            el.scrollIntoView({{ behavior: 'instant', block: 'center' }});
        }}
        return JSON.stringify({{ success: true, scrolledTo: sel, scrollY: window.scrollY, offsetY: offsetY }});
    }}

    function getScrollContainer() {{
        var modal = document.querySelector('[role=""dialog""], [aria-modal=""true""], .modal, .dialog');
        if (modal) {{
            if (modal.scrollHeight > modal.clientHeight + 20) return modal;
            var scrollables = modal.querySelectorAll('*');
            for (var i = 0; i < scrollables.length; i++) {{
                var s = scrollables[i];
                var sty = window.getComputedStyle(s);
                if ((sty.overflowY === 'auto' || sty.overflowY === 'scroll') && s.scrollHeight > s.clientHeight + 20) {{
                    return s;
                }}
            }}
        }}

        var cur = document.activeElement;
        while (cur && cur !== document.body && cur !== document.documentElement) {{
            var sty = window.getComputedStyle(cur);
            if ((sty.overflowY === 'auto' || sty.overflowY === 'scroll') && cur.scrollHeight > cur.clientHeight + 20) {{
                return cur;
            }}
            cur = cur.parentElement;
        }}

        var bestEl = null;
        var bestHeight = 0;
        var all = document.querySelectorAll('main, [role=""main""], div, section');
        for (var i = 0; i < all.length; i++) {{
            var el = all[i];
            var sty = window.getComputedStyle(el);
            if ((sty.overflowY === 'auto' || sty.overflowY === 'scroll') && el.scrollHeight > el.clientHeight + 50) {{
                if (el.scrollHeight > bestHeight) {{
                    bestHeight = el.scrollHeight;
                    bestEl = el;
                }}
            }}
        }}
        if (bestEl) return bestEl;

        return null;
    }}

    var targetContainer = getScrollContainer();

    if (targetContainer) {{
        var cMax = targetContainer.scrollHeight;
        if (targetY !== null) {{
            targetContainer.scrollTop = targetY;
        }} else if (dir === 'top') {{
            targetContainer.scrollTop = 0;
        }} else if (dir === 'bottom') {{
            targetContainer.scrollTop = cMax;
        }} else if (dir === 'up') {{
            targetContainer.scrollTop -= px;
        }} else {{
            targetContainer.scrollTop += px;
        }}
        return JSON.stringify({{
            success: true,
            direction: dir,
            pixels: px,
            targetY: targetY,
            container: targetContainer.tagName.toLowerCase() + (targetContainer.id ? '#' + targetContainer.id : ''),
            scrollY: targetContainer.scrollTop,
            maxScrollY: cMax
        }});
    }}

    var scrollEl = document.scrollingElement || document.documentElement || document.body;
    var maxScroll = scrollEl ? scrollEl.scrollHeight : 0;

    if (targetY !== null) {{
        window.scrollTo({{ top: targetY, behavior: 'instant' }});
    }} else if (dir === 'top') {{
        window.scrollTo({{ top: 0, behavior: 'instant' }});
    }} else if (dir === 'bottom') {{
        window.scrollTo({{ top: maxScroll, behavior: 'instant' }});
    }} else if (dir === 'up') {{
        window.scrollBy({{ top: -px, behavior: 'instant' }});
    }} else {{
        window.scrollBy({{ top: px, behavior: 'instant' }});
    }}

    return JSON.stringify({{ success: true, direction: dir, pixels: px, targetY: targetY, scrollY: window.scrollY, maxScrollY: maxScroll }});
}})();";

        var result = await RunOnDispatcherAsync<object>(async () =>
        {
            if (_browserControl.CoreWebView2 == null) return new { success = false, error = "WebView2 not initialized" };
            try
            {
                string raw = await ExecuteScriptWithCdpAsync(script);
                return ParseJsResult(raw);
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message };
            }
        });

        bool isSuccess = false;
        string? errorMsg = null;
        try
        {
            if (result != null)
            {
                var succProp = result.GetType().GetProperty("success");
                if (succProp?.GetValue(result) is bool b)
                {
                    isSuccess = b;
                }
                var errProp = result.GetType().GetProperty("error");
                errorMsg = errProp?.GetValue(result)?.ToString();
                if (!string.IsNullOrEmpty(errorMsg))
                {
                    isSuccess = false;
                }

                var rProp = result.GetType().GetProperty("raw");
                string? rawJson = rProp?.GetValue(result) as string;
                if (!string.IsNullOrEmpty(rawJson))
                {
                    using var jDoc = JsonDocument.Parse(rawJson);
                    if (jDoc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (jDoc.RootElement.TryGetProperty("success", out var sProp))
                            isSuccess = sProp.GetBoolean();
                        if (jDoc.RootElement.TryGetProperty("error", out var eProp))
                            errorMsg = eProp.GetString();
                    }
                }
            }
        }
        catch { }

        string targetDesc = !string.IsNullOrEmpty(selector) ? (offsetY != 0 ? $"{selector} (offsetY={offsetY})" : selector) : (targetY.HasValue ? $"targetY={targetY}" : $"{direction} {pixels}px");
        RecordFlight("scroll", "POST", _currentSourceUrl, sw.ElapsedMilliseconds, 200, isSuccess, targetDesc, errorMsg, null, convId);

        await SendJsonResponseAsync(resp, 200, result);
    }

    private async Task HandlePostUploadAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();
        string body = await ReadBodyAsStringAsync(req);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;

        string convId = ExtractConversationId(req, root);
        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);

        if (IsTakeoverBlocked(req, root))
        {
            await SendJsonResponseAsync(resp, 409, new { success = false, error = "takeover_active", message = "Menschlicher Takeover ist aktiv. Upload abgelehnt." });
            return;
        }

        string filePath = root.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "";
        string selector = root.TryGetProperty("selector", out var s) ? s.GetString() ?? "" : "";
        int id = root.TryGetProperty("id", out var i)
            ? i.GetInt32()
            : (root.TryGetProperty("elementId", out var ei) ? ei.GetInt32() : 0);
        if (id > 0) selector = $"[data-agy-id='{id}']";
        if (string.IsNullOrWhiteSpace(selector)) selector = "input[type='file']";

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            RecordFlight("upload", "POST", _currentSourceUrl, sw.ElapsedMilliseconds, 400, false, filePath, $"Datei nicht gefunden: '{filePath}'", null, convId);
            await SendJsonResponseAsync(resp, 400, new { success = false, error = $"Datei nicht gefunden: '{filePath}'" });
            return;
        }

        var result = await RunOnDispatcherAsync<object>(async () =>
        {
            if (_browserControl.CoreWebView2 == null) return new { success = false, error = "WebView2 not initialized" };
            try
            {
                // 1. DOM im CDP aktivieren
                await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.enable", "{}");

                // 2. Root Document mit Tiefe 1 initialisieren (verhindert MaxDepth-64-Crash bei tief verschachtelten Angular/Quill-DOMs)
                string docRes = await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{\"depth\": 1, \"pierce\": true}");
                using var docJson = JsonDocument.Parse(docRes, LenientJsonOptions);
                int rootNodeId = docJson.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();

                // 3. Node für selector ermitteln
                int targetNodeId = 0;
                string resolvedObjectId = "";
                try
                {
                    string queryRes = await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.querySelector", JsonSerializer.Serialize(new
                    {
                        nodeId = rootNodeId,
                        selector = selector
                    }));
                    using var queryJson = JsonDocument.Parse(queryRes, LenientJsonOptions);
                    if (queryJson.RootElement.TryGetProperty("nodeId", out var nIdProp))
                    {
                        targetNodeId = nIdProp.GetInt32();
                    }
                }
                catch { }

                // Fallback: Falls targetNodeId == 0 (z. B. in Iframe oder tiefer Shadow-DOM), Element über querySelectorDeep + DOM.requestNode / objectId auflösen!
                if (targetNodeId <= 0)
                {
                    string evalScript = $@"
(function() {{
    {DeepDomHelperJs}
    var el = querySelectorDeep({JsonSerializer.Serialize(selector)});
    return el;
}})()";
                    string evalRes = await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
                    {
                        expression = evalScript,
                        returnByValue = false
                    }));
                    using var evalJson = JsonDocument.Parse(evalRes, LenientJsonOptions);
                    if (evalJson.RootElement.TryGetProperty("result", out var rObj) && rObj.TryGetProperty("objectId", out var oIdProp))
                    {
                        resolvedObjectId = oIdProp.GetString() ?? "";
                        if (!string.IsNullOrEmpty(resolvedObjectId))
                        {
                            try
                            {
                                string reqNodeRes = await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.requestNode", JsonSerializer.Serialize(new
                                {
                                    objectId = resolvedObjectId
                                }));
                                using var reqNodeJson = JsonDocument.Parse(reqNodeRes, LenientJsonOptions);
                                if (reqNodeJson.RootElement.TryGetProperty("nodeId", out var rNodeIdProp))
                                {
                                    targetNodeId = rNodeIdProp.GetInt32();
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (targetNodeId <= 0 && string.IsNullOrEmpty(resolvedObjectId))
                {
                    return new { success = false, error = $"File-Input Element '{selector}' wurde im DOM nicht gefunden." };
                }

                // 4. CDP DOM.setFileInputFiles atomar ausführen (ohne Windows-Explorer-Dialog!)
                string setFilesPayload = targetNodeId > 0
                    ? JsonSerializer.Serialize(new { files = new[] { Path.GetFullPath(filePath) }, nodeId = targetNodeId })
                    : JsonSerializer.Serialize(new { files = new[] { Path.GetFullPath(filePath) }, objectId = resolvedObjectId });
                await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", setFilesPayload);

                // 5. Change-Events dispatchen (auch im Deep-DOM!)
                string dispatchScript = $@"
(function() {{
    {DeepDomHelperJs}
    var el = querySelectorDeep({JsonSerializer.Serialize(selector)});
    if (el) {{
        el.dispatchEvent(new Event('input', {{ bubbles: true }}));
        el.dispatchEvent(new Event('change', {{ bubbles: true }}));
    }}
}})()";
                await ExecuteScriptWithCdpAsync(dispatchScript);

                return new { success = true, selector = selector, file = Path.GetFileName(filePath), fullPath = Path.GetFullPath(filePath) };
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message };
            }
        });

        bool isSuccess = false;
        string? errorMsg = null;
        try
        {
            if (result != null)
            {
                var sProp = result.GetType().GetProperty("success");
                if (sProp?.GetValue(result) is bool b) isSuccess = b;
                var eProp = result.GetType().GetProperty("error");
                errorMsg = eProp?.GetValue(result)?.ToString();
            }
        }
        catch { }

        RecordFlight("upload", "POST", _currentSourceUrl, sw.ElapsedMilliseconds, 200, isSuccess, $"{selector} -> {Path.GetFileName(filePath)}", errorMsg, null, convId);

        await SendJsonResponseAsync(resp, 200, result);
    }

    private async Task HandleGetScreenshotAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();
        string convId = ExtractConversationId(req);
        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);

        string format = req.QueryString["format"] ?? "png";
        bool isJpeg = format.Equals("jpeg", StringComparison.OrdinalIgnoreCase) || format.Equals("jpg", StringComparison.OrdinalIgnoreCase);

        byte[]? imageBytes = null;
        string? base64Data = null;

        // 1. Primär: Schneller und robuster Screenshot über Chrome DevTools Protocol (CDP Page.captureScreenshot)
        try
        {
            var cdpTask = RunOnDispatcherAsync<string?>(async () =>
            {
                if (_browserControl.CoreWebView2 == null) return null;
                try
                {
                    string cdpParams = JsonSerializer.Serialize(new { format = isJpeg ? "jpeg" : "png" });
                    return await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", cdpParams);
                }
                catch
                {
                    return null;
                }
            });

            // 2.5 Sekunden Timeout gegen Deadlocks
            if (await Task.WhenAny(cdpTask, Task.Delay(2500)) == cdpTask)
            {
                string? cdpJson = await cdpTask;
                if (!string.IsNullOrEmpty(cdpJson))
                {
                    using var doc = JsonDocument.Parse(cdpJson);
                    if (doc.RootElement.TryGetProperty("data", out var dataProp))
                    {
                        base64Data = dataProp.GetString();
                        if (!string.IsNullOrEmpty(base64Data))
                        {
                            imageBytes = Convert.FromBase64String(base64Data);
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback auf WinUI CapturePreviewAsync
        }

        // 2. Sekundär: Fallback auf WinUI CapturePreviewAsync (falls CDP fehlschlug)
        if (imageBytes == null || imageBytes.Length == 0)
        {
            try
            {
                var previewTask = RunOnDispatcherAsync(async () =>
                {
                    if (_browserControl.CoreWebView2 == null) return null;
                    try
                    {
                        using var memStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                        var imgFormat = isJpeg ? CoreWebView2CapturePreviewImageFormat.Jpeg : CoreWebView2CapturePreviewImageFormat.Png;
                        await _browserControl.CoreWebView2.CapturePreviewAsync(imgFormat, memStream);
                        memStream.Seek(0);
                        using var netStream = System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(memStream);
                        using var ms = new MemoryStream();
                        await netStream.CopyToAsync(ms);
                        return ms.ToArray();
                    }
                    catch
                    {
                        return null;
                    }
                });

                if (await Task.WhenAny(previewTask, Task.Delay(1500)) == previewTask)
                {
                    imageBytes = await previewTask;
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        base64Data = Convert.ToBase64String(imageBytes);
                    }
                }
            }
            catch { }
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            RecordFlight("screenshot", "GET", _currentSourceUrl, sw.ElapsedMilliseconds, 500, false, format, "Screenshot capture timed out or failed", null, convId);
            await SendJsonResponseAsync(resp, 500, new { success = false, error = "Screenshot capture timed out or failed" });
            return;
        }

        RecordFlight("screenshot", "GET", _currentSourceUrl, sw.ElapsedMilliseconds, 200, true, $"{format} ({(imageBytes.Length + 1023) / 1024} KB)", null, null, convId);

        // Wenn 'raw=true', rohes Bild senden; sonst JSON mit Base64
        bool raw = req.QueryString["raw"] == "true";
        if (raw)
        {
            resp.ContentType = isJpeg ? "image/jpeg" : "image/png";
            resp.StatusCode = 200;
            resp.ContentLength64 = imageBytes.Length;
            await resp.OutputStream.WriteAsync(imageBytes);
            resp.Close();
        }
        else
        {
            base64Data ??= Convert.ToBase64String(imageBytes);
            await SendJsonResponseAsync(resp, 200, new
            {
                success = true,
                format = format,
                sizeBytes = imageBytes.Length,
                base64 = base64Data
            });
        }
    }

    private async Task HandlePostHandoffAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string body = await ReadBodyAsStringAsync(req);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = doc.RootElement;

        string action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        string reason = root.TryGetProperty("reason", out var r) ? r.GetString() ?? "" : "";

        if (action != "waiting_for_user" && IsTakeoverBlocked(req, root))
        {
            await SendJsonResponseAsync(resp, 409, new { success = false, error = "takeover_active", message = "Menschlicher Takeover ist aktiv. Handoff kann nicht überschreiben." });
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (action == "waiting_for_user")
            {
                _setWaitingForUserAction(reason);
            }
            else if (action == "controlling")
            {
                _setStatusAction(string.IsNullOrEmpty(reason) ? "🤖 KI steuert..." : $"🤖 {reason}");
            }
            else if (action == "ready")
            {
                _setReadyAction();
            }
        });

        await SendJsonResponseAsync(resp, 200, new { success = true, action = action, reason = reason });
    }

    public async Task EnsureNetworkInterceptionAsync()
    {
        if (_networkInterceptionEnabled) return;

        await RunOnDispatcherAsync(async () =>
        {
            if (_networkInterceptionEnabled || _browserControl.CoreWebView2 == null) return true;
            try
            {
                await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync(
                    "Network.enable",
                    "{\"maxTotalBufferSize\":10485760,\"maxResourceBufferSize\":5242880}");

                _reqReceiver = _browserControl.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
                _reqReceiver.DevToolsProtocolEventReceived += OnCdpRequestWillBeSent;

                _respReceiver = _browserControl.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived");
                _respReceiver.DevToolsProtocolEventReceived += OnCdpResponseReceived;

                _failReceiver = _browserControl.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFailed");
                _failReceiver.DevToolsProtocolEventReceived += OnCdpLoadingFailed;

                _finishReceiver = _browserControl.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFinished");
                _finishReceiver.DevToolsProtocolEventReceived += OnCdpLoadingFinished;

                _networkInterceptionEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CoBrowser Network Interception Error] {ex.Message}");
            }
            return true;
        });
    }

    public void CleanupNetworkInterception()
    {
        if (!_networkInterceptionEnabled) return;
        _networkInterceptionEnabled = false;

        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (_reqReceiver != null) _reqReceiver.DevToolsProtocolEventReceived -= OnCdpRequestWillBeSent;
                if (_respReceiver != null) _respReceiver.DevToolsProtocolEventReceived -= OnCdpResponseReceived;
                if (_failReceiver != null) _failReceiver.DevToolsProtocolEventReceived -= OnCdpLoadingFailed;
                if (_finishReceiver != null) _finishReceiver.DevToolsProtocolEventReceived -= OnCdpLoadingFinished;

                _reqReceiver = null;
                _respReceiver = null;
                _failReceiver = null;
                _finishReceiver = null;

                if (_browserControl.CoreWebView2 != null)
                {
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.disable", "{}");
                }
            }
            catch { }
        });
    }

    private void OnCdpRequestWillBeSent(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        string json = e.ParameterObjectAsJson;
        ThreadPool.QueueUserWorkItem(_ => ProcessCdpRequestWillBeSent(json));
    }

    private void ProcessCdpRequestWillBeSent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string reqId = root.TryGetProperty("requestId", out var rId) ? rId.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(reqId)) return;

            string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            string url = "";
            string method = "GET";
            if (root.TryGetProperty("request", out var reqObj))
            {
                url = reqObj.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                method = reqObj.TryGetProperty("method", out var m) ? m.GetString() ?? "GET" : "GET";
            }

            if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;

            var entry = new NetworkRequestEntry
            {
                RequestId = reqId,
                Url = url,
                Method = method,
                ResourceType = type,
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };

            lock (_networkLock)
            {
                _pendingRequests[reqId] = entry;
                _networkEntries.Add(entry);
                while (_networkEntries.Count > 150)
                {
                    var removed = _networkEntries[0];
                    _networkEntries.RemoveAt(0);
                    if (!string.IsNullOrEmpty(removed.RequestId))
                    {
                        _pendingRequests.Remove(removed.RequestId);
                    }
                }
                if (_pendingRequests.Count > 250)
                {
                    var keysToRemove = _pendingRequests.Keys.Take(100).ToList();
                    foreach (var k in keysToRemove) _pendingRequests.Remove(k);
                }
            }
        }
        catch { }
    }

    private void OnCdpResponseReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        string json = e.ParameterObjectAsJson;
        ThreadPool.QueueUserWorkItem(_ => ProcessCdpResponseReceived(json));
    }

    private void ProcessCdpResponseReceived(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string reqId = root.TryGetProperty("requestId", out var rId) ? rId.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(reqId)) return;

            if (root.TryGetProperty("response", out var respObj))
            {
                int status = respObj.TryGetProperty("status", out var s) ? s.GetInt32() : 0;
                string statusText = respObj.TryGetProperty("statusText", out var st) ? st.GetString() ?? "" : "";
                string mimeType = respObj.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "" : "";
                long len = 0;
                if (respObj.TryGetProperty("encodedDataLength", out var edl)) len = edl.GetInt64();

                lock (_networkLock)
                {
                    if (_pendingRequests.TryGetValue(reqId, out var entry))
                    {
                        entry.StatusCode = status;
                        entry.StatusText = statusText;
                        entry.MimeType = mimeType;
                        entry.ContentLength = len;
                    }
                }
            }
        }
        catch { }
    }

    private void OnCdpLoadingFailed(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        string json = e.ParameterObjectAsJson;
        ThreadPool.QueueUserWorkItem(_ => ProcessCdpLoadingFailed(json));
    }

    private void ProcessCdpLoadingFailed(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string reqId = root.TryGetProperty("requestId", out var rId) ? rId.GetString() ?? "" : "";
            string error = root.TryGetProperty("errorText", out var err) ? err.GetString() ?? "Failed" : "Failed";

            lock (_networkLock)
            {
                if (_pendingRequests.TryGetValue(reqId, out var entry))
                {
                    entry.Failed = true;
                    entry.ErrorText = error;
                    _pendingRequests.Remove(reqId);
                }
            }
        }
        catch { }
    }

    private void OnCdpLoadingFinished(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        string json = e.ParameterObjectAsJson;
        ThreadPool.QueueUserWorkItem(_ => ProcessCdpLoadingFinished(json));
    }

    private void ProcessCdpLoadingFinished(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string reqId = root.TryGetProperty("requestId", out var rId) ? rId.GetString() ?? "" : "";

            lock (_networkLock)
            {
                _pendingRequests.Remove(reqId);
            }
        }
        catch { }
    }

    private async Task HandleGetNetworkAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        await EnsureNetworkInterceptionAsync();

        bool clear = req.QueryString["clear"] == "true";
        if (clear)
        {
            lock (_networkLock)
            {
                _networkEntries.Clear();
                _pendingRequests.Clear();
            }
            await SendJsonResponseAsync(resp, 200, new { success = true, message = "Network buffer cleared" });
            return;
        }

        string filter = req.QueryString["type"] ?? req.QueryString["filter"] ?? "xhr,fetch,json";
        string search = (req.QueryString["search"] ?? "").Trim();
        string targetRequestId = req.QueryString["requestId"] ?? "";
        bool includeBody = req.QueryString["includeBody"] == "true";
        int limit = 25;
        if (int.TryParse(req.QueryString["limit"], out int l) && l > 0) limit = Math.Min(l, 100);

        var filterTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(filter) && filter != "all")
        {
            foreach (var part in filter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                filterTypes.Add(part.Trim());
            }
        }

        List<NetworkRequestEntry> matchingEntries;
        lock (_networkLock)
        {
            matchingEntries = new List<NetworkRequestEntry>(_networkEntries);
        }

        var filtered = matchingEntries.Where(e =>
        {
            if (!string.IsNullOrEmpty(targetRequestId) && !string.Equals(e.RequestId, targetRequestId, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(search) && !e.Url.Contains(search, StringComparison.OrdinalIgnoreCase))
                return false;

            if (filterTypes.Count > 0)
            {
                bool matchType = filterTypes.Contains(e.ResourceType);
                bool matchMime = !string.IsNullOrEmpty(e.MimeType) && filterTypes.Any(ft => e.MimeType.Contains(ft, StringComparison.OrdinalIgnoreCase));
                if (!matchType && !matchMime) return false;
            }

            return true;
        }).TakeLast(limit).ToList();

        if (includeBody && filtered.Count > 0)
        {
            var bodyTargets = filtered.TakeLast(5).ToList();
            foreach (var target in bodyTargets)
            {
                if (target.ResponseBody == null && !target.Failed && target.StatusCode > 0)
                {
                    bool isTextOrJson = string.IsNullOrEmpty(target.MimeType) ||
                                        target.MimeType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                                        target.MimeType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                                        target.MimeType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
                                        target.MimeType.Contains("javascript", StringComparison.OrdinalIgnoreCase);

                    if (!isTextOrJson)
                    {
                        target.ResponseBody = $"<Binärdaten: {target.MimeType} ({target.ContentLength} Bytes)>";
                        continue;
                    }

                    try
                    {
                        target.ResponseBody = await RunOnDispatcherAsync(async () =>
                        {
                            if (_browserControl.CoreWebView2 == null) return null;
                            try
                            {
                                string cdpRes = await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync(
                                    "Network.getResponseBody",
                                    JsonSerializer.Serialize(new { requestId = target.RequestId }));
                                using var doc = JsonDocument.Parse(cdpRes);
                                if (doc.RootElement.TryGetProperty("body", out var b))
                                {
                                    bool base64 = doc.RootElement.TryGetProperty("base64Encoded", out var enc) && enc.GetBoolean();
                                    string bodyStr = b.GetString() ?? "";
                                    if (base64)
                                    {
                                        byte[] rawBytes = Convert.FromBase64String(bodyStr);
                                        return Encoding.UTF8.GetString(rawBytes);
                                    }
                                    return bodyStr;
                                }
                            }
                            catch { }
                            return null;
                        });
                    }
                    catch { }
                }
            }
        }

        await SendJsonResponseAsync(resp, 200, new
        {
            success = true,
            count = filtered.Count,
            totalBuffered = matchingEntries.Count,
            filter = filter,
            search = search,
            entries = filtered
        });
    }

    public async Task EnsureConsoleInterceptionAsync()
    {
        if (_consoleInterceptionEnabled) return;

        await RunOnDispatcherAsync(async () =>
        {
            if (_consoleInterceptionEnabled || _browserControl.CoreWebView2 == null) return true;
            try
            {
                await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");

                _consoleReceiver = _browserControl.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
                _consoleReceiver.DevToolsProtocolEventReceived += OnCdpConsoleAPICalled;

                _exceptionReceiver = _browserControl.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown");
                _exceptionReceiver.DevToolsProtocolEventReceived += OnCdpExceptionThrown;

                _currentSourceUrl = _browserControl.CoreWebView2?.Source ?? _browserControl.Source?.ToString() ?? "";
                _browserControl.CoreWebView2!.SourceChanged += (s, e) =>
                {
                    try
                    {
                        _currentSourceUrl = _browserControl.CoreWebView2?.Source ?? "";
                    }
                    catch { }
                };

                _consoleInterceptionEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CoBrowser Console Interception Error] {ex.Message}");
            }
            return true;
        });
    }

    public void CleanupConsoleInterception()
    {
        if (!_consoleInterceptionEnabled) return;
        _consoleInterceptionEnabled = false;

        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (_consoleReceiver != null) _consoleReceiver.DevToolsProtocolEventReceived -= OnCdpConsoleAPICalled;
                if (_exceptionReceiver != null) _exceptionReceiver.DevToolsProtocolEventReceived -= OnCdpExceptionThrown;

                _consoleReceiver = null;
                _exceptionReceiver = null;

                if (_browserControl.CoreWebView2 != null)
                {
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.disable", "{}");
                }
            }
            catch { }
        });
    }

    private void OnCdpConsoleAPICalled(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        string json = e.ParameterObjectAsJson;
        ThreadPool.QueueUserWorkItem(_ => ProcessCdpConsoleAPICalled(json));
    }

    private void ProcessCdpConsoleAPICalled(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "log" : "log";

            var msgParts = new List<string>();
            if (root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
            {
                foreach (var arg in args.EnumerateArray())
                {
                    if (arg.TryGetProperty("value", out var val))
                    {
                        if (val.ValueKind == JsonValueKind.String)
                        {
                            msgParts.Add(val.GetString() ?? "");
                        }
                        else
                        {
                            msgParts.Add(val.GetRawText());
                        }
                    }
                    else if (arg.TryGetProperty("description", out var desc))
                    {
                        msgParts.Add(desc.GetString() ?? "");
                    }
                    else if (arg.TryGetProperty("type", out var at) && at.GetString() == "undefined")
                    {
                        msgParts.Add("undefined");
                    }
                }
            }

            string message = string.Join(" ", msgParts);
            string url = "";
            int line = 0;
            int col = 0;

            if (root.TryGetProperty("stackTrace", out var st) &&
                st.TryGetProperty("callFrames", out var frames) &&
                frames.ValueKind == JsonValueKind.Array &&
                frames.GetArrayLength() > 0)
            {
                var firstFrame = frames[0];
                url = firstFrame.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                line = firstFrame.TryGetProperty("lineNumber", out var l) && l.TryGetInt32(out int pl) ? pl : 0;
                col = firstFrame.TryGetProperty("columnNumber", out var c) && c.TryGetInt32(out int pc) ? pc : 0;
            }

            var entry = new ConsoleMessageEntry
            {
                Type = type,
                Message = message,
                Url = url,
                PageUrl = _currentSourceUrl,
                LineNumber = line,
                ColumnNumber = col,
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };

            lock (_consoleLock)
            {
                _consoleEntries.Add(entry);
                while (_consoleEntries.Count > 200)
                {
                    _consoleEntries.RemoveAt(0);
                }
            }
        }
        catch { }
    }

    private void OnCdpExceptionThrown(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        string json = e.ParameterObjectAsJson;
        ThreadPool.QueueUserWorkItem(_ => ProcessCdpExceptionThrown(json));
    }

    private void ProcessCdpExceptionThrown(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string message = "Uncaught Exception";
            string url = "";
            int line = 0;
            int col = 0;

            if (root.TryGetProperty("exceptionDetails", out var exc))
            {
                if (exc.TryGetProperty("text", out var t) && !string.IsNullOrWhiteSpace(t.GetString()))
                {
                    message = t.GetString()!;
                }
                if (exc.TryGetProperty("exception", out var excObj))
                {
                    if (excObj.TryGetProperty("description", out var d) && !string.IsNullOrWhiteSpace(d.GetString()))
                    {
                        message = d.GetString()!;
                    }
                    else if (excObj.TryGetProperty("value", out var v))
                    {
                        message = v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : v.GetRawText();
                    }
                }
                url = exc.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                line = exc.TryGetProperty("lineNumber", out var l) && l.TryGetInt32(out int pl) ? pl : 0;
                col = exc.TryGetProperty("columnNumber", out var c) && c.TryGetInt32(out int pc) ? pc : 0;
            }

            var entry = new ConsoleMessageEntry
            {
                Type = "exception",
                Message = message,
                Url = url,
                PageUrl = _currentSourceUrl,
                LineNumber = line,
                ColumnNumber = col,
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };

            lock (_consoleLock)
            {
                _consoleEntries.Add(entry);
                while (_consoleEntries.Count > 200)
                {
                    _consoleEntries.RemoveAt(0);
                }
            }
        }
        catch { }
    }

    private async Task HandleGetConsoleAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        await EnsureConsoleInterceptionAsync();

        bool clear = req.QueryString["clear"] == "true";
        if (clear)
        {
            lock (_consoleLock)
            {
                _consoleEntries.Clear();
            }
            await SendJsonResponseAsync(resp, 200, new { success = true, message = "Console buffer cleared" });
            return;
        }

        string filter = req.QueryString["type"] ?? req.QueryString["level"] ?? "all";
        string search = (req.QueryString["search"] ?? "").Trim();
        bool currentUrlOnly = req.QueryString["currentUrlOnly"] == "true" || req.QueryString["current_url_only"] == "true";
        int limit = 50;
        if (int.TryParse(req.QueryString["limit"], out int l) && l > 0) limit = Math.Min(l, 200);

        var filterTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(filter) && filter != "all")
        {
            foreach (var part in filter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                filterTypes.Add(part.Trim());
            }
            if (filterTypes.Contains("warn") || filterTypes.Contains("warning"))
            {
                filterTypes.Add("warn");
                filterTypes.Add("warning");
            }
        }

        List<ConsoleMessageEntry> allEntries;
        lock (_consoleLock)
        {
            allEntries = new List<ConsoleMessageEntry>(_consoleEntries);
        }

        string currentUrl = _currentSourceUrl;
        if (string.IsNullOrEmpty(currentUrl) && _browserControl.CoreWebView2 != null)
        {
            try
            {
                currentUrl = _browserControl.CoreWebView2.Source ?? "";
            }
            catch { }
        }
        string currentHost = "";
        if (Uri.TryCreate(currentUrl, UriKind.Absolute, out var curUri))
        {
            currentHost = curUri.Host;
        }

        if (currentUrlOnly && !string.IsNullOrWhiteSpace(currentHost))
        {
            allEntries = allEntries.Where(e =>
            {
                if (string.IsNullOrWhiteSpace(e.PageUrl)) return true;
                if (Uri.TryCreate(e.PageUrl, UriKind.Absolute, out var entryUri))
                {
                    return entryUri.Host.Equals(currentHost, StringComparison.OrdinalIgnoreCase);
                }
                return e.PageUrl.Contains(currentHost, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        var filtered = allEntries.Where(e =>
        {
            if (filterTypes.Count > 0 && !filterTypes.Contains(e.Type))
            {
                if (filterTypes.Contains("error") && e.Type.Equals("exception", StringComparison.OrdinalIgnoreCase))
                {
                    // Match
                }
                else
                {
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(search))
            {
                bool matchMessage = e.Message.Contains(search, StringComparison.OrdinalIgnoreCase);
                bool matchUrl = e.Url.Contains(search, StringComparison.OrdinalIgnoreCase);
                if (!matchMessage && !matchUrl) return false;
            }

            return true;
        }).TakeLast(limit).ToList();

        int errorCount = allEntries.Count(e => e.Type.Equals("error", StringComparison.OrdinalIgnoreCase) || e.Type.Equals("exception", StringComparison.OrdinalIgnoreCase));
        int warningCount = allEntries.Count(e => e.Type.Equals("warning", StringComparison.OrdinalIgnoreCase) || e.Type.Equals("warn", StringComparison.OrdinalIgnoreCase));

        await SendJsonResponseAsync(resp, 200, new
        {
            success = true,
            currentUrl = currentUrl,
            totalBuffered = allEntries.Count,
            count = filtered.Count,
            errorCount = errorCount,
            warningCount = warningCount,
            filter = filter,
            search = search,
            entries = filtered
        });
    }

    private async Task HandleGetAXTreeAsync(HttpListenerRequest req, HttpListenerResponse resp)
    {
        var sw = Stopwatch.StartNew();
        string convId = ExtractConversationId(req);
        if (!string.IsNullOrEmpty(convId)) UpdateActiveConversationId(convId);

        bool interactiveOnly = req.QueryString["interactiveOnly"] != "false";
        int maxDepth = 10;
        if (int.TryParse(req.QueryString["maxDepth"], out int md) && md > 0) maxDepth = Math.Min(md, 50);

        try
        {
            var result = await RunOnDispatcherAsync(async () =>
            {
                if (_browserControl.CoreWebView2 == null)
                {
                    throw new InvalidOperationException("WebView2 ist noch nicht initialisiert");
                }

                try
                {
                    await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Accessibility.enable", "{}");
                }
                catch { }

                string axParams = maxDepth > 0
                    ? JsonSerializer.Serialize(new { depth = maxDepth })
                    : "{}";
                string cdpRes = await _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Accessibility.getFullAXTree", axParams);
                using var doc = JsonDocument.Parse(cdpRes, LenientJsonOptions);
                var root = doc.RootElement;

                if (!root.TryGetProperty("nodes", out var nodesArray))
                {
                    return (object)new
                    {
                        success = false,
                        error = "Accessibility.getFullAXTree lieferte keine Knoten"
                    };
                }

                string title = _browserControl.CoreWebView2.DocumentTitle ?? "";
                string currentUrl = _browserControl.Source?.ToString() ?? "";

                var relevantRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "button", "link", "textbox", "searchbox", "spinbutton", "combobox", "checkbox", "radio",
                    "switch", "slider", "menuitem", "menuitemcheckbox", "menuitemradio", "tab",
                    "heading", "alert", "dialog", "treeitem", "listbox", "option"
                };

                var formattedNodes = new List<object>();
                var summaryLines = new List<string>();

                foreach (var node in nodesArray.EnumerateArray())
                {
                    bool ignored = node.TryGetProperty("ignored", out var ign) && ign.GetBoolean();
                    if (ignored) continue;

                    string role = "";
                    if (node.TryGetProperty("role", out var rObj) && rObj.TryGetProperty("value", out var rv))
                    {
                        role = rv.GetString() ?? "";
                    }

                    if (string.IsNullOrEmpty(role) || role == "none" || role == "genericContainer") continue;
                    if (interactiveOnly && !relevantRoles.Contains(role)) continue;

                    string name = "";
                    if (node.TryGetProperty("name", out var nObj) && nObj.TryGetProperty("value", out var nv))
                    {
                        name = nv.GetString() ?? "";
                    }

                    string val = "";
                    if (node.TryGetProperty("value", out var vObj) && vObj.TryGetProperty("value", out var vv))
                    {
                        val = vv.GetString() ?? "";
                    }

                    string description = "";
                    if (node.TryGetProperty("description", out var dObj) && dObj.TryGetProperty("value", out var dv))
                    {
                        description = dv.GetString() ?? "";
                    }

                    string nodeId = node.TryGetProperty("nodeId", out var nId) ? nId.GetString() ?? "" : "";

                    var states = new List<string>();
                    if (node.TryGetProperty("properties", out var props))
                    {
                        foreach (var p in props.EnumerateArray())
                        {
                            string pName = p.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "";
                            string pVal = "";
                            if (p.TryGetProperty("value", out var pv) && pv.TryGetProperty("value", out var pvv))
                            {
                                pVal = pvv.ToString() ?? "";
                            }

                            bool isTrue = string.Equals(pVal, "true", StringComparison.OrdinalIgnoreCase);
                            if (pName == "disabled" && isTrue) states.Add("disabled");
                            if (pName == "expanded") states.Add(isTrue ? "aufgeklappt" : "zugeklappt");
                            if (pName == "checked")
                            {
                                if (isTrue) states.Add("aktiviert");
                                else if (string.Equals(pVal, "mixed", StringComparison.OrdinalIgnoreCase)) states.Add("teilweise aktiviert");
                                else states.Add("deaktiviert");
                            }
                            if (pName == "focused" && isTrue) states.Add("fokussiert");
                            if (pName == "required" && isTrue) states.Add("pflichtfeld");
                        }
                    }

                    string stateStr = states.Count > 0 ? $" ({string.Join(", ", states)})" : "";
                    string valStr = !string.IsNullOrWhiteSpace(val) ? $" [Wert: \"{val}\"]" : "";
                    string line = $"• [{role}] \"{name}\"{valStr}{stateStr}";
                    if (!string.IsNullOrWhiteSpace(description)) line += $" - {description}";

                    summaryLines.Add(line);

                    formattedNodes.Add(new
                    {
                        id = nodeId,
                        role = role,
                        name = name,
                        value = val,
                        description = description,
                        states = states
                    });
                }

                return (object)new
                {
                    success = true,
                    title = title,
                    url = currentUrl,
                    nodeCount = formattedNodes.Count,
                    summary = string.Join("\n", summaryLines),
                    nodes = formattedNodes
                };
            });

            bool isSuccess = false;
            string? errorMsg = null;
            int nodeCount = 0;
            try
            {
                if (result != null)
                {
                    var sProp = result.GetType().GetProperty("success");
                    if (sProp?.GetValue(result) is bool b) isSuccess = b;
                    var eProp = result.GetType().GetProperty("error");
                    errorMsg = eProp?.GetValue(result)?.ToString();
                    var nCountProp = result.GetType().GetProperty("nodeCount");
                    if (nCountProp?.GetValue(result) is int n) nodeCount = n;
                }
            }
            catch { }

            RecordFlight("axtree", "GET", _currentSourceUrl, sw.ElapsedMilliseconds, 200, isSuccess, $"nodes={nodeCount}", errorMsg, null, convId);
            await SendJsonResponseAsync(resp, 200, result);
        }
        catch (Exception ex)
        {
            RecordFlight("axtree", "GET", _currentSourceUrl, sw.ElapsedMilliseconds, 500, false, null, ex.Message, null, convId);
            await SendJsonResponseAsync(resp, 500, new { success = false, error = ex.Message });
        }
    }

    #endregion

    #region Hilfsmethoden

    private async Task<string> ExecuteScriptWithCdpAsync(string script, bool awaitPromise = true, int timeoutMs = 10000)
    {
        if (_browserControl.CoreWebView2 == null)
        {
            throw new InvalidOperationException("WebView2 not initialized");
        }

        string trimmed = script.Trim();
        if (trimmed.StartsWith("await ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(6).Trim();
        }

        string evalParams = JsonSerializer.Serialize(new
        {
            expression = trimmed,
            awaitPromise = awaitPromise,
            returnByValue = true
        });

        Task<string> cdpTask;
        try
        {
            cdpTask = _browserControl.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate", evalParams).AsTask();
        }
        catch (System.Runtime.InteropServices.COMException comEx) when ((uint)comEx.HResult == 0x80070057)
        {
            throw new InvalidOperationException("CoreWebView2 ungültiger Zustand / Navigation aktiv (E_INVALIDARG): " + comEx.Message, comEx);
        }

        var completed = await Task.WhenAny(cdpTask, Task.Delay(timeoutMs));
        if (completed != cdpTask)
        {
            throw new TimeoutException($"Runtime.evaluate Ausführung überschritt Timeout von {timeoutMs}ms");
        }

        string cdpResponse;
        try
        {
            cdpResponse = await cdpTask;
        }
        catch (System.Runtime.InteropServices.COMException comEx) when ((uint)comEx.HResult == 0x80070057)
        {
            throw new InvalidOperationException("CoreWebView2 ungültiger Zustand / Navigation aktiv (E_INVALIDARG): " + comEx.Message, comEx);
        }

        using var doc = JsonDocument.Parse(cdpResponse, LenientJsonOptions);
        var root = doc.RootElement;

        if (root.TryGetProperty("exceptionDetails", out var exc))
        {
            string excMsg = "JavaScript error";
            if (exc.TryGetProperty("exception", out var excObj) && excObj.TryGetProperty("description", out var desc) && !string.IsNullOrWhiteSpace(desc.GetString()))
            {
                excMsg = desc.GetString()!;
            }
            else if (exc.TryGetProperty("text", out var t) && !string.IsNullOrWhiteSpace(t.GetString()))
            {
                excMsg = t.GetString()!;
            }
            throw new InvalidOperationException(excMsg);
        }

        if (root.TryGetProperty("result", out var res))
        {
            if (res.TryGetProperty("value", out var val))
            {
                if (val.ValueKind == JsonValueKind.String)
                {
                    return val.GetString() ?? "";
                }
                return val.GetRawText();
            }
            if (res.TryGetProperty("description", out var d))
            {
                return d.GetString() ?? "";
            }
        }

        return "null";
    }

    private static object ParseJsResult(string raw)
    {
        if (raw == null)
        {
            return new { success = false, error = "Script returned null response" };
        }
        if (raw == "null")
        {
            return new { success = true, result = (object?)null, raw = "null" };
        }
        if (raw == "\"\"" || raw == "")
        {
            return new { success = true, result = (object?)"", raw = "" };
        }
        try
        {
            string unescaped = raw;
            if (raw.StartsWith("\"") && raw.EndsWith("\"") && raw.Length >= 2)
            {
                unescaped = JsonSerializer.Deserialize<string>(raw) ?? raw;
            }
            using var doc = JsonDocument.Parse(unescaped, LenientJsonOptions);
            return new { success = true, result = doc.RootElement.Clone(), raw = unescaped };
        }
        catch
        {
            return new { success = true, result = (object?)raw, raw = raw };
        }
    }

    private Task<T> RunOnDispatcherAsync<T>(Func<Task<T>> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var result = await func();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        if (!queued)
        {
            tcs.SetException(new InvalidOperationException("DispatcherQueue konnte Aufgabe nicht einreihen"));
        }

        return tcs.Task;
    }

    private async Task<string> ReadBodyAsStringAsync(HttpListenerRequest req)
    {
        if (!req.HasEntityBody) return "{}";
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private async Task SendJsonResponseAsync(HttpListenerResponse resp, int statusCode, object? data)
    {
        try
        {
            resp.StatusCode = statusCode;
            resp.ContentType = "application/json; charset=utf-8";
            object payload = data ?? new { success = statusCode < 400 };
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes);
        }
        catch
        {
            // Socket disconnected or write failed
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    #endregion
}

