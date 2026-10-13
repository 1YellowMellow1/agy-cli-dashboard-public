using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AgyCliDashboard.Helpers;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private string _ruleControlPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "rule_control.json");
    private string _projectRulesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "project_rules.json");
    private string _uiStatesFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "ui_states.json");

    private void LoadProjectSettings()
    {
        try
        {
            var loaded = Services.FileTimestampCache.GetOrLoadSync(_settingsFilePath, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<ProjectSettingsData>(json, _jsonOptions);
                }
                return null;
            });
            if (loaded != null)
            {
                _projectSettings = loaded;
                if (_projectSettings.TerminalFontSize >= 8 && _projectSettings.TerminalFontSize <= 72)
                {
                    _baseTerminalFontSize = _projectSettings.TerminalFontSize;
                    _fontSize = _baseTerminalFontSize;
                    if (TxtFontSize != null) TxtFontSize.Text = _fontSize.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Settings] Error loading project settings: {ex.Message}");
        }

        _projectSettings ??= new ProjectSettingsData();

        if (_projectSettings.AutoPushIntervalMinutes <= 0)
        {
            _projectSettings.AutoPushIntervalMinutes = 30;
        }

        if (_projectSettings.DisabledAutoReflexionProjects == null)
        {
            _projectSettings.DisabledAutoReflexionProjects = new List<string>();
        }

        if (string.IsNullOrWhiteSpace(_projectSettings.BaseFolder) || !Directory.Exists(_projectSettings.BaseFolder))
        {
            _projectSettings.BaseFolder = ProjectSettingsData.GetDefaultBaseFolder();
        }

        TxtProjectsBaseFolder.Text = _projectSettings.BaseFolder;
        ScanAndPopulateProjects();
    }

    private void ScanAndPopulateProjects()
    {
        _allProjects.Clear();
        if (Directory.Exists(_projectSettings.BaseFolder))
        {
            try
            {
                var dirs = Directory.GetDirectories(_projectSettings.BaseFolder);
                var tempProjects = new List<ProjectItemModel>();
                foreach (var dir in dirs)
                {
                    string dirName = Path.GetFileName(dir);
                    bool isHidden = _projectSettings.HiddenProjects.Contains(dirName, StringComparer.OrdinalIgnoreCase);
                    bool isAutoReflexion = !(_projectSettings.DisabledAutoReflexionProjects?.Contains(dirName, StringComparer.OrdinalIgnoreCase) ?? false);
                    var item = new ProjectItemModel
                    {
                        Name = dirName,
                        Path = dir,
                        IsVisible = !isHidden,
                        IsAutoReflexionEnabled = isAutoReflexion
                    };
                    EnsureProjectTodoFile(dir, dirName);
                    tempProjects.Add(item);
                }

                // Sort: Projects alphabetically
                var sorted = tempProjects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase);

                foreach (var p in sorted)
                {
                    _allProjects.Add(p);
                }
            }
            catch { }
        }
        UpdateProjectComboBox();
    }

    private static void EnsureProjectTodoFile(string dir, string dirName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
            if (!string.IsNullOrWhiteSpace(dirName) && dirName.EndsWith("-public", StringComparison.OrdinalIgnoreCase)) return;
            string todoPath = Path.Combine(dir, "TODO.md");
            string todoLower = Path.Combine(dir, "todo.md");
            if (!File.Exists(todoPath) && !File.Exists(todoLower))
            {
                string content = $"# {dirName} TODOs & Notizen\r\n\r\n- [ ] Erste Projektaufgabe erfassen...\r\n";
                File.WriteAllText(todoPath, content, System.Text.Encoding.UTF8);
            }
        }
        catch { }
    }

    public bool GetProjectAutoReflexionDefault(string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return true;
        }

        if (projectName.Contains('/') || projectName.Contains('\\'))
        {
            projectName = Path.GetFileName(projectName.TrimEnd('\\', '/'));
        }

        if (string.IsNullOrWhiteSpace(projectName) ||
            projectName.Contains("Global", StringComparison.OrdinalIgnoreCase) ||
            projectName.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_projectSettings?.DisabledAutoReflexionProjects != null &&
            _projectSettings.DisabledAutoReflexionProjects.Contains(projectName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private void SaveProjectSettings(bool refreshUI = true)
    {
        try
        {
            _projectSettings.BaseFolder = !string.IsNullOrWhiteSpace(TxtProjectsBaseFolder?.Text) 
                ? TxtProjectsBaseFolder.Text 
                : ProjectSettingsData.GetDefaultBaseFolder();
            _projectSettings.HiddenProjects = _allProjects
                .Where(p => !p.IsVisible)
                .Select(p => p.Name)
                .ToList();
            _projectSettings.DisabledAutoReflexionProjects = _allProjects
                .Where(p => !p.IsAutoReflexionEnabled)
                .Select(p => p.Name)
                .ToList();

            string? dir = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(_projectSettings, _jsonOptions);
            WriteAllTextAtomic(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SaveProjectSettings error: {ex.Message}");
        }

        SyncRulesToGeminiMd();

        if (refreshUI)
        {
            UpdateProjectComboBox();
        }
    }

    private void LoadUiStates()
    {
        try
        {
            var loaded = Services.FileTimestampCache.GetOrLoadSync(_uiStatesFilePath, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<UiStatesData>(json, _jsonOptions);
                }
                return null;
            });
            if (loaded != null)
            {
                _uiStates = loaded;
                _longPressTarget = _uiStates.LongPressTarget ?? string.Empty;
                _isDeepThinkSticky = _uiStates.IsDeepThinkSticky;
                if (_isDeepThinkSticky) _isDeepThinkEnabled = true;
                if (ChkRestoreSessions != null) ChkRestoreSessions.IsChecked = _uiStates.RestoreOpenSessionsOnStartup;
            }
            else if (File.Exists(_lastProjectFilePath))
            {
                string proj = File.ReadAllText(_lastProjectFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(proj))
                {
                    _uiStates.LastSelectedProject = proj;
                }
            }
            else if (!string.IsNullOrWhiteSpace(_projectSettings.LastSelectedProject))
            {
                _uiStates.LastSelectedProject = _projectSettings.LastSelectedProject;
            }
            _uiStates.OpenSessions ??= new();
        }
        catch { }
    }

    private void SaveUiStates()
    {
        if (_isRestoringSessions) return;
        try
        {
            _uiStates.LastSelectedProject = GetSelectedProjectName();

            if (_isSessionsRestored)
            {
                if (_uiStates.RestoreOpenSessionsOnStartup)
                {
                    var sessionsToSave = new List<SavedChatSessionState>();
                    foreach (var s in _terminalSessions)
                    {
                        if (!string.IsNullOrWhiteSpace(s.ConversationId))
                        {
                            sessionsToSave.Add(new SavedChatSessionState
                            {
                                ConversationId = s.ConversationId,
                                Title = s.Title,
                                AgentType = s.AgentType,
                                ProjectName = s.ProjectName,
                                ProjectPath = s.ProjectPath,
                                IsActive = (s.Id == _activeTerminalId),
                                IsAutoReflexionEnabled = s.IsAutoReflexionEnabled
                            });
                        }
                    }
                    _uiStates.OpenSessions = sessionsToSave;

                    var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
                    _uiStates.LastActiveConversationId = activeSession?.ConversationId;
                }
                else
                {
                    _uiStates.OpenSessions.Clear();
                    _uiStates.LastActiveConversationId = null;
                }
            }

            string json = JsonSerializer.Serialize(_uiStates, _jsonOptions);
            WriteAllTextAtomic(_uiStatesFilePath, json);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SaveUiStates error: {ex.Message}");
        }
    }

    private void RestoreUiLayout()
    {
        _isSidebarVisible = _uiStates.IsSidebarVisible;
        ApplySidebarWidth();
    }

    private void RestoreActiveTab()
    {
        string tabTag = _uiStates.LastActiveTab ?? "chats";
        Button? targetBtn = tabTag switch
        {
            "prompts" => TabPromptsBtn,
            "files" => TabFilesBtn,
            "docs" => TabDocsBtn,
            "git" => TabGitBtn,
            "notes" => TabNotesBtn,
            "todos" => TabNotesBtn,
            _ => TabChatsBtn
        };

        if (targetBtn != null)
        {
            TabNav_Click(targetBtn, new RoutedEventArgs());
        }
    }

    private string GetSelectedProjectName()
    {
        if (CmbProjectSelector?.SelectedItem != null)
        {
            if (CmbProjectSelector.SelectedItem is ProjectSelectorItemModel psi)
            {
                if (!string.IsNullOrWhiteSpace(psi.Name))
                {
                    return psi.Name;
                }
            }

            string raw = CmbProjectSelector.SelectedItem is ComboBoxItem item
                ? (item.Content?.ToString() ?? string.Empty)
                : (CmbProjectSelector.SelectedItem.ToString() ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(raw))
            {
                return raw;
            }
        }

        if (!string.IsNullOrWhiteSpace(_uiStates.LastSelectedProject))
        {
            return _uiStates.LastSelectedProject;
        }

        return "Alle Projekte (Global)";
    }

    private void UpdateProjectComboBox()
    {
        if (CmbProjectSelector == null) return;

        string target = _uiStates.LastSelectedProject ?? string.Empty;

        CmbProjectSelector.SelectionChanged -= CmbProjectSelector_SelectionChanged;

        var itemsList = new List<ProjectSelectorItemModel>
        {
            new ProjectSelectorItemModel
            {
                Name = "Alle Projekte (Global)",
                DisplayName = "Alle Projekte (Global)",
                IsGlobal = true,
                IsBoss = false
            }
        };

        // Sort: Projects alphabetically
        var visibleProjects = _allProjects.Where(p => p.IsVisible).ToList();
        var sortedProjects = visibleProjects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var proj in sortedProjects)
        {
            bool isBoss = false;
            itemsList.Add(new ProjectSelectorItemModel
            {
                Name = proj.Name,
                DisplayName = proj.Name,
                IsBoss = isBoss,
                IsGlobal = false
            });
        }

        CmbProjectSelector.ItemsSource = itemsList;

        int selectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(target) && !target.Contains("Global", StringComparison.OrdinalIgnoreCase))
        {
            int foundIdx = itemsList.FindIndex(item => string.Equals(item.Name, target, StringComparison.OrdinalIgnoreCase));
            if (foundIdx >= 0) selectedIndex = foundIdx;
        }

        CmbProjectSelector.SelectedIndex = selectedIndex;
        CmbProjectSelector.SelectionChanged += CmbProjectSelector_SelectionChanged;

        UpdateProjectFocusBadgeUI();
    }

    private void BtnProjectSettings_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnProjectSettings_Click)} triggered.");
        if (PnlInlineProjectSettings != null)
        {
            bool isCurrentlyVisible = PnlInlineProjectSettings.Visibility == Visibility.Visible;
            if (isCurrentlyVisible)
            {
                SaveProjectSettings();
            }
            PnlInlineProjectSettings.Visibility = isCurrentlyVisible ? Visibility.Collapsed : Visibility.Visible;
            if (!isCurrentlyVisible)
            {
                TxtProjectsBaseFolder.Text = _projectSettings.BaseFolder;
                ScanAndPopulateProjects();
                if (ChkRestoreSessions != null)
                {
                    ChkRestoreSessions.IsChecked = _uiStates.RestoreOpenSessionsOnStartup;
                }
                UpdateLanguageUiState();
            }
        }
    }

    private void ChkRestoreSessions_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(ChkRestoreSessions_Click)} triggered.");
        _uiStates.RestoreOpenSessionsOnStartup = ChkRestoreSessions?.IsChecked == true;
        SaveUiStates();
    }

    private bool _isUpdatingLanguageUi = false;

    private void BtnLanguageToggle_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnLanguageToggle_Click)} triggered.");
        string current = Services.LocalizationService.Instance.CurrentLanguage;
        string newLang = string.Equals(current, "de", StringComparison.OrdinalIgnoreCase) ? "en" : "de";
        _projectSettings.Language = newLang;
        SaveProjectSettings(refreshUI: false);
        Services.LocalizationService.Instance.SetLanguage(newLang);
        ShowNotice(Loc.Tr("Common.Active", "Aktiv"), Loc.Format("Chat.LanguageToggled", newLang.ToUpperInvariant()), InfoBarSeverity.Informational);
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingLanguageUi) return;
        TelemetryLogger.Log($"[Selection Trace] {nameof(CmbLanguage_SelectionChanged)} triggered.");
        if (CmbLanguage?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            if (!string.Equals(_projectSettings.Language, tag, StringComparison.OrdinalIgnoreCase))
            {
                _projectSettings.Language = tag;
                SaveProjectSettings(refreshUI: false);
                Services.LocalizationService.Instance.SetLanguage(tag);
                ShowNotice(Loc.Tr("Common.Active", "Aktiv"), Loc.Format("Chat.LanguageToggled", Services.LocalizationService.Instance.CurrentLanguage.ToUpperInvariant()), InfoBarSeverity.Informational);
            }
        }
    }

    private void UpdateLanguageUiState()
    {
        try
        {
            _isUpdatingLanguageUi = true;
            string current = Services.LocalizationService.Instance.CurrentLanguage;
            if (TxtLanguageIndicator != null)
            {
                TxtLanguageIndicator.Text = current.ToUpperInvariant();
            }
            if (BtnLanguageToggle != null)
            {
                ToolTipService.SetToolTip(BtnLanguageToggle, Loc.Tr("Settings.LanguageLabel", "Sprache wechseln (DE / EN)"));
            }

            if (TxtSettingsTitle != null) TxtSettingsTitle.Text = Loc.Tr("Settings.Title", "Projekteinstellungen");
            if (TxtSettingsBaseFolder != null) TxtSettingsBaseFolder.Text = Loc.Tr("Settings.MasterFolder", "Master-Ordner für Projekte:");
            if (TxtSettingsProjectsVisible != null) TxtSettingsProjectsVisible.Text = Loc.Tr("Settings.ProjectsVisible", "Projekte (Sichtbar):");
            if (TxtSettingsDefaultReflection != null) TxtSettingsDefaultReflection.Text = Loc.Tr("Settings.DefaultReflection", "Standard-Reflexion:");
            if (TxtSettingsRestoreSessions != null) TxtSettingsRestoreSessions.Text = Loc.Tr("Settings.RestoreSessions", "Offene Chats bei Neustart wiederherstellen");
            if (TxtSettingsLanguage != null) TxtSettingsLanguage.Text = Loc.Tr("Settings.LanguageLabel", "Sprache / Language:");
            if (BtnCloseInlineSettings != null) BtnCloseInlineSettings.Content = Loc.Tr("Settings.Done", "Fertig");

            if (CmbLanguage != null)
            {
                string targetTag = string.IsNullOrWhiteSpace(_projectSettings.Language) ? "auto" : _projectSettings.Language.ToLowerInvariant();
                int targetIndex = 0;
                for (int i = 0; i < CmbLanguage.Items.Count; i++)
                {
                    if (CmbLanguage.Items[i] is ComboBoxItem cbi)
                    {
                        string tag = cbi.Tag?.ToString()?.ToLowerInvariant() ?? "";
                        if (tag == "auto") cbi.Content = Loc.Tr("Settings.LanguageAuto", "Automatisch (System)");
                        else if (tag == "de") cbi.Content = Loc.Tr("Settings.LanguageDe", "Deutsch (DE)");
                        else if (tag == "en") cbi.Content = Loc.Tr("Settings.LanguageEn", "English (EN)");

                        if (tag == targetTag)
                        {
                            targetIndex = i;
                        }
                    }
                }
                if (CmbLanguage.SelectedIndex != targetIndex)
                {
                    CmbLanguage.SelectedIndex = targetIndex;
                }
            }
        }
        finally
        {
            _isUpdatingLanguageUi = false;
        }
    }

    private Dictionary<string, List<CustomInjectionItem>> LoadProjectRulesData()
    {
        Dictionary<string, List<CustomInjectionItem>> result = new Dictionary<string, List<CustomInjectionItem>>();
        try
        {
            var loaded = Services.FileTimestampCache.GetOrLoadSync(_projectRulesPath, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<Dictionary<string, List<CustomInjectionItem>>>(json, _jsonOptions);
                }
                return null;
            });
            if (loaded != null)
            {
                foreach (var list in loaded.Values)
                {
                    foreach (var item in list)
                    {
                        if (item.Content != null && item.Content.StartsWith("HINWEIS:", StringComparison.OrdinalIgnoreCase))
                        {
                            item.Content = item.Content.Substring(8).Trim();
                        }
                    }
                }
                result = loaded;
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"LoadProjectRulesData ERROR: {ex.Message}");
        }

        if (!File.Exists(_projectRulesPath))
        {
            SaveProjectRulesData(result);
        }

        return result;
    }

    private RuleControlData LoadRuleControlData()
    {
        RuleControlData data = new RuleControlData();
        try
        {
            var loaded = Services.FileTimestampCache.GetOrLoadSync(_ruleControlPath, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<RuleControlData>(json, _jsonOptions);
                }
                return null;
            });
            if (loaded != null) data = loaded;
        }
        catch { }

        if (data.custom_global_injections != null)
        {
            foreach (var item in data.custom_global_injections)
            {
                if (item.Content != null && item.Content.StartsWith("HINWEIS:", StringComparison.OrdinalIgnoreCase))
                {
                    item.Content = item.Content.Substring(8).Trim();
                }
            }
        }

        EnsureDefaultGlobalRules(data);
        return data;
    }

    private static string NormalizeRuleKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        string clean = name.Trim().ToLowerInvariant();
        // Häufige doppelt kodierte UTF-8 Sequenzen (Mojibake) und Ersatzzeichen auflösen
        clean = clean.Replace("ã¶", "ö").Replace("ã¤", "ä").Replace("ã¼", "ü")
                     .Replace("ã„", "ä").Replace("ã–", "ö").Replace("ãœ", "ü")
                     .Replace("ãÿ", "ß").Replace("â€™", "'").Replace("â€˜", "'")
                     .Replace("â€ž", "\"").Replace("â€œ", "\"").Replace("‚", "'").Replace("‘", "'").Replace("’", "'").Replace("“", "\"").Replace("”", "\"");
        clean = clean.Replace("\ufffd-ffne", "oeffne").Replace("\ufffd-", "").Replace("\ufffd", "");
        // Umlaute für stabilen Namensabgleich normalisieren
        clean = clean.Replace("ö", "oe").Replace("ä", "ae").Replace("ü", "ue").Replace("ß", "ss");
        // Alle Sonderzeichen & Leerzeichen entfernen
        var sb = new StringBuilder();
        foreach (char c in clean)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private void EnsureDefaultGlobalRules(RuleControlData data)
    {
        if (data.custom_global_injections == null) data.custom_global_injections = new List<CustomInjectionItem>();

        // 1. Bereits vorhandene Duplikate bereinigen (z. B. durch frühere Kodierungsunterschiede)
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<CustomInjectionItem>();
        foreach (var item in data.custom_global_injections)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Name)) continue;
            string key = NormalizeRuleKey(item.Name);
            if (string.IsNullOrEmpty(key) || seenKeys.Add(key))
            {
                deduped.Add(item);
            }
        }
        data.custom_global_injections = deduped;

        // Veraltete getrennte Regeln (Auto-Reflexion, Zusammenfassung & Status) entfernen, wenn der kombinierte Abschlussblock genutzt wird
        data.custom_global_injections.RemoveAll(x =>
            x != null && !string.IsNullOrWhiteSpace(x.Name) &&
            (NormalizeRuleKey(x.Name) == "autoreflexion" || NormalizeRuleKey(x.Name) == "zusammenfassungstatus"));

        var defaults = new List<(string Name, string Content)>
        {
            ("Deutsch erzwingen", "Kommuniziere mit dem Benutzer ausschließlich auf Deutsch (Deutsch)."),
            ("Freigabe-Pflicht ('go')", "Bevor du Quellcode schreibst, veränderst oder neue Programmdateien erstellst, warte AUSNAHMSLOS IMMER auf die ausdrückliche Freigabe ‚go‘ durch den Benutzer (Ausnahmen: autonomer Ziel-Modus via ‚/goal‘ oder Deployment via ‚deploy‘). Das gilt strikt auch für Bugfixes, kleine Korrekturen und Folgefehler an Quellcode-Dateien! Ausdrücklich ausgenommen von der Freigabe-Pflicht und immer automatisch erlaubt sind interne Wartungs- und Dokumentationsaufgaben: Git-Sicherungen (git commit, git add), das Führen von docs/protocol.md sowie die Wissens-Sicherung in docs/knowledge.md der globalen Wissensdatenbank und das Pflegen der TODO.md. Bevor du ein Schreib-Tool (write_to_file, replace_file_content etc.) für Quellcodedateien aufrufst, MUSST du in deinen internen Gedanken prüfen: ‚Hat der User in dieser Nachricht das Wort „go“ (bzw. „/goal“ oder bei Deployments „deploy“) geschrieben?‘ Nur dann darfst du Quellcode schreiben. Lese-, Analyse- und Suchwerkzeuge dürfen ohne Freigabe ausgeführt werden."),
            ("Abschlussblock & Status (Auto-Reflexion, Freigabe, Fazit)", "Beende deine finale Antwort eines Turns verbindlich mit einem einheitlichen, kompakten Abschlussblock in exakt dieser Reihenfolge:\n\n1. Trennlinie ('---'):\nLeite den gesamten formalen Abschlussblock immer zwingend mit genau einer horizontalen Trennlinie ('---') ein, um ihn optisch sauber vom eigentlichen Antworttext abzugrenzen. (Wichtig: Es gibt im gesamten Abschlussblock AUSNAHMSLOS NUR DIESE EINE Trennlinie ganz zu Beginn; zwischen den Zeilen Auto-Reflexion, Freigabe-Status und Zusammenfassung dürfen NIEMALS weitere Trennstriche stehen!).\n\n2. Auto-Reflexion:\nIst die Auto-Reflexion in der Benutzeroberfläche (per Schild-Button bzw. 'IsAutoReflexionEnabled: false' in dashboard_chat.json) oder im Tab 'Wissen' deaktiviert, entfällt diese Zeile vollständig.\nIst sie aktiv: Die Zeile steht direkt über dem Freigabe-Status.\n- Bei reinen Fragen/Analysen/Konzepten/Doku ohne Codeänderungen: '🔍 **Auto-Reflexion:** Übersprungen' (bzw. '🔍 **Auto-Reflexion (Kein Check):** Prüfung übersprungen – keine Codeänderungen').\n- Bei geringem Aufwand (Schnell-Modus: <= 2 Dateien und <= 80 Zeilen, UI/Styles oder isolierter Bugfix): Führe im Turn einen schnellen Build-/Syntaxcheck oder präzisen Selbstcheck durch (strikt OHNE Subagent): '🔍 **Auto-Reflexion:** Gering (0 Fehler)' (oder 'Gering (X Fehler behoben)').\n- Bei mittlerem/hohem Aufwand (Sicherheits-Audit: ab 3 Quellcode-Dateien ODER > 80 Zeilen Logik ODER Concurrency im Diff): Beauftrage VOR deiner Antwort zwingend einen neutralen Subagenten (Code Auditor). Die Zeile zeigt transparent das reale Audit-Ergebnis:\n  * Wenn Blocker gefunden und behoben wurden: '🔍 **Auto-Reflexion:** X Blocker behoben (<Kurzhinweis>)' (z. B. '1 Blocker behoben (Pfad korrigiert)').\n  * Wenn keine Blocker gefunden wurden: '🔍 **Auto-Reflexion:** Freigabe (0 Blocker im 4-Augen-Audit)' (bzw. 'Mittel (0 Blocker im 4-Augen-Audit)').\n\n3. Freigabe-Status (Ampel-Icon):\nPlatziere den Freigabe-Status als eigenständigen Absatz vor der Zusammenfassung. Texte müssen maximal KURZ, EINZEILIG und OHNE redundante Zusätze sein:\n- Sobald Code- oder Dateiänderungen vorgeschlagen, geplant, als Konzept/Variante zur Auswahl gestellt werden oder noch anstehen (auch wenn der Benutzer erst eine Variante wählen oder freigeben muss): '🚦 **Freigabe-Status:** 🔴 **'go' erforderlich**'\n- Wenn 'go' in diesem Turn erteilt und die Umsetzung vollendet wurde: '🚦 **Freigabe-Status:** 🟢 **'go' ausgeführt**'\n- Ausschließlich bei reinen Wissensfragen, Code-Erklärungen, abgeschlossenen Recherchen oder wenn alles zu 100% fertig ist und KEINERLEI Codeänderungen mehr anstehen: '🚦 **Freigabe-Status:** 🟢 **Nicht erforderlich**'\n- Bei anstehenden Deployments: '🚦 **Freigabe-Status:** 🟡 **'deploy' erforderlich**'\n\n4. Zusammenfassung (Allerletzte Zeile):\nDie fettgedruckte Zusammenfassung beginnend mit '💡  **Einfach gesagt: <Text>**' (zwei Leerzeichen nach dem Emoji) und kurzen Status-Daten ( <Time> (Format: DD.MM.YYYY, HH:mm:ss Uhr)) MUSS zwingend und ausnahmslos als ALLERLETZTE Zeile ganz am Ende der Antwort stehen.\n\nBei rein technischen Zwischenmeldungen und reaktiven Task-Wakeups (z. B. während oder nach asynchronen Hintergrundbefehlen wie Builds/PM2) entfällt dieser Abschlussblock vollständig."),
            ("Git-Commit vor Änderung", "Bevor du Code schreibst oder Dateien erstellst, stelle zwingend sicher, dass alle aktuellen Änderungen im Repository in einem Git-Commit gesichert sind (z. B. vorab durch einen commit), um einen sauberen Ausgangspunkt zu haben. Git-Operationen zur Arbeitsstandssicherung (z. B. git add, git commit) dürfen und sollen immer automatisch ohne gesondertes 'go' ausgeführt werden."),
            ("Protokoll-Führung", "Immer wenn der Agent Code-, Dokumentations- oder Konfigurationsänderungen am Projekt vornimmt, muss er am Ende seines Arbeitszyklus (vor der finalen Antwort) automatisch und ohne vorheriges 'go' einen Eintrag in docs/protocol.md im Shorthand-Format hinzufügen."),
            ("Wissens-Sicherung", $"Prüfe am Ende jedes Arbeitszyklus (vor der finalen Antwort) eigenständig, ob im Gesprächsverlauf neue Erkenntnisse über die in diesem Chat verwendete Programmiersprache, Frameworks oder Bibliotheken entstanden sind. Falls ja, sichere dieses Wissen immer direkt und vollautomatisch (ohne vorheriges 'go') in der lokalen Wissensdatenbank docs/knowledge.md ."),
            ("Chat-Koordination", "Bevor du Code- oder Konfigurationsänderungen vornimmst: 1. Prüfe vorab `.agents/coordination.json` auf aktive Einträge anderer Chats (nicht älter als 30 Minuten). 2. Schätze ab, ob Konflikte drohen (z. B. durch simultanes Bauen/Kompilieren oder Datei-Überschneidungen) – falls ja, informiere den Benutzer und warte auf Klärung. 3. Nach Erhalt der Freigabe ('go'): Trage deine geplante Änderung (Chat-ID/Session, Zeitstempel, Zweck, Umfang, betroffene Dateien) vor Beginn der Dateiänderungen ein und behalte bestehende Einträge bei. 4. Entferne deinen Eintrag nach Abschluss der Arbeiten (vor der finalen Antwort) wieder aus `.agents/coordination.json`."),
            ("Webbrowser öffnen", "Öffne NIEMALS externe Webbrowser über OS-Befehle (`start http://...`, `xdg-open`, `webbrowser.open()`), da dies ConPTY-Terminals und Chrome-Profile blockiert. Verwende für alle Web-Aufrufe, Recherche-Seiten, Web-Apps und Formulare IMMER den integrierten Dashboard KI-Co-Browser (via MCP-Tools `cobrowser_navigate`, `cobrowser_interactive_map` etc. oder `scripts/co-browser-api.ps1`). Bei Web-Recherchen zu Dokumentationen und Webseiten nutze den Co-Browser still im Hintergrund (Split-Screen bleibt geschlossen, `open: false`), und erwähne gewonnene Zusatzinfos gesondert im Ergebnis. Bei CLI-Tools mit OAuth-Logins (`gcloud`, `firebase`, `az`): Nutze die headless/no-browser Variante (`--no-launch-browser`, `--no-localhost`), kopiere die generierte Auth-URL und öffne sie im KI-Co-Browser, damit der Benutzer sie direkt im Split-Screen einsehen und abschließen kann."),
            ("Tools", "Bevor du die Installation eines Werkzeugs (z. B. CLI-Tools, npm-Packages) vorschlägst  oder anfragst, prüfe IMMER zuerst per Kommandozeile (z. B. mit `toolname --version`), ob es  bereits global auf dem System verfügbar ist. Verlasse dich bei der Prüfung nicht  ausschließlich auf lokale Projektdateien wie die `package.json`."),
            ("Deployment", "Bevor du ein Deployment oder eine Veröffentlichung (z. B. cloud deploy, npm publish, git push) ausführst, warte AUSNAHMSLOS IMMER auf die ausdrückliche Freigabe ‚deploy‘ durch den Benutzer."),
            ("Subagents", "Nutze immer, wenn möglich Subagents um Zeit zu sparen, wo es sinnig ist. Ohne vorherige Freigabe ('go') durch den Benutzer dürfen Subagents ausschließlich für Lese-, Such- und Recherche-Aufgaben eingesetzt werden.")
        };

        foreach (var def in defaults)
        {

            string defKey = NormalizeRuleKey(def.Name);
            bool alreadyExists = data.custom_global_injections.Any(x =>
            {
                if (x == null || string.IsNullOrWhiteSpace(x.Name)) return false;
                if (string.Equals(x.Name, def.Name, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(x.Content, def.Content, StringComparison.Ordinal)) return true;

                string existingKey = NormalizeRuleKey(x.Name);
                if (string.IsNullOrEmpty(existingKey) || string.IsNullOrEmpty(defKey)) return false;

                if (existingKey == defKey) return true;
                if (existingKey.StartsWith(defKey, StringComparison.OrdinalIgnoreCase) && defKey.Length >= 6) return true;
                if (defKey.StartsWith(existingKey, StringComparison.OrdinalIgnoreCase) && existingKey.Length >= 6) return true;

                return false;
            });

            if (!alreadyExists)
            {
                data.custom_global_injections.Add(new CustomInjectionItem
                {
                    Name = def.Name,
                    Content = def.Content,
                    IsEnabled = true,
                    IsGlobal = true,
                    TargetProject = string.Empty
                });
            }
        }
    }

    private void SaveRuleControlData(RuleControlData data)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_ruleControlPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(data, _jsonOptions);
            WriteAllTextAtomic(_ruleControlPath, json);
        }
        catch { }

        SyncRulesToGeminiMd();
    }

    private void SaveProjectRulesData(Dictionary<string, List<CustomInjectionItem>> data)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_projectRulesPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(data, _jsonOptions);
            WriteAllTextAtomic(_projectRulesPath, json);

            // Export active project rules directly into project workspace directories (.agents/rules, .gemini/rules, and root GEMINI.md)
            string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) 
                ? _projectSettings.BaseFolder 
                : ProjectSettingsData.GetDefaultBaseFolder();
            foreach (var kvp in data)
            {
                string projName = kvp.Key;
                if (string.IsNullOrWhiteSpace(projName) || projName.Contains("Global")) continue;

                string projPath = _allProjects.FirstOrDefault(p => p.Name.Equals(projName, StringComparison.OrdinalIgnoreCase))?.Path
                    ?? Path.Combine(baseFolder, projName);
                if (!Directory.Exists(projPath)) continue;

                var activeProjRules = kvp.Value?
                    .Where(r => r != null && r.IsEnabled && !string.IsNullOrWhiteSpace(r.Content))
                    .ToList();

                string agentsRulesDir = Path.Combine(projPath, ".agents", "rules");
                string agentsRulesMdPath = Path.Combine(agentsRulesDir, "project_rules.md");
                string geminiRulesDir = Path.Combine(projPath, ".gemini", "rules");
                string projRulesMdPath = Path.Combine(geminiRulesDir, "project_rules.md");
                string rootGeminiMdPath = Path.Combine(projPath, "GEMINI.md");
                string legacyGeminiMdPath = Path.Combine(projPath, ".gemini", "GEMINI.md");

                string todoCheckPath = Path.Combine(projPath, "TODO.md");
                if (!File.Exists(todoCheckPath) && File.Exists(Path.Combine(projPath, "todo.md")))
                {
                    todoCheckPath = Path.Combine(projPath, "todo.md");
                }
                bool hasTodo = File.Exists(todoCheckPath) && !string.IsNullOrWhiteSpace(File.ReadAllText(todoCheckPath));

                if ((activeProjRules != null && activeProjRules.Count > 0) || hasTodo)
                {
                    var sbProj = new StringBuilder();
                    sbProj.AppendLine($"# Projekt-Regeln ({projName})");
                    sbProj.AppendLine();
                    sbProj.AppendLine("<rules>");
                    if (activeProjRules != null)
                    {
                        foreach (var rule in activeProjRules)
                        {
                            string safeRuleName = System.Security.SecurityElement.Escape(rule.Name ?? "Projekt-Regel");
                            sbProj.AppendLine($"  <rule name=\"{safeRuleName}\">");
                            sbProj.AppendLine($"    {rule.Content?.Trim()}");
                            sbProj.AppendLine("  </rule>");
                        }
                    }
                    sbProj.AppendLine("</rules>");

                    string rulesMdContent = sbProj.ToString();

                    // 1. Write to standard Antigravity workspace rules location (.agents/rules/)
                    try
                    {
                        if (!Directory.Exists(agentsRulesDir)) Directory.CreateDirectory(agentsRulesDir);
                        WriteAllTextAtomic(agentsRulesMdPath, rulesMdContent);
                    }
                    catch { }

                    // 2. Write to .gemini/rules/ for backward compatibility
                    try
                    {
                        if (!Directory.Exists(geminiRulesDir)) Directory.CreateDirectory(geminiRulesDir);
                        WriteAllTextAtomic(projRulesMdPath, rulesMdContent);
                    }
                    catch { }

                    // 3. Sync into root GEMINI.md
                    try
                    {
                        SyncProjectRulesToRootGeminiMd(rootGeminiMdPath, legacyGeminiMdPath, activeProjRules, projName);
                    }
                    catch { }
                }
                else
                {
                    if (File.Exists(agentsRulesMdPath))
                    {
                        try { File.Delete(agentsRulesMdPath); } catch { }
                    }
                    if (File.Exists(projRulesMdPath))
                    {
                        try { File.Delete(projRulesMdPath); } catch { }
                    }
                    try
                    {
                        RemoveProjectRulesFromRootGeminiMd(rootGeminiMdPath);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SaveProjectRulesData ERROR: {ex.ToString()}");
            System.Diagnostics.Debug.WriteLine(ex.ToString());
        }

        SyncRulesToGeminiMd();
    }

    private void SyncRulesToGeminiMd()
    {
        try
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string geminiDir = Path.Combine(userHome, ".gemini");
            if (!Directory.Exists(geminiDir))
            {
                Directory.CreateDirectory(geminiDir);
            }
            string geminiMdPath = Path.Combine(geminiDir, "GEMINI.md");

            var activeGlobalRules = new List<(string Name, string Content)>();

            var ruleData = LoadRuleControlData();
            if (ruleData.custom_global_injections != null)
            {
                foreach (var item in ruleData.custom_global_injections)
                {
                    if (item != null && item.IsEnabled && !string.IsNullOrWhiteSpace(item.Content))
                    {
                        activeGlobalRules.Add((item.Name ?? "Regel", item.Content.Trim()));
                    }
                }
            }

            var rulesBlock = new StringBuilder();
            rulesBlock.AppendLine("<rules>");
            foreach (var rule in activeGlobalRules)
            {
                string safeName = System.Security.SecurityElement.Escape(rule.Name);
                rulesBlock.AppendLine($"  <rule name=\"{safeName}\">");
                rulesBlock.AppendLine($"    {rule.Content}");
                rulesBlock.AppendLine("  </rule>");
            }
            rulesBlock.Append("</rules>");

            string finalContent;
            if (File.Exists(geminiMdPath))
            {
                string existing = File.ReadAllText(geminiMdPath);
                int rulesStart = existing.IndexOf("<rules>", StringComparison.OrdinalIgnoreCase);
                int rulesEnd = existing.IndexOf("</rules>", StringComparison.OrdinalIgnoreCase);

                if (rulesStart >= 0 && rulesEnd > rulesStart)
                {
                    string prefix = existing.Substring(0, rulesStart).TrimEnd();
                    string suffix = existing.Substring(rulesEnd + "</rules>".Length).TrimStart();
                    var sb = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        sb.AppendLine(prefix);
                        sb.AppendLine();
                    }
                    sb.AppendLine(rulesBlock.ToString());
                    if (!string.IsNullOrWhiteSpace(suffix))
                    {
                        sb.AppendLine();
                        sb.Append(suffix);
                    }
                    finalContent = sb.ToString();
                }
                else
                {
                    var sb = new StringBuilder();
                    string cleanExisting = existing.Trim();
                    if (!string.IsNullOrWhiteSpace(cleanExisting))
                    {
                        sb.AppendLine(cleanExisting);
                        sb.AppendLine();
                    }
                    sb.AppendLine(rulesBlock.ToString());
                    finalContent = sb.ToString();
                }
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine("Der User bevorzugt robuste und einfache Codelösungen.");
                sb.AppendLine();
                sb.AppendLine(rulesBlock.ToString());
                finalContent = sb.ToString();
            }

            WriteAllTextAtomic(geminiMdPath, finalContent);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SyncRulesToGeminiMd ERROR: {ex.Message}");
        }
    }

    private void SyncProjectRulesToRootGeminiMd(string rootGeminiMdPath, string legacyGeminiMdPath, List<CustomInjectionItem>? activeProjRules, string projName)
    {
        try
        {
            var rulesBlock = new StringBuilder();
            rulesBlock.AppendLine("<rules>");
            if (activeProjRules != null)
            {
                foreach (var rule in activeProjRules)
                {
                    string safeName = System.Security.SecurityElement.Escape(rule.Name ?? "Projekt-Regel");
                    rulesBlock.AppendLine($"  <rule name=\"{safeName}\">");
                    rulesBlock.AppendLine($"    {rule.Content?.Trim()}");
                    rulesBlock.AppendLine("  </rule>");
                }
            }
            rulesBlock.Append("</rules>");

            string finalContent;
            if (File.Exists(rootGeminiMdPath))
            {
                string existing = File.ReadAllText(rootGeminiMdPath);
                int rulesStart = existing.IndexOf("<rules>", StringComparison.OrdinalIgnoreCase);
                int rulesEnd = existing.IndexOf("</rules>", StringComparison.OrdinalIgnoreCase);

                if (rulesStart >= 0 && rulesEnd > rulesStart)
                {
                    string prefix = existing.Substring(0, rulesStart).TrimEnd();
                    string suffix = existing.Substring(rulesEnd + "</rules>".Length).TrimStart();
                    var sb = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        sb.AppendLine(prefix);
                        sb.AppendLine();
                    }
                    sb.AppendLine(rulesBlock.ToString());
                    if (!string.IsNullOrWhiteSpace(suffix))
                    {
                        sb.AppendLine();
                        sb.Append(suffix);
                    }
                    finalContent = sb.ToString();
                }
                else
                {
                    var sb = new StringBuilder();
                    string cleanExisting = existing.Trim();
                    if (!string.IsNullOrWhiteSpace(cleanExisting))
                    {
                        sb.AppendLine(cleanExisting);
                        sb.AppendLine();
                    }
                    sb.AppendLine(rulesBlock.ToString());
                    finalContent = sb.ToString();
                }
            }
            else if (File.Exists(legacyGeminiMdPath))
            {
                // Migrate existing legacy .gemini/GEMINI.md to root GEMINI.md
                string legacy = File.ReadAllText(legacyGeminiMdPath);
                int rulesStart = legacy.IndexOf("<rules>", StringComparison.OrdinalIgnoreCase);
                int rulesEnd = legacy.IndexOf("</rules>", StringComparison.OrdinalIgnoreCase);

                if (rulesStart >= 0 && rulesEnd > rulesStart)
                {
                    string prefix = legacy.Substring(0, rulesStart).TrimEnd();
                    string suffix = legacy.Substring(rulesEnd + "</rules>".Length).TrimStart();
                    var sb = new StringBuilder();
                    if (!string.IsNullOrWhiteSpace(prefix))
                    {
                        sb.AppendLine(prefix);
                        sb.AppendLine();
                    }
                    sb.AppendLine(rulesBlock.ToString());
                    if (!string.IsNullOrWhiteSpace(suffix))
                    {
                        sb.AppendLine();
                        sb.Append(suffix);
                    }
                    finalContent = sb.ToString();
                }
                else
                {
                    var sb = new StringBuilder();
                    string cleanLegacy = legacy.Trim();
                    if (!string.IsNullOrWhiteSpace(cleanLegacy))
                    {
                        sb.AppendLine(cleanLegacy);
                        sb.AppendLine();
                    }
                    sb.AppendLine(rulesBlock.ToString());
                    finalContent = sb.ToString();
                }
            }
            else
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# Projekt: {projName}");
                sb.AppendLine();
                sb.AppendLine(rulesBlock.ToString());
                finalContent = sb.ToString();
            }

            WriteAllTextAtomic(rootGeminiMdPath, finalContent);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SyncProjectRulesToRootGeminiMd ERROR: {ex.Message}");
        }
    }

    private void RemoveProjectRulesFromRootGeminiMd(string rootGeminiMdPath)
    {
        try
        {
            if (!File.Exists(rootGeminiMdPath)) return;
            string existing = File.ReadAllText(rootGeminiMdPath);
            int rulesStart = existing.IndexOf("<rules>", StringComparison.OrdinalIgnoreCase);
            int rulesEnd = existing.IndexOf("</rules>", StringComparison.OrdinalIgnoreCase);

            if (rulesStart >= 0 && rulesEnd > rulesStart)
            {
                string prefix = existing.Substring(0, rulesStart).TrimEnd();
                string suffix = existing.Substring(rulesEnd + "</rules>".Length).TrimStart();
                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(prefix))
                {
                    sb.AppendLine(prefix);
                }
                if (!string.IsNullOrWhiteSpace(suffix))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(suffix);
                }
                string finalContent = sb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(finalContent))
                {
                    try { File.Delete(rootGeminiMdPath); } catch { }
                }
                else
                {
                    WriteAllTextAtomic(rootGeminiMdPath, finalContent);
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"RemoveProjectRulesFromRootGeminiMd ERROR: {ex.Message}");
        }
    }
}

