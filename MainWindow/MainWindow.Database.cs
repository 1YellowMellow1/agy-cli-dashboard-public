using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgyCliDashboard.Services;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private async Task LoadChatsAsync()
    {
        if (Interlocked.CompareExchange(ref _isLoadChatsRunning, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _pendingLoadChatsRequest, 1);
            TelemetryLogger.Log("LoadChatsAsync skipped due to concurrent execution, queued pending refresh.");
            return;
        }
        try
        {
            TelemetryLogger.Log("LoadChatsAsync started.");
            var tempChats = await _databaseService.LoadChatsAsync();

            TelemetryLogger.Log($"LoadChatsAsync: {tempChats.Count} tempChats generated. Syncing with _terminalSessions ({_terminalSessions.Count} active sessions).");

            lock (_allMasterChats)
            {
                _allMasterChats.Clear();
                foreach (var chat in tempChats)
                {
                    _allMasterChats.Add(chat);
                }
            }

            string baseFolder = Services.DatabaseService.NormalizePath(!string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder());
            foreach (var chat in tempChats.OrderByDescending(c => c.DisplayDate))
            {
                var existingSession = _terminalSessions.FirstOrDefault(t => string.Equals(t.ConversationId, chat.Id, StringComparison.OrdinalIgnoreCase));
                if (existingSession == null)
                {
                    // Fallback: Falls der Tab noch ungebunden ist, aber im selben Projekt liegt und zeitnah erstellt wurde.
                    // WICHTIG: Strikter Tab- & Session-Schutz gegen Tab-Hijacking:
                    // 1. Initial-Platzhalter (_initialTerminalId) oder unbenutzte Tabs (!IsUsed && !HasPromptBeenDispatched) dürfen NIEMALS gekapert werden!
                    // 2. Zeitstempel-Abgleich erfolgt strikt auf chat.CreatedAt (wann der Chat entstand), NICHT auf DisplayDate/LastModified (letzte Aktivität)!
                    // 3. Nur brandneue Chats (StepCount <= 2) können aus einem frischen Tab stammen - bestehende Chats mit Historie werden ignoriert.
                    string normChatWs = Services.DatabaseService.NormalizePath(chat.Workspace);
                    if (!string.IsNullOrEmpty(normChatWs) && !string.Equals(normChatWs, baseFolder, StringComparison.OrdinalIgnoreCase) && chat.StepCount <= 2)
                    {
                        DateTime chatCreatedUtc = chat.CreatedAt.Kind == DateTimeKind.Utc
                            ? chat.CreatedAt
                            : chat.CreatedAt.ToUniversalTime();

                        if (chatCreatedUtc != DateTime.MinValue)
                        {
                            var candidateSessions = _terminalSessions.Where(t => 
                                string.IsNullOrEmpty(t.ConversationId) &&
                                (t.IsUsed || t.HasPromptBeenDispatched) &&
                                !string.IsNullOrEmpty(t.ProjectPath) &&
                                string.Equals(Services.DatabaseService.NormalizePath(t.ProjectPath), normChatWs, StringComparison.OrdinalIgnoreCase) &&
                                chatCreatedUtc >= t.CreatedAt.AddSeconds(-30) &&
                                chatCreatedUtc <= t.CreatedAt.AddHours(2) &&
                                t.CreatedAt >= DateTime.UtcNow.AddHours(-12)
                            ).ToList();

                            existingSession = candidateSessions.FirstOrDefault(t => t.HasPromptBeenDispatched && t.Id == _activeTerminalId)
                                              ?? candidateSessions.FirstOrDefault(t => t.HasPromptBeenDispatched)
                                              ?? candidateSessions.FirstOrDefault(t => t.IsUsed && t.Id == _activeTerminalId)
                                              ?? candidateSessions.FirstOrDefault(t => t.IsUsed);

                            if (existingSession != null)
                            {
                                existingSession.ConversationId = chat.Id;
                                existingSession.IsUsed = true;
                                existingSession.HasPromptBeenDispatched = true;
                                if (existingSession.Id == _initialTerminalId)
                                {
                                    _initialTerminalId = null;
                                }
                                _activeUiChatIds[chat.Id] = 1;
                                TelemetryLogger.Log($"LoadChatsAsync: Bound unlinked session {existingSession.Id} to convId '{chat.Id}'.");
                                SaveUiStates();
                            }
                        }
                    }
                }

                if (existingSession != null)
                {
                    if (!string.IsNullOrWhiteSpace(chat.Title) &&
                        !chat.Title.Equals("Neuer Chat", StringComparison.OrdinalIgnoreCase) &&
                        !chat.Title.StartsWith("Chat:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (existingSession.Title != chat.Title)
                        {
                            TelemetryLogger.Log($"LoadChatsAsync: Syncing existing session {existingSession.Id} (ConvId: {existingSession.ConversationId}) to Title: '{chat.Title}'. Old Title was: '{existingSession.Title}'.");
                            existingSession.Title = chat.Title;
                        }
                    }
                }
            }

            TelemetryLogger.Log("LoadChatsAsync: Calling FilterChatsBySelectedProject and RenderWorkspaceTabs.");
            try
            {
                FilterChatsBySelectedProject();
                RenderWorkspaceTabs();
                TelemetryLogger.Log("LoadChatsAsync: Filter and Render completed successfully.");
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"LoadChatsAsync ERROR: {ex.Message}\n{ex.StackTrace}");
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isLoadChatsRunning, 0);
            if (Interlocked.Exchange(ref _pendingLoadChatsRequest, 0) == 1)
            {
                TelemetryLogger.Log("LoadChatsAsync: Executing queued pending refresh.");
                _ = Task.Delay(1200).ContinueWith(_ => DispatcherQueue?.TryEnqueue(() => _ = LoadChatsAsync()));
            }
        }
    }

    private async Task DeleteChatFolderAsync(string convId)
    {
        await DeleteChatsAsync([convId]);
    }

    private async Task DeleteChatsAsync(IEnumerable<string> convIds)
    {
        var allDeletedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in convIds)
        {
            var deleted = await _databaseService.DeleteChatSessionAsync(id);
            foreach (var d in deleted) allDeletedIds.Add(d);
        }

        try
        {
            var matchingSessions = _terminalSessions
                .Where(s => !string.IsNullOrEmpty(s.ConversationId) && allDeletedIds.Contains(s.ConversationId))
                .ToList();

            foreach (var sess in matchingSessions)
            {
                TelemetryLogger.Log($"[DeleteChat] Closing active terminal tab session {sess.Id} for conv {sess.ConversationId}");
                DisposeTerminalSession(sess);
                TerminalContainer?.Children.Remove(sess.Control);
                _terminalSessions.Remove(sess);
                if (_activeTerminalId == sess.Id)
                {
                    _activeTerminalId = string.Empty;
                }
            }
            RenderWorkspaceTabs();
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[DeleteChat Terminal Cleanup Error] {ex.Message}");
        }

        lock (_allMasterChats)
        {
            _allMasterChats.RemoveAll(c => allDeletedIds.Contains(c.Id));
        }
        foreach (var id in allDeletedIds)
        {
            _pinnedChatIds.Remove(id);
        }

        EnsureAtLeastOneTerminalSession();
        if (!string.IsNullOrEmpty(_activeTerminalId))
        {
            SwitchToTerminalTab(_activeTerminalId);
            RefreshActiveTerminal(forceRestart: false, showNotice: false);
        }
        SavePinnedChatIds();
        FilterChatsBySelectedProject();
    }

    // [WARNUNG] VERBOT FÜR KI-AGENTEN (Antigravity, Claude, GPT, Gemini, etc.):
    // Diese Methode darf KEINE Chat-Titel (title, preview) in die SQLite-Datenbank schreiben!
    // Chat-Titel werden AUSSCHLIESSLICH von AGY CLI selbst generiert und via /resume aktualisiert.
    // Jeder Versuch, hier eigene Fallback-Titel zu setzen (z.B. aus dem ersten User-Prompt),
    // führt dazu, dass der AGY-generierte Titel überschrieben oder blockiert wird.
    // ERLAUBT: step_count, last_user_input_step_index, last_modified_time, workspace_uris
    // VERBOTEN: title, preview – FINGER WEG!
    private void EnsureChatSummaryInDb(string convId, string transcriptFilePath)
    {
        try
        {
            if (string.IsNullOrEmpty(convId) || !File.Exists(transcriptFilePath)) return;

            string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "conversation_summaries.db");
            if (!File.Exists(dbPath)) return;

            int maxStep = 0;
            int lastUserStep = -1;

            using (var fs = new FileStream(transcriptFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("step_index", out var stepProp) && stepProp.TryGetInt32(out int step))
                        {
                            if (step > maxStep) maxStep = step;
                            if (root.TryGetProperty("source", out var srcProp) && srcProp.GetString() == "USER_EXPLICIT")
                            {
                                lastUserStep = step;
                            }
                        }
                    }
                    catch { }
                }
            }

            int stepCount = maxStep + 1;
            string nowUtc = DateTime.UtcNow.ToString("o");
            
            string baseFolder = Services.DatabaseService.NormalizePath(!string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder());
            string workspacePath = baseFolder;
            try
            {
                var masterChatsRef = _allMasterChats;
                if (masterChatsRef != null)
                {
                    AgyChatSession? existingChat;
                    lock (masterChatsRef)
                    {
                        existingChat = masterChatsRef.FirstOrDefault(c => string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase));
                    }
                    if (existingChat != null && !string.IsNullOrEmpty(existingChat.Workspace) && !string.Equals(Services.DatabaseService.NormalizePath(existingChat.Workspace), baseFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        workspacePath = Services.DatabaseService.NormalizePath(existingChat.Workspace);
                    }
                }

                if (string.Equals(workspacePath, baseFolder, StringComparison.OrdinalIgnoreCase))
                {
                    TerminalTabSession? boundSession = null;
                    TerminalTabSession? activeSession = null;
                    try
                    {
                        var sessionsSnapshot = _terminalSessions.ToArray();
                        boundSession = sessionsSnapshot.FirstOrDefault(t => string.Equals(t.ConversationId, convId, StringComparison.OrdinalIgnoreCase));
                        activeSession = sessionsSnapshot.FirstOrDefault(t => t.Id == _activeTerminalId);
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(boundSession?.ProjectPath) && Directory.Exists(boundSession.ProjectPath) && !string.Equals(Services.DatabaseService.NormalizePath(boundSession.ProjectPath), baseFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        workspacePath = Services.DatabaseService.NormalizePath(boundSession.ProjectPath);
                    }
                    else if (!string.IsNullOrEmpty(activeSession?.ProjectPath) && Directory.Exists(activeSession.ProjectPath) && !string.Equals(Services.DatabaseService.NormalizePath(activeSession.ProjectPath), baseFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        workspacePath = Services.DatabaseService.NormalizePath(activeSession.ProjectPath);
                    }
                }
            }
            catch { }

            using (var conn = new SqliteConnection($"Data Source={dbPath};Default Timeout=10;"))
            {
                conn.Open();
                string workspaceUri = "file:///" + workspacePath.Replace('\\', '/');
                string workspaceJson = System.Text.Json.JsonSerializer.Serialize(new[] { workspaceUri });

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO conversation_summaries (conversation_id, parent_conversation_id, title, preview, workspace_uris, step_count, last_user_input_step_index, last_modified_time, last_user_input_time)
                    VALUES (@id, '', 'Neuer Chat', '', @workspaceUris, @stepCount, @lastUserStep, @nowUtc, @nowUtc)
                    ON CONFLICT(conversation_id) DO UPDATE SET
                        step_count = excluded.step_count,
                        last_user_input_step_index = CASE WHEN excluded.last_user_input_step_index >= 0 THEN excluded.last_user_input_step_index ELSE last_user_input_step_index END,
                        last_modified_time = excluded.last_modified_time,
                        last_user_input_time = excluded.last_user_input_time;";
                cmd.Parameters.AddWithValue("@id", convId);
                cmd.Parameters.AddWithValue("@workspaceUris", workspaceJson);
                cmd.Parameters.AddWithValue("@stepCount", stepCount);
                cmd.Parameters.AddWithValue("@lastUserStep", lastUserStep);
                cmd.Parameters.AddWithValue("@nowUtc", nowUtc);
                cmd.ExecuteNonQuery();

                TelemetryLogger.Log($"[AutoSync Direct DB] Ensured metadata for convId '{convId}' (StepCount: {stepCount}, LastUserStep: {lastUserStep}). Title NOT touched – AGY CLI only.");
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoSync Direct DB ERROR] {ex.Message}");
        }
    }


}

