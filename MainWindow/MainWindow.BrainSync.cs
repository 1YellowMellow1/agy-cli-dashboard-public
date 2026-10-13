using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private readonly object _syncLockBrainTimer = new object();
    private FileSystemWatcher? _jetboxWatcher;
    private Timer? _jetboxDebounceTimer;
    private FileSystemWatcher? _conversationsWatcher;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Timer> _conversationDebounceTimers = new();
    private DateTime _lastNativeSchemaWarningTime = DateTime.MinValue;

    public void NotifyNativeSchemaWarning(Exception ex)
    {
        if (ex == null) return;
        if ((DateTime.UtcNow - _lastNativeSchemaWarningTime).TotalSeconds < 60) return;
        _lastNativeSchemaWarningTime = DateTime.UtcNow;

        TelemetryLogger.Log($"[NativeSchemaWarning] Raising visible user alert: {ex.Message}");
        DispatcherQueue?.TryEnqueue(() =>
        {
            ShowNotice("Antigravity Status-Warnung",
                $"Natives SQLite-Schema nicht lesbar ({ex.Message}) – Fallback auf PTY-Puffer & Protobuf aktiv. Optimierung greift nicht.",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
        });
    }

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

                var nativeState = Services.DatabaseService.GetNativeConversationWorkingState(convId);
                if (nativeState.SchemaError != null)
                {
                    NotifyNativeSchemaWarning(nativeState.SchemaError);
                }

                bool isWorking = nativeState.Evaluated ? nativeState.IsWorking : CheckIfTranscriptIsWorking(changedFilePath);
                _ = RefreshTranscriptTokensAsync(changedFilePath);

                DispatcherQueue?.TryEnqueue(() => 
                {
                    UpdateTokenPeakMeter(false);
                    var matchingSession = _terminalSessions.FirstOrDefault(t => string.Equals(t.ConversationId, convId, StringComparison.OrdinalIgnoreCase));
                    if (matchingSession == null || isWorking || (DateTime.UtcNow - matchingSession.LastPromptDispatchedUtc).TotalMilliseconds > 3000)
                    {
                        SetTerminalWorkingState(convId, isWorking);
                    }

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
                        if (isWorking && unlinkedSession.Control?.ConPTYTerm != null)
                        {
                            bool isRecentlyDispatched = (DateTime.UtcNow - unlinkedSession.LastPromptDispatchedUtc).TotalMilliseconds <= 3000;
                            if (!isRecentlyDispatched && Helpers.TerminalBufferManager.IsTerminalAtIdlePrompt(unlinkedSession.Control.ConPTYTerm))
                            {
                                isWorking = false;
                            }
                        }
                        unlinkedSession.IsWorking = isWorking;
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

                timer.Change(150, Timeout.Infinite);
            }
        }
        TriggerBrainDebounced();
    }

    private void OnConversationDbChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            string fileName = Path.GetFileName(e.Name ?? "");
            if (string.IsNullOrEmpty(fileName)) return;

            string convId = fileName;
            int dotIdx = convId.IndexOf(".db", StringComparison.OrdinalIgnoreCase);
            if (dotIdx > 0)
            {
                convId = convId.Substring(0, dotIdx);
            }
            if (convId.Length < 30 || convId.StartsWith("sync-knowledge-", StringComparison.OrdinalIgnoreCase)) return;

            var timer = _conversationDebounceTimers.GetOrAdd(convId, cid => new Timer(state =>
            {
                if (state is string id)
                {
                    try
                    {
                        var native = Services.DatabaseService.GetNativeConversationWorkingState(id);
                        if (native.SchemaError != null)
                        {
                            NotifyNativeSchemaWarning(native.SchemaError);
                        }

                        if (native.Evaluated)
                        {
                            DispatcherQueue?.TryEnqueue(() =>
                            {
                                var sess = _terminalSessions.FirstOrDefault(t => string.Equals(t.ConversationId, id, StringComparison.OrdinalIgnoreCase));
                                if (sess != null && !native.IsWorking && (DateTime.UtcNow - sess.LastPromptDispatchedUtc).TotalMilliseconds <= 3000)
                                {
                                    return;
                                }
                                SetTerminalWorkingState(id, native.IsWorking);
                            });
                        }
                    }
                    catch { }
                }
            }, convId, Timeout.Infinite, Timeout.Infinite));

            timer.Change(100, Timeout.Infinite);
        }
        catch { }
    }

    private void OnJetboxSummariesChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            _jetboxDebounceTimer ??= new Timer(_ =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    _ = LoadChatsAsync();
                });
            });
            _jetboxDebounceTimer.Change(250, Timeout.Infinite);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[JetboxWatcher] Debounce error: {ex.Message}");
        }
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

            string convPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "conversations");
            if (Directory.Exists(convPath))
            {
                if (_conversationsWatcher != null)
                {
                    _conversationsWatcher.EnableRaisingEvents = false;
                    _conversationsWatcher.Changed -= OnConversationDbChanged;
                    _conversationsWatcher.Created -= OnConversationDbChanged;
                    _conversationsWatcher.Dispose();
                    _conversationsWatcher = null;
                }

                _conversationsWatcher = new FileSystemWatcher(convPath, "*.db*")
                {
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _conversationsWatcher.Changed += OnConversationDbChanged;
                _conversationsWatcher.Created += OnConversationDbChanged;
            }

            string agyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli");
            if (Directory.Exists(agyDir))
            {
                if (_jetboxWatcher != null)
                {
                    _jetboxWatcher.EnableRaisingEvents = false;
                    _jetboxWatcher.Changed -= OnJetboxSummariesChanged;
                    _jetboxWatcher.Created -= OnJetboxSummariesChanged;
                    _jetboxWatcher.Dispose();
                    _jetboxWatcher = null;
                }

                _jetboxWatcher = new FileSystemWatcher(agyDir, "jetbox_summaries_proto.pb")
                {
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _jetboxWatcher.Changed += OnJetboxSummariesChanged;
                _jetboxWatcher.Created += OnJetboxSummariesChanged;
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
        }
        catch { }
    }

    /// <summary>
    /// Wird nach dem Senden eines Prompts aufgerufen, um die Chat-Liste latenzfrei zu aktualisieren.
    /// Das frühere /resume PTY-Hintergrundterminal wurde vollständig abgelöst durch die
    /// native Protobuf-Feld-15-Auslese (summary_annotations) und den FileSystemWatcher auf jetbox_summaries_proto.pb.
    /// </summary>
    private void TriggerPromptResumeSync()
    {
        _ = LoadChatsAsync();
    }

    public static bool CheckIfTranscriptIsWorking(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length == 0) return false;

            long readLen = Math.Min(131072, fs.Length);
            fs.Seek(-readLen, SeekOrigin.End);

            byte[] buf = new byte[readLen];
            int read = fs.Read(buf, 0, (int)readLen);
            string tail = Encoding.UTF8.GetString(buf, 0, read);

            string[] lines = tail.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return false;

            // 1. Finde den Start des aktuellen Turns (jüngstes USER_INPUT)
            int lastUserIdx = 0;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i];
                if (line.Contains("\"USER_INPUT\"") || line.Contains("\"USER_EXPLICIT\""))
                {
                    lastUserIdx = i;
                    break;
                }
            }

            // 2. Erfasse asynchrone Hintergrund-Tasks und Subagenten im aktuellen Turn
            var activeTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var activeSubagents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = lastUserIdx; i < lines.Length; i++)
            {
                string line = lines[i];

                // Background Task gestartet? (z.B. task-177)
                if (line.Contains("task-") && (line.Contains("\"RUNNING\"") || line.Contains("background task with task id") || line.Contains("Tool is running as a background task")))
                {
                    var matches = Regex.Matches(line, @"task-(\d+)", RegexOptions.IgnoreCase);
                    foreach (Match m in matches)
                    {
                        activeTasks.Add("task-" + m.Groups[1].Value);
                    }
                }
                // Background Task abgeschlossen, abgebrochen oder beendet?
                else if (activeTasks.Count > 0 && (
                    (line.Contains("sender=") && line.Contains("task-")) ||
                    line.Contains("finished with result") ||
                    line.Contains("was canceled") ||
                    line.Contains("was cancelled") ||
                    line.Contains("Tool execution was canceled") ||
                    (line.Contains("Task \"") && (line.Contains("cancelled") || line.Contains("canceled") || line.Contains("killed"))) ||
                    (line.Contains("Task id \"") && (line.Contains("finished") || line.Contains("canceled") || line.Contains("cancelled") || line.Contains("killed") || line.Contains("failed")))
                ))
                {
                    var matches = Regex.Matches(line, @"task-(\d+)", RegexOptions.IgnoreCase);
                    foreach (Match m in matches)
                    {
                        activeTasks.Remove("task-" + m.Groups[1].Value);
                    }
                }

                // Subagent gestartet?
                if (line.Contains("Created the following subagents") || line.Contains("\"name\":\"invoke_subagent\""))
                {
                    var matches = Regex.Matches(line, @"(?:conversationId|conversation_id)[`'\""]?\s*[:=]\s*[`'\""]?([a-f0-9\-]{36})", RegexOptions.IgnoreCase);
                    foreach (Match m in matches)
                    {
                        activeSubagents.Add(m.Groups[1].Value);
                    }
                }
                // Subagent geantwortet oder abgeschlossen?
                else if (activeSubagents.Count > 0 && (
                    line.Contains("sender=") ||
                    line.Contains("\"sender\":") ||
                    (line.Contains("manage_subagents") && (line.Contains("\"kill\"") || line.Contains("'kill'")))
                ))
                {
                    var matches = Regex.Matches(line, @"(?:sender|conversationId|conversation_id)[`'\""]?\s*[:=]\s*[`'\""]?([a-f0-9\-]{36})", RegexOptions.IgnoreCase);
                    foreach (Match m in matches)
                    {
                        activeSubagents.Remove(m.Groups[1].Value);
                    }
                }

                if (line.Contains("\"kill_all\"") || line.Contains("'kill_all'"))
                {
                    activeSubagents.Clear();
                }
            }

            // 3. Wenn asynchrone Tasks oder Subagenten aktiv sind: Tab pulsiert im Hintergrund
            if (activeTasks.Count > 0)
            {
                TelemetryLogger.Log($"[CheckIfTranscriptIsWorking] Active tasks still running: {string.Join(", ", activeTasks)}");
                return true;
            }

            if (activeSubagents.Count > 0)
            {
                TelemetryLogger.Log($"[CheckIfTranscriptIsWorking] Active subagents still running: {string.Join(", ", activeSubagents)}");
                return true;
            }

            // 4. Wenn keine asynchronen Tasks/Subagenten aktiv sind: Letzten signifikanten Schritt rückwärts prüfen
            for (int i = lines.Length - 1; i >= lastUserIdx; i--)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("{")) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    string type = root.TryGetProperty("type", out var tProp) && tProp.ValueKind == JsonValueKind.String ? tProp.GetString() ?? "" : "";
                    string source = root.TryGetProperty("source", out var sProp) && sProp.ValueKind == JsonValueKind.String ? sProp.GetString() ?? "" : "";
                    string status = root.TryGetProperty("status", out var stProp) && stProp.ValueKind == JsonValueKind.String ? stProp.GetString() ?? "" : "";

                    // Fehler- und Abbruchschritte sofort als Inaktivität werten (außer Tool-Fehler vom Typ GENERIC, nach denen die KI weiterarbeitet)
                    if (string.Equals(type, "ERROR_MESSAGE", StringComparison.OrdinalIgnoreCase) ||
                        (!string.Equals(type, "GENERIC", StringComparison.OrdinalIgnoreCase) && string.Equals(status, "ERROR", StringComparison.OrdinalIgnoreCase)) ||
                        string.Equals(status, "INTERRUPTED", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("\"The stream was interrupted") ||
                        line.Contains("\"status\":\"INTERRUPTED\""))
                    {
                        TelemetryLogger.Log($"[CheckIfTranscriptIsWorking] Step {type}/{status} indicates error or interruption. Working state: false.");
                        return false;
                    }

                    // Modell hat geantwortet:
                    if (string.Equals(type, "PLANNER_RESPONSE", StringComparison.OrdinalIgnoreCase))
                    {
                        bool hasTools = false;
                        if (root.TryGetProperty("tool_calls", out var tcProp) &&
                            tcProp.ValueKind == JsonValueKind.Array &&
                            tcProp.GetArrayLength() > 0)
                        {
                            hasTools = true;
                        }

                        if (!hasTools && string.Equals(status, "DONE", StringComparison.OrdinalIgnoreCase))
                        {
                            // Finale Antwort an den Benutzer: Turn abgeschlossen!
                            return false;
                        }

                        if (hasTools)
                        {
                            return true;
                        }
                    }

                    if (string.Equals(type, "USER_INPUT", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(source, "USER_INPUT", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(source, "USER_EXPLICIT", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (string.Equals(type, "GENERIC", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Incomplete leading slice of buffer, skip to previous line
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CheckIfTranscriptIsWorking ERROR] {ex.Message}");
        }

        return false;
    }
}
