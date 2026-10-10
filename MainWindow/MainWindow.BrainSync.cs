using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text;
using System.Threading;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private readonly object _syncLockBrainTimer = new object();
    private readonly SemaphoreSlim _resumeSyncGate = new(1, 1);
    private DateTime _lastDbLastWriteTimeUtc;

    private void TriggerBrainDebounced()
    {
        lock (_syncLockBrainTimer)
        {
            if (_brainWatcher == null) return;

            _brainWatcherTimer ??= new Timer(_ =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    _ = LoadChatsAsync();
                });
            }, null, Timeout.Infinite, Timeout.Infinite);

            _brainWatcherTimer.Change(1200, Timeout.Infinite);
        }
    }

    private async void ProcessTranscriptChanged(string changedFilePath)
    {
        try
        {
            TelemetryLogger.Log($"[BrainSync] ProcessTranscriptChanged called for: {changedFilePath}");
            if (string.IsNullOrEmpty(changedFilePath) || !File.Exists(changedFilePath)) return;

            string? convId = null;
            var parts = changedFilePath.Replace('/', '\\').Split('\\');
            for (int i = parts.Length - 2; i >= 0; i--)
            {
                if (parts[i].Equals("brain", StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                {
                    convId = parts[i + 1];
                    break;
                }
            }

            if (!string.IsNullOrEmpty(convId))
            {
                if (convId.StartsWith("sync-knowledge-", StringComparison.OrdinalIgnoreCase)) return;

                // Subagent check done asynchronously on background thread
                try
                {
                    string brainPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
                    var subIds = await Services.DatabaseService.CollectSubagentIdsAsync(brainPath);
                    if (subIds.Contains(convId))
                    {
                        TelemetryLogger.Log($"[BrainSync] Ignoring transcript change for subagent convId: {convId}");
                        return;
                    }
                }
                catch { }

                EnsureChatSummaryInDb(convId, changedFilePath);
                var (fallback, firstInputUtc, lastInputUtc) = Services.DatabaseService.ExtractFallbackInfoFromTranscript(changedFilePath);
                DateTime transcriptTimeUtc = firstInputUtc ?? File.GetCreationTimeUtc(changedFilePath);
                DateTime activityTimeUtc = lastInputUtc ?? File.GetLastWriteTimeUtc(changedFilePath);

                _ = RefreshTranscriptTokensAsync(changedFilePath);

                DispatcherQueue?.TryEnqueue(() => 
                {
                    UpdateTokenPeakMeter(false);
                    
                    // 1. Prüfen, ob bereits eine Terminal-Session an diese convId gebunden ist
                    var matchingSession = _terminalSessions.FirstOrDefault(t => string.Equals(t.ConversationId, convId, StringComparison.OrdinalIgnoreCase));
                    if (matchingSession != null)
                    {
                        // Falls in _allMasterChats bereits ein echter KI-Titel vorliegt, synchronisiere ihn direkt
                        string? realTitle = null;
                        lock (_allMasterChats)
                        {
                            var mc = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase));
                            if (mc != null && !string.IsNullOrWhiteSpace(mc.Title) &&
                                !mc.Title.Equals("Neuer Chat", StringComparison.OrdinalIgnoreCase) &&
                                !mc.Title.StartsWith("Chat:", StringComparison.OrdinalIgnoreCase))
                            {
                                realTitle = mc.Title;
                            }
                        }

                        if (!string.IsNullOrEmpty(realTitle) && matchingSession.Title != realTitle)
                        {
                            matchingSession.Title = realTitle;
                            RenderWorkspaceTabs();
                        }
                        else if ((matchingSession.Title == "Neuer Chat" || string.IsNullOrWhiteSpace(matchingSession.Title)) && !string.IsNullOrWhiteSpace(fallback))
                        {
                            matchingSession.Title = fallback;
                            RenderWorkspaceTabs();
                        }

                        if (string.Equals(_uiStates.LastActiveTab, "prompts", StringComparison.OrdinalIgnoreCase))
                        {
                            UpdatePromptsTab();
                        }
                        return;
                    }

                    // 2. Dynamisch einen ungebundenen Tab mit der ConversationId verknüpfen
                    // WICHTIG: Strikte Tab- & Projekt-Isolierung:
                    // a) Altersschutz: Das Transkript bzw. dessen letzte Aktivität muss zeitlich NACH (oder max. 30s vor) der Erstellung des Tabs entstanden sein. Alte Chats dürfen niemals freie Tabs kapern!
                    // b) Projektschutz: Der Projektpfad muss exakt übereinstimmen (oder Tab ist "Globaler Fokus" und Chat liegt im Root-Verzeichnis).
                    // c) Kein unsicherer Fallback auf _activeTerminalId über Projektgrenzen hinweg!
                    string chatProjDir = ResolveChatProjectPath(convId);
                    string normChatProjDir = Services.DatabaseService.NormalizePath(chatProjDir);
                    string baseFolder = Services.DatabaseService.NormalizePath(!string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder());

                    var candidateSessions = _terminalSessions.Where(t => 
                        string.IsNullOrEmpty(t.ConversationId) &&
                        !string.IsNullOrEmpty(t.ProjectPath) &&
                        (t.IsUsed || t.HasPromptBeenDispatched || t.Id == _activeTerminalId) &&
                        (
                            string.Equals(Services.DatabaseService.NormalizePath(t.ProjectPath), normChatProjDir, StringComparison.OrdinalIgnoreCase) ||
                            (t.ProjectName == "Globaler Fokus" && normChatProjDir.StartsWith(Services.DatabaseService.NormalizePath(t.ProjectPath), StringComparison.OrdinalIgnoreCase)) ||
                            (string.Equals(normChatProjDir, baseFolder, StringComparison.OrdinalIgnoreCase) && t.Id == _activeTerminalId)
                        ) &&
                        (
                            transcriptTimeUtc >= (t.CreatedAt - TimeSpan.FromSeconds(30)) &&
                            transcriptTimeUtc <= (t.CreatedAt + TimeSpan.FromHours(2)) &&
                            t.CreatedAt >= DateTime.UtcNow.AddHours(-12)
                        )
                    ).ToList();

                    var unlinkedSession = candidateSessions.FirstOrDefault(t => t.HasPromptBeenDispatched && t.Id == _activeTerminalId)
                                          ?? candidateSessions.FirstOrDefault(t => t.HasPromptBeenDispatched)
                                          ?? candidateSessions.FirstOrDefault(t => t.IsUsed && t.Id == _activeTerminalId)
                                          ?? candidateSessions.FirstOrDefault(t => t.IsUsed)
                                          ?? candidateSessions.FirstOrDefault(t => t.Id == _activeTerminalId);

                    if (unlinkedSession != null)
                    {
                        unlinkedSession.ConversationId = convId;
                        unlinkedSession.IsUsed = true;
                        unlinkedSession.HasPromptBeenDispatched = true;
                        if (unlinkedSession.Id == _initialTerminalId)
                        {
                            _initialTerminalId = null;
                        }
                        _activeUiChatIds[convId] = 1;
                        SaveChatAutoReflexion(convId, unlinkedSession.IsAutoReflexionEnabled);

                        if (!string.IsNullOrEmpty(normChatProjDir) && Directory.Exists(normChatProjDir) &&
                            !string.Equals(normChatProjDir, Services.DatabaseService.NormalizePath(Environment.CurrentDirectory), StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(normChatProjDir, baseFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            unlinkedSession.ProjectPath = normChatProjDir;
                            unlinkedSession.ProjectName = Path.GetFileName(normChatProjDir.TrimEnd('\\', '/'));
                        }

                        TelemetryLogger.Log($"[BrainSync] Dynamically linked tab session {unlinkedSession.Id} (Project: {unlinkedSession.ProjectPath}) to convId {convId}");
                        SaveUiStates();

                            // Prüfen, ob in _allMasterChats bereits ein echter Titel vorliegt
                            string? realTitle = null;
                            lock (_allMasterChats)
                            {
                                var mc = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase));
                                if (mc != null && !string.IsNullOrWhiteSpace(mc.Title) &&
                                    !mc.Title.Equals("Neuer Chat", StringComparison.OrdinalIgnoreCase) &&
                                    !mc.Title.StartsWith("Chat:", StringComparison.OrdinalIgnoreCase))
                                {
                                    realTitle = mc.Title;
                                }
                            }

                            if (!string.IsNullOrEmpty(realTitle) && unlinkedSession.Title != realTitle)
                            {
                                unlinkedSession.Title = realTitle;
                                RenderWorkspaceTabs();
                            }
                            else if (unlinkedSession.Title == "Neuer Chat" || string.IsNullOrWhiteSpace(unlinkedSession.Title))
                            {
                                if (!string.IsNullOrWhiteSpace(fallback))
                                {
                                    unlinkedSession.Title = fallback;
                                    RenderWorkspaceTabs();
                                }
                            }
                    }
                    else
                    {
                        TelemetryLogger.Log($"[BrainSync] Notice: Transcript change for convId '{convId}' (Project: '{normChatProjDir}', CreatedAt: {transcriptTimeUtc:O}, Activity: {activityTimeUtc:O}) not linked to any tab (no matching unlinked session found).");
                    }

                    if (string.Equals(_uiStates.LastActiveTab, "prompts", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdatePromptsTab();
                    }
                });
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoSync ERROR] ProcessTranscriptChanged: {ex.Message}");
        }
    }

    private string ResolveChatProjectPath(string convId)
    {
        if (string.IsNullOrEmpty(convId)) return Services.DatabaseService.NormalizePath(Environment.CurrentDirectory);

        // 1. Check _allMasterChats for the chat's actual recorded Workspace
        AgyChatSession? chat = null;
        if (_allMasterChats != null)
        {
            lock (_allMasterChats)
            {
                chat = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase));
            }
        }
        if (chat != null && !string.IsNullOrWhiteSpace(chat.Workspace))
        {
            string ws = Services.DatabaseService.NormalizePath(chat.Workspace);
            if (Directory.Exists(ws)) return ws;
        }

        // 2. Check workspace_uris directly from SQLite
        try
        {
            string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "conversation_summaries.db");
            if (File.Exists(dbPath))
            {
                using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT workspace_uris FROM conversation_summaries WHERE conversation_id = @id LIMIT 1";
                cmd.Parameters.AddWithValue("@id", convId);
                var val = cmd.ExecuteScalar()?.ToString();
                if (!string.IsNullOrEmpty(val))
                {
                    try
                    {
                        using var uriDoc = System.Text.Json.JsonDocument.Parse(val);
                        if (uriDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && uriDoc.RootElement.GetArrayLength() > 0)
                        {
                            string rawFirst = uriDoc.RootElement[0].GetString() ?? "";
                            string ws = Services.DatabaseService.NormalizePath(rawFirst);
                            if (Directory.Exists(ws)) return ws;
                        }
                    }
                    catch
                    {
                        string unescaped = Uri.UnescapeDataString(val);
                        var match = System.Text.RegularExpressions.Regex.Match(unescaped, @"[A-Za-z]:\\[^""\n\r<\]]+|[A-Za-z]:\/[^""\n\r<\]]+");
                        if (match.Success)
                        {
                            string ws = Services.DatabaseService.NormalizePath(match.Value);
                            if (Directory.Exists(ws)) return ws;
                        }
                    }
                }
            }
        }
        catch { }

        // 3. Fallback: Check if there is an active session explicitly bound to convId
        TerminalTabSession[] sessionsSnapshot;
        try { sessionsSnapshot = _terminalSessions.ToArray(); } catch { sessionsSnapshot = Array.Empty<TerminalTabSession>(); }
        var boundSession = sessionsSnapshot.FirstOrDefault(t => string.Equals(t.ConversationId, convId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(boundSession?.ProjectPath))
        {
            string ws = Services.DatabaseService.NormalizePath(boundSession.ProjectPath);
            if (Directory.Exists(ws)) return ws;
        }

        return Services.DatabaseService.NormalizePath(Environment.CurrentDirectory);
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Timer> _transcriptDebounceTimers = new();

    private void OnBrainTranscriptChanged(object sender, FileSystemEventArgs e)
    {
        lock (_syncLockBrainTimer)
        {
            if (_brainWatcher == null) return;

            if (e.ChangeType == WatcherChangeTypes.Changed || e.ChangeType == WatcherChangeTypes.Created)
            {
                string fullPath = e.FullPath;
                var timer = _transcriptDebounceTimers.GetOrAdd(fullPath, path => new Timer(state =>
                {
                    if (state is string p)
                    {
                        try
                        {
                            ProcessTranscriptChanged(p);
                        }
                        finally
                        {
                            if (_transcriptDebounceTimers.TryRemove(p, out var finishedTimer))
                            {
                                try { finishedTimer.Dispose(); } catch { }
                            }
                        }
                    }
                }, path, Timeout.Infinite, Timeout.Infinite));

                timer.Change(2000, Timeout.Infinite);
            }
        }
        TriggerBrainDebounced();
    }

    private void OnBrainTranscriptRenamed(object sender, RenamedEventArgs e)
    {
        TriggerBrainDebounced();
    }

    private void InitBrainWatcher()
    {
        try
        {
            string brainPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
            if (!Directory.Exists(brainPath))
                Directory.CreateDirectory(brainPath);

            if (_brainWatcher != null)
            {
                _brainWatcher.EnableRaisingEvents = false;
                _brainWatcher.Changed -= OnBrainTranscriptChanged;
                _brainWatcher.Created -= OnBrainTranscriptChanged;
                _brainWatcher.Deleted -= OnBrainTranscriptChanged;
                _brainWatcher.Renamed -= OnBrainTranscriptRenamed;
                _brainWatcher.Dispose();
                _brainWatcher = null;
            }

            if (_dbPollingTimer != null)
            {
                _dbPollingTimer.Stop();
                _dbPollingTimer = null;
            }

            _brainWatcher = new FileSystemWatcher(brainPath, "transcript.jsonl")
            {
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _brainWatcher.Changed += OnBrainTranscriptChanged;
            _brainWatcher.Created += OnBrainTranscriptChanged;
            _brainWatcher.Deleted += OnBrainTranscriptChanged;
            _brainWatcher.Renamed += OnBrainTranscriptRenamed;

            _dbPollingTimer = new DispatcherTimer();
            _dbPollingTimer.Interval = TimeSpan.FromSeconds(10);
            _dbPollingTimer.Tick += async (s, e) =>
            {
                _dbPollingTimer.Stop();
                try { AllowSetForegroundWindow(ASFW_ANY); } catch { }
                try
                {
                    string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "conversation_summaries.db");
                    if (!File.Exists(dbPath)) return;

                    DateTime currentWriteTime = File.GetLastWriteTimeUtc(dbPath);
                    if (currentWriteTime == _lastDbLastWriteTimeUtc) return;

                    string currentHash = await Task.Run(() =>
                    {
                        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
                        conn.Open();
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT conversation_id, title, last_modified_time, preview FROM conversation_summaries WHERE conversation_id NOT LIKE 'sync-knowledge-%' ORDER BY last_modified_time DESC LIMIT 30";
                        using var reader = cmd.ExecuteReader();
                        var hashBuilder = new StringBuilder();
                        while (reader.Read())
                        {
                            string id = reader.IsDBNull(0) ? "" : reader.GetString(0);
                            string dbTitle = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            string modTime = reader.IsDBNull(2) ? "" : reader.GetString(2);
                            string preview = reader.IsDBNull(3) ? "" : reader.GetString(3);
                            hashBuilder.Append($"{id}:{dbTitle}:{modTime}:{preview}|");
                        }
                        return hashBuilder.ToString();
                    });

                    _lastDbLastWriteTimeUtc = currentWriteTime;
                    if (currentHash != _lastDbHash)
                    {
                        TelemetryLogger.Log($"DB Polling: Hash changed! Old length: {_lastDbHash.Length}, New length: {currentHash.Length}. Triggering reload.");
                        if (!string.IsNullOrEmpty(_lastDbHash))
                        {
                            TriggerBrainDebounced();
                        }
                        _lastDbHash = currentHash;
                    }
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"DB Polling Error: {ex.Message}");
                }
                finally
                {
                    _dbPollingTimer?.Start();
                }
            };
            _dbPollingTimer.Start();
        }
        catch { }
    }

    // [WARNUNG] WARNUNG FÜR KI-AGENTEN (Antigravity, Claude, GPT, Gemini, etc.):
    // Die /resume PTY-Methode ist der EINZIGE funktionierende Weg, um AGY CLI dazu zu bringen,
    // Chat-Titel in conversation_summaries.db korrekt zu aktualisieren.
    // Folgende Alternativen wurden AUSFÜHRLICH getestet und funktionieren NICHT:
    //   [X] Direktes SQLite-Schreiben (title/preview) → AGY CLI überschreibt es wieder
    //   [X] Nur DB-Polling ohne /resume → Titel bleiben "Neuer Chat"
    //   [X] FileSystemWatcher + isModelDone Retry-Loops → Fragil, COMExceptions, Timing-Races
    //   [X] REST-API / IPC-Ansätze → Existieren nicht in AGY CLI
    // DIESE METHODE NICHT DURCH ALTERNATIVEN ERSETZEN!

    /// <summary>
    /// Triggered directly whenever the user sends a prompt into the terminal.
    /// Executes the robust 4-step AutoSync waterfall:
    /// 1. Initial 500ms delay -> Sends /resume to hidden sync terminal
    /// 2. 400ms delay -> Sends ESC (\x1b) to close TUI dialog & force AGY CLI to flush SQLite DB
    /// 3. 200ms delay -> Sends ANSI clear (\x1b[2J\x1b[3J\x1b[H) to keep hidden buffer clean
    /// 4. Triggers LoadChatsAsync() UI refresh
    /// </summary>
    private async void TriggerPromptResumeSync()
    {
        if (!await _resumeSyncGate.WaitAsync(0))
        {
            TelemetryLogger.Log("[AutoSync PTY] TriggerPromptResumeSync already running, skipping overlapping run.");
            return;
        }

        try
        {
            InitHiddenSyncTerminal();

            // Readiness check: poll up to 30 retries (3 seconds) until ConPTYTerm is ready
            int retries = 0;
            while ((_hiddenSyncTerminal == null || _hiddenSyncTerminal.ConPTYTerm == null) && retries < 30)
            {
                await System.Threading.Tasks.Task.Delay(100);
                retries++;
            }

            if (_hiddenSyncTerminal?.ConPTYTerm != null)
            {
                // Initial short delay
                await System.Threading.Tasks.Task.Delay(500);

                // Step 1: Send /resume to load transcripts
                TelemetryLogger.Log($"[AutoSync PTY Waterfall] Step 1: Sending '/resume' to hidden sync terminal (ready after {retries * 100}ms).");
                _hiddenSyncTerminal.ConPTYTerm.WriteToTerm("/resume\r");

                // Step 2: 400ms delay + Send ESC (\x1b) to close TUI dialog & force SQLite DB flush
                await System.Threading.Tasks.Task.Delay(400);
                TelemetryLogger.Log("[AutoSync PTY Waterfall] Step 2: Sending ESC (\\x1b) to force AGY CLI SQLite flush.");
                _hiddenSyncTerminal.ConPTYTerm.WriteToTerm("\x1b");

                // Step 3: 200ms delay + Send ANSI Clear to keep hidden terminal buffer clean
                await System.Threading.Tasks.Task.Delay(200);
                TelemetryLogger.Log("[AutoSync PTY Waterfall] Step 3: Sending ANSI Clear.");
                _hiddenSyncTerminal.ConPTYTerm.WriteToTerm("\x1b[2J\x1b[3J\x1b[H");
            }
            else
            {
                TelemetryLogger.Log("[AutoSync PTY ERROR] Hidden sync terminal remained null after 30 retries.");
                DispatcherQueue?.TryEnqueue(() => ShowNotice("AutoSync Hinweis", "Verstecktes Sync-Terminal konnte nicht gestartet werden.", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning));
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoSync PTY WARNING] TriggerPromptResumeSync exception: {ex.Message}");
            DispatcherQueue?.TryEnqueue(() => ShowNotice("AutoSync Fehler", $"Sync-Terminal Fehler: {ex.Message}", Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning));
        }
        finally
        {
            _resumeSyncGate.Release();
        }

        // Step 4: Trigger UI Refresh
        _ = LoadChatsAsync();
    }


    private void CleanSyncKnowledgeChat()
    {
        try
        {
            // 1. Ordner im brain/-Verzeichnis löschen
            string syncDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain", "sync-knowledge-dashboard-sync");
            if (Directory.Exists(syncDir))
            {
                Directory.Delete(syncDir, true);
                TelemetryLogger.Log("[AutoSync Clean] Deleted brain/sync-knowledge-dashboard-sync directory.");
            }

            // 2. DB-Eintrag aus conversation_summaries.db löschen
            string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "conversation_summaries.db");
            if (File.Exists(dbPath))
            {
                using var conn = new SqliteConnection($"Data Source={dbPath};");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM conversation_summaries WHERE conversation_id LIKE 'sync-knowledge-%' OR conversation_id = 'sync-knowledge-dashboard-sync'";
                int rows = cmd.ExecuteNonQuery();
                if (rows > 0)
                {
                    TelemetryLogger.Log($"[AutoSync Clean] Removed {rows} sync entries from conversation_summaries.db.");
                }
            }
        }
        catch { }
    }
}
