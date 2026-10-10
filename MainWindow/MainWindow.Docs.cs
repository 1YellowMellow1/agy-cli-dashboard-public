using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private CustomInjectionItem? _editingInjectionItem;
    private bool _addingGlobalInjection;
    private int _renderKnowledgeSeq;
    private int _renderInjectionsSeq;

    private void UpdateDocsTab(bool isGlobal, string projectPath, string projectName)
    {
        string safeProjName = projectName ?? "";
        string resolvedPath = projectPath;
        if (string.IsNullOrWhiteSpace(resolvedPath) || !Directory.Exists(resolvedPath))
        {
            var match = _allProjects.FirstOrDefault(p => p.Name.Equals(safeProjName, StringComparison.OrdinalIgnoreCase));
            resolvedPath = match?.Path ?? Path.Combine(_projectSettings?.BaseFolder ?? ProjectSettingsData.GetDefaultBaseFolder(), safeProjName);
        }

        RenderTextInjections(isGlobal, safeProjName);
        RenderKnowledgeResources(isGlobal, resolvedPath, safeProjName);
    }

    private void RenderKnowledgeResources(bool isGlobal, string projectPath, string projectName)
    {
        try
        {
            int currentSeq = System.Threading.Interlocked.Increment(ref _renderKnowledgeSeq);
            string targetProjName = isGlobal ? _projectSettings.LastSelectedProject : projectName;
            if (string.IsNullOrWhiteSpace(targetProjName) || targetProjName.Contains("Global"))
            {
                targetProjName = _allProjects.FirstOrDefault(p => p.IsVisible)?.Name ?? "agy-cli-dashboard";
            }

            string actualProjPath = projectPath;
            if (isGlobal || string.IsNullOrWhiteSpace(actualProjPath) || !Directory.Exists(actualProjPath))
            {
                var matchProj = _allProjects.FirstOrDefault(p => p.Name.Equals(targetProjName, StringComparison.OrdinalIgnoreCase));
                actualProjPath = matchProj?.Path ?? Path.Combine(_projectSettings?.BaseFolder ?? ProjectSettingsData.GetDefaultBaseFolder(), targetProjName);
            }

            if (TxtProjectDocsHeader != null)
            {
                TxtProjectDocsHeader.Text = "PROJEKT RESSOURCEN";
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string globalKnowledgeDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "skills", "knowledge-base", "custom-knowledge");
            string globalSkillsDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "skills");
            string configSkillsDir = Path.Combine(userProfile, ".gemini", "config", "skills");
            string globalBaseDir = Path.Combine(userProfile, ".gemini", "antigravity-cli");
            string globalRulesDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "rules");

            Task.Run(() =>
            {
                string docsFolder = Path.Combine(actualProjPath, "docs");
                bool docsFolderExists = Directory.Exists(docsFolder);
                var docFiles = docsFolderExists
                    ? Directory.GetFiles(docsFolder, "*.md")
                        .Select(f => Path.GetFileName(f))
                        .Where(f => !f.Equals("protocol.md", StringComparison.OrdinalIgnoreCase) && !f.Equals("protokoll.md", StringComparison.OrdinalIgnoreCase))
                        .ToList()
                    : new List<string>();

                var projectRuleFiles = new List<string>();
                string geminiMd = Path.Combine(actualProjPath, "GEMINI.md");
                if (File.Exists(geminiMd)) projectRuleFiles.Add(geminiMd);
                string agentsMd = Path.Combine(actualProjPath, "AGENTS.md");
                if (File.Exists(agentsMd)) projectRuleFiles.Add(agentsMd);
                string cursorRules = Path.Combine(actualProjPath, ".cursorrules");
                if (File.Exists(cursorRules)) projectRuleFiles.Add(cursorRules);
                string clineRules = Path.Combine(actualProjPath, ".clinerules");
                if (File.Exists(clineRules)) projectRuleFiles.Add(clineRules);

                string projAgentsRules = Path.Combine(actualProjPath, ".agents", "rules");
                if (Directory.Exists(projAgentsRules)) projectRuleFiles.AddRange(Directory.GetFiles(projAgentsRules, "*.md"));
                string projRulesDir = Path.Combine(actualProjPath, ".gemini", "rules");
                if (Directory.Exists(projRulesDir)) projectRuleFiles.AddRange(Directory.GetFiles(projRulesDir, "*.md"));
                string projAntigravityRules = Path.Combine(actualProjPath, ".antigravity", "rules");
                if (Directory.Exists(projAntigravityRules)) projectRuleFiles.AddRange(Directory.GetFiles(projAntigravityRules, "*.md"));

                string projRulesJson = Path.Combine(globalBaseDir, "project_rules.json");
                if (File.Exists(projRulesJson)) projectRuleFiles.Add(projRulesJson);

                var globalDocs = new List<string>
                {
                    Path.Combine(userProfile, ".gemini", "GEMINI.md"),
                    Path.Combine(globalBaseDir, "GEMINI.md"),
                    Path.Combine(userProfile, ".gemini", "config", "GEMINI.md")
                }.Where(File.Exists).ToList();

                var kFiles = Directory.Exists(globalKnowledgeDir)
                    ? Directory.GetFiles(globalKnowledgeDir, "*.md").Take(10).ToList()
                    : new List<string>();

                var skillDirs = new List<string>();
                if (Directory.Exists(globalSkillsDir)) skillDirs.AddRange(Directory.GetDirectories(globalSkillsDir));
                if (Directory.Exists(configSkillsDir)) skillDirs.AddRange(Directory.GetDirectories(configSkillsDir));

                var globalRuleFiles = new List<string>();
                string mainGeminiMd = Path.Combine(userProfile, ".gemini", "GEMINI.md");
                if (File.Exists(mainGeminiMd)) globalRuleFiles.Add(mainGeminiMd);
                string agyCliGeminiMd = Path.Combine(globalBaseDir, "GEMINI.md");
                if (File.Exists(agyCliGeminiMd) && !globalRuleFiles.Contains(agyCliGeminiMd)) globalRuleFiles.Add(agyCliGeminiMd);

                if (Directory.Exists(globalRulesDir)) globalRuleFiles.AddRange(Directory.GetFiles(globalRulesDir, "*.md"));
                string userRulesDir = Path.Combine(userProfile, ".gemini", "rules");
                if (Directory.Exists(userRulesDir)) globalRuleFiles.AddRange(Directory.GetFiles(userRulesDir, "*.md"));

                var settingsFiles = new List<string>
                {
                    Path.Combine(globalBaseDir, "rule_control.json"),
                    Path.Combine(globalBaseDir, "project_rules.json"),
                    Path.Combine(userProfile, ".gemini", "antigravity-cli", "mcp.json"),
                    Path.Combine(userProfile, ".gemini", "config", "mcp.json")
                }.Where(File.Exists).ToList();

                var projectRulesData = LoadProjectRulesData();

                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (currentSeq != _renderKnowledgeSeq) return;

                    if (PnlProjectDocsList != null)
                    {
                        PnlProjectDocsList.Children.Clear();
                        if (docsFolderExists)
                        {
                            if (docFiles.Count > 0)
                            {
                                foreach (var df in docFiles)
                                {
                                    var btn = CreateFileTreeButton(df, isFolder: false, fullPath: Path.Combine(docsFolder, df));
                                    PnlProjectDocsList.Children.Add(btn);
                                }
                            }
                            else
                            {
                                PnlProjectDocsList.Children.Add(new TextBlock
                                {
                                    Text = "Keine Wissensdateien in docs/",
                                    FontSize = 10,
                                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                                    Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                                    Margin = new Thickness(4, 2, 0, 2)
                                });
                            }
                        }
                        else
                        {
                            PnlProjectDocsList.Children.Add(new TextBlock
                            {
                                Text = "Ordner docs/ existiert nicht.",
                                FontSize = 10,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                                Margin = new Thickness(4, 2, 0, 2)
                            });
                        }
                    }

                    if (PnlProjectRulesFilesList != null)
                    {
                        PnlProjectRulesFilesList.Children.Clear();
                        int countRules = 0;
                        foreach (var rf in projectRuleFiles.Distinct())
                        {
                            var btn = CreateFileTreeButton(Path.GetFileName(rf), isFolder: false, fullPath: rf);
                            PnlProjectRulesFilesList.Children.Add(btn);
                            countRules++;
                        }

                        if (!string.IsNullOrEmpty(targetProjName) && projectRulesData.TryGetValue(targetProjName, out var pList) && pList != null)
                        {
                            foreach (var pr in pList)
                            {
                                if (pr == null) continue;
                                string ruleName = string.IsNullOrWhiteSpace(pr.Name) ? "Projekt-Regel" : pr.Name;
                                var btn = CreateFileTreeButton(ruleName, isFolder: false, fullPath: projRulesJson);
                                if (btn.Content is StackPanel sp)
                                {
                                    var pinIcon = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/pin.svg"), Width = 12, Height = 12, Margin = new Thickness(0, 0, 4, 0) };
                                    sp.Children.Insert(1, pinIcon);
                                }
                                PnlProjectRulesFilesList.Children.Add(btn);
                                countRules++;
                            }
                        }

                        if (countRules == 0)
                        {
                            PnlProjectRulesFilesList.Children.Add(new TextBlock
                            {
                                Text = "Keine Projekt-Regeln.",
                                FontSize = 10,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                                Margin = new Thickness(4, 2, 0, 2)
                            });
                        }
                    }

                    if (PnlGlobalDocsList != null)
                    {
                        PnlGlobalDocsList.Children.Clear();
                        int countDocs = 0;
                        foreach (var gd in globalDocs)
                        {
                            var btn = CreateFileTreeButton(Path.GetFileName(gd), isFolder: false, fullPath: gd);
                            PnlGlobalDocsList.Children.Add(btn);
                            countDocs++;
                        }

                        if (countDocs == 0)
                        {
                            PnlGlobalDocsList.Children.Add(new TextBlock
                            {
                                Text = "Keine globalen Dokumente.",
                                FontSize = 10,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                                Margin = new Thickness(4, 2, 0, 2)
                            });
                        }
                    }

                    if (PnlGlobalKnowledgeList != null)
                    {
                        PnlGlobalKnowledgeList.Children.Clear();
                        if (kFiles.Count > 0)
                        {
                            foreach (var kf in kFiles)
                            {
                                var btn = CreateFileTreeButton(Path.GetFileName(kf), isFolder: false, fullPath: kf);
                                PnlGlobalKnowledgeList.Children.Add(btn);
                            }
                        }
                        else
                        {
                            PnlGlobalKnowledgeList.Children.Add(new TextBlock
                            {
                                Text = "Keine benutzerdefinierten Dokumente.",
                                FontSize = 10,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                                Margin = new Thickness(4, 2, 0, 2)
                            });
                        }
                    }

                    if (PnlGlobalSkillsList != null)
                    {
                        PnlGlobalSkillsList.Children.Clear();
                        if (skillDirs.Count > 0)
                        {
                            foreach (var sd in skillDirs.Distinct().Take(10))
                            {
                                string skillMd = Path.Combine(sd, "SKILL.md");
                                string displayName = Path.GetFileName(sd);
                                string targetPath = File.Exists(skillMd) ? skillMd : sd;
                                var btn = CreateFileTreeButton(displayName, isFolder: true, fullPath: targetPath);
                                PnlGlobalSkillsList.Children.Add(btn);
                            }
                        }
                        else
                        {
                            PnlGlobalSkillsList.Children.Add(new TextBlock
                            {
                                Text = "Keine benutzerdefinierten Skills.",
                                FontSize = 10,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                                Margin = new Thickness(4, 2, 0, 2)
                            });
                        }
                    }

                    if (PnlGlobalRulesFilesList != null)
                    {
                        PnlGlobalRulesFilesList.Children.Clear();
                        if (globalRuleFiles.Count > 0)
                        {
                            foreach (var grf in globalRuleFiles.Distinct())
                            {
                                var btn = CreateFileTreeButton(Path.GetFileName(grf), isFolder: false, fullPath: grf);
                                PnlGlobalRulesFilesList.Children.Add(btn);
                            }
                        }
                        else
                        {
                            PnlGlobalRulesFilesList.Children.Add(new TextBlock
                            {
                                Text = "Keine globalen Regeln.",
                                FontSize = 10,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                                Margin = new Thickness(4, 2, 0, 2)
                            });
                        }
                    }

                    if (PnlGlobalSettingsList != null)
                    {
                        PnlGlobalSettingsList.Children.Clear();
                        int count = 0;
                        foreach (var sf in settingsFiles)
                        {
                            var btn = CreateFileTreeButton(Path.GetFileName(sf), isFolder: false, fullPath: sf);
                            PnlGlobalSettingsList.Children.Add(btn);
                            count++;
                        }

                        if (count == 0)
                        {
                            PnlGlobalSettingsList.Children.Add(new TextBlock
                            {
                                Text = "Keine Einstellungen vorhanden.",
                                FontSize = 10,
                                FontStyle = Windows.UI.Text.FontStyle.Italic,
                                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                                Margin = new Thickness(4, 2, 0, 2)
                            });
                        }
                    }
                });
            });
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"RenderKnowledgeResources ERROR: {ex.Message}");
        }
    }

    private async void BtnAddProjectRuleFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string targetProjName = _projectSettings.LastSelectedProject ?? "agy-cli-dashboard";
            var matchProj = _allProjects.FirstOrDefault(p => p.Name.Equals(targetProjName, StringComparison.OrdinalIgnoreCase));
            string projPath = matchProj?.Path ?? Path.Combine(_projectSettings?.BaseFolder ?? ProjectSettingsData.GetDefaultBaseFolder(), targetProjName);
            string rulesDir = Path.Combine(projPath, ".gemini", "rules");
            Directory.CreateDirectory(rulesDir);

            string fileName = $"rule_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            string filePath = Path.Combine(rulesDir, fileName);
            await File.WriteAllTextAsync(filePath, $"# Projekt-Regel ({targetProjName})\n\nRegel-Anweisung hier einfügen...\n");

            bool isGlob = _projectSettings?.LastSelectedProject == "Globaler Fokus (Alle Projects)" || _projectSettings?.LastSelectedProject == "Globaler Fokus";
            UpdateDocsTab(isGlob, projPath, targetProjName);
            ExpandCard(PnlProjectResourcesContent, TxtChevronProjectResources);
            OpenFileInEditor(filePath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnAddProjectRuleFile_Click error: {ex.Message}");
        }
    }

    private void BtnToggleProjectResourcesCard_Click(object sender, RoutedEventArgs e)
    {
        ToggleCard(PnlProjectResourcesContent, TxtChevronProjectResources);
    }

    private void BtnToggleGlobalResourcesCard_Click(object sender, RoutedEventArgs e)
    {
        ToggleCard(PnlGlobalResourcesContent, TxtChevronGlobalResources);
    }

    private void BtnToggleGlobalInjectionsCard_Click(object sender, RoutedEventArgs e)
    {
        ToggleCard(PnlGlobalInjectionsCardContent, TxtChevronGlobalInjections);
    }

    private void BtnToggleProjectInjectionsCard_Click(object sender, RoutedEventArgs e)
    {
        ToggleCard(PnlProjectInjectionsCardContent, TxtChevronProjectInjections);
    }

    private void ToggleCard(StackPanel? contentPanel, TextBlock? chevronBlock)
    {
        if (contentPanel == null) return;
        bool isCollapsed = contentPanel.Visibility == Visibility.Collapsed;
        contentPanel.Visibility = isCollapsed ? Visibility.Visible : Visibility.Collapsed;
        if (chevronBlock != null) chevronBlock.Text = isCollapsed ? "▼" : "▶";
    }

    private void ExpandCard(StackPanel? contentPanel, TextBlock? chevronBlock)
    {
        if (contentPanel == null) return;
        contentPanel.Visibility = Visibility.Visible;
        if (chevronBlock != null) chevronBlock.Text = "▼";
    }

    private async void BtnAddGlobalRuleFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string globalRulesDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "rules");
            Directory.CreateDirectory(globalRulesDir);

            string fileName = $"global_rule_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            string filePath = Path.Combine(globalRulesDir, fileName);
            await File.WriteAllTextAsync(filePath, $"# Globale Regel\n\nRegel-Anweisung hier einfügen...\n");

            bool isGlob = _projectSettings.LastSelectedProject == "Globaler Fokus (Alle Projects)" || _projectSettings.LastSelectedProject == "Globaler Fokus";
            string selProj = _projectSettings.LastSelectedProject ?? "agy-cli-dashboard";
            UpdateDocsTab(isGlob, selProj, selProj);
            OpenFileInEditor(filePath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnAddGlobalRuleFile_Click error: {ex.Message}");
        }
    }

    private void BtnCheckGlobalUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RecordMaintenanceTimestamp("check_global_updates");
            bool isGlob = _projectSettings.LastSelectedProject == "Globaler Fokus (Alle Projects)" || _projectSettings.LastSelectedProject == "Globaler Fokus";
            string selProj = _projectSettings.LastSelectedProject ?? "agy-cli-dashboard";
            UpdateDocsTab(isGlob, selProj, selProj);

            if (TxtChatInput != null)
            {
                string customKnowledgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "skills", "knowledge-base", "custom-knowledge").Replace('\\', '/');
                TxtChatInput.Text = $"[Recherchiere tiefgehend im Internet und aktualisiere das globale Wissen unter {customKnowledgePath}. Setze hierfür maximal viele Subagenten ein, um die Informationen parallel zu prüfen und auf den neuesten Stand zu bringen!]";
                TxtChatInput.Focus(FocusState.Programmatic);
                TxtChatInput.SelectionStart = TxtChatInput.Text.Length;
            }

            ShowNotice("Knowledge Updates", "Update-Prompt für Wissensdatenbank in Chat-Eingabe eingefügt.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnCheckGlobalUpdates_Click error: {ex.Message}");
        }
    }

    private void BtnUpdateModelSpecs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RecordMaintenanceTimestamp("update_model_specs");
            if (TxtChatInput != null)
            {
                string prompt = "Prüfe per CLI-Befehl 'agy models', welche KI-Modelle aktuell in Antigravity verfügbar sind. Recherchiere anschließend im Internet (z. B. auf OpenRouter oder der offiziellen Google AI Doku) nach den genauen Kontextgrenzen, Attention-Zonen und Output-Limits für jedes gefundene Modell. Aktualisiere abschließend unsere Skalierungs- und Zonenlogik in MainWindow.Prompt.cs, damit der Token-Monitor für alle aktuellen Modelle exakt kalibriert ist.";
                TxtChatInput.Text = prompt;
                TxtChatInput.Focus(FocusState.Programmatic);
                TxtChatInput.SelectionStart = TxtChatInput.Text.Length;
            }

            ShowNotice("Modell- & Skalen-Sync", "Modell-Update-Prompt in Chat-Eingabe eingefügt.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnUpdateModelSpecs_Click error: {ex.Message}");
        }
    }

    private async void BtnOpenProjectProtocol_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string targetProjName = _projectSettings.LastSelectedProject ?? "agy-cli-dashboard";
            var matchProj = _allProjects.FirstOrDefault(p => p.Name.Equals(targetProjName, StringComparison.OrdinalIgnoreCase));
            string projPath = matchProj?.Path ?? Path.Combine(_projectSettings?.BaseFolder ?? ProjectSettingsData.GetDefaultBaseFolder(), targetProjName);
            string protoPath = Path.Combine(projPath, "docs", "protocol.md");

            if (!File.Exists(protoPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(protoPath)!);
                await File.WriteAllTextAsync(protoPath, $"# Protokoll ({targetProjName})\n\n- [{DateTime.Now:yyyy-MM-dd HH:mm}] Initiales Protokoll erstellt.\n");
            }

            OpenFileInEditor(protoPath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnOpenProjectProtocol_Click error: {ex.Message}");
        }
    }

    private async void BtnAddProjectDoc_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string targetProjName = _projectSettings.LastSelectedProject ?? "agy-cli-dashboard";
            var matchProj = _allProjects.FirstOrDefault(p => p.Name.Equals(targetProjName, StringComparison.OrdinalIgnoreCase));
            string projPath = matchProj?.Path ?? Path.Combine(_projectSettings?.BaseFolder ?? ProjectSettingsData.GetDefaultBaseFolder(), targetProjName);
            string docsDir = Path.Combine(projPath, "docs");
            Directory.CreateDirectory(docsDir);

            string fileName = $"wissen_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            string filePath = Path.Combine(docsDir, fileName);
            await File.WriteAllTextAsync(filePath, $"# Neues Projekt-Wissen ({targetProjName})\n\nInhalt hier einfügen...\n");

            bool isGlob = _projectSettings?.LastSelectedProject == "Globaler Fokus (Alle Projects)" || _projectSettings?.LastSelectedProject == "Globaler Fokus";
            UpdateDocsTab(isGlob, targetProjName, targetProjName);
            OpenFileInEditor(filePath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnAddProjectDoc_Click error: {ex.Message}");
        }
    }

    private async void BtnAddGlobalKnowledge_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string globalKnowledgeDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "skills", "knowledge-base", "custom-knowledge");
            Directory.CreateDirectory(globalKnowledgeDir);

            string fileName = $"custom_knowledge_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            string filePath = Path.Combine(globalKnowledgeDir, fileName);
            await File.WriteAllTextAsync(filePath, $"# Globales Wissen\n\nErkenntnisse und Dokumentation hier einfügen...\n");

            bool isGlob = _projectSettings.LastSelectedProject == "Globaler Fokus (Alle Projects)" || _projectSettings.LastSelectedProject == "Globaler Fokus";
            string selProj = _projectSettings.LastSelectedProject ?? "agy-cli-dashboard";
            UpdateDocsTab(isGlob, selProj, selProj);
            OpenFileInEditor(filePath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnAddGlobalKnowledge_Click error: {ex.Message}");
        }
    }

    private async void RenderTextInjections(bool isGlobal, string projectName)
    {
        if (PnlGlobalInjectionsList == null || PnlProjectInjectionsList == null) return;

        try
        {
            int currentSeq = System.Threading.Interlocked.Increment(ref _renderInjectionsSeq);
            var ruleData = await Task.Run(() => LoadRuleControlData());
            var projectRulesData = await Task.Run(() => LoadProjectRulesData());
            if (currentSeq != _renderInjectionsSeq) return;

            PnlGlobalInjectionsList.Children.Clear();


            if (ruleData.custom_global_injections != null)
            {
                foreach (var item in ruleData.custom_global_injections)
                {
                    if (item == null) continue;
                    AddCustomInjectionRow(PnlGlobalInjectionsList, item, isGlobalList: true, onToggle: (val) => {
                        item.IsEnabled = val;
                        SaveRuleControlData(ruleData);
                    }, onEdit: () => {
                        StartEditingRule(item, isGlobalRule: true);
                    }, onDelete: () => {
                        ShowNotice("Geschützte Regel", $"Die globale Regel '{item.Name}' ist geschützt und kann nicht gelöscht werden. Du kannst sie jedoch per Schalter deaktivieren.", InfoBarSeverity.Warning);
                    });
                }
            }

            PnlProjectInjectionsList.Children.Clear();
            string targetProjKey = isGlobal ? _projectSettings.LastSelectedProject : projectName;
            if (string.IsNullOrWhiteSpace(targetProjKey) || targetProjKey.Contains("Global"))
            {
                targetProjKey = _allProjects.FirstOrDefault(p => p.IsVisible)?.Name ?? "agy-cli-dashboard";
            }

            if (TxtProjectInjectionHeader != null)
            {
                TxtProjectInjectionHeader.Text = "PROJEKT REGELN";
                SetSafeToolTip(TxtProjectInjectionHeader, $"Projektspezifische Regeln für '{targetProjKey}'");
            }

            if (!string.IsNullOrEmpty(targetProjKey) && projectRulesData.TryGetValue(targetProjKey, out var projList) && projList != null)
            {
                foreach (var pItem in projList.ToList())
                {
                    if (pItem == null) continue;
                    AddCustomInjectionRow(PnlProjectInjectionsList, pItem, isGlobalList: false, onToggle: (val) => {
                        pItem.IsEnabled = val;
                        SaveProjectRulesData(projectRulesData);
                    }, onEdit: () => {
                        StartEditingRule(pItem, isGlobalRule: false);
                    }, onDelete: () => {
                        projList.Remove(pItem);
                        SaveProjectRulesData(projectRulesData);
                        RenderTextInjections(isGlobal, projectName);
                    });
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"RenderTextInjections ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"RenderTextInjections error: {ex.Message}");
        }
    }

    private void StartEditingRule(CustomInjectionItem item, bool isGlobalRule)
    {
        _editingInjectionItem = item;
        _addingGlobalInjection = isGlobalRule;
        if (TxtAddInjectionTitle != null) TxtAddInjectionTitle.Text = isGlobalRule ? "GLOBALE REGEL BEARBEITEN" : "PROJEKT-REGEL BEARBEITEN";
        if (TxtNewInjectionName != null) TxtNewInjectionName.Text = item.Name ?? string.Empty;
        if (TxtNewInjectionContent != null) TxtNewInjectionContent.Text = item.Content ?? string.Empty;
        if (PnlAddInjectionDialog != null) PnlAddInjectionDialog.Visibility = Visibility.Visible;
        if (DocsPanelScrollViewer != null) DocsPanelScrollViewer.ChangeView(null, 0, null);
        if (TxtNewInjectionName != null) TxtNewInjectionName.Focus(FocusState.Programmatic);
    }

    private void SetSafeToolTip(UIElement element, string? text)
    {
        if (element == null || string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var tt = new ToolTip
            {
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 360
                }
            };
            ToolTipService.SetToolTip(element, tt);
        }
        catch { }
    }

    private static readonly HashSet<string> EssentialSystemRuleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Deutsch erzwingen",
        "Freigabe-Pflicht ('go')",
        "Auto-Reflexion",
        "Zusammenfassung & Status",
        "Git-Commit vor Änderung",
        "Protokoll-Führung",
        "Wissens-Sicherung",
        "Chat-Koordination",
        "Webbrowser öffnen",
        "Tools",
        "Deployment",
        "Subagents",
        "Wissen nutzen",
        "Dokumenten-Validierung",
        "DRY",
        "Tabellen verbot",
        "Dogfooding"
    };

    private static bool IsEssentialSystemRule(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string clean = name.Trim();
        if (EssentialSystemRuleNames.Contains(clean)) return true;
        string normalized = clean.Replace('‘', '\'').Replace('’', '\'').Replace('‚', '\'');
        return EssentialSystemRuleNames.Contains(normalized);
    }

    private void AddCustomInjectionRow(StackPanel parent, CustomInjectionItem item, bool isGlobalList, Action<bool> onToggle, Action onEdit, Action onDelete)
    {
        if (item == null) return;
        string safeName = item.Name ?? "Regel";
        string safeContent = item.Content ?? string.Empty;

        var border = new Border
        {
            Background = (Brush)Application.Current.Resources["AgySurfaceBg"],
            BorderBrush = (Brush)Application.Current.Resources["AgyBorderFaint"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 2, 0, 2)
        };

        SetSafeToolTip(border, safeContent);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bool isAutoSync = string.Equals(item.Id, CuratedModelsRuleId, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(item.Name, CuratedModelsRuleName, StringComparison.OrdinalIgnoreCase);

        var spText = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var pnlTitle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var txtName = new TextBlock
        {
            Text = safeName,
            FontSize = 11.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["AgyTextSecondary"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        pnlTitle.Children.Add(txtName);

        if (isAutoSync)
        {
            var badgeAuto = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(32, 59, 130, 246)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(90, 59, 130, 246)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            var txtBadge = new TextBlock
            {
                Text = "AUTO-SYNC",
                FontSize = 8.5,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 96, 165, 250))
            };
            badgeAuto.Child = txtBadge;
            SetSafeToolTip(badgeAuto, "Wird beim Dashboard-Start automatisch über die OpenRouter-API aktualisiert (schreibgeschützt).");
            pnlTitle.Children.Add(badgeAuto);
        }
        spText.Children.Add(pnlTitle);

        if (isGlobalList)
        {
            var txtSnippet = new TextBlock
            {
                Text = isAutoSync 
                    ? "OpenRouter Live-Katalog • Automatisch gepflegt" 
                    : (safeContent.Length > 40 ? safeContent.Substring(0, 37) + "..." : safeContent),
                FontSize = 9,
                Foreground = isAutoSync 
                    ? new SolidColorBrush(Windows.UI.Color.FromArgb(220, 96, 165, 250)) 
                    : (Brush)Application.Current.Resources["AgyTextSubtle"],
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            spText.Children.Add(txtSnippet);
        }
        Grid.SetColumn(spText, 0);
        grid.Children.Add(spText);

        var ts = new ToggleSwitch
        {
            IsOn = item.IsEnabled,
            OffContent = null,
            OnContent = null,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(4, 0, 4, 0)
        };
        ts.Toggled += (s, e) => onToggle(ts.IsOn);
        Grid.SetColumn(ts, 1);
        grid.Children.Add(ts);

        if (isAutoSync)
        {
            var btnSyncInfo = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 2, 0),
                IsEnabled = false,
                Opacity = 0.55
            };
            SetSafeToolTip(btnSyncInfo, "Automatisch synchronisierte Regel (schreibgeschützt – wird beim Dashboard-Start über OpenRouter aktualisiert)");
            var imgSync = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/refresh-cw.svg"), Width = 11, Height = 11 };
            btnSyncInfo.Content = imgSync;
            Grid.SetColumn(btnSyncInfo, 2);
            grid.Children.Add(btnSyncInfo);
        }
        else if (!isGlobalList)
        {
            var btnEdit = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 2, 0)
            };
            SetSafeToolTip(btnEdit, "Regel bearbeiten");
            var imgEdit = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/edit-3.svg"), Width = 12, Height = 12 };
            btnEdit.Content = imgEdit;
            btnEdit.Click += (s, e) => onEdit();
            Grid.SetColumn(btnEdit, 2);
            grid.Children.Add(btnEdit);
        }

        bool isProtected = isGlobalList || IsEssentialSystemRule(item.Name);
        if (isProtected)
        {
            var btnLock = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                IsEnabled = false,
                Opacity = 0.55
            };
            SetSafeToolTip(btnLock, isGlobalList 
                ? "Geschützte globale Regel (kann deaktiviert, aber nicht gelöscht werden)" 
                : "Geschützte Systemregel (kann deaktiviert, aber nicht gelöscht werden)");
            var imgLock = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/lock.svg"), Width = 12, Height = 12 };
            btnLock.Content = imgLock;
            Grid.SetColumn(btnLock, 3);
            grid.Children.Add(btnLock);
        }
        else
        {
            var btnDel = new Button
            {
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4)
            };
            SetSafeToolTip(btnDel, "Regel löschen");
            var imgDel = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/trash-2.svg"), Width = 12, Height = 12 };
            btnDel.Content = imgDel;
            btnDel.Click += (s, e) => onDelete();
            Grid.SetColumn(btnDel, 3);
            grid.Children.Add(btnDel);
        }

        border.Child = grid;
        parent.Children.Add(border);
    }

    private void BtnAddGlobalInjection_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnAddGlobalInjection_Click)} triggered.");
        _editingInjectionItem = null;
        _addingGlobalInjection = true;
        if (TxtAddInjectionTitle != null) TxtAddInjectionTitle.Text = "NEUE GLOBALE REGEL HINZUFÜGEN";
        if (TxtNewInjectionName != null) TxtNewInjectionName.Text = string.Empty;
        if (TxtNewInjectionContent != null) TxtNewInjectionContent.Text = string.Empty;
        if (PnlAddInjectionDialog != null) PnlAddInjectionDialog.Visibility = Visibility.Visible;
        if (DocsPanelScrollViewer != null) DocsPanelScrollViewer.ChangeView(null, 0, null);
        if (TxtNewInjectionName != null) TxtNewInjectionName.Focus(FocusState.Programmatic);
    }

    private void BtnAddProjectInjection_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnAddProjectInjection_Click)} triggered.");
        string selectedProject = GetSelectedProjectName();
        if (string.IsNullOrWhiteSpace(selectedProject) || selectedProject.Contains("Global"))
        {
            selectedProject = _allProjects.FirstOrDefault(p => p.IsVisible)?.Name ?? "agy-cli-dashboard";
        }

        _editingInjectionItem = null;
        _addingGlobalInjection = false;
        if (TxtAddInjectionTitle != null) TxtAddInjectionTitle.Text = $"NEUE PROJEKT-REGEL HINZUFÜGEN ({selectedProject})";
        if (TxtNewInjectionName != null) TxtNewInjectionName.Text = string.Empty;
        if (TxtNewInjectionContent != null) TxtNewInjectionContent.Text = string.Empty;
        if (PnlAddInjectionDialog != null) PnlAddInjectionDialog.Visibility = Visibility.Visible;
        if (DocsPanelScrollViewer != null) DocsPanelScrollViewer.ChangeView(null, 0, null);
        if (TxtNewInjectionName != null) TxtNewInjectionName.Focus(FocusState.Programmatic);
    }

    private void BtnCancelAddInjection_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnCancelAddInjection_Click)} triggered.");
        _editingInjectionItem = null;
        if (PnlAddInjectionDialog != null) PnlAddInjectionDialog.Visibility = Visibility.Collapsed;
    }

    private void BtnSaveNewInjection_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnSaveNewInjection_Click)} triggered.");
        string name = TxtNewInjectionName?.Text.Trim() ?? string.Empty;
        string content = TxtNewInjectionContent?.Text.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowNotice("Eingabe erforderlich", "Bitte gib einen Kurznamen für das Wissen / die Regel ein.", InfoBarSeverity.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(content))
        {
            ShowNotice("Eingabe erforderlich", "Bitte gib den Inhalt für das Wissen / die Regel ein.", InfoBarSeverity.Warning);
            return;
        }

        if (_editingInjectionItem != null)
        {
            string targetId = _editingInjectionItem.Id;
            string oldName = _editingInjectionItem.Name ?? string.Empty;

            if (_addingGlobalInjection)
            {
                var ruleData = LoadRuleControlData();
                var targetInGlobal = ruleData.custom_global_injections?.FirstOrDefault(x => (!string.IsNullOrEmpty(targetId) && x.Id == targetId) || x.Name == oldName || x == _editingInjectionItem);
                if (targetInGlobal != null)
                {
                    targetInGlobal.Name = name;
                    targetInGlobal.Content = content;
                    if (string.IsNullOrWhiteSpace(targetInGlobal.Id)) targetInGlobal.Id = targetId;
                }
                SaveRuleControlData(ruleData);
            }
            else
            {
                var projData = LoadProjectRulesData();
                foreach (var kvp in projData)
                {
                    var targetInProj = kvp.Value.FirstOrDefault(x => (!string.IsNullOrEmpty(targetId) && x.Id == targetId) || x.Name == oldName || x == _editingInjectionItem);
                    if (targetInProj != null)
                    {
                        targetInProj.Name = name;
                        targetInProj.Content = content;
                        if (string.IsNullOrWhiteSpace(targetInProj.Id)) targetInProj.Id = targetId;
                        break;
                    }
                }
                SaveProjectRulesData(projData);
            }

            _editingInjectionItem.Name = name;
            _editingInjectionItem.Content = content;

            _editingInjectionItem = null;
            if (PnlAddInjectionDialog != null) PnlAddInjectionDialog.Visibility = Visibility.Collapsed;
            if (TxtNewInjectionName != null) TxtNewInjectionName.Text = string.Empty;
            if (TxtNewInjectionContent != null) TxtNewInjectionContent.Text = string.Empty;

            string selProj = GetSelectedProjectName();
            bool isGlob = string.IsNullOrWhiteSpace(selProj) || selProj.Contains("Global") || selProj.Equals("all", StringComparison.OrdinalIgnoreCase);
            UpdateDocsTab(isGlob, selProj, selProj);
            ShowNotice("Speichern erfolgreich", $"Die Regel/das Wissen '{name}' wurde erfolgreich aktualisiert.", InfoBarSeverity.Success);
            return;
        }

        string projectKey = GetSelectedProjectName();
        if (string.IsNullOrWhiteSpace(projectKey) || projectKey.Contains("Global"))
        {
            projectKey = _allProjects.FirstOrDefault(p => p.IsVisible)?.Name ?? "agy-cli-dashboard";
        }

        var newItem = new CustomInjectionItem
        {
            Name = name,
            Content = content,
            IsEnabled = true,
            IsGlobal = _addingGlobalInjection,
            TargetProject = _addingGlobalInjection ? string.Empty : projectKey
        };

        try
        {
            if (_addingGlobalInjection)
            {
                var ruleData = LoadRuleControlData();
                if (ruleData.custom_global_injections == null) ruleData.custom_global_injections = new List<CustomInjectionItem>();
                ruleData.custom_global_injections.Add(newItem);
                SaveRuleControlData(ruleData);
            }
            else
            {
                var projData = LoadProjectRulesData();
                if (!projData.ContainsKey(projectKey))
                {
                    projData[projectKey] = new List<CustomInjectionItem>();
                }
                projData[projectKey].Add(newItem);
                SaveProjectRulesData(projData);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnSaveNewInjection_Click ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Error saving injection: {ex.Message}");
        }

        _editingInjectionItem = null;
        if (PnlAddInjectionDialog != null) PnlAddInjectionDialog.Visibility = Visibility.Collapsed;
        if (TxtNewInjectionName != null) TxtNewInjectionName.Text = string.Empty;
        if (TxtNewInjectionContent != null) TxtNewInjectionContent.Text = string.Empty;

        string activeSelProj = GetSelectedProjectName();
        bool activeIsGlob = string.IsNullOrWhiteSpace(activeSelProj) || activeSelProj.Contains("Global") || activeSelProj.Equals("all", StringComparison.OrdinalIgnoreCase);
        UpdateDocsTab(activeIsGlob, activeSelProj, activeSelProj);
        ShowNotice("Speichern erfolgreich", $"Die Regel/das Wissen '{name}' wurde erfolgreich neu hinzugefügt.", InfoBarSeverity.Success);
    }
}

