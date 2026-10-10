using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AgyCliDashboard.Helpers;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private static readonly SolidColorBrush GitOrangeBgBrush = new(Windows.UI.Color.FromArgb(20, 240, 80, 50));
    private static readonly SolidColorBrush GitOrangeBorderBrush = new(Windows.UI.Color.FromArgb(40, 240, 80, 50));
    private static readonly SolidColorBrush GitOrangeFgBrush = new(Windows.UI.Color.FromArgb(255, 240, 80, 50));
    private static readonly SolidColorBrush GitAmberBgBrush = new(Windows.UI.Color.FromArgb(40, 245, 158, 11));
    private static readonly SolidColorBrush GitAmberBorderBrush = new(Windows.UI.Color.FromArgb(80, 245, 158, 11));
    private static readonly SolidColorBrush GitAmberFgBrush = new(Windows.UI.Color.FromArgb(255, 245, 158, 11));
    private static readonly SolidColorBrush AutoPushActiveBgBrush = new(Windows.UI.Color.FromArgb(28, 16, 185, 129));
    private static readonly SolidColorBrush AutoPushActiveBorderBrush = new(Windows.UI.Color.FromArgb(80, 16, 185, 129));
    private bool _isPushing;
    private bool _isAutoPushRunning;
    private DispatcherTimer? _autoPushTimer;
    private int _gitRenderSeq;

    private async Task<string> GetGitRemoteUrlAsync(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath)) return "";
        try
        {
            var psiRemote = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "config --get remote.origin.url",
                WorkingDirectory = projectPath
            };
            psiRemote.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            var result = await ProcessRunner.RunAsync(psiRemote, TimeSpan.FromSeconds(5));
            return result.StandardOutput?.Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[GetGitRemoteUrl ERROR] {ex.Message}");
        }
        return "";
    }

    private async Task OpenInlineGitRemotePanelAsync()
    {
        string projectPath = GetCurrentProjectPath();
        string currentRemote = await GetGitRemoteUrlAsync(projectPath);

        if (PnlInlineGitRemote != null)
        {
            PnlInlineGitRemote.Visibility = Visibility.Visible;
        }

        if (TxtGitRemoteUrlInput != null)
        {
            TxtGitRemoteUrlInput.Text = currentRemote;
            TxtGitRemoteUrlInput.Focus(FocusState.Programmatic);
            TxtGitRemoteUrlInput.SelectAll();
        }
    }

    private async void BtnEditRemote_Click(object sender, RoutedEventArgs e)
    {
        if (PnlInlineGitRemote != null)
        {
            if (PnlInlineGitRemote.Visibility == Visibility.Visible)
            {
                PnlInlineGitRemote.Visibility = Visibility.Collapsed;
            }
            else
            {
                await OpenInlineGitRemotePanelAsync();
            }
        }
    }

    private async void BtnProjectHeaderLink_Click(object sender, RoutedEventArgs e)
    {
        string projectPath = GetCurrentProjectPath();
        string remoteUrl = await GetGitRemoteUrlAsync(projectPath);

        if (!string.IsNullOrWhiteSpace(remoteUrl))
        {
            string webUrl = remoteUrl;
            if (webUrl.StartsWith("git@github.com:"))
            {
                webUrl = "https://github.com/" + webUrl.Substring("git@github.com:".Length);
            }
            if (webUrl.EndsWith(".git"))
            {
                webUrl = webUrl.Substring(0, webUrl.Length - 4);
            }

            _ = OpenUrlInDefaultBrowserAsync(webUrl);
        }
        else
        {
            await OpenInlineGitRemotePanelAsync();
            ShowNotice("Git Remote Verknüpfen", "Bitte gib deine GitHub-Repository-URL ein und klicke auf 'Speichern & Linken'.", InfoBarSeverity.Informational);
        }
    }

    public void InitializeAutoPush()
    {
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(InitializeAutoPush);
            return;
        }

        SetupAutoPushContextMenu();
        UpdateAutoPushUI();

        if (_projectSettings.IsAutoPushEnabled)
        {
            RestartAutoPushTimer();
            _ = Task.Delay(15000).ContinueWith(_ =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (_projectSettings.IsAutoPushEnabled)
                    {
                        _ = RunAutoCloudBackupAsync(isManualTrigger: false);
                    }
                });
            });
        }
    }

    private void SetupAutoPushContextMenu()
    {
        if (BtnAutoPush != null)
        {
            var flyout = new MenuFlyout();

            var toggleItem = new MenuFlyoutItem
            {
                Text = _projectSettings.IsAutoPushEnabled ? "Auto-Push deaktivieren" : "Auto-Push aktivieren"
            };
            toggleItem.Click += (s, e) => ToggleAutoPush();
            flyout.Items.Add(toggleItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var intervalSub = new MenuFlyoutSubItem { Text = "Intervall anpassen" };
            int[] intervals = { 15, 30, 60, 120 };
            foreach (int min in intervals)
            {
                string label = min < 60 ? $"Alle {min} Minuten" : $"Alle {min / 60} Stunde(n)";
                if (min == 30) label += " (Standard)";

                var intervalItem = new MenuFlyoutItem
                {
                    Text = (_projectSettings.AutoPushIntervalMinutes == min ? "✓ " : "   ") + label,
                    Tag = min
                };
                intervalItem.Click += (s, e) =>
                {
                    if (s is MenuFlyoutItem mfi && mfi.Tag is int chosenMin)
                    {
                        _projectSettings.AutoPushIntervalMinutes = chosenMin;
                        SaveProjectSettings(refreshUI: false);
                        UpdateAutoPushUI();
                        RestartAutoPushTimer();
                        SetupAutoPushContextMenu();
                        ShowNotice("Intervall aktualisiert", $"Auto-Cloud-Backup wird nun alle {chosenMin} Minuten ausgeführt.", InfoBarSeverity.Informational);
                    }
                };
                intervalSub.Items.Add(intervalItem);
            }
            flyout.Items.Add(intervalSub);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var syncAllNowItem = new MenuFlyoutItem
            {
                Text = "Alle Projekte jetzt sofort in Cloud sichern"
            };
            syncAllNowItem.Click += (s, e) =>
            {
                _ = RunAutoCloudBackupAsync(isManualTrigger: true);
            };
            flyout.Items.Add(syncAllNowItem);

            BtnAutoPush.ContextFlyout = flyout;
        }

        if (BtnGitPush != null)
        {
            var pushFlyout = new MenuFlyout();
            var pushCurrentItem = new MenuFlyoutItem { Text = "Aktives Projekt online pushen" };
            pushCurrentItem.Click += (s, e) => BtnGitPush_Click(s, e);
            pushFlyout.Items.Add(pushCurrentItem);

            pushFlyout.Items.Add(new MenuFlyoutSeparator());

            var pushAllItem = new MenuFlyoutItem { Text = "Alle Projekte jetzt in Cloud sichern" };
            pushAllItem.Click += (s, e) =>
            {
                _ = RunAutoCloudBackupAsync(isManualTrigger: true);
            };
            pushFlyout.Items.Add(pushAllItem);

            BtnGitPush.ContextFlyout = pushFlyout;
        }
    }

    private void ToggleAutoPush()
    {
        _projectSettings.IsAutoPushEnabled = !_projectSettings.IsAutoPushEnabled;
        SaveProjectSettings(refreshUI: false);
        UpdateAutoPushUI();
        SetupAutoPushContextMenu();

        if (_projectSettings.IsAutoPushEnabled)
        {
            RestartAutoPushTimer();
            ShowNotice("Auto-Cloud-Backup aktiviert", $"Alle Projekte mit Git-Remote werden nun alle {_projectSettings.AutoPushIntervalMinutes} Minuten automatisch in der Cloud gesichert.", InfoBarSeverity.Success);
            _ = RunAutoCloudBackupAsync(isManualTrigger: false);
        }
        else
        {
            StopAutoPushTimer();
            ShowNotice("Auto-Cloud-Backup deaktiviert", "Das automatische Cloud-Backup im Hintergrund wurde angehalten.", InfoBarSeverity.Informational);
        }
    }

    private void BtnAutoPush_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnAutoPush_Click)} triggered.");
        ToggleAutoPush();
    }

    private void RestartAutoPushTimer()
    {
        StopAutoPushTimer();
        if (!_projectSettings.IsAutoPushEnabled) return;

        int intervalMin = _projectSettings.AutoPushIntervalMinutes > 0 ? _projectSettings.AutoPushIntervalMinutes : 30;
        _autoPushTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(intervalMin)
        };
        _autoPushTimer.Tick += (s, e) =>
        {
            _ = RunAutoCloudBackupAsync(isManualTrigger: false);
        };
        _autoPushTimer.Start();
        TelemetryLogger.Log($"[AutoPush] Timer gestartet mit Intervall {intervalMin} Minuten.");
    }

    private void StopAutoPushTimer()
    {
        if (_autoPushTimer != null)
        {
            _autoPushTimer.Stop();
            _autoPushTimer = null;
            TelemetryLogger.Log("[AutoPush] Timer gestoppt.");
        }
    }

    private void UpdateAutoPushUI()
    {
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(UpdateAutoPushUI);
            return;
        }

        bool isEnabled = _projectSettings?.IsAutoPushEnabled ?? true;
        int intervalMin = (_projectSettings?.AutoPushIntervalMinutes > 0) ? _projectSettings.AutoPushIntervalMinutes : 30;

        if (DotAutoPush != null)
        {
            DotAutoPush.Fill = isEnabled
                ? (Brush)Application.Current.Resources["AgyStatusSuccess"]
                : (Brush)Application.Current.Resources["AgyTextSubtle"];
        }

        if (TxtAutoPush != null)
        {
            TxtAutoPush.Foreground = isEnabled
                ? (Brush)Application.Current.Resources["AgyStatusSuccess"]
                : (Brush)Application.Current.Resources["AgyTextMuted"];
            TxtAutoPush.Text = "Auto";
        }

        if (BtnAutoPush != null)
        {
            BtnAutoPush.Background = isEnabled
                ? AutoPushActiveBgBrush
                : (Brush)Application.Current.Resources["AgySurfaceBg"];
            BtnAutoPush.BorderBrush = isEnabled
                ? AutoPushActiveBorderBrush
                : (Brush)Application.Current.Resources["AgyBorderDefault"];

            string tooltip = isEnabled
                ? $"Auto-Cloud-Backup: AKTIV (alle {intervalMin} Min.)\nLinksklick: Deaktivieren\nRechtsklick: Optionen & Sofort-Push"
                : "Auto-Cloud-Backup: AUS\nLinksklick: Aktivieren\nRechtsklick: Optionen";
            ToolTipService.SetToolTip(BtnAutoPush, tooltip);
        }
    }

    public async Task RunAutoCloudBackupAsync(bool isManualTrigger = false)
    {
        if (_isAutoPushRunning || _isPushing)
        {
            if (isManualTrigger)
            {
                ShowNotice("Cloud-Backup aktiv", "Ein Sicherungslauf wird momentan bereits ausgeführt.", InfoBarSeverity.Informational);
            }
            return;
        }

        _isAutoPushRunning = true;
        TelemetryLogger.Log($"[AutoPush] Cloud backup started. isManualTrigger={isManualTrigger}");

        if (isManualTrigger)
        {
            ShowNotice("Cloud-Backup", "Prüfe und sichere alle Projekte in der Cloud...", InfoBarSeverity.Informational);
        }

        int pushedCount = 0;
        int upToDateCount = 0;
        int failedCount = 0;

        try
        {
            string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder)
                ? _projectSettings.BaseFolder
                : ProjectSettingsData.GetDefaultBaseFolder();

            if (!Directory.Exists(baseFolder))
            {
                TelemetryLogger.Log($"[AutoPush] BaseFolder nicht gefunden: {baseFolder}");
                return;
            }

            var candidateDirs = new List<string>();
            try
            {
                foreach (var dir in Directory.GetDirectories(baseFolder))
                {
                    if (Directory.Exists(Path.Combine(dir, ".git")))
                    {
                        candidateDirs.Add(dir);
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[AutoPush] Verzeichnis-Scan Fehler: {ex.Message}");
            }

            foreach (var projPath in candidateDirs)
            {
                string projName = Path.GetFileName(projPath);
                try
                {
                    string remoteUrl = await GetGitRemoteUrlAsync(projPath);
                    if (string.IsNullOrWhiteSpace(remoteUrl))
                    {
                        continue;
                    }

                    var pushRes = await PushSingleProjectCoreAsync(projPath, isAuto: !isManualTrigger);
                    if (pushRes.Success)
                    {
                        if (pushRes.IsUpToDate)
                        {
                            upToDateCount++;
                        }
                        else
                        {
                            pushedCount++;
                            TelemetryLogger.Log($"[AutoPush] Erfolgreich gepusht: {projName}");
                        }
                    }
                    else
                    {
                        failedCount++;
                        TelemetryLogger.Log($"[AutoPush] Push fehlgeschlagen für {projName}: {pushRes.Message}");
                    }
                }
                catch (Exception pEx)
                {
                    failedCount++;
                    TelemetryLogger.Log($"[AutoPush] Fehler bei Projekt {projName}: {pEx.Message}");
                }
            }

            DispatcherQueue?.TryEnqueue(UpdateActiveProjectGitCardInfo);

            if (isManualTrigger)
            {
                string summary = $"Sicherung abgeschlossen: {pushedCount} Projekt(e) neu gepusht, {upToDateCount} bereits aktuell";
                if (failedCount > 0) summary += $", {failedCount} übersprungen/fehlgeschlagen";
                ShowNotice("Cloud-Backup Ergebnis", summary, failedCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            }
            else if (pushedCount > 0)
            {
                ShowNotice("Auto-Cloud-Backup", $"{pushedCount} Projekt(e) automatisch in der Cloud gesichert.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoPush Run ERROR] {ex.Message}");
            if (isManualTrigger)
            {
                ShowNotice("Cloud-Backup Fehler", ex.Message, InfoBarSeverity.Error);
            }
        }
        finally
        {
            _isAutoPushRunning = false;
        }
    }

    private sealed class PushSingleProjectResult
    {
        public bool Success { get; set; }
        public bool IsUpToDate { get; set; }
        public bool NeedsAuth { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    private async Task<PushSingleProjectResult> PushSingleProjectCoreAsync(string projectPath, bool isAuto)
    {
        var res = new PushSingleProjectResult();
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            res.Message = "Projektpfad nicht gefunden.";
            return res;
        }

        try
        {
            // 1. Uncommitted Work im Auto-Modus automatisch sichern
            if (isAuto)
            {
                var psiStatus = new ProcessStartInfo("git", "status --porcelain")
                {
                    WorkingDirectory = projectPath
                };
                psiStatus.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                var statusRes = await ProcessRunner.RunAsync(psiStatus, TimeSpan.FromSeconds(10));
                if (statusRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(statusRes.StandardOutput))
                {
                    var psiAdd = new ProcessStartInfo("git", "add .") { WorkingDirectory = projectPath };
                    psiAdd.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                    await ProcessRunner.RunAsync(psiAdd, TimeSpan.FromSeconds(30));

                    string timeStamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                    var psiCommit = new ProcessStartInfo("git", $"commit -m \"Auto-Cloud-Backup: {timeStamp}\"")
                    {
                        WorkingDirectory = projectPath
                    };
                    psiCommit.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                    psiCommit.EnvironmentVariables["GIT_AUTHOR_NAME"] = "AgyDashboard";
                    psiCommit.EnvironmentVariables["GIT_AUTHOR_EMAIL"] = "dashboard@local";
                    psiCommit.EnvironmentVariables["GIT_COMMITTER_NAME"] = "AgyDashboard";
                    psiCommit.EnvironmentVariables["GIT_COMMITTER_EMAIL"] = "dashboard@local";
                    await ProcessRunner.RunAsync(psiCommit, TimeSpan.FromSeconds(30));
                }
            }

            // 2. Initial-Commit Check falls noch kein Commit existiert
            var psiCheckRev = new ProcessStartInfo("git", "rev-parse --verify HEAD")
            {
                WorkingDirectory = projectPath
            };
            psiCheckRev.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            var revRes = await ProcessRunner.RunAsync(psiCheckRev, TimeSpan.FromSeconds(5));
            if (revRes.ExitCode != 0)
            {
                var psiAdd = new ProcessStartInfo("git", "add .") { WorkingDirectory = projectPath };
                psiAdd.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                await ProcessRunner.RunAsync(psiAdd, TimeSpan.FromMinutes(2));

                var psiCommit = new ProcessStartInfo("git", "commit -m \"Auto-Commit vor Push\"") { WorkingDirectory = projectPath };
                psiCommit.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                psiCommit.EnvironmentVariables["GIT_AUTHOR_NAME"] = "AgyDashboard";
                psiCommit.EnvironmentVariables["GIT_AUTHOR_EMAIL"] = "dashboard@local";
                psiCommit.EnvironmentVariables["GIT_COMMITTER_NAME"] = "AgyDashboard";
                psiCommit.EnvironmentVariables["GIT_COMMITTER_EMAIL"] = "dashboard@local";
                await ProcessRunner.RunAsync(psiCommit, TimeSpan.FromMinutes(2));
            }

            // 3. Push ausführen
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "push -u origin HEAD",
                WorkingDirectory = projectPath
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            psi.EnvironmentVariables["GCM_INTERACTIVE"] = "false";
            psi.EnvironmentVariables["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes";

            var result = await ProcessRunner.RunAsync(psi, TimeSpan.FromMinutes(2));
            TelemetryLogger.Log($"[GitPushCore] {projectPath} finished TimedOut={result.TimedOut}, ExitCode={result.ExitCode}");

            if (!result.TimedOut && result.ExitCode == 0)
            {
                string text = (result.StandardOutput ?? "") + " " + (result.StandardError ?? "");
                res.Success = true;
                res.IsUpToDate = text.Contains("Everything up-to-date", StringComparison.OrdinalIgnoreCase);
                res.Message = !string.IsNullOrWhiteSpace(result.StandardOutput)
                    ? result.StandardOutput
                    : (!string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardError : "Projekt erfolgreich online gepusht.");
                return res;
            }

            // 4. Fehlerprüfung
            string errStr = (result.StandardError ?? "") + " " + (result.StandardOutput ?? "");
            if (errStr.Contains("Cannot prompt", StringComparison.OrdinalIgnoreCase) ||
                errStr.Contains("user interactivity", StringComparison.OrdinalIgnoreCase) ||
                errStr.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase) ||
                errStr.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
            {
                res.NeedsAuth = true;
                res.Message = "GitHub-Authentifizierung erforderlich.";
                return res;
            }

            if (errStr.Contains("[rejected]", StringComparison.OrdinalIgnoreCase) ||
                errStr.Contains("fetch first", StringComparison.OrdinalIgnoreCase) ||
                errStr.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase))
            {
                TelemetryLogger.Log($"[GitPushCore] {projectPath} rejected. Auto-resolving...");
                bool resolved = await TryAutoResolvePushRejectionAsync(projectPath);
                if (resolved)
                {
                    var retryRes = await ProcessRunner.RunAsync(psi, TimeSpan.FromMinutes(2));
                    if (!retryRes.TimedOut && retryRes.ExitCode == 0)
                    {
                        res.Success = true;
                        string text = (retryRes.StandardOutput ?? "") + " " + (retryRes.StandardError ?? "");
                        res.IsUpToDate = text.Contains("Everything up-to-date", StringComparison.OrdinalIgnoreCase);
                        res.Message = !string.IsNullOrWhiteSpace(retryRes.StandardOutput)
                            ? retryRes.StandardOutput
                            : (!string.IsNullOrWhiteSpace(retryRes.StandardError) ? retryRes.StandardError : "Projekt erfolgreich online gepusht (Auto-Sync).");
                        return res;
                    }
                }
            }

            res.Message = !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError
                : (result.TimedOut ? "Git Push abgebrochen (Timeout)." : "Push fehlgeschlagen oder bereits aktuell.");
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[GitPushCore ERROR] {projectPath}: {ex.Message}");
            res.Message = ex.Message;
        }

        return res;
    }

    private async void BtnGitPush_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnGitPush_Click)} triggered.");
        if (_isPushing || _isAutoPushRunning)
        {
            ShowNotice("Git Push Hinweis", "Ein Sicherungslauf wird momentan bereits ausgeführt.", InfoBarSeverity.Informational);
            return;
        }
        _isPushing = true;
        if (BtnGitPush != null) BtnGitPush.IsEnabled = false;

        try
        {
            string projectPath = GetCurrentProjectPath();
            TelemetryLogger.Log($"[GitPush] projectPath='{projectPath}'");
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            {
                ShowNotice("Git Push Error", "Kein aktiver Projektpfad vorhanden.", InfoBarSeverity.Warning);
                return;
            }

            string remoteUrl = await GetGitRemoteUrlAsync(projectPath);
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                await OpenInlineGitRemotePanelAsync();
                ShowNotice("Kein Remote Verknüpft", "Für dieses Projekt ist noch kein Git Remote eingerichtet. Bitte gib die GitHub URL ein:", InfoBarSeverity.Warning);
                return;
            }

            ShowNotice("Git Push", "Führe git push aus...", InfoBarSeverity.Informational);
            var pushRes = await PushSingleProjectCoreAsync(projectPath, isAuto: false);

            if (pushRes.Success)
            {
                ShowNotice("Git Push Erfolgreich", pushRes.Message, InfoBarSeverity.Success);
                UpdateActiveProjectGitCardInfo();
            }
            else if (pushRes.NeedsAuth)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c start \"GitHub Login & Push\" cmd /k \"cd /d \"\"{projectPath}\"\" && echo Bitte schließe die GitHub-Anmeldung im Fenster ab... && git push -u origin HEAD && timeout /t 5\"",
                        UseShellExecute = true
                    });
                    ShowNotice("GitHub-Anmeldung erforderlich", "Es wurde ein Fenster zur einmaligen GitHub-Anmeldung geöffnet. Nach der Anmeldung ist der Push abgeschlossen!", InfoBarSeverity.Informational);
                }
                catch (Exception launchEx)
                {
                    ShowNotice("Anmelde-Fehler", $"Anmeldefenster konnte nicht geöffnet werden: {launchEx.Message}", InfoBarSeverity.Error);
                }
            }
            else
            {
                ShowNotice("Git Push Hinweis", pushRes.Message, InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[BtnGitPush_Click CRASH PROTECTED] {ex.Message}\n{ex.StackTrace}");
            ShowNotice("Git Push Fehler", ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            _isPushing = false;
            if (BtnGitPush != null) BtnGitPush.IsEnabled = true;
        }
    }

    private async Task<bool> TryAutoResolvePushRejectionAsync(string projectPath)
    {
        TelemetryLogger.Log("[GitPush Auto-Resolve] Attempting to auto-resolve push rejection...");
        ShowNotice("Git Auto-Sync", "Löse Divergenz automatisch auf (Fetch & Rebase)...", InfoBarSeverity.Informational);

        try
        {
            // 1. Aktuellen Branch ermitteln
            var psiBranch = new ProcessStartInfo("git", "rev-parse --abbrev-ref HEAD")
            {
                WorkingDirectory = projectPath
            };
            psiBranch.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            var branchRes = await ProcessRunner.RunAsync(psiBranch, TimeSpan.FromSeconds(5));
            string branch = (branchRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(branchRes.StandardOutput))
                ? branchRes.StandardOutput.Trim()
                : "master";

            // 2. Remote-Status abrufen
            var psiFetch = new ProcessStartInfo("git", $"fetch origin {branch}")
            {
                WorkingDirectory = projectPath
            };
            psiFetch.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            psiFetch.EnvironmentVariables["GCM_INTERACTIVE"] = "false";
            psiFetch.EnvironmentVariables["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes";
            await ProcessRunner.RunAsync(psiFetch, TimeSpan.FromSeconds(30));

            // 3. Normaler Sync: pull --rebase --autostash (schützt ungespeicherte lokale Dateien)
            var psiPull = new ProcessStartInfo("git", $"pull --rebase --autostash origin {branch}")
            {
                WorkingDirectory = projectPath
            };
            psiPull.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            psiPull.EnvironmentVariables["GCM_INTERACTIVE"] = "false";
            psiPull.EnvironmentVariables["GIT_SSH_COMMAND"] = "ssh -o BatchMode=yes";
            var pullRes = await ProcessRunner.RunAsync(psiPull, TimeSpan.FromSeconds(60));

            if (pullRes.ExitCode == 0)
            {
                TelemetryLogger.Log("[GitPush Auto-Resolve] pull --rebase --autostash succeeded cleanly.");
                return true;
            }

            string pullErr = (pullRes.StandardError ?? "") + " " + (pullRes.StandardOutput ?? "");
            TelemetryLogger.Log($"[GitPush Auto-Resolve] pull --rebase failed: {pullErr}");

            // Falls Rebase durch Konflikt blockiert, sauber abbrechen (Rollback)
            bool isRebaseActive = Directory.Exists(Path.Combine(projectPath, ".git", "rebase-merge")) ||
                                 Directory.Exists(Path.Combine(projectPath, ".git", "rebase-apply"));
            if (isRebaseActive)
            {
                var psiAbort = new ProcessStartInfo("git", "rebase --abort") { WorkingDirectory = projectPath };
                psiAbort.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                await ProcessRunner.RunAsync(psiAbort, TimeSpan.FromSeconds(10));
            }

            // 4. Sonderfall: Unrelated Histories (neu-initialisiertes lokales Repo auf bestehendem Remote)
            if (pullErr.Contains("unrelated histories", StringComparison.OrdinalIgnoreCase))
            {
                TelemetryLogger.Log("[GitPush Auto-Resolve] Detected unrelated histories. Finding root commit...");
                ShowNotice("Git Auto-Sync", "Erkenne neu-initialisierte Historie. Führe verlustfreie Einbindung durch...", InfoBarSeverity.Informational);

                // Ältesten/ersten Commit des Branches finden
                var psiRoot = new ProcessStartInfo("git", "rev-list --max-parents=0 HEAD")
                {
                    WorkingDirectory = projectPath
                };
                psiRoot.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                var rootRes = await ProcessRunner.RunAsync(psiRoot, TimeSpan.FromSeconds(5));
                string rootCommit = (rootRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(rootRes.StandardOutput))
                    ? rootRes.StandardOutput.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim()
                    : "";

                if (!string.IsNullOrEmpty(rootCommit))
                {
                    // Prüfen, ob der Dateibaum des Root-Commits mit dem Remote-Stand identisch ist
                    var psiDiff = new ProcessStartInfo("git", $"diff --quiet origin/{branch} {rootCommit}")
                    {
                        WorkingDirectory = projectPath
                    };
                    psiDiff.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                    var diffRes = await ProcessRunner.RunAsync(psiDiff, TimeSpan.FromSeconds(5));

                    if (diffRes.ExitCode == 0)
                    {
                        // Identischer Baum: Verlustfreier Rebase aller lokalen Folge-Commits direkt auf origin/<branch>
                        TelemetryLogger.Log($"[GitPush Auto-Resolve] Root commit {rootCommit} tree matches origin/{branch}. Rebasing --onto...");
                        var psiOnto = new ProcessStartInfo("git", $"rebase --onto origin/{branch} {rootCommit} {branch}")
                        {
                            WorkingDirectory = projectPath
                        };
                        psiOnto.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                        psiOnto.EnvironmentVariables["GCM_INTERACTIVE"] = "false";
                        var ontoRes = await ProcessRunner.RunAsync(psiOnto, TimeSpan.FromSeconds(60));
                        if (ontoRes.ExitCode == 0)
                        {
                            TelemetryLogger.Log("[GitPush Auto-Resolve] rebase --onto succeeded cleanly.");
                            return true;
                        }
                        else
                        {
                            var psiAbort = new ProcessStartInfo("git", "rebase --abort") { WorkingDirectory = projectPath };
                            psiAbort.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                            await ProcessRunner.RunAsync(psiAbort, TimeSpan.FromSeconds(10));
                        }
                    }
                    else
                    {
                        // Bäume weichen ab: Merge mit --allow-unrelated-histories
                        TelemetryLogger.Log("[GitPush Auto-Resolve] Root tree differs. Attempting merge with --allow-unrelated-histories...");
                        var psiMerge = new ProcessStartInfo("git", $"merge --allow-unrelated-histories origin/{branch} -m \"Auto-Merge Remote History\"")
                        {
                            WorkingDirectory = projectPath
                        };
                        psiMerge.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                        psiMerge.EnvironmentVariables["GCM_INTERACTIVE"] = "false";
                        var mergeRes = await ProcessRunner.RunAsync(psiMerge, TimeSpan.FromSeconds(60));
                        if (mergeRes.ExitCode == 0)
                        {
                            TelemetryLogger.Log("[GitPush Auto-Resolve] merge --allow-unrelated-histories succeeded.");
                            return true;
                        }
                        else
                        {
                            var psiAbortMerge = new ProcessStartInfo("git", "merge --abort") { WorkingDirectory = projectPath };
                            psiAbortMerge.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                            await ProcessRunner.RunAsync(psiAbortMerge, TimeSpan.FromSeconds(10));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[TryAutoResolvePushRejectionAsync ERROR] {ex.Message}");
        }

        return false;
    }

    private void BtnCancelGitRemote_Click(object sender, RoutedEventArgs e)
    {
        if (PnlInlineGitRemote != null)
        {
            PnlInlineGitRemote.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnSaveGitRemote_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveGitRemoteFromInput();
    }

    private void TxtGitRemoteUrlInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            _ = SaveGitRemoteFromInput();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            if (PnlInlineGitRemote != null)
            {
                PnlInlineGitRemote.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async Task SaveGitRemoteFromInput()
    {
        string projectPath = GetCurrentProjectPath();
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
        {
            ShowNotice("Fehler beim Speichern", "Kein gültiger Projektpfad vorhanden.", InfoBarSeverity.Warning);
            return;
        }

        string newUrl = TxtGitRemoteUrlInput?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(newUrl))
        {
            ShowNotice("Eingabe erforderlich", "Bitte eine gültige Git Remote URL (z. B. https://github.com/benutzer/repo.git) eingeben.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var psiRemove = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "remote remove origin",
                WorkingDirectory = projectPath
            };
            psiRemove.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            await ProcessRunner.RunAsync(psiRemove, TimeSpan.FromSeconds(5));

            var psiAdd = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"remote add origin \"{newUrl}\"",
                WorkingDirectory = projectPath
            };
            psiAdd.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            var addRes = await ProcessRunner.RunAsync(psiAdd, TimeSpan.FromSeconds(5));

            if (addRes.ExitCode != 0 && !string.IsNullOrWhiteSpace(addRes.StandardError))
            {
                ShowNotice("Git Remote Fehler", addRes.StandardError, InfoBarSeverity.Error);
                return;
            }

            if (PnlInlineGitRemote != null)
            {
                PnlInlineGitRemote.Visibility = Visibility.Collapsed;
            }

            ShowNotice("Git Remote Gespeichert", $"Remote-URL erfolgreich verknüpft: {newUrl}", InfoBarSeverity.Success);
            UpdateActiveProjectGitCardInfo();
        }
        catch (Exception ex)
        {
            ShowNotice("Fehler beim Speichern", ex.Message, InfoBarSeverity.Error);
        }
    }

    private string? _lastGitProjectPath;
    private async void UpdateGitTab(bool isGlobal, string projectPath)
    {
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateGitTab(isGlobal, projectPath));
            return;
        }
        if (GitPanel == null) return;
        int seq = Interlocked.Increment(ref _gitRenderSeq);
        bool isProjectSwitch = !string.Equals(_lastGitProjectPath, projectPath, StringComparison.OrdinalIgnoreCase);
        _lastGitProjectPath = projectPath;
        if (isProjectSwitch)
        {
            GitPanelScrollViewer?.ChangeView(null, 0, null, true);
        }
        GitPanel.Children.Clear();

        var header = new TextBlock
        {
            Text = "GIT REPOSITORY & HISTORIE",
            Margin = new Thickness(0, 0, 0, 8),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (Application.Current.Resources.TryGetValue("AgySidebarSectionHeaderStyle", out var gitHeaderStyle) && gitHeaderStyle is Style gs)
        {
            header.Style = gs;
        }
        else
        {
            header.FontSize = 12;
            header.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            header.Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"];
            header.CharacterSpacing = 10;
        }
        GitPanel.Children.Add(header);

        if (isGlobal)
        {
            var msg = new TextBlock
            {
                Text = "Bitte wähle oben ein konkretes Projekt aus, um dessen Git-Status anzuzeigen.",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["AgyTextMuted"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            GitPanel.Children.Add(msg);
            return;
        }

        if (Directory.Exists(projectPath) && Directory.Exists(Path.Combine(projectPath, ".git")))
        {
            try
            {
                var psiBranch = new ProcessStartInfo("git", "rev-parse --abbrev-ref HEAD")
                {
                    WorkingDirectory = projectPath
                };
                var branchRes = await ProcessRunner.RunAsync(psiBranch, TimeSpan.FromSeconds(5));
                if (seq != _gitRenderSeq || !string.Equals(_lastGitProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)) return;
                string branch = !string.IsNullOrWhiteSpace(branchRes.StandardOutput) ? branchRes.StandardOutput.Trim() : "master";

                var psiStatus = new ProcessStartInfo("git", "status --porcelain")
                {
                    WorkingDirectory = projectPath
                };
                var statusRes = await ProcessRunner.RunAsync(psiStatus, TimeSpan.FromSeconds(5));
                if (seq != _gitRenderSeq || !string.Equals(_lastGitProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)) return;
                string statusText = statusRes.ExitCode != 0 ? "Git-Status konnte nicht ermittelt werden" : "Arbeitsverzeichnis sauber";
                if (statusRes.ExitCode == 0 && !string.IsNullOrWhiteSpace(statusRes.StandardOutput))
                {
                    int count = statusRes.StandardOutput.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    statusText = $"{count} ungespeicherte Änderung(en)";
                }

                var summaryCard = new Border
                {
                    Background = GitOrangeBgBrush,
                    BorderBrush = GitOrangeBorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                var summaryGrid = new Grid();
                summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var summarySp = new StackPanel { Spacing = 4 };
                var branchSp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var gitIcon = new ImageIcon
                {
                    Source = CreateHighResSvg("ms-appx:///Assets/Icons/git-logo.svg"),
                    Width = 16,
                    Height = 16
                };
                var branchTxt = new TextBlock
                {
                    Text = $"Branch: {branch}",
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = GitOrangeFgBrush
                };
                branchSp.Children.Add(gitIcon);
                branchSp.Children.Add(branchTxt);

                var statusTxt = new TextBlock
                {
                    Text = statusText,
                    FontSize = 12,
                    Foreground = !string.IsNullOrWhiteSpace(statusRes.StandardOutput) ? GitAmberFgBrush : (Brush)Application.Current.Resources["AgyStatusSuccess"]
                };
                summarySp.Children.Add(branchSp);
                summarySp.Children.Add(statusTxt);

                Grid.SetColumn(summarySp, 0);
                summaryGrid.Children.Add(summarySp);
                summaryCard.Child = summaryGrid;
                GitPanel.Children.Add(summaryCard);

                var psiLog = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "log -n 15 --format=\"%h%x1f%s%x1f%cd%x1f%an\" --date=format:\"%d.%m.%Y %H:%M\"",
                    WorkingDirectory = projectPath
                };

                var logRes = await ProcessRunner.RunAsync(psiLog, TimeSpan.FromSeconds(5));
                if (seq != _gitRenderSeq || !string.Equals(_lastGitProjectPath, projectPath, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.IsNullOrWhiteSpace(logRes.StandardOutput))
                {
                    var lines = logRes.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('\x1f');
                        string hash = parts.Length > 0 ? parts[0] : "";
                        string subject = parts.Length > 1 ? parts[1] : line;
                        string time = parts.Length > 2 ? parts[2] : "";
                        string author = parts.Length > 3 ? parts[3] : "";

                        var card = CreateGitCommitCard(hash, subject, time);
                        GitPanel.Children.Add(card);
                    }
                }
            }
            catch (Exception ex)
            {
                var msg = new TextBlock
                {
                    Text = $"Git-Status konnte nicht geladen werden: {ex.Message}",
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                    Margin = new Thickness(0, 8, 0, 0)
                };
                GitPanel.Children.Add(msg);
            }
        }
        else
        {
            var msg = new TextBlock
            {
                Text = "Kein Git-Repository in diesem Projekt gefunden.",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                Margin = new Thickness(0, 8, 0, 0)
            };
            GitPanel.Children.Add(msg);
        }
    }

    private Button CreateGitCommitCard(string hash, string subject, string time)
    {
        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["AgySurfaceBg"],
            BorderBrush = (Brush)Application.Current.Resources["AgyBorderFaint"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Tag = hash
        };

        ToolTipService.SetToolTip(btn, $"Commit {hash}\nKlick für Optionen (Hash kopieren · Reset-Prompt)");

        var flyout = new MenuFlyout();

        var copyHashItem = new MenuFlyoutItem
        {
            Text = $"Commit-Hash kopieren ({hash})",
            Icon = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/copy.svg") }
        };
        copyHashItem.Click += (s, e) =>
        {
            try
            {
                var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(hash);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                ShowMockupNotice($"Commit-Hash {hash} kopiert.");
            }
            catch { }
        };
        flyout.Items.Add(copyHashItem);

        var resetPromptItem = new MenuFlyoutItem
        {
            Text = "Hard-Reset Prompt in Chat einfügen",
            Icon = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/history.svg") }
        };
        resetPromptItem.Click += (s, e) =>
        {
            TelemetryLogger.Log($"[Click Trace] GitCommitCard Reset Prompt triggered for {hash}");
            string projName = GetSelectedProjectName();
            if (string.IsNullOrWhiteSpace(projName) || projName.Equals("Alle Projekte", StringComparison.OrdinalIgnoreCase))
            {
                projName = "agy-cli-dashboard";
            }
            if (TxtChatInput != null)
            {
                TxtChatInput.Text = $"[Führe einen sicheren Hard-Reset auf den Commit {hash} durch für Projekt {projName}.]";
                TxtChatInput.Focus(FocusState.Programmatic);
                TxtChatInput.SelectionStart = TxtChatInput.Text.Length;
            }
            ShowMockupNotice($"Hard-Reset Prompt für Commit {hash} im Chatfeld eingefügt.");
        };
        flyout.Items.Add(resetPromptItem);

        btn.ContextFlyout = flyout;
        btn.Click += (s, e) =>
        {
            flyout.ShowAt(btn);
        };

        var mainSp = new StackPanel { Spacing = 6 };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var hashSp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var gitIcon = new ImageIcon
        {
            Source = CreateHighResSvg("ms-appx:///Assets/Icons/git-logo.svg"),
            Width = 14,
            Height = 14
        };
        var hashBorder = new Border
        {
            Background = GitAmberBgBrush,
            BorderBrush = GitAmberBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1)
        };
        var hashTxt = new TextBlock
        {
            Text = hash,
            FontSize = 12,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = GitAmberFgBrush
        };
        hashBorder.Child = hashTxt;

        hashSp.Children.Add(gitIcon);
        hashSp.Children.Add(hashBorder);
        Grid.SetColumn(hashSp, 0);
        headerGrid.Children.Add(hashSp);

        if (!string.IsNullOrEmpty(time))
        {
            var timeTxt = new TextBlock
            {
                Text = time,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(timeTxt, 1);
            headerGrid.Children.Add(timeTxt);
        }

        mainSp.Children.Add(headerGrid);

        var msgTxt = new TextBlock
        {
            Text = subject,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["AgyTextSecondary"],
            TextWrapping = TextWrapping.Wrap
        };
        mainSp.Children.Add(msgTxt);

        btn.Content = mainSp;
        return btn;
    }
}
