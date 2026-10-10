using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private void BringDashboardToFrontSafely()
    {
        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                if (IsIconic(_hwnd))
                {
                    ShowWindow(_hwnd, 9); // SW_RESTORE nur wenn tatsächlich minimiert
                }
                BringWindowToTop(_hwnd);
                SetForegroundWindow(_hwnd);
            }
            this.Activate();
        }
        catch { }
    }

    private FileSystemWatcher? _handoffWatcher;
    private readonly object _syncLockHandoff = new object();
    private System.Threading.Timer? _handoffDebounceTimer;
    private string? _lastProcessedHandoffId;

    private void InitHandoffWatcher()
    {
        try
        {
            string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli");
            if (!Directory.Exists(appDataDir)) Directory.CreateDirectory(appDataDir);

            if (_handoffWatcher != null)
            {
                _handoffWatcher.EnableRaisingEvents = false;
                _handoffWatcher.Changed -= OnHandoffFileTriggered;
                _handoffWatcher.Created -= OnHandoffFileTriggered;
                _handoffWatcher.Dispose();
                _handoffWatcher = null;
            }

            _handoffWatcher = new FileSystemWatcher(appDataDir, "pending_handoff.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _handoffWatcher.Changed += OnHandoffFileTriggered;
            _handoffWatcher.Created += OnHandoffFileTriggered;

            TelemetryLogger.Log("[InitHandoffWatcher] FileSystemWatcher for pending_handoff.json initialized.");

            // Einmalige Prüfung beim Start, falls bereits eine Handoff-Datei vorliegt
            _ = Task.Run(async () =>
            {
                await Task.Delay(1200);
                ProcessPendingHandoff();
            });
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[InitHandoffWatcher ERROR] {ex.Message}");
        }
    }

    private void OnHandoffFileTriggered(object sender, FileSystemEventArgs e)
    {
        lock (_syncLockHandoff)
        {
            _handoffDebounceTimer ??= new System.Threading.Timer(_ =>
            {
                ProcessPendingHandoff();
            }, null, Timeout.Infinite, Timeout.Infinite);

            _handoffDebounceTimer.Change(120, Timeout.Infinite);
        }
    }

    private void ProcessPendingHandoff()
    {
        try
        {
            string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "pending_handoff.json");
            if (!File.Exists(filePath)) return;

            string json = string.Empty;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    json = reader.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(json)) break;
                }
                catch
                {
                    Thread.Sleep(80);
                }
            }

            if (string.IsNullOrWhiteSpace(json)) return;

            string cleanJson = json.TrimStart('\uFEFF');
            using var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            string? id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrEmpty(id) || string.Equals(id, _lastProcessedHandoffId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            string defaultProjectFallback = !string.IsNullOrWhiteSpace(_projectSettings?.LastSelectedProject)
                ? _projectSettings.LastSelectedProject
                : (!string.IsNullOrWhiteSpace(_uiStates?.LastSelectedProject) ? _uiStates.LastSelectedProject : "Default");
            string project = root.TryGetProperty("project", out var projProp) ? (projProp.GetString() ?? defaultProjectFallback) : defaultProjectFallback;
            string subject = root.TryGetProperty("subject", out var subjProp) ? (subjProp.GetString() ?? string.Empty) : string.Empty;

            var stagedFiles = new List<string>();
            if (root.TryGetProperty("attachments", out var attsProp) && attsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in attsProp.EnumerateArray())
                {
                    string? attPath = item.GetString();
                    if (!string.IsNullOrWhiteSpace(attPath))
                    {
                        stagedFiles.Add(attPath);
                    }
                }
            }

            int folderAttachments = 0;
            if (root.TryGetProperty("attachmentCount", out var attCountProp) &&
                attCountProp.ValueKind == JsonValueKind.Number &&
                attCountProp.TryGetInt32(out var count) && count > 0)
            {
                folderAttachments = count;
            }

            TelemetryLogger.Log($"[Handoff] Processing handoff '{id}' for project '{project}', subject '{subject}', staged: {stagedFiles.Count}, folder attachments: {folderAttachments}");

            DispatcherQueue?.TryEnqueue(() =>
            {
                try
                {
                    // 1. Fenster wiederherstellen und in den Vordergrund bringen (ohne Vollbild zu zerstören)
                    BringDashboardToFrontSafely();

                    // 2. Projekt auswählen
                    if (_uiStates != null) _uiStates.LastSelectedProject = project;
                    if (_projectSettings != null) _projectSettings.LastSelectedProject = project;
                    SaveUiStates();
                    SaveProjectSettings(refreshUI: false);

                    UpdateProjectComboBox();
                    UpdateProjectFocusBadgeUI();
                    UpdateActiveTabContent();
                    UpdateProjectFileWatcher();

                    // 3. Neuen Chat im Projekt mit sprechendem E-Mail-Titel öffnen
                    CloseInitialPlaceholderTab();

                    if (_terminalSessions.Count >= MaxTerminalTabs)
                    {
                        ShowNotice("Tab-Limit erreicht", $"Es können maximal {MaxTerminalTabs} Tabs gleichzeitig geöffnet sein. Bitte schließen Sie einen Tab für die E-Mail-Übernahme.", InfoBarSeverity.Warning);
                        _lastProcessedHandoffId = null;
                        return;
                    }

                    string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) && Directory.Exists(_projectSettings.BaseFolder)
                        ? _projectSettings.BaseFolder
                        : ProjectSettingsData.GetDefaultBaseFolder();
                    string projPath = Path.Combine(baseFolder, project);
                    if (!Directory.Exists(projPath))
                    {
                        projPath = Directory.Exists(baseFolder) ? baseFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    }

                    string cleanSubj = (subject ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
                    string tabTitle = !string.IsNullOrWhiteSpace(cleanSubj)
                        ? (cleanSubj.Length > 28 ? $"📧 {cleanSubj.Substring(0, 25)}..." : $"📧 {cleanSubj}")
                        : "📧 E-Mail Chat";

                    var session = CreateNewTerminalSession(tabTitle, conversationId: null, customStartupCommand: null, projectName: project, projectPath: projPath);
                    _initialTerminalId = session.Id;
                    SwitchToTerminalTab(session.Id);
                    UpdateProjectFocusBadgeUI();

                    // 4. Angehängte Dokumente stagen (idempotent und ohne versehentliches Toggle-Unstaging)
                    _stagedAttachments.Clear();
                    if (AttachmentStagingControl != null)
                    {
                        AttachmentStagingControl.Visibility = Visibility.Collapsed;
                    }

                    int attachedCount = 0;
                    foreach (var filePathToStage in stagedFiles)
                    {
                        if (File.Exists(filePathToStage) || Directory.Exists(filePathToStage))
                        {
                            if (!_stagedAttachments.Any(a => a.FilePath.Equals(filePathToStage, StringComparison.OrdinalIgnoreCase)))
                            {
                                StageFileAttachment(filePathToStage);
                                attachedCount++;
                            }
                        }
                    }

                    // 5. Chatfeld leeren und bereitstellen (kein automatischer Initial-Turn, E-Mail ist als Hintergrunddatei gestagt)
                    if (TxtChatInput != null)
                    {
                        TxtChatInput.Text = string.Empty;
                    }


                    string noticeText = string.IsNullOrWhiteSpace(subject)
                        ? "E-Mail als Hintergrundinformation geladen."
                        : $"E-Mail '{subject}' als Hintergrundinformation geladen.";
                    if (folderAttachments > 0)
                    {
                        noticeText += $" ({folderAttachments} {(folderAttachments == 1 ? "Anhang liegt" : "Anhänge liegen")} im Hintergrundordner bereit).";
                    }
                    noticeText += " Geben Sie direkt Ihre Frage ein.";

                    ShowNotice("E-Mail-Chat bereit", noticeText, InfoBarSeverity.Success);

                    // 6. Nach 1 Sekunde robuster Vordergrund-Fokus (überwindet Windows Focus-Stealing Lock nach Klick im Browser)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            BringDashboardToFrontSafely();
                            TxtChatInput?.Focus(FocusState.Programmatic);
                        });
                    });

                    // 7. Übergabedatei nach erfolgreicher Übernahme aufräumen
                    try
                    {
                        if (File.Exists(filePath)) File.Delete(filePath);
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[Handoff Dispatch ERROR] {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[ProcessPendingHandoff ERROR] {ex.Message}");
        }
    }

    private void DisposeHandoffWatcher()
    {
        try
        {
            if (_handoffWatcher != null)
            {
                _handoffWatcher.EnableRaisingEvents = false;
                _handoffWatcher.Changed -= OnHandoffFileTriggered;
                _handoffWatcher.Created -= OnHandoffFileTriggered;
                _handoffWatcher.Dispose();
                _handoffWatcher = null;
            }
            lock (_syncLockHandoff)
            {
                _handoffDebounceTimer?.Dispose();
                _handoffDebounceTimer = null;
            }
        }
        catch { }
    }
}
