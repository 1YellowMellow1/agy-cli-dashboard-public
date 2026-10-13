using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Windows.System;

namespace AgyCliDashboard.Views;

public sealed class CoBrowserOpenEventArgs : EventArgs
{
    public string? Url { get; }
    public string? ConversationId { get; }

    public CoBrowserOpenEventArgs(string? url = null, string? conversationId = null)
    {
        Url = url;
        ConversationId = conversationId;
    }
}

public sealed partial class CoBrowserView : UserControl, IDisposable
{
    public event EventHandler? CloseRequested;
    public event EventHandler? PopOutRequested;
    public event EventHandler<CoBrowserOpenEventArgs>? OpenRequested;
    public event Func<bool>? IsBrowserOpenQuery;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? AssociatedChatClicked;
    public event EventHandler<string>? ConversationActive;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private volatile bool _isDisposed = false;
    private volatile bool _isTakeoverActive = false;
    private bool _isInitialized = false;
    private string _defaultHomeUrl = "https://www.google.de";
    private Services.CoBrowserHttpServer? _httpServer;

    private string? _associatedChatTitle;
    private string? _associatedConversationId;
    public string? AssociatedConversationId => _associatedConversationId;
    public string? AssociatedChatTitle => _associatedChatTitle;

    private static CoreWebView2Environment? s_sharedEnvironment;
    private static readonly SemaphoreSlim s_sharedEnvLock = new(1, 1);
    private static int s_sharedCdpPort = 9222;

    private string _currentStatusText = "Bereit";
    public string CurrentStatusText => _currentStatusText;

    public bool IsPopOutMode
    {
        get => MenuItemPopOut?.Visibility == Visibility.Collapsed;
        set
        {
            if (MenuItemPopOut != null)
            {
                MenuItemPopOut.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    public string? CurrentUrl
    {
        get
        {
            try
            {
                if (BrowserControl?.Source != null)
                {
                    string src = BrowserControl.Source.ToString();
                    if (!string.IsNullOrWhiteSpace(src) && src != "about:blank")
                    {
                        return src;
                    }
                }
            }
            catch { }
            return !string.IsNullOrWhiteSpace(TxtUrl?.Text) ? TxtUrl.Text : null;
        }
    }

    public int CdpPort { get; private set; } = 9222;
    public int ApiPort => _httpServer?.Port ?? 9223;
    public bool IsInitialized => _isInitialized;

    public const double DefaultZoomFactor = 0.85; // 85% Baseline für normale responsive Seiten
    private double _currentZoomFactor = DefaultZoomFactor;
    private bool _isAutoFitEnabled = true;
    public bool IsAutoFitEnabled
    {
        get => _isAutoFitEnabled;
        set
        {
            _isAutoFitEnabled = value;
            if (MenuItemAutoFitZoom != null) MenuItemAutoFitZoom.IsChecked = value;
            UpdateZoomUi();
            if (value) _ = ApplyAutoFitZoomAsync();
        }
    }

    public double CurrentZoomFactor
    {
        get => _currentZoomFactor;
        set => _ = ApplyZoomAsync(value, isManual: true);
    }

    public CoBrowserView()
    {
        InitializeComponent();
        UpdateZoomUi();
    }

    /// <summary>
    /// Initialisiert die WebView2-Instanz asynchron (Lazy-Loading) mit konfiguriertem Remote-Debugging-Port.
    /// </summary>
    public async Task<bool> EnsureInitializedAsync(string? initialUrl = null)
    {
        if (_isDisposed) return false;
        await _initLock.WaitAsync();
        try
        {
            if (_isDisposed) return false;
            if (_isInitialized)
            {
                if (!string.IsNullOrWhiteSpace(initialUrl))
                {
                    Navigate(initialUrl);
                }
                return true;
            }

            PnlPlaceholder.Visibility = Visibility.Visible;
            ProgressBarLoading.Visibility = Visibility.Visible;
            if (RingPlaceholder != null)
            {
                RingPlaceholder.IsActive = true;
                RingPlaceholder.Visibility = Visibility.Visible;
            }
            if (TxtPlaceholderTitle != null)
            {
                TxtPlaceholderTitle.Text = "KI-Co-Browser initialisiert...";
            }
            if (TxtPlaceholderSubtitle != null)
            {
                TxtPlaceholderSubtitle.Text = "Wird mit Remote-Debugging-Port verbunden...";
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appSubDir = AppDomain.CurrentDomain.BaseDirectory.Contains("-public", StringComparison.OrdinalIgnoreCase)
                ? "AgyCliDashboard-Public"
                : "AgyCliDashboard";
            string userDataFolder = Path.Combine(localAppData, appSubDir, "CoBrowserProfile");
            Directory.CreateDirectory(userDataFolder);

            // 1. Gemeinsames CoreWebView2Environment threadsicher abrufen oder einmalig erstellen
            await s_sharedEnvLock.WaitAsync();
            try
            {
                if (s_sharedEnvironment == null)
                {
                    s_sharedCdpPort = FindAvailablePort(9222);
                    var options = new CoreWebView2EnvironmentOptions
                    {
                        AdditionalBrowserArguments = $"--remote-debugging-port={s_sharedCdpPort}"
                    };
                    try
                    {
                        s_sharedEnvironment = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, options);
                    }
                    catch (Exception exEnv)
                    {
                        TelemetryLogger.Log($"[CoBrowser] CreateWithOptionsAsync failed on '{userDataFolder}': {exEnv.Message}. Falling back to instance folder.");
                        string fallbackFolder = Path.Combine(userDataFolder, $"Instance_{Environment.ProcessId}");
                        Directory.CreateDirectory(fallbackFolder);
                        s_sharedEnvironment = await CoreWebView2Environment.CreateWithOptionsAsync(null, fallbackFolder, options);
                    }
                }
            }
            finally
            {
                s_sharedEnvLock.Release();
            }

            CdpPort = s_sharedCdpPort;
            UpdateChatBadgeTooltip();

            // 2. WebView2 an das geteilte Environment anbinden
            await BrowserControl.EnsureCoreWebView2Async(s_sharedEnvironment);

            if (BrowserControl?.CoreWebView2 == null)
            {
                TelemetryLogger.Log("[CoBrowser] CoreWebView2 ist nach EnsureCoreWebView2Async null.");
                return false;
            }

            // 3. Events registrieren
            BrowserControl.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            BrowserControl.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            BrowserControl.CoreWebView2.DOMContentLoaded += CoreWebView2_DOMContentLoaded;
            BrowserControl.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
            BrowserControl.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
            BrowserControl.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            BrowserControl.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            // Globales Zoom-Skript vorab registrieren (injiziert persistentes <style id="agy-zoom-style">, fängt Ctrl +/- ab und schützt gegen SPA-Hydration)
            await BrowserControl.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                (function() {
                    function applyZoomStyle(pct) {
                        try {
                            window.__agyCurrentZoomPct = pct;
                            let style = document.getElementById('agy-zoom-style');
                            if (!style) {
                                style = document.createElement('style');
                                style.id = 'agy-zoom-style';
                                (document.head || document.documentElement).appendChild(style);
                            }
                            style.textContent = 'html { zoom: ' + pct + '% !important; }';
                            if (document.documentElement) {
                                document.documentElement.style.zoom = pct + '%';
                            }
                        } catch(e) {}
                    }

                    window.__agyApplyZoom = applyZoomStyle;
                    applyZoomStyle(85);

                    function ensureObserver() {
                        try {
                            let target = document.head || document.documentElement;
                            if (target && !window.__agyZoomObsInstalled) {
                                window.__agyZoomObsInstalled = true;
                                let obs = new MutationObserver(function() {
                                    let style = document.getElementById('agy-zoom-style');
                                    let cur = window.__agyCurrentZoomPct || 85;
                                    let expected = 'html { zoom: ' + cur + '% !important; }';
                                    if (!style || style.textContent !== expected) {
                                        applyZoomStyle(cur);
                                    }
                                });
                                obs.observe(target, { childList: true, subtree: false });
                                if (document.documentElement) {
                                    obs.observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });
                                }
                            }
                        } catch(e) {}
                    }

                    ensureObserver();
                    if (document.readyState === 'loading') {
                        document.addEventListener('DOMContentLoaded', function() {
                            let p = window.__agyCurrentZoomPct || 85;
                            applyZoomStyle(p);
                            ensureObserver();
                        });
                    }

                    window.addEventListener('keydown', function(e) {
                        if (e.ctrlKey) {
                            if (e.key === '-' || e.key === '_') {
                                window.chrome.webview.postMessage(JSON.stringify({ type: 'zoomOut' }));
                                e.preventDefault();
                            } else if (e.key === '+' || e.key === '=') {
                                window.chrome.webview.postMessage(JSON.stringify({ type: 'zoomIn' }));
                                e.preventDefault();
                            } else if (e.key === '0') {
                                window.chrome.webview.postMessage(JSON.stringify({ type: 'zoomReset' }));
                                e.preventDefault();
                            }
                        }
                    }, { capture: true });
                })();
            ");

            _isInitialized = true;
            PnlPlaceholder.Visibility = Visibility.Collapsed;
            ProgressBarLoading.Visibility = Visibility.Collapsed;

            // 4. Signal-Watcher für CLI-Kommunikation starten
            StartSignalWatcher(userDataFolder);

            // 5. Ultra-Fast HTTP-RPC Server starten
            _httpServer = new Services.CoBrowserHttpServer(
                BrowserControl,
                DispatcherQueue,
                status => SetAgentControlling(true, status),
                reason => SetWaitingForUser(reason),
                () => SetReady(),
                () => _currentStatusText,
                isTakeoverActiveFunc: () => _isTakeoverActive,
                requestOpenAction: (url, convId) => OpenRequested?.Invoke(this, new CoBrowserOpenEventArgs(url, convId)),
                requestCloseAction: () => CloseRequested?.Invoke(this, EventArgs.Empty),
                isBrowserOpenFunc: () => IsBrowserOpenQuery?.Invoke() ?? false,
                onConversationActiveAction: convId => ConversationActive?.Invoke(this, convId),
                getZoomFunc: () => _currentZoomFactor,
                setZoomAction: factor =>
                {
                    if (factor <= 0)
                    {
                        _isAutoFitEnabled = true;
                        _ = ApplyAutoFitZoomAsync();
                    }
                    else
                    {
                        _ = ApplyZoomAsync(factor, isManual: true);
                    }
                },
                startingPort: 9223)
            {
                CdpPort = CdpPort
            };
            _httpServer.Start();
            _ = _httpServer.EnsureNetworkInterceptionAsync();
            _ = _httpServer.EnsureConsoleInterceptionAsync();
            UpdateChatBadgeTooltip();

            // 6. Initiale Navigation
            string targetUrl = string.IsNullOrWhiteSpace(initialUrl) ? _defaultHomeUrl : initialUrl;
            Navigate(targetUrl);

            SetReady();
            return true;
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                ProgressBarLoading.Visibility = Visibility.Collapsed;
                _currentStatusText = "Fehler";
                DotStatus.Fill = (SolidColorBrush)Application.Current.Resources["AgyStatusDanger"];
                ToolTipService.SetToolTip(BdrStatusBadge, $"Status: Fehler ({ex.Message})");

                if (RingPlaceholder != null)
                {
                    RingPlaceholder.IsActive = false;
                    RingPlaceholder.Visibility = Visibility.Collapsed;
                }
                if (TxtPlaceholderTitle != null)
                {
                    TxtPlaceholderTitle.Text = "Initialisierungsfehler";
                }
                if (TxtPlaceholderSubtitle != null)
                {
                    TxtPlaceholderSubtitle.Text = ex.Message;
                }
            }
            System.Diagnostics.Debug.WriteLine($"[CoBrowserView] Init Error: {ex.Message}");
            return false;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private int FindAvailablePort(int startingPort)
    {
        for (int port = startingPort; port < startingPort + 50; port++)
        {
            try
            {
                var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException)
            {
                // Port belegt, nächsten versuchen
            }
        }
        return startingPort;
    }

    public void Navigate(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        string target = url.Trim();
        if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) &&
            !target.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            if (target.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                target = "http://" + target;
            }
            else if (target.Contains('.') && !target.Contains(' '))
            {
                target = "https://" + target;
            }
            else
            {
                target = "https://www.google.com/search?q=" + Uri.EscapeDataString(target);
            }
        }

        try
        {
            TxtUrl.Text = target;
            BrowserControl.Source = new Uri(target);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoBrowserView] Navigate Error: {ex.Message}");
        }
    }

    public async Task<string> ExecuteScriptAsync(string javascript)
    {
        if (!_isInitialized || BrowserControl.CoreWebView2 == null) return string.Empty;
        try
        {
            return await BrowserControl.ExecuteScriptAsync(javascript);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    #region Status & Handoff Management

    public void SetReady()
    {
        _isTakeoverActive = false;
        _currentStatusText = "Bereit";
        if (PnlSpeechBubble != null)
        {
            PnlSpeechBubble.Visibility = Visibility.Collapsed;
        }
        BdrStatusBadge.Opacity = 1.0;
        DotStatus.Fill = (SolidColorBrush)Application.Current.Resources["AgyStatusSuccess"];
        BdrStatusBadge.Background = (SolidColorBrush)Application.Current.Resources["AgySurfaceSubtle"];
        BdrStatusBadge.BorderBrush = (SolidColorBrush)Application.Current.Resources["AgyBorderFaint"];
        ToolTipService.SetToolTip(BdrStatusBadge, Helpers.Loc.Tr("CoBrowser.StatusReady", "Status: Bereit (Klick: Hinweis-Sprechblase)"));
        StatusChanged?.Invoke(this, "ready");
    }

    public void SetAgentControlling(bool isControlling, string? detail = null)
    {
        _isTakeoverActive = false;
        if (PnlSpeechBubble != null)
        {
            PnlSpeechBubble.Visibility = Visibility.Collapsed;
        }
        if (isControlling)
        {
            _currentStatusText = string.IsNullOrEmpty(detail) ? "🤖 KI steuert..." : $"🤖 {detail}";
            DotStatus.Fill = (SolidColorBrush)Application.Current.Resources["AgyCategoryCyan"];
            BdrStatusBadge.Background = (SolidColorBrush)Application.Current.Resources["AgyAccentSubtle"];
            BdrStatusBadge.BorderBrush = (SolidColorBrush)Application.Current.Resources["AgyCategoryCyan"];
            ToolTipService.SetToolTip(BdrStatusBadge, _currentStatusText);
            StatusChanged?.Invoke(this, "controlling");
        }
        else
        {
            SetReady();
        }
    }

    public void SetWaitingForUser(string reason)
    {
        _isTakeoverActive = true;
        _currentStatusText = "👤 Du bist dran!";
        var warningBrush = (SolidColorBrush)Application.Current.Resources["AgyStatusWarning"];
        DotStatus.Fill = warningBrush;
        BdrStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 245, 158, 11));
        BdrStatusBadge.BorderBrush = warningBrush;
        BdrStatusBadge.Opacity = 1.0;

        string displayMsg = string.IsNullOrWhiteSpace(reason)
            ? "Ich benötige kurz deine Unterstützung im Browser (z. B. Login oder Captcha)."
            : reason.Trim();

        if (TxtSpeechBubbleMessage != null)
        {
            TxtSpeechBubbleMessage.Text = displayMsg;
        }
        if (PnlSpeechBubble != null)
        {
            PnlSpeechBubble.Visibility = Visibility.Visible;
        }

        ToolTipService.SetToolTip(BdrStatusBadge, $"Antigravity bittet um Aktion:\n{displayMsg}\n(Klicken, um Sprechblase umzuschalten)");
        StatusChanged?.Invoke(this, $"waiting:{reason}");
    }

    public void UpdateAssociatedChat(string? chatTitle, string? conversationId = null)
    {
        if (DispatcherQueue == null) return;
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateAssociatedChat(chatTitle, conversationId));
            return;
        }

        string normalizedTitle = !string.IsNullOrWhiteSpace(chatTitle) ? chatTitle.Trim() : "Kein Chat verbunden";
        string? normalizedId = !string.IsNullOrWhiteSpace(conversationId) ? conversationId.Trim() : null;

        // Idempotenz-Check: Keine unnötigen UI- oder Tooltip-Neuberechnungen bei identischen Daten
        if (_associatedChatTitle == normalizedTitle && _associatedConversationId == normalizedId && MenuItemAssociatedChat?.Text == $"Chat: {normalizedTitle}")
        {
            return;
        }

        _associatedChatTitle = normalizedTitle;
        _associatedConversationId = normalizedId;

        if (MenuItemAssociatedChat != null)
        {
            MenuItemAssociatedChat.Text = $"Chat: {normalizedTitle}";
        }

        UpdateChatBadgeTooltip();
    }

    public void UpdateChatBadgeTooltip()
    {
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateChatBadgeTooltip());
            return;
        }

        string displayTitle = !string.IsNullOrWhiteSpace(_associatedChatTitle) ? _associatedChatTitle : "Kein Chat verbunden";
        string idInfo = !string.IsNullOrWhiteSpace(_associatedConversationId) ? _associatedConversationId : "Nicht verknüpft";
        int apiPort = _httpServer?.Port ?? 0;
        string tooltip = $"Zugehöriger Chat: {displayTitle}\nConversation-ID: {idInfo}\n(Klicken, um zum Chat-Tab zu wechseln)\nCDP: {CdpPort} | API: {apiPort}";
        if (MenuItemAssociatedChat != null)
        {
            ToolTipService.SetToolTip(MenuItemAssociatedChat, tooltip);
        }
    }

    private void MenuItemAssociatedChat_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AssociatedChatClicked?.Invoke(this, _associatedConversationId ?? "");
        }
        catch { }
    }

    private void BdrStatusBadge_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (BdrStatusBadge != null)
        {
            BdrStatusBadge.BorderBrush = (SolidColorBrush)Application.Current.Resources["AgyAccent"];
        }
    }

    private void BdrStatusBadge_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (BdrStatusBadge != null)
        {
            if (_isTakeoverActive)
            {
                BdrStatusBadge.BorderBrush = (SolidColorBrush)Application.Current.Resources["AgyStatusWarning"];
            }
            else if (_currentStatusText.StartsWith("🤖"))
            {
                BdrStatusBadge.BorderBrush = (SolidColorBrush)Application.Current.Resources["AgyCategoryCyan"];
            }
            else
            {
                BdrStatusBadge.BorderBrush = (SolidColorBrush)Application.Current.Resources["AgyBorderFaint"];
            }
        }
    }

    public void ToggleOverlay()
    {
        if (_httpServer != null)
        {
            _httpServer.IsOverlayActive = false;
            _ = ExecuteScriptAsync("(function() { var old = document.getElementById('agy-cobrowser-badges-root'); if (old) old.remove(); })()");
        }
    }

    #endregion

    #region Event Handlers

    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        ProgressBarLoading.Visibility = Visibility.Visible;
        if (!string.IsNullOrEmpty(e.Uri))
        {
            TxtUrl.Text = e.Uri;
        }
    }

    private void CoreWebView2_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        ProgressBarLoading.Visibility = Visibility.Collapsed;
        UpdateNavButtons();
        TriggerAutoOrManualZoom();
    }

    private void CoreWebView2_DOMContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
    {
        TriggerAutoOrManualZoom();
    }

    private void TriggerAutoOrManualZoom()
    {
        if (_isAutoFitEnabled)
        {
            _ = ApplyAutoFitZoomAsync();
            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                if (!_isDisposed && DispatcherQueue != null)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_isAutoFitEnabled)
                        {
                            _ = ApplyAutoFitZoomAsync();
                        }
                    });
                }
            });
        }
        else
        {
            _ = ApplyZoomAsync(_currentZoomFactor, isManual: false);
        }
    }

    private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (BrowserControl.Source != null)
        {
            TxtUrl.Text = BrowserControl.Source.ToString();
        }
        UpdateNavButtons();
    }

    private void CoreWebView2_HistoryChanged(object? sender, object e)
    {
        UpdateNavButtons();
    }

    private async void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            // Wenn s_sharedEnvironment verfügbar ist, erzeugen wir ein dediziertes Popup-Fenster (z.B. für Google, Apple, GitHub OAuth)
            if (s_sharedEnvironment != null)
            {
                var popupWin = new Window();
                string host = "";
                try
                {
                    if (!string.IsNullOrWhiteSpace(e.Uri) && Uri.TryCreate(e.Uri, UriKind.Absolute, out var u))
                    {
                        host = u.Host;
                    }
                }
                catch { }

                popupWin.Title = !string.IsNullOrWhiteSpace(host) ? $"Anmeldung – {host}" : "Anmeldung";

                if (popupWin.AppWindow?.TitleBar != null)
                {
                    popupWin.AppWindow.TitleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 24, 25, 27);
                    popupWin.AppWindow.TitleBar.ForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    popupWin.AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 24, 25, 27);
                    popupWin.AppWindow.TitleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                }

                int width = (e.WindowFeatures != null && e.WindowFeatures.HasSize && e.WindowFeatures.Width > 0)
                    ? (int)e.WindowFeatures.Width
                    : 560;
                int height = (e.WindowFeatures != null && e.WindowFeatures.HasSize && e.WindowFeatures.Height > 0)
                    ? (int)e.WindowFeatures.Height
                    : 680;

                width = Math.Clamp(width, 450, 950);
                height = Math.Clamp(height, 550, 1000);

                if (popupWin.AppWindow != null)
                {
                    popupWin.AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
                }

                var popupGrid = new Grid
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 25, 27))
                };
                var popupWebView = new WebView2
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                popupGrid.Children.Add(popupWebView);
                popupWin.Content = popupGrid;

                popupWin.Closed += (s, args) =>
                {
                    try
                    {
                        popupGrid.Children.Clear();
                        popupWebView.Close();
                    }
                    catch { }
                };

                await popupWebView.EnsureCoreWebView2Async(s_sharedEnvironment);

                if (popupWebView.CoreWebView2 != null)
                {
                    popupWebView.CoreWebView2.WindowCloseRequested += (s, args) =>
                    {
                        try
                        {
                            popupWin.Close();
                        }
                        catch { }
                    };

                    popupWebView.CoreWebView2.NewWindowRequested += (s, args) =>
                    {
                        args.Handled = true;
                        if (!string.IsNullOrEmpty(args.Uri))
                        {
                            popupWebView.CoreWebView2.Navigate(args.Uri);
                        }
                    };

                    e.NewWindow = popupWebView.CoreWebView2;
                    e.Handled = true;
                    deferral.Complete();

                    popupWin.Activate();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CoBrowser] Fehler bei NewWindowRequested Popup: {ex.Message}");
        }

        // Fallback: Wenn Popup-Fenster nicht erzeugt werden konnte, im bestehenden Fenster navigieren
        try
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(e.Uri))
            {
                Navigate(e.Uri);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void UpdateNavButtons()
    {
        if (MenuItemBack != null) MenuItemBack.IsEnabled = BrowserControl?.CanGoBack == true;
        if (MenuItemForward != null) MenuItemForward.IsEnabled = BrowserControl?.CanGoForward == true;
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserControl.CanGoBack) BrowserControl.GoBack();
    }

    private void BtnForward_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserControl.CanGoForward) BrowserControl.GoForward();
    }

    private void BtnReload_Click(object sender, RoutedEventArgs e)
    {
        BrowserControl.Reload();
    }

    private void BtnHome_Click(object sender, RoutedEventArgs e)
    {
        Navigate(_defaultHomeUrl);
    }

    private void TxtUrl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            Navigate(TxtUrl.Text);
            e.Handled = true;
        }
    }


    private void BtnPopOut_Click(object sender, RoutedEventArgs e)
    {
        PopOutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BdrStatusBadge_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (_isTakeoverActive)
        {
            e.Handled = true;
            if (PnlSpeechBubble != null)
            {
                PnlSpeechBubble.Visibility = PnlSpeechBubble.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }
    }

    private void BtnCloseSpeechBubble_Click(object sender, RoutedEventArgs e)
    {
        if (PnlSpeechBubble != null)
        {
            PnlSpeechBubble.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnSpeechBubbleDone_Click(object sender, RoutedEventArgs e)
    {
        SetReady();
    }

    #endregion

    #region CLI Signal Watcher

    private FileSystemWatcher? _signalWatcher;
    private readonly object _signalLock = new();

    private void StartSignalWatcher(string userDataFolder)
    {
        try
        {
            if (_signalWatcher != null) return;
            string signalPath = Path.Combine(userDataFolder, "co_browser_signal.json");

            _signalWatcher = new FileSystemWatcher(userDataFolder, "co_browser_signal.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _signalWatcher.Changed += (s, e) => ProcessSignalFile(signalPath);
            _signalWatcher.Created += (s, e) => ProcessSignalFile(signalPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SignalWatcher Error] {ex.Message}");
        }
    }

    private void ProcessSignalFile(string path)
    {
        lock (_signalLock)
        {
            if (_signalWatcher == null) return;
        }

        Task.Delay(50).ContinueWith(_ =>
        {
            try
            {
                if (!File.Exists(path)) return;
                string json;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    json = sr.ReadToEnd();
                }

                if (string.IsNullOrWhiteSpace(json)) return;

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("action", out var actionProp))
                {
                    string action = actionProp.GetString() ?? "";
                    string detail = root.TryGetProperty("detail", out var d) ? (d.GetString() ?? "") : "";
                    string? sigConv = root.TryGetProperty("conversationId", out var cProp) ? cProp.GetString() : (root.TryGetProperty("convId", out cProp) ? cProp.GetString() : null);

                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        if (action == "controlling")
                        {
                            SetAgentControlling(true, detail);
                        }
                        else if (action == "waiting_for_user")
                        {
                            SetWaitingForUser(detail);
                        }
                        else if (action == "ready")
                        {
                            SetReady();
                        }
                        else if (action == "open" || action == "show")
                        {
                            OpenRequested?.Invoke(this, new CoBrowserOpenEventArgs(string.IsNullOrWhiteSpace(detail) ? null : detail, sigConv));
                        }
                        else if (action == "close" || action == "hide")
                        {
                            CloseRequested?.Invoke(this, EventArgs.Empty);
                        }
                        else if (action == "navigate" && !string.IsNullOrWhiteSpace(detail))
                        {
                            OpenRequested?.Invoke(this, new CoBrowserOpenEventArgs(null, sigConv));
                            Navigate(detail);
                        }
                    });
                }
            }
            catch { }
        });
    }

    public void SaveEndpointDiscovery()
    {
        _httpServer?.SaveEndpointDiscovery();
    }

    public void Dispose()
    {
        _isDisposed = true;
        _httpServer?.Dispose();
        _httpServer = null;

        if (_resizeDebounceTimer != null)
        {
            _resizeDebounceTimer.Stop();
            _resizeDebounceTimer = null;
        }

        lock (_signalLock)
        {
            if (_signalWatcher != null)
            {
                try
                {
                    _signalWatcher.EnableRaisingEvents = false;
                    _signalWatcher.Dispose();
                }
                catch { }
                _signalWatcher = null;
            }
        }

        try
        {
            BrowserControl?.Close();
        }
        catch { }
    }

    #endregion

    #region Zoom Management

    private DispatcherTimer? _resizeDebounceTimer;
    private double _lastAutoFitWidth = 0;

    private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isAutoFitEnabled || !_isInitialized || BrowserControl?.CoreWebView2 == null) return;
        if (Math.Abs(e.NewSize.Width - _lastAutoFitWidth) < 6) return;

        if (_resizeDebounceTimer == null)
        {
            _resizeDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _resizeDebounceTimer.Tick += (s, args) =>
            {
                (s as DispatcherTimer)?.Stop();
                if (_isAutoFitEnabled && !_isDisposed)
                {
                    _lastAutoFitWidth = this.ActualWidth;
                    _ = ApplyAutoFitZoomAsync();
                }
            };
        }
        _resizeDebounceTimer.Stop();
        _resizeDebounceTimer.Start();
    }

    public async Task ApplyAutoFitZoomAsync()
    {
        if (!_isAutoFitEnabled || !_isInitialized || BrowserControl?.CoreWebView2 == null) return;

        try
        {
            string measureScript = @"
                (function() {
                    try {
                        let style = document.getElementById('agy-zoom-style');
                        let savedStyle = style ? style.textContent : null;
                        let savedInline = document.documentElement.style.zoom;

                        // Synchron auf 100% zur echten unskalierten Breitenmessung
                        if (style) style.textContent = '';
                        document.documentElement.style.zoom = '1';

                        let clientW = document.documentElement.clientWidth || window.innerWidth || 600;
                        let docW = document.documentElement.scrollWidth || 0;
                        let bodyW = document.body ? (document.body.scrollWidth || 0) : 0;
                        let trueContentW = Math.max(docW, bodyW);

                        // Vor dem Frame-Paint sofort wiederherstellen (kein visueller Flicker)
                        if (style && savedStyle !== null) style.textContent = savedStyle;
                        if (savedInline) document.documentElement.style.zoom = savedInline;

                        const baseline = 0.85; // Standard-Lesegröße für responsive Seiten
                        if (trueContentW > clientW + 10) {
                            let needed = (clientW - 6) / trueContentW;
                            return Math.min(baseline, Math.max(0.50, Math.round(needed * 100) / 100));
                        }
                        return baseline;
                    } catch(e) {
                        return 0.85;
                    }
                })();
            ";

            string resultJson = await BrowserControl.CoreWebView2.ExecuteScriptAsync(measureScript);
            if (double.TryParse(resultJson, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double targetFactor))
            {
                if (targetFactor >= 0.25 && targetFactor <= 3.0)
                {
                    _currentZoomFactor = targetFactor;
                    await ApplyZoomAsync(targetFactor, isManual: false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoBrowserView] ApplyAutoFitZoom Error: {ex.Message}");
        }
    }

    public async Task ApplyZoomAsync(double? zoomFactor = null, bool isManual = true)
    {
        if (isManual)
        {
            _isAutoFitEnabled = false;
        }

        if (zoomFactor.HasValue)
        {
            _currentZoomFactor = Math.Clamp(zoomFactor.Value, 0.25, 3.0);
        }

        UpdateZoomUi();

        if (!_isInitialized || BrowserControl?.CoreWebView2 == null) return;

        try
        {
            int percent = (int)Math.Round(_currentZoomFactor * 100);
            string script = $@"
                (function() {{
                    try {{
                        window.__agyCurrentZoomPct = {percent};
                        let style = document.getElementById('agy-zoom-style');
                        if (!style) {{
                            style = document.createElement('style');
                            style.id = 'agy-zoom-style';
                            (document.head || document.documentElement).appendChild(style);
                        }}
                        style.textContent = 'html {{ zoom: {percent}% !important; }}';
                        if (document.documentElement) {{
                            document.documentElement.style.zoom = '{percent}%';
                        }}
                    }} catch(e) {{}}
                }})();
            ";
            await BrowserControl.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CoBrowserView] ApplyZoom Error: {ex.Message}");
        }
    }

    private void UpdateZoomUi()
    {
        int percent = (int)Math.Round(_currentZoomFactor * 100);
        if (MenuItemAutoFitZoom != null)
        {
            MenuItemAutoFitZoom.IsChecked = _isAutoFitEnabled;
        }
        if (MenuSubZoomLevels != null)
        {
            MenuSubZoomLevels.Text = _isAutoFitEnabled
                ? $"Feste Zoom-Stufe (Auto-Fit: {percent}%)"
                : $"Feste Zoom-Stufe (Aktuell: {percent}%)";
        }
        if (MenuItemZoomReset != null)
        {
            MenuItemZoomReset.Text = _isAutoFitEnabled
                ? "Zoom auf 100% (Original)"
                : "Auto-Fit wieder aktivieren";
        }
        if (BtnMoreActions != null)
        {
            string mode = _isAutoFitEnabled ? "Auto-Fit" : "Manuell";
            ToolTipService.SetToolTip(BtnMoreActions, $"Menü, Navigation & Zoom ({percent}%, {mode})");
        }
    }

    public void ZoomIn()
    {
        double next = Math.Round((_currentZoomFactor + 0.1) * 10) / 10.0;
        _ = ApplyZoomAsync(next, isManual: true);
    }

    public void ZoomOut()
    {
        double next = Math.Round((_currentZoomFactor - 0.1) * 10) / 10.0;
        _ = ApplyZoomAsync(next, isManual: true);
    }

    public void ResetZoom(double target = DefaultZoomFactor)
    {
        _isAutoFitEnabled = true;
        _ = ApplyAutoFitZoomAsync();
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();
    private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();

    private void MenuItemAutoFitZoom_Click(object sender, RoutedEventArgs e)
    {
        _isAutoFitEnabled = MenuItemAutoFitZoom?.IsChecked ?? true;
        if (_isAutoFitEnabled)
        {
            _ = ApplyAutoFitZoomAsync();
        }
        else
        {
            _ = ApplyZoomAsync(DefaultZoomFactor, isManual: true);
        }
    }

    private void BtnZoomReset_Click(object sender, RoutedEventArgs e)
    {
        if (_isAutoFitEnabled)
        {
            _isAutoFitEnabled = false;
            _ = ApplyZoomAsync(1.0, isManual: true);
        }
        else
        {
            _isAutoFitEnabled = true;
            _ = ApplyAutoFitZoomAsync();
        }
    }

    private void MenuZoomLevel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string tagStr &&
            double.TryParse(tagStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double factor))
        {
            _ = ApplyZoomAsync(factor, isManual: true);
        }
    }

    private void UserControl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        bool isCtrlDown = ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (isCtrlDown)
        {
            if (e.Key == Windows.System.VirtualKey.Subtract || (int)e.Key == 189)
            {
                ZoomOut();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Add || (int)e.Key == 187)
            {
                ZoomIn();
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Number0 || e.Key == Windows.System.VirtualKey.NumberPad0)
            {
                _isAutoFitEnabled = true;
                _ = ApplyAutoFitZoomAsync();
                e.Handled = true;
            }
        }
    }

    private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            string msg = args.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(msg))
            {
                msg = args.WebMessageAsJson;
            }
            if (!string.IsNullOrEmpty(msg))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(msg);
                if (doc.RootElement.TryGetProperty("type", out var typeEl))
                {
                    string type = typeEl.GetString() ?? "";
                    if (type == "zoomIn") ZoomIn();
                    else if (type == "zoomOut") ZoomOut();
                    else if (type == "zoomReset") ResetZoom(DefaultZoomFactor);
                }
            }
        }
        catch { }
    }

    public void ApplyLocalization()
    {
        if (TxtUrl != null)
        {
            TxtUrl.PlaceholderText = Helpers.Loc.Tr("CoBrowser.UrlPlaceholder", "URL eingeben (z. B. https://google.com)...");
        }
        if (BdrStatusBadge != null)
        {
            ToolTipService.SetToolTip(BdrStatusBadge, Helpers.Loc.Tr("CoBrowser.StatusReady", "Status: Bereit (Klick: Hinweis-Sprechblase)"));
        }
        if (BtnMoreActions != null)
        {
            ToolTipService.SetToolTip(BtnMoreActions, Helpers.Loc.Tr("CoBrowser.MoreActionsTooltip", "Co-Browser Menü: Navigation (Zurück, Vor, Neu laden), Auto-Fit Zoom, Skalierungsstufen und Fenstersteuerung"));
        }
        if (MenuItemBack != null) MenuItemBack.Text = Helpers.Loc.Tr("CoBrowser.MenuBack", "Zurück");
        if (MenuItemForward != null) MenuItemForward.Text = Helpers.Loc.Tr("CoBrowser.MenuForward", "Vorwärts");
        if (MenuItemReload != null) MenuItemReload.Text = Helpers.Loc.Tr("CoBrowser.MenuReload", "Neu laden");
        if (MenuItemHome != null) MenuItemHome.Text = Helpers.Loc.Tr("CoBrowser.MenuHome", "Startseite");
        if (MenuItemAutoFitZoom != null) MenuItemAutoFitZoom.Text = Helpers.Loc.Tr("CoBrowser.MenuAutoFit", "Auto-Fit (Breite anpassen)");
        if (MenuItemZoomIn != null) MenuItemZoomIn.Text = Helpers.Loc.Tr("CoBrowser.MenuZoomIn", "Zoom vergrößern (+10%)");
        if (MenuItemZoomOut != null) MenuItemZoomOut.Text = Helpers.Loc.Tr("CoBrowser.MenuZoomOut", "Zoom verkleinern (-10%)");
        if (MenuItemZoomReset != null) MenuItemZoomReset.Text = Helpers.Loc.Tr("CoBrowser.MenuZoomReset", "Zoom zurücksetzen (Auto-Fit)");
        if (MenuItemCloseBrowser != null) MenuItemCloseBrowser.Text = Helpers.Loc.Tr("CoBrowser.MenuClose", "Co-Browser schließen");
    }

    #endregion
}

