using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgyCliDashboard.Helpers;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private bool _isCliUpdatePending = false;
    private string _latestCliVersion = "";
    private string _currentLocalCliVersion = "";
    private DateTime _lastAgyExeWriteTimeUtc = DateTime.MinValue;
    private DateTime _lastCliUpdateCheckTime = DateTime.MinValue;
    private static readonly TimeSpan CliCheckCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly Regex SemVerRegex = new(@"\b\d+\.\d+\.\d+\b", RegexOptions.Compiled);
    private readonly SemaphoreSlim _cliCheckLock = new(1, 1);
    private readonly object _syncLockCliWatcherTimer = new();
    private FileSystemWatcher? _cliFileWatcher;
    private Timer? _cliWatcherDebounceTimer;

    public static string GetAgyExePath()
    {
        try
        {
            string localAgy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
            if (File.Exists(localAgy)) return localAgy;

            string geminiAgy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "bin", "agy.exe");
            if (File.Exists(geminiAgy)) return geminiAgy;
        }
        catch { }
        return "agy.exe";
    }

    public void InitializeCliVersionWatcher()
    {
        try
        {
            string agyExe = GetAgyExePath();
            if (File.Exists(agyExe))
            {
                _lastAgyExeWriteTimeUtc = File.GetLastWriteTimeUtc(agyExe);
            }

            string? dir = Path.GetDirectoryName(agyExe);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                _cliFileWatcher = new FileSystemWatcher(dir)
                {
                    Filter = "*agy*",
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                _cliFileWatcher.Changed += OnAgyFileChanged;
                _cliFileWatcher.Created += OnAgyFileChanged;
                _cliFileWatcher.Renamed += OnAgyFileRenamed;
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[InitializeCliVersionWatcher] Fehler beim Starten des FileSystemWatchers: {ex.Message}");
        }
    }

    private void OnAgyFileChanged(object sender, FileSystemEventArgs e)
    {
        string fileName = e.Name ?? string.Empty;
        if (!fileName.Contains("agy", StringComparison.OrdinalIgnoreCase)) return;

        lock (_syncLockCliWatcherTimer)
        {
            if (_cliFileWatcher == null) return;

            if (_cliWatcherDebounceTimer == null)
            {
                _cliWatcherDebounceTimer = new Timer(_ =>
                {
                    DispatcherQueue?.TryEnqueue(async () =>
                    {
                        try
                        {
                            TelemetryLogger.Log("[InitializeCliVersionWatcher] agy.exe Dateisystem-Event erkannt. Aktualisiere CLI-Version.");
                            await RefreshCliVersionDisplayAsync(forceRemoteCheck: false);
                        }
                        catch (Exception ex)
                        {
                            TelemetryLogger.Log($"[InitializeCliVersionWatcher] Fehler: {ex.Message}");
                        }
                    });
                }, null, 750, Timeout.Infinite);
            }
            else
            {
                _cliWatcherDebounceTimer.Change(750, Timeout.Infinite);
            }
        }
    }

    private void OnAgyFileRenamed(object sender, RenamedEventArgs e)
    {
        OnAgyFileChanged(sender, e);
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentLocalCliVersion))
        {
            BtnSystemCheck_Click(this, new RoutedEventArgs());
            return;
        }

        if (BtnCheckUpdate != null) BtnCheckUpdate.IsEnabled = false;
        try
        {
            if (SpinnerCliStatus != null)
            {
                SpinnerCliStatus.Visibility = Visibility.Visible;
                SpinnerCliStatus.IsActive = true;
            }
            if (DotCliStatus != null) DotCliStatus.Visibility = Visibility.Collapsed;
            if (TxtCliStatus != null) TxtCliStatus.Text = "Lade...";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/google-antigravity/antigravity-cli/releases/latest");
                request.Headers.UserAgent.ParseAdd("AgyCliDashboard");
                using var httpResp = await _healthHttpClient.SendAsync(request);
                httpResp.EnsureSuccessStatusCode();
                var response = await httpResp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(response);
                var version = doc.RootElement.GetProperty("tag_name").GetString();
                if (version != null && version.StartsWith("v", StringComparison.OrdinalIgnoreCase)) version = version.Substring(1);
                var body = doc.RootElement.GetProperty("body").GetString() ?? "";

                if (!string.IsNullOrWhiteSpace(body))
                {
                    string prompt = $"[Hier ist der Changelog für CLI Version v{version}:\n\n---\n{body.Trim()}\n---\n\nBitte jeden Punkt oben einfach, kurz, verständlich und übersichtlich auf Deutsch zusammenfassen.]";

                    if (TxtChatInput != null)
                    {
                        TxtChatInput.Text = prompt;
                        TxtChatInput.Focus(FocusState.Programmatic);
                        TxtChatInput.SelectionStart = TxtChatInput.Text.Length;
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[BtnCheckUpdate_Click] Fehler beim Laden des Changelogs: {ex.Message}");
                ShowNotice("Changelog Fehler", "Konnte Changelog nicht laden.", InfoBarSeverity.Error);
            }
            finally
            {
                await RefreshCliVersionDisplayAsync(forceRemoteCheck: false);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnCheckUpdate_Click exception: {ex.Message}");
        }
        finally
        {
            if (BtnCheckUpdate != null) BtnCheckUpdate.IsEnabled = true;
        }
    }

    private async void BtnCheckUpdate_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        TelemetryLogger.Log("[BtnCheckUpdate_RightTapped] Manuelle Überprüfung der CLI-Version und Updates ausgelöst.");
        await CheckForCliUpdatesAsync(showNoticeOnUpToDate: true, forceRefresh: true);
    }

    private async Task<string> GetLocalAgyVersionAsync()
    {
        try
        {
            string agyExe = GetAgyExePath();
            if (File.Exists(agyExe))
            {
                _lastAgyExeWriteTimeUtc = File.GetLastWriteTimeUtc(agyExe);
            }

            var psi = new ProcessStartInfo(agyExe, "--version");
            var res = await ProcessRunner.RunAsync(psi, TimeSpan.FromSeconds(3));
            if (!res.TimedOut && !string.IsNullOrEmpty(res.StandardOutput))
            {
                var match = SemVerRegex.Match(res.StandardOutput);
                if (match.Success)
                {
                    _currentLocalCliVersion = match.Value;
                    return match.Value;
                }
                string trimmed = res.StandardOutput.Trim();
                _currentLocalCliVersion = trimmed;
                return trimmed;
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[GetLocalAgyVersionAsync] Fehler: {ex.Message}");
        }
        return _currentLocalCliVersion;
    }

    private static bool IsVersionNewer(string localVerStr, string latestVerStr)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(localVerStr) || string.IsNullOrWhiteSpace(latestVerStr)) return false;

            var cleanLocal = Regex.Replace(localVerStr.Trim().TrimStart('v', 'V'), @"[^\d\.\-a-zA-Z]", "");
            var cleanLatest = Regex.Replace(latestVerStr.Trim().TrimStart('v', 'V'), @"[^\d\.\-a-zA-Z]", "");

            var partsLocal = cleanLocal.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var partsLatest = cleanLatest.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);

            int maxLen = Math.Max(partsLocal.Length, partsLatest.Length);
            for (int i = 0; i < maxLen; i++)
            {
                string p1 = i < partsLocal.Length ? partsLocal[i] : "0";
                string p2 = i < partsLatest.Length ? partsLatest[i] : "0";

                bool isNum1 = long.TryParse(p1, out long n1);
                bool isNum2 = long.TryParse(p2, out long n2);

                if (isNum1 && isNum2)
                {
                    if (n2 > n1) return true;
                    if (n2 < n1) return false;
                }
                else
                {
                    if (!isNum1 && isNum2) return true;
                    if (isNum1 && !isNum2) return false;
                    int cmp = string.Compare(p2, p1, StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp > 0;
                }
            }
        }
        catch { }
        return false;
    }

    private void UpdateCliBadgeTooltip(string localVersion, string latestVersion, bool isUpdateAvailable, string? releaseNotes = null)
    {
        if (BtnCheckUpdate == null) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            string tooltipText;
            if (!string.IsNullOrEmpty(releaseNotes))
            {
                tooltipText = $"Aktuelle Version: v{localVersion}\n\n{releaseNotes}";
            }
            else
            {
                if (string.IsNullOrEmpty(localVersion))
                {
                    tooltipText = "Antigravity CLI ('agy') fehlt im PATH\n\nKlicke, um den System-Check zu öffnen und die CLI zu installieren (Rechtsklick: Neu prüfen)";
                }
                else
                {
                    string actionHint = isUpdateAvailable
                        ? "Klicke für CLI-Changelog & Release-Notes des Updates (Rechtsklick: Neu prüfen)"
                        : "Klicke für CLI-Changelog & Release-Notes (Rechtsklick: Version & Updates sofort prüfen)";

                    string versionHeader = isUpdateAvailable
                        ? $"Update verfügbar: v{latestVersion} (Installiert: v{localVersion})"
                        : $"Aktuelle Version: v{localVersion}";

                    tooltipText = $"{versionHeader}\n\n{actionHint}";
                }
            }

            var tooltipContent = new TextBlock
            {
                Text = tooltipText,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                MaxWidth = 1200
            };
            var tt = new ToolTip
            {
                Content = tooltipContent,
                MaxWidth = 1200
            };
            ToolTipService.SetToolTip(BtnCheckUpdate, tt);
        });
    }

    public async Task RefreshCliVersionDisplayAsync(bool forceRemoteCheck = false, bool showNoticeOnUpToDate = false)
    {
        if (forceRemoteCheck || (DateTime.Now - _lastCliUpdateCheckTime) > CliCheckCacheDuration)
        {
            await CheckForCliUpdatesAsync(showNoticeOnUpToDate, forceRefresh: forceRemoteCheck);
            return;
        }

        string localVersion = await GetLocalAgyVersionAsync();
        if (string.IsNullOrEmpty(localVersion) && !string.IsNullOrEmpty(_currentLocalCliVersion))
        {
            localVersion = _currentLocalCliVersion;
        }

        bool isUpdateAvailable = !string.IsNullOrEmpty(_latestCliVersion) && !string.IsNullOrEmpty(localVersion) && IsVersionNewer(localVersion, _latestCliVersion);
        _isCliUpdatePending = isUpdateAvailable;

        DispatcherQueue.TryEnqueue(() =>
        {
            bool isCliMissing = string.IsNullOrEmpty(localVersion);
            if (DotCliStatus != null)
            {
                DotCliStatus.Visibility = Visibility.Visible;
                DotCliStatus.Fill = isCliMissing ? HealthRedBrush : (isUpdateAvailable ? HealthAmberBrush : HealthGreenBrush);
            }
            if (SpinnerCliStatus != null)
            {
                SpinnerCliStatus.IsActive = false;
                SpinnerCliStatus.Visibility = Visibility.Collapsed;
            }
            if (TxtCliStatus != null)
            {
                TxtCliStatus.Text = isCliMissing
                    ? "CLI fehlt"
                    : (isUpdateAvailable ? $"Update ({_latestCliVersion})" : $"v{localVersion}");
            }
            UpdateCliBadgeTooltip(localVersion, _latestCliVersion, isUpdateAvailable);
            if (showNoticeOnUpToDate)
            {
                if (isCliMissing)
                {
                    ShowNotice("CLI Status", "Antigravity CLI ('agy') fehlt im PATH. Bitte über den System-Check installieren.", InfoBarSeverity.Error);
                }
                else
                {
                    ShowNotice("CLI Status", $"CLI (v{localVersion}) ist auf dem neuesten Stand.", InfoBarSeverity.Success);
                }
            }
        });
    }

    public async Task CheckForCliUpdatesAsync(bool showNoticeOnUpToDate = false, bool forceRefresh = false)
    {
        if (!await _cliCheckLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (!forceRefresh && !showNoticeOnUpToDate && (DateTime.Now - _lastCliUpdateCheckTime) < CliCheckCacheDuration && !string.IsNullOrEmpty(_latestCliVersion))
            {
                TelemetryLogger.Log("[CheckForCliUpdatesAsync] Ausführung übersprungen: Verwende gecachte Update-Informationen.");
                return;
            }
            _lastCliUpdateCheckTime = DateTime.Now;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (SpinnerCliStatus != null)
                {
                    SpinnerCliStatus.Visibility = Visibility.Visible;
                    SpinnerCliStatus.IsActive = true;
                }
                if (DotCliStatus != null) DotCliStatus.Visibility = Visibility.Collapsed;
                if (TxtCliStatus != null) TxtCliStatus.Text = "Prüfe...";
            });

            string localVersion = await GetLocalAgyVersionAsync();
            string latestVersion = "";
            string releaseBody = "";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/google-antigravity/antigravity-cli/releases/latest");
                request.Headers.UserAgent.ParseAdd("AgyCliDashboard");
                using var httpResp = await _healthHttpClient.SendAsync(request);
                httpResp.EnsureSuccessStatusCode();
                var response = await httpResp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(response);
                var tag = doc.RootElement.GetProperty("tag_name").GetString();
                if (tag != null && tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)) tag = tag.Substring(1);
                latestVersion = tag?.Trim() ?? "";
                releaseBody = doc.RootElement.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[CheckForCliUpdatesAsync] GitHub API Abfrage fehlgeschlagen: {ex.Message}");
            }

            bool isUpdateAvailable = !string.IsNullOrEmpty(latestVersion) && !string.IsNullOrEmpty(localVersion) && IsVersionNewer(localVersion, latestVersion);
            _isCliUpdatePending = isUpdateAvailable;
            _latestCliVersion = !string.IsNullOrEmpty(latestVersion) ? latestVersion : localVersion;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (SpinnerCliStatus != null)
                {
                    SpinnerCliStatus.IsActive = false;
                    SpinnerCliStatus.Visibility = Visibility.Collapsed;
                }
                bool isCliMissing = string.IsNullOrEmpty(localVersion);
                if (DotCliStatus != null)
                {
                    DotCliStatus.Visibility = Visibility.Visible;
                    DotCliStatus.Fill = isCliMissing ? HealthRedBrush : (isUpdateAvailable ? HealthAmberBrush : HealthGreenBrush);
                }

                if (isCliMissing)
                {
                    if (TxtCliStatus != null) TxtCliStatus.Text = "CLI fehlt";
                    if (showNoticeOnUpToDate)
                    {
                        ShowNotice("CLI Status", "Antigravity CLI ('agy') fehlt im PATH. Bitte über den System-Check installieren.", InfoBarSeverity.Error);
                    }
                }
                else if (isUpdateAvailable)
                {
                    if (TxtCliStatus != null) TxtCliStatus.Text = $"Update ({latestVersion})";
                    ShowNotice("CLI Update", $"CLI-Update verfügbar: v{latestVersion} (Klicke für Changelog)", InfoBarSeverity.Informational);
                }
                else
                {
                    if (TxtCliStatus != null) TxtCliStatus.Text = $"v{localVersion}";
                    if (showNoticeOnUpToDate)
                    {
                        ShowNotice("CLI Status", $"CLI (v{localVersion}) ist auf dem neuesten Stand.", InfoBarSeverity.Success);
                    }
                }

                UpdateCliBadgeTooltip(localVersion, _latestCliVersion, isUpdateAvailable);
            });
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"CheckForCliUpdatesAsync error: {ex.Message}");
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (SpinnerCliStatus != null)
                {
                    SpinnerCliStatus.IsActive = false;
                    SpinnerCliStatus.Visibility = Visibility.Collapsed;
                }
                if (DotCliStatus != null)
                {
                    DotCliStatus.Visibility = Visibility.Visible;
                }
            });
            _cliCheckLock.Release();
        }
    }
}
