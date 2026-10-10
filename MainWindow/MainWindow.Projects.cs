using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private void CmbProjectSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(CmbProjectSelector_SelectionChanged)} triggered.");
        if (CmbProjectSelector == null || CmbProjectSelector.SelectedIndex < 0 || CmbProjectSelector.SelectedItem == null) return;

        string val = GetSelectedProjectName();
        _uiStates.LastSelectedProject = val;
        _projectSettings.LastSelectedProject = val;
        SaveUiStates();
        SaveProjectSettings(refreshUI: false);

        UpdateProjectFocusBadgeUI();

        UpdateActiveTabContent();
        UpdateProjectFileWatcher();

        CloseInitialPlaceholderTab();

        if (_terminalSessions.Count < MaxTerminalTabs)
        {
            bool isGlobal = string.IsNullOrWhiteSpace(val) || val.Contains("Global") || val.Equals("all", StringComparison.OrdinalIgnoreCase);
            string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder();
            string projPath = isGlobal ? baseFolder : Path.Combine(baseFolder, val);

            var session = CreateNewTerminalSession("Neuer Chat", conversationId: null, customStartupCommand: null, projectName: isGlobal ? "Globaler Fokus" : val, projectPath: projPath);
            _initialTerminalId = session.Id;
            SwitchToTerminalTab(session.Id);
            UpdateProjectFocusBadgeUI();
        }
        else
        {
            ShowNotice("Tab-Limit erreicht", $"Es können maximal {MaxTerminalTabs} Tabs gleichzeitig geöffnet sein.", InfoBarSeverity.Warning);
        }
    }

    private (string ProjectName, string ProjectPath) GetActiveTerminalProjectFocus()
    {
        var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
        if (activeSession == null && _terminalSessions.Count > 0)
        {
            activeSession = _terminalSessions[0];
        }

        if (activeSession != null)
        {
            // 1. Priorität: Falls Unterhaltung an dieser Session hängt, aus dem Chat-Workspace auslesen
            if (!string.IsNullOrEmpty(activeSession.ConversationId))
            {
                AgyChatSession? chat = null;
                lock (_allMasterChats)
                {
                    chat = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, activeSession.ConversationId, StringComparison.OrdinalIgnoreCase));
                }
                if (chat == null)
                {
                    chat = _chats.FirstOrDefault(c => string.Equals(c.Id, activeSession.ConversationId, StringComparison.OrdinalIgnoreCase));
                }

                if (chat != null && !string.IsNullOrWhiteSpace(chat.Workspace))
                {
                    string ws = chat.Workspace.Trim();
                    if (ws.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                    {
                        try { ws = Uri.UnescapeDataString(new Uri(ws).LocalPath); } catch { }
                    }

                    string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder();
                    string baseFolderName = Path.GetFileName(baseFolder.TrimEnd('\\', '/'));

                    if (Directory.Exists(ws) || ws.Contains('\\') || ws.Contains('/'))
                    {
                        string cleanWs = ws.Replace('/', '\\').TrimEnd('\\');
                        string folderName = Path.GetFileName(cleanWs);

                        // Wenn der Workspace der Root-Basisordner selbst ist: Echter globaler Fokus!
                        if (string.Equals(cleanWs, baseFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(folderName, baseFolderName, StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrWhiteSpace(folderName))
                        {
                            return ("Globaler Fokus", baseFolder);
                        }

                        return (folderName, cleanWs);
                    }
                    else if (ws.Equals("all", StringComparison.OrdinalIgnoreCase) || ws.Contains("Global", StringComparison.OrdinalIgnoreCase))
                    {
                        return ("Globaler Fokus", baseFolder);
                    }
                    else
                    {
                        string path = Path.Combine(baseFolder, ws);
                        return (ws, path);
                    }
                }
            }

            // 2. Priorität: Gespeicherter Projekt-Fokus an der TerminalSession selbst
            if (!string.IsNullOrWhiteSpace(activeSession.ProjectName))
            {
                string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder();
                bool isGlobal = activeSession.ProjectName.Contains("Global", StringComparison.OrdinalIgnoreCase) || activeSession.ProjectName.Equals("all", StringComparison.OrdinalIgnoreCase);
                string name = isGlobal ? "Globaler Fokus" : activeSession.ProjectName;
                string path = isGlobal ? baseFolder : (string.IsNullOrWhiteSpace(activeSession.ProjectPath) ? GetCurrentProjectPath() : activeSession.ProjectPath);
                return (name, path);
            }
        }

        // Fallback: Aktuelles Dropdown-Projekt
        string selected = GetSelectedProjectName();
        string projPath = GetCurrentProjectPath();
        string fallbackName = (string.IsNullOrWhiteSpace(selected) || selected.Contains("Global")) ? "Globaler Fokus" : selected;
        return (fallbackName, projPath);
    }

    private void UpdateProjectFocusBadgeUI()
    {
        // Rechter Sidebar-Header (Dropdown-Kontext für Dateien/Docs/Git-Karten)
        string activeName = GetSelectedProjectName();
        string rightHeaderText = (string.IsNullOrWhiteSpace(activeName) || activeName.Contains("Global")) ? "Globaler Fokus" : activeName;
        if (TxtActiveProject != null)
        {
            TxtActiveProject.Text = rightHeaderText;
        }

        // Chat-Projektfokusanzeige (ECHTER Fokus der aktiven Terminal-Session)
        var (termProjName, termProjPath) = GetActiveTerminalProjectFocus();
        if (TxtCenterActiveProject != null)
        {
            TxtCenterActiveProject.Text = termProjName;
        }
        if (BdrProjectFocusBadge != null)
        {
            ToolTipService.SetToolTip(BdrProjectFocusBadge, $"Projekt-Fokus: {termProjPath}");
        }

        UpdateActiveProjectGitCardInfo();
        UpdateCustomPromptButtonToolTip();
        UpdateModeButtonVisuals();
        UpdateMaintenanceToolTips();
    }

    private void UpdateActiveTabContent()
    {
        FilterChatsBySelectedProject();

        string selectedProject = GetSelectedProjectName();
        bool isGlobal = string.IsNullOrWhiteSpace(selectedProject) || selectedProject.Contains("Global") || selectedProject.Equals("all", StringComparison.OrdinalIgnoreCase);
        string projectPath = isGlobal ? string.Empty : Path.Combine(_projectSettings.BaseFolder, selectedProject);

        if (string.Equals(_uiStates.LastActiveTab, "prompts", StringComparison.OrdinalIgnoreCase))
        {
            UpdatePromptsTab();
        }

        UpdateFilesTab(isGlobal, projectPath, selectedProject);
        UpdateDocsTab(isGlobal, projectPath, selectedProject);
        UpdateGitTab(isGlobal, projectPath);
    }

    private void BtnCloseInlineSettings_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnCloseInlineSettings_Click)} triggered.");
        SaveProjectSettings();
        if (PnlInlineProjectSettings != null)
        {
            PnlInlineProjectSettings.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnBrowseBaseFolder_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnBrowseBaseFolder_Click)} triggered.");
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                TxtProjectsBaseFolder.Text = folder.Path;
                _projectSettings.BaseFolder = folder.Path;
                ScanAndPopulateProjects();
            }
        }
        catch { }
    }
}
