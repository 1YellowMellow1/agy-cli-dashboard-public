using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private HashSet<string> _pinnedChatIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _isMultiSelectMode = false;
    private readonly HashSet<string> _openingConvIds = new(StringComparer.OrdinalIgnoreCase);

    private void UcChatSidebar_OpenAgyChatRequested(object? sender, string convId)
    {
        AgyChatSession? chat = null;
        lock (_allMasterChats)
        {
            chat = _allMasterChats.FirstOrDefault(c => c.Id == convId);
        }
        chat ??= _chats.FirstOrDefault(c => c.Id == convId);
        _ = OpenChatWithAgent(chat, convId, "agy");
    }

    private void UcChatSidebar_OpenClawChatRequested(object? sender, string convId)
    {
        AgyChatSession? chat = null;
        lock (_allMasterChats)
        {
            chat = _allMasterChats.FirstOrDefault(c => c.Id == convId);
        }
        chat ??= _chats.FirstOrDefault(c => c.Id == convId);
        _ = OpenChatWithAgent(chat, convId, "clawcode");
    }

    private void UcChatSidebar_PinToggled(object? sender, string convId)
    {
        if (_pinnedChatIds.Contains(convId)) _pinnedChatIds.Remove(convId);
        else _pinnedChatIds.Add(convId);
        SavePinnedChatIds();
        FilterChatsBySelectedProject();
    }

    private async void UcChatSidebar_SingleChatDeleted(object? sender, string targetId)
    {
        await DeleteChatFolderAsync(targetId);
        ShowNotice("Chat gelöscht", "Der ausgewählte Chat wurde erfolgreich gelöscht.", InfoBarSeverity.Informational);
    }

    private async void UcChatSidebar_MultiChatsDeleted(object? sender, List<string> targetIds)
    {
        await DeleteChatsAsync(targetIds);
        ShowNotice("Chats gelöscht", $"{targetIds.Count} Chats wurden erfolgreich gelöscht.", InfoBarSeverity.Informational);
    }

    private void UcChatSidebar_MultiSelectChanged(object? sender, bool isMultiSelect)
    {
        _isMultiSelectMode = isMultiSelect;
        if (BtnChatsMore == null) return;
        if (isMultiSelect)
        {
            BtnChatsMore.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x4D, 0xEF, 0x44, 0x44));
            BtnChatsMore.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x80, 0xEF, 0x44, 0x44));
        }
        else
        {
            BtnChatsMore.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            BtnChatsMore.BorderBrush = (Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["AgyBorderFaint"];
        }
    }

    private void BtnUnpinAll_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnUnpinAll_Click)} triggered.");
        _pinnedChatIds.Clear();
        SavePinnedChatIds();
        FilterChatsBySelectedProject();
    }

    private void BtnDeleteAllChats_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnDeleteAllChats_Click)} triggered.");
        UcChatSidebar?.SetMultiSelectMode(!_isMultiSelectMode);
    }

    private void LoadPinnedChatIds()
    {
        try
        {
            var list = Services.FileTimestampCache.GetOrLoadSync(_pinnedFilePath, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<List<string>>(json, _jsonOptions);
                }
                return null;
            });
            if (list != null)
            {
                _pinnedChatIds = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { }
    }

    private void SavePinnedChatIds()
    {
        try
        {
            string json = JsonSerializer.Serialize(_pinnedChatIds.ToList(), _jsonOptions);
            WriteAllTextAtomic(_pinnedFilePath, json);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SavePinnedChatIds error: {ex.Message}");
        }
    }

    private void FilterChatsBySelectedProject()
    {
        string selectedProject = GetSelectedProjectName();
        bool isGlobal = string.IsNullOrWhiteSpace(selectedProject) || selectedProject.Contains("Global") || selectedProject.Equals("all", StringComparison.OrdinalIgnoreCase);
        string targetProjectPath = isGlobal ? string.Empty : Path.Combine(_projectSettings.BaseFolder, selectedProject);

        AgyChatSession[] snapshot;
        lock (_allMasterChats)
        {
            snapshot = _allMasterChats.ToArray();
        }
        var filtered = new List<AgyChatSession>();
        foreach (var chat in snapshot)
        {
            chat.IsPinned = _pinnedChatIds.Contains(chat.Id);
            chat.IsSelectionModeActive = _isMultiSelectMode;

            if (isGlobal)
            {
                filtered.Add(chat);
            }
            else
            {
                if (!string.IsNullOrEmpty(chat.Workspace))
                {
                    try
                    {
                        string wsUnescaped = Uri.UnescapeDataString(chat.Workspace);
                        string wsClean = wsUnescaped.Replace('/', '\\').TrimEnd('\\');
                        string wsName = Path.GetFileName(wsClean);

                        if (wsName.Equals(selectedProject, StringComparison.OrdinalIgnoreCase) ||
                            wsClean.IndexOf(selectedProject, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            wsUnescaped.IndexOf(selectedProject, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            filtered.Add(chat);
                        }
                    }
                    catch (Exception ex)
                    {
                        TelemetryLogger.Log($"FilterChatsBySelectedProject error parsing workspace '{chat.Workspace}': {ex.Message}");
                    }
                }
                else
                {
                    bool belongsToCurrentTerminal = _terminalSessions.Any(t => string.Equals(t.ConversationId, chat.Id, StringComparison.OrdinalIgnoreCase));
                    bool recentlyCreated = (DateTime.Now - chat.CreatedAt).TotalMinutes < 30;
                    if (belongsToCurrentTerminal || recentlyCreated)
                    {
                        chat.Workspace = targetProjectPath;
                        filtered.Add(chat);
                    }
                }
            }
        }

        var newOrder = filtered.OrderByDescending(c => c.IsPinned).ThenByDescending(c => c.DisplayDate).ToList();

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_chats.Count == 0)
            {
                foreach (var chat in newOrder)
                {
                    _chats.Add(chat);
                }
                return;
            }

            for (int i = _chats.Count - 1; i >= 0; i--)
            {
                if (!newOrder.Any(nc => nc.Id == _chats[i].Id))
                {
                    _chats.RemoveAt(i);
                }
            }

            for (int i = 0; i < newOrder.Count; i++)
            {
                var newChat = newOrder[i];
                var existingChat = _chats.FirstOrDefault(c => c.Id == newChat.Id);

                if (existingChat != null)
                {
                    existingChat.Title = newChat.Title;
                    existingChat.LastModified = newChat.LastModified;
                    existingChat.StepCount = newChat.StepCount;
                    existingChat.Workspace = newChat.Workspace;
                    existingChat.IsPinned = newChat.IsPinned;
                    existingChat.IsSelectionModeActive = newChat.IsSelectionModeActive;

                    int currentIndex = _chats.IndexOf(existingChat);
                    if (currentIndex != i)
                    {
                        _chats.Move(currentIndex, i);
                    }
                }
                else
                {
                    _chats.Insert(i, newChat);
                }
            }
        });
    }

    private async System.Threading.Tasks.Task OpenChatWithAgent(AgyChatSession? chat, string convId, string agentType)
    {
        if (string.IsNullOrEmpty(convId)) return;

        lock (_openingConvIds)
        {
            if (!_openingConvIds.Add(convId))
            {
                TelemetryLogger.Log($"[OpenChatWithAgent] Chat {convId} is already opening in-flight. Ignoring duplicate call.");
                return;
            }
        }

        try
        {
            var conflictingSessions = _terminalSessions.Where(t => t.ConversationId == convId && t.AgentType != agentType).ToList();
            foreach (var sess in conflictingSessions)
            {
                TelemetryLogger.Log($"[OpenChatWithAgent] Closing conflicting tab session {sess.Id} (Agent: {sess.AgentType}) for conv {convId}");
                DisposeTerminalSession(sess);
                TerminalContainer?.Children.Remove(sess.Control);
                _terminalSessions.Remove(sess);
            }

            var existingTab = _terminalSessions.FirstOrDefault(t => t.ConversationId == convId && t.AgentType == agentType);
            if (existingTab != null)
            {
                CloseInitialPlaceholderTab(exceptSessionId: existingTab.Id);
                SwitchToTerminalTab(existingTab.Id);
                return;
            }

            bool hasUnusedPlaceholder = !string.IsNullOrEmpty(_initialTerminalId) && _terminalSessions.Any(t => t.Id == _initialTerminalId && !t.IsUsed && !t.HasPromptBeenDispatched);
            if (_terminalSessions.Count - (hasUnusedPlaceholder ? 1 : 0) >= MaxTerminalTabs)
            {
                ShowNotice("Tab-Limit erreicht", $"Es können maximal {MaxTerminalTabs} Tabs gleichzeitig geöffnet sein.", InfoBarSeverity.Warning);
                return;
            }

        string title = chat?.Title ?? "Chat";
        string? customCmd = null;

        string savedModel = LoadChatModel(convId);
        if (agentType == "clawcode")
        {
            if (string.IsNullOrEmpty(savedModel) || !savedModel.Contains("/") || savedModel.Contains(" "))
            {
                savedModel = LoadClawCodeModelFromSettings();
            }
            if (string.IsNullOrEmpty(savedModel) || !savedModel.Contains("/") || savedModel.Contains(" "))
            {
                savedModel = ProjectSettingsData.DefaultClawCodeModel;
            }

            WriteModelToClawSettings(savedModel);
        }
        else
        {
            if (string.IsNullOrEmpty(savedModel))
            {
                string brainPath = ResolveActiveTranscriptPath(convId);
                if (_transcriptTokensCache.TryGetValue(brainPath, out var cached) && !string.IsNullOrWhiteSpace(cached.DetectedModel))
                {
                    savedModel = cached.DetectedModel;
                }
            }
            if (string.IsNullOrEmpty(savedModel))
            {
                savedModel = LoadAgyModelFromSettings();
            }
        }

        if (!string.IsNullOrEmpty(savedModel))
        {
            if (TxtModelName != null) CurrentModelId = savedModel;
            if (chat != null) chat.SelectedModel = savedModel;
            SaveChatModel(convId, savedModel);
        }

        bool autoReflexion = LoadChatAutoReflexion(convId);
        if (chat != null) chat.IsAutoReflexionEnabled = autoReflexion;

        string resolvedProjName = string.Empty;
        string resolvedProjPath = string.Empty;
        string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder();
        string baseFolderName = Path.GetFileName(baseFolder.TrimEnd('\\', '/'));

        if (chat != null && !string.IsNullOrWhiteSpace(chat.Workspace))
        {
            string ws = chat.Workspace.Trim();
            if (ws.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            {
                try { ws = Uri.UnescapeDataString(new Uri(ws).LocalPath); } catch { }
            }

            string cleanWs = ws.Replace('/', '\\').TrimEnd('\\');
            string folderName = Path.GetFileName(cleanWs);

            if (Directory.Exists(cleanWs) &&
                !string.Equals(cleanWs, baseFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(folderName, baseFolderName, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(folderName))
            {
                resolvedProjName = folderName;
                resolvedProjPath = cleanWs;
            }
            else if (!ws.Equals("all", StringComparison.OrdinalIgnoreCase) && !ws.Contains("Global", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(baseFolder, ws);
                if (Directory.Exists(candidate))
                {
                    resolvedProjName = ws;
                    resolvedProjPath = candidate;
                }
            }
        }

        if (string.IsNullOrEmpty(resolvedProjName))
        {
            string selectedProject = GetSelectedProjectName();
            bool isGlobal = string.IsNullOrWhiteSpace(selectedProject) || selectedProject.Contains("Global") || selectedProject.Equals("all", StringComparison.OrdinalIgnoreCase);
            resolvedProjName = isGlobal ? "Globaler Fokus" : selectedProject;
            resolvedProjPath = isGlobal ? baseFolder : Path.Combine(baseFolder, selectedProject);
        }

        if (agentType == "clawcode")
        {
            if (!TryBuildClawCodeStartupCommand(convId, resolvedProjPath, ref title, out customCmd, out string? error))
            {
                ShowNotice(error?.Contains("OPENROUTER") == true ? "OpenRouter Key fehlt" : "ClawCode Fehler", error ?? "Fehler beim Starten von ClawCode", InfoBarSeverity.Error);
                return;
            }
        }
        else
        {
            title = $"\U0001F680 {title}";
            if (!string.IsNullOrEmpty(convId))
            {
                string transcriptPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain", convId, ".system_generated", "logs", "transcript.jsonl");
                EnsureChatSummaryInDb(convId, transcriptPath);
            }
        }

        // 1. Tab-Limit erneut prüfen (falls während des asynchronen Delays andere Tabs geöffnet wurden)
        bool hasPlaceholderAfterSync = !string.IsNullOrEmpty(_initialTerminalId) && _terminalSessions.Any(t => t.Id == _initialTerminalId && !t.IsUsed && !t.HasPromptBeenDispatched);
        if (_terminalSessions.Count - (hasPlaceholderAfterSync ? 1 : 0) >= MaxTerminalTabs)
        {
            ShowNotice("Tab-Limit erreicht", $"Es können maximal {MaxTerminalTabs} Tabs gleichzeitig geöffnet sein.", InfoBarSeverity.Warning);
            return;
        }

        // 2. Re-Check auf bestehende oder konfligierende Sessions nach dem asynchronen Delay
        var conflictingAfterSync = _terminalSessions.Where(t => t.ConversationId == convId && t.AgentType != agentType).ToList();
        foreach (var sess in conflictingAfterSync)
        {
            TelemetryLogger.Log($"[OpenChatWithAgent] Closing conflicting tab session {sess.Id} (Agent: {sess.AgentType}) for conv {convId}");
            DisposeTerminalSession(sess);
            TerminalContainer?.Children.Remove(sess.Control);
            _terminalSessions.Remove(sess);
        }

        var existingAfterSync = _terminalSessions.FirstOrDefault(t => t.ConversationId == convId && t.AgentType == agentType);
        if (existingAfterSync != null)
        {
            CloseInitialPlaceholderTab(exceptSessionId: existingAfterSync.Id);
            SwitchToTerminalTab(existingAfterSync.Id);
            return;
        }

        // 3. Unbenutzten Initial-Platzhalter jetzt unmittelbar vor Erstellung der neuen Session sauber schließen
        CloseInitialPlaceholderTab();

        var newTermSession = CreateNewTerminalSession(title, convId, customCmd, projectName: resolvedProjName, projectPath: resolvedProjPath);
        newTermSession.ConversationId = convId;
        newTermSession.AgentType = agentType;

        SwitchToTerminalTab(newTermSession.Id);
    }
    finally
    {
        lock (_openingConvIds)
        {
            _openingConvIds.Remove(convId);
        }
    }
}

    private void BtnNewChat_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnNewChat_Click)} triggered.");

        CloseInitialPlaceholderTab();

        if (_terminalSessions.Count >= MaxTerminalTabs)
        {
            ShowNotice("Tab-Limit erreicht", $"Es können maximal {MaxTerminalTabs} Tabs gleichzeitig geöffnet sein.", InfoBarSeverity.Warning);
            return;
        }

        string projName = GetSelectedProjectName();
        bool isGlobal = string.IsNullOrWhiteSpace(projName) || projName.Contains("Global") || projName.Equals("all", StringComparison.OrdinalIgnoreCase);
        string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder();
        string projPath = isGlobal ? baseFolder : Path.Combine(baseFolder, projName);

        var session = CreateNewTerminalSession("Neuer Chat", conversationId: null, customStartupCommand: null, projectName: isGlobal ? "Globaler Fokus" : projName, projectPath: projPath);
        _initialTerminalId = session.Id;
        SwitchToTerminalTab(session.Id);
        UpdateProjectFocusBadgeUI();
    }

    private void BtnRefreshSidebar_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnRefreshSidebar_Click)} triggered.");
        _ = LoadChatsAsync();
        ScanAndPopulateProjects();
        UpdateActiveTabContent();
    }
}
