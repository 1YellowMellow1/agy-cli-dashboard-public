using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private readonly object _settingsDebounceLock = new object();

    private List<OpenRouterModelItem> _cachedOpenRouterModels = new();
    private HashSet<string> _favoriteModelIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient _modelHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    private string GetFavoriteModelsFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "favorite_models.json");

    private string GetOpenRouterCacheFilePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "openrouter_models_cache.json");

    private string _modelFilterProvider = "";
    private bool _modelFilterFreeOnly = false;
    private bool _showPriceScore = false;
    private string _modelSortMode = "name";
    private bool _modelFilterFavoritesOnly = false;
    private bool _isUpdatingFilterControls = false;

    private void OnSettingsFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!string.Equals(e.Name, "settings.json", StringComparison.OrdinalIgnoreCase)) return;

        lock (_settingsDebounceLock)
        {
            if (_settingsWatcher == null) return;

            _settingsDebounceTimer ??= new Timer(_ =>
            {
                UpdateModelNameFromSettings();
            }, null, Timeout.Infinite, Timeout.Infinite);

            _settingsDebounceTimer.Change(300, Timeout.Infinite);
        }
    }

    private void OnSettingsFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!string.Equals(e.Name, "settings.json", StringComparison.OrdinalIgnoreCase)) return;

        lock (_settingsDebounceLock)
        {
            if (_settingsWatcher == null) return;

            _settingsDebounceTimer ??= new Timer(_ =>
            {
                UpdateModelNameFromSettings();
            }, null, Timeout.Infinite, Timeout.Infinite);

            _settingsDebounceTimer.Change(300, Timeout.Infinite);
        }
    }

    private async void InitModelWatcher()
    {
        try
        {
            LoadFavoriteModels();
            await FetchOpenRouterModelsAsync(forceRefresh: false);
            UpdateModelNameFromSettings();

            string configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli");
            if (Directory.Exists(configDir))
            {
                if (_settingsWatcher != null)
                {
                    _settingsWatcher.EnableRaisingEvents = false;
                    _settingsWatcher.Changed -= OnSettingsFileChanged;
                    _settingsWatcher.Created -= OnSettingsFileChanged;
                    _settingsWatcher.Renamed -= OnSettingsFileRenamed;
                    _settingsWatcher.Dispose();
                    _settingsWatcher = null;
                }

                _settingsWatcher = new FileSystemWatcher(configDir, "*.*");
                _settingsWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.Attributes | NotifyFilters.Size;

                _settingsWatcher.Changed += OnSettingsFileChanged;
                _settingsWatcher.Created += OnSettingsFileChanged;
                _settingsWatcher.Renamed += OnSettingsFileRenamed;

                _settingsWatcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[InitModelWatcher] Exception: {ex.Message}");
        }
    }

    private async void UpdateModelNameFromSettings()
    {
        string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "settings.json");
        if (!File.Exists(settingsPath)) return;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string json = await reader.ReadToEndAsync();
                
                if (string.IsNullOrWhiteSpace(json)) 
                {
                    await Task.Delay(150);
                    continue;
                }

                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("model", out JsonElement modelElem))
                {
                    string? modelName = modelElem.GetString();
                    if (!string.IsNullOrEmpty(modelName))
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
                            if (activeSession != null && string.Equals(activeSession.AgentType, "clawcode", StringComparison.OrdinalIgnoreCase))
                            {
                                return;
                            }

                            string effectiveModel = modelName;
                            if (activeSession != null && !string.IsNullOrEmpty(activeSession.ConversationId))
                            {
                                string perChatModel = LoadChatModel(activeSession.ConversationId);
                                if (!string.IsNullOrEmpty(perChatModel))
                                {
                                    effectiveModel = perChatModel;
                                }
                                else
                                {
                                    SaveChatModel(activeSession.ConversationId, effectiveModel);
                                }

                                AgyChatSession? activeChat = null;
                                lock (_allMasterChats)
                                {
                                    activeChat = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, activeSession.ConversationId, StringComparison.OrdinalIgnoreCase));
                                }
                                activeChat ??= _chats.FirstOrDefault(c => string.Equals(c.Id, activeSession.ConversationId, StringComparison.OrdinalIgnoreCase));
                                if (activeChat != null)
                                {
                                    activeChat.SelectedModel = effectiveModel;
                                }
                            }

                            if (TxtModelName != null)
                            {
                                CurrentModelId = effectiveModel;
                            }
                        });
                    }
                }
                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Lesen der settings.json (Versuch {attempt + 1}): {ex.Message}");
                if (attempt < 2) await Task.Delay(150);
            }
        }
    }



    private void PopulateModelProviderComboBox()
    {
        if (CboModelProvider == null) return;
        _isUpdatingFilterControls = true;
        CboModelProvider.Items.Clear();

        CboModelProvider.Items.Add(new ComboBoxItem 
        { 
            Content = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                Spacing = 8,
                Children = 
                { 
                    new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/globe.svg"), Width = 14, Height = 14 },
                    new TextBlock { Text = "Alle Anbieter", VerticalAlignment = VerticalAlignment.Center }
                } 
            }, 
            Tag = "" 
        });
        CboModelProvider.Items.Add(new ComboBoxItem 
        { 
            Content = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                Spacing = 8,
                Children = 
                { 
                    new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/pin.svg"), Width = 14, Height = 14 },
                    new TextBlock { Text = "Nur Favoriten", VerticalAlignment = VerticalAlignment.Center }
                } 
            }, 
            Tag = "FAVORITES_ONLY" 
        });
        CboModelProvider.Items.Add(new ComboBoxItem 
        { 
            Content = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                Spacing = 8,
                Children = 
                { 
                    new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/zap.svg"), Width = 14, Height = 14 },
                    new TextBlock { Text = "Nur Kostenlos", VerticalAlignment = VerticalAlignment.Center }
                } 
            }, 
            Tag = "FREE_ONLY" 
        });

        var providers = _cachedOpenRouterModels
            .Select(m => m.Provider)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p)
            .ToList();

        foreach (var p in providers)
        {
            CboModelProvider.Items.Add(new ComboBoxItem { Content = p, Tag = p });
        }

        CboModelProvider.SelectedIndex = 0;
        _isUpdatingFilterControls = false;
    }

    private void CboModelProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingFilterControls || CboModelProvider?.SelectedItem is not ComboBoxItem selected) return;

        string tag = selected.Tag?.ToString() ?? "";
        if (tag == "FAVORITES_ONLY")
        {
            _modelFilterProvider = "";
            _modelFilterFreeOnly = false;
            _modelFilterFavoritesOnly = true;
        }
        else if (tag == "FREE_ONLY")
        {
            _modelFilterProvider = "";
            _modelFilterFreeOnly = true;
            _modelFilterFavoritesOnly = false;
        }
        else
        {
            _modelFilterProvider = tag;
            _modelFilterFreeOnly = false;
            _modelFilterFavoritesOnly = false;
        }

        RenderModelPickerList(_cachedOpenRouterModels, TxtModelSearch?.Text ?? string.Empty);
    }

    private void CboModelSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingFilterControls || CboModelSort?.SelectedItem is not ComboBoxItem selected) return;

        _modelSortMode = selected.Tag?.ToString() ?? "name";
        RenderModelPickerList(_cachedOpenRouterModels, TxtModelSearch?.Text ?? string.Empty);
    }

    private void BtnTogglePriceScore_Click(object sender, RoutedEventArgs e)
    {
        if (BtnTogglePriceScore == null) return;
        _showPriceScore = BtnTogglePriceScore.IsChecked == true;

        if (_showPriceScore)
        {
            BtnTogglePriceScore.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246));
            BtnTogglePriceScore.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246));
            BtnTogglePriceScore.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(45, 59, 130, 246));
        }
        else
        {
            BtnTogglePriceScore.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184));
            BtnTogglePriceScore.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 65, 85));
            BtnTogglePriceScore.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42));
        }

        RenderModelPickerList(_cachedOpenRouterModels, TxtModelSearch?.Text ?? string.Empty);
    }

    private void LoadFavoriteModels()
    {
        try
        {
            string path = GetFavoriteModelsFilePath();
            var list = Services.FileTimestampCache.GetOrLoadSync(path, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<List<string>>(json);
                }
                return null;
            });
            if (list != null)
            {
                _favoriteModelIds = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
                return;
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"LoadFavoriteModels Error: {ex.Message}");
        }

        _favoriteModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "google/gemini-3.6-flash",
            "anthropic/claude-sonnet-5",
            "openai/gpt-5.6-sol",
            "anthropic/claude-sonnet-4.6",
            "google/gemini-2.5-flash-thinking",
            "openai/gpt-4o"
        };
        SaveFavoriteModels();
    }

    private void SaveFavoriteModels()
    {
        try
        {
            string path = GetFavoriteModelsFilePath();
            string? dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(_favoriteModelIds.ToList(), _jsonOptions);
            WriteAllTextAtomic(path, json);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SaveFavoriteModels Error: {ex.Message}");
        }
    }

    private async Task<List<OpenRouterModelItem>> FetchOpenRouterModelsAsync(bool forceRefresh = false)
    {
        string cachePath = GetOpenRouterCacheFilePath();
        List<OpenRouterModelItem> result;

        try
        {
            if (!forceRefresh && File.Exists(cachePath) && (DateTime.Now - File.GetLastWriteTime(cachePath)).TotalHours < 12)
            {
                string cachedJson = File.ReadAllText(cachePath);
                result = ParseOpenRouterModelsJson(cachedJson);
                if (result.Count > 0)
                {
                    _cachedOpenRouterModels = result;
                    SyncCuratedModelsToContextAndKnowledge(result);
                    return result;
                }
            }

            var resp = await _modelHttpClient.GetAsync("https://openrouter.ai/api/v1/models");
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync();
                result = ParseOpenRouterModelsJson(json);
                if (result.Count > 0)
                {
                    _cachedOpenRouterModels = result;
                    string? dir = Path.GetDirectoryName(cachePath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    WriteAllTextAtomic(cachePath, json);
                    SyncCuratedModelsToContextAndKnowledge(result);
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"FetchOpenRouterModelsAsync Error: {ex.Message}");
        }

        if (File.Exists(cachePath))
        {
            try
            {
                string cachedJson = File.ReadAllText(cachePath);
                result = ParseOpenRouterModelsJson(cachedJson);
                if (result.Count > 0)
                {
                    _cachedOpenRouterModels = result;
                    SyncCuratedModelsToContextAndKnowledge(result);
                    return result;
                }
            }
            catch { }
        }

        result = GetDefaultFallbackModels();
        _cachedOpenRouterModels = result;
        SyncCuratedModelsToContextAndKnowledge(result);
        return result;
    }

    private const string CuratedModelsRuleId = "sys_curated_ai_models";
    private const string CuratedModelsRuleName = "Aktuelle KI-Modelle & API-Referenz";

    private void SyncCuratedModelsToContextAndKnowledge(List<OpenRouterModelItem> allModels)
    {
        if (allModels == null || allModels.Count == 0) return;

        Task.Run(() =>
        {
            try
            {
                var validModels = allModels.Where(m => 
                    !string.IsNullOrWhiteSpace(m.Id) &&
                    !m.Id.EndsWith(":batch", StringComparison.OrdinalIgnoreCase) &&
                    !m.Id.EndsWith(":free", StringComparison.OrdinalIgnoreCase) &&
                    !m.Id.Contains("guard", StringComparison.OrdinalIgnoreCase) &&
                    !m.IsImageModel &&
                    m.ContextLength >= 16000).ToList();

                // 1. Provider-basierte Spitzenmodelle rein nach 'Created' absteigend sortiert (ohne Versionsnummern-Hardcoding)
                var googleModels = validModels
                    .Where(m => m.Id.StartsWith("google/gemini-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.Created)
                    .Take(4)
                    .ToList();

                var anthropicModels = validModels
                    .Where(m => m.Id.StartsWith("anthropic/claude-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.Created)
                    .Take(4)
                    .ToList();

                var openaiFrontier = validModels
                    .Where(m => m.Id.StartsWith("openai/gpt-", StringComparison.OrdinalIgnoreCase) &&
                                m.Id.IndexOf("codex", StringComparison.OrdinalIgnoreCase) < 0)
                    .OrderByDescending(m => m.Created)
                    .Take(2)
                    .ToList();

                var openaiCodex = validModels
                    .Where(m => m.Id.StartsWith("openai/", StringComparison.OrdinalIgnoreCase) &&
                                m.Id.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderByDescending(m => m.Created)
                    .Take(2)
                    .ToList();

                var openaiReasoning = validModels
                    .Where(m => m.Id.StartsWith("openai/o", StringComparison.OrdinalIgnoreCase) &&
                                m.Id.Length > 8 && char.IsDigit(m.Id[8]))
                    .OrderByDescending(m => m.Created)
                    .Take(2)
                    .ToList();

                var deepseekModels = validModels
                    .Where(m => m.Id.StartsWith("deepseek/deepseek-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.Created)
                    .Take(3)
                    .ToList();

                var metaModels = validModels
                    .Where(m => m.Id.StartsWith("meta-llama/llama-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.Created)
                    .Take(2)
                    .ToList();

                var xaiModels = validModels
                    .Where(m => m.Id.StartsWith("x-ai/grok-", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.Created)
                    .Take(2)
                    .ToList();

                var allOpenAi = openaiFrontier.Concat(openaiCodex).Concat(openaiReasoning).DistinctBy(m => m.Id).ToList();

                // 2. Dynamische Generierung der Empfehlungen aus den tatsächlich ermittelten Spitzenmodellen
                string googleRec = googleModels.Count > 0 
                    ? string.Join(", ", googleModels.Take(3).Select(m => m.DisplayName)) 
                    : "aktuelle Google Gemini Spitzenmodelle";

                string openaiRec = allOpenAi.Count > 0 
                    ? string.Join(", ", allOpenAi.Take(3).Select(m => m.DisplayName)) 
                    : "aktuelle OpenAI GPT/Codex/o-Series Spitzenmodelle";

                string anthropicRec = anthropicModels.Count > 0 
                    ? string.Join(", ", anthropicModels.Take(3).Select(m => m.DisplayName)) 
                    : "aktuelle Anthropic Claude Spitzenmodelle";

                string deepseekRec = deepseekModels.Count > 0 
                    ? string.Join(", ", deepseekModels.Take(2).Select(m => m.DisplayName)) 
                    : "aktuelle DeepSeek Spitzenmodelle";

                string currentDefaultGoogle = googleModels.FirstOrDefault()?.DisplayName ?? "Google Gemini 3.8 Flash";

                var sbRule = new StringBuilder();
                sbRule.AppendLine("Verbindliche Richtlinie für KI-Modellnamen, API-Integrationen und Modell-Empfehlungen (Stand: OpenRouter Live-Sync):");
                sbRule.AppendLine();
                sbRule.AppendLine("1. KOGNITIVE DIREKTIVE (Pre-Training Imbalance & Recency Bias):");
                sbRule.AppendLine("Deine Trainingsdaten unterliegen einer statistischen Verzerrung (Pre-Training Imbalance & Recency Bias): Ältere Modell-Generationen sind in deinen neuronalen Gewichten unverhältnismäßig stark verankert. Verlasse dich bei Modell-Empfehlungen, Architekturen, Skripten und API-Aufrufen NIEMALS auf dein internes Trainingsgedächtnis, sondern verwende AUSSCHLIESSLICH die in Abschnitt 3 live synchronisierten Spitzenmodelle.");
                sbRule.AppendLine($"- Empfohlene Nachfolger Google: {googleRec}.");
                sbRule.AppendLine($"- Empfohlene Nachfolger OpenAI: {openaiRec}.");
                sbRule.AppendLine($"- Empfohlene Nachfolger Anthropic: {anthropicRec}.");
                if (deepseekModels.Count > 0)
                {
                    sbRule.AppendLine($"- Empfohlene Nachfolger DeepSeek: {deepseekRec}.");
                }
                sbRule.AppendLine("- Jedes Modell, das NICHT in der folgenden Live-Referenz aufgeführt ist, gilt verbindlich als veraltet und darf für Neuentwicklungen nicht vorgeschlagen werden.");
                sbRule.AppendLine();
                sbRule.AppendLine("2. EIGENES MODELL / IDENTITÄT:");
                sbRule.AppendLine($"Wenn du nach deinem Modell gefragt wirst oder dich selbst benennst, beachte die Session-Settings (Standard: {currentDefaultGoogle}). Behaupte niemals, auf älteren Vorgänger-Modellen zu laufen.");
                sbRule.AppendLine();
                sbRule.AppendLine("3. AKTUELLE FRONTIER- & CODING-SPITZENMODELLE:");

                if (googleModels.Count > 0)
                {
                    sbRule.AppendLine("- Google:");
                    foreach (var m in googleModels) sbRule.AppendLine($"  * {m.Id} ({m.ContextFormatted}) - {m.DisplayName}");
                }

                if (anthropicModels.Count > 0)
                {
                    sbRule.AppendLine("- Anthropic:");
                    foreach (var m in anthropicModels) sbRule.AppendLine($"  * {m.Id} ({m.ContextFormatted}) - {m.DisplayName}");
                }

                if (allOpenAi.Count > 0)
                {
                    sbRule.AppendLine("- OpenAI:");
                    foreach (var m in allOpenAi) sbRule.AppendLine($"  * {m.Id} ({m.ContextFormatted}) - {m.DisplayName}");
                }

                if (deepseekModels.Count > 0)
                {
                    sbRule.AppendLine("- DeepSeek:");
                    foreach (var m in deepseekModels) sbRule.AppendLine($"  * {m.Id} ({m.ContextFormatted}) - {m.DisplayName}");
                }

                if (metaModels.Count > 0 || xaiModels.Count > 0)
                {
                    sbRule.AppendLine("- Meta & xAI:");
                    foreach (var m in metaModels) sbRule.AppendLine($"  * {m.Id} ({m.ContextFormatted}) - {m.DisplayName}");
                    foreach (var m in xaiModels) sbRule.AppendLine($"  * {m.Id} ({m.ContextFormatted}) - {m.DisplayName}");
                }

                string newRuleContent = sbRule.ToString().Trim();

                // 2. In rule_control.json einpflegen
                var ruleData = LoadRuleControlData();
                ruleData.custom_global_injections ??= new List<CustomInjectionItem>();

                var existingItem = ruleData.custom_global_injections.FirstOrDefault(i =>
                    string.Equals(i.Id, CuratedModelsRuleId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(i.Name, CuratedModelsRuleName, StringComparison.OrdinalIgnoreCase));

                bool ruleChanged = false;
                if (existingItem == null)
                {
                    existingItem = new CustomInjectionItem
                    {
                        Id = CuratedModelsRuleId,
                        Name = CuratedModelsRuleName,
                        Content = newRuleContent,
                        IsEnabled = true,
                        IsGlobal = true
                    };
                    ruleData.custom_global_injections.Add(existingItem);
                    ruleChanged = true;
                }
                else
                {
                    if (existingItem.Content != newRuleContent || !existingItem.IsEnabled || existingItem.Name != CuratedModelsRuleName)
                    {
                        existingItem.Id = CuratedModelsRuleId;
                        existingItem.Name = CuratedModelsRuleName;
                        existingItem.Content = newRuleContent;
                        existingItem.IsEnabled = true;
                        existingItem.IsGlobal = true;
                        ruleChanged = true;
                    }
                }

                if (ruleChanged)
                {
                    SaveRuleControlData(ruleData);
                }

                // 3. Ausführliche Custom-Knowledge Wissensdatei schreiben (ohne Markdown-Tabellen)
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string customKnowledgeDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "skills", "knowledge-base", "custom-knowledge");
                if (Directory.Exists(customKnowledgeDir))
                {
                    string knowledgeFilePath = Path.Combine(customKnowledgeDir, "ai-models.md");
                    var sbKnow = new StringBuilder();
                    sbKnow.AppendLine("# Aktuelle KI-Modelle & API-Referenz (OpenRouter Live-Katalog)");
                    sbKnow.AppendLine();
                    sbKnow.AppendLine($"- **Letzter Abgleich**: {DateTime.Now:dd.MM.yyyy HH:mm:ss} Uhr");
                    sbKnow.AppendLine($"- **Quelle**: OpenRouter Models API (`https://openrouter.ai/api/v1/models`)");
                    sbKnow.AppendLine($"- **Gesamtanzahl im Katalog**: {allModels.Count} Modelle");
                    sbKnow.AppendLine();
                    sbKnow.AppendLine("## Kognitiver Schutzfilter (Pre-Training Imbalance & Recency Bias)");
                    sbKnow.AppendLine("- Ältere Modell-Generationen sind in neuronalen Trainingsgewichten statistisch überrepräsentiert (Pre-Training Imbalance).");
                    sbKnow.AppendLine("- Verlasse dich bei Modell-Empfehlungen niemals auf dein internes Trainingsgedächtnis, sondern nutze ausschließlich die folgende tagesaktuelle Live-Referenz:");
                    sbKnow.AppendLine($"  * Empfohlene Nachfolger Google: {googleRec}.");
                    sbKnow.AppendLine($"  * Empfohlene Nachfolger OpenAI: {openaiRec}.");
                    sbKnow.AppendLine($"  * Empfohlene Nachfolger Anthropic: {anthropicRec}.");
                    if (deepseekModels.Count > 0)
                    {
                        sbKnow.AppendLine($"  * Empfohlene Nachfolger DeepSeek: {deepseekRec}.");
                    }
                    sbKnow.AppendLine("- Jedes Modell, das nicht in der folgenden Referenz gelistet ist, gilt verbindlich als veraltet.");
                    sbKnow.AppendLine();
                    sbKnow.AppendLine("## Kuratierte Spitzenmodelle für Coding & Architektur");
                    sbKnow.AppendLine();
                    if (googleModels.Count > 0)
                    {
                        sbKnow.AppendLine("### Google (Gemini)");
                        foreach (var m in googleModels) AppendModelDetail(sbKnow, m);
                        sbKnow.AppendLine();
                    }
                    if (anthropicModels.Count > 0)
                    {
                        sbKnow.AppendLine("### Anthropic (Claude)");
                        foreach (var m in anthropicModels) AppendModelDetail(sbKnow, m);
                        sbKnow.AppendLine();
                    }
                    if (allOpenAi.Count > 0)
                    {
                        sbKnow.AppendLine("### OpenAI (Frontier, Codex & Reasoning)");
                        foreach (var m in allOpenAi) AppendModelDetail(sbKnow, m);
                        sbKnow.AppendLine();
                    }
                    if (deepseekModels.Count > 0)
                    {
                        sbKnow.AppendLine("### DeepSeek (Coding & Open Weights)");
                        foreach (var m in deepseekModels) AppendModelDetail(sbKnow, m);
                        sbKnow.AppendLine();
                    }
                    if (metaModels.Count > 0 || xaiModels.Count > 0)
                    {
                        sbKnow.AppendLine("### Meta & xAI");
                        foreach (var m in metaModels) AppendModelDetail(sbKnow, m);
                        foreach (var m in xaiModels) AppendModelDetail(sbKnow, m);
                        sbKnow.AppendLine();
                    }

                    string newKnowledgeContent = sbKnow.ToString();
                    if (!File.Exists(knowledgeFilePath) || File.ReadAllText(knowledgeFilePath) != newKnowledgeContent)
                    {
                        WriteAllTextAtomic(knowledgeFilePath, newKnowledgeContent);
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"SyncCuratedModelsToContextAndKnowledge Error: {ex.Message}");
            }
        });
    }

    private static void AppendModelDetail(StringBuilder sb, OpenRouterModelItem m)
    {
        sb.AppendLine($"- **{m.DisplayName}** (`{m.Id}`)");
        sb.AppendLine($"  - Kontext: {m.ContextFormatted} ({m.ContextLength:N0} Tokens)");
        if (!string.IsNullOrWhiteSpace(m.Description))
        {
            string shortDesc = m.Description.Replace("\r", " ").Replace("\n", " ").Trim();
            if (shortDesc.Length > 200) shortDesc = shortDesc.Substring(0, 197) + "...";
            sb.AppendLine($"  - Beschreibung: {shortDesc}");
        }
        if (m.PromptPrice > 0 || m.CompletionPrice > 0)
        {
            sb.AppendLine($"  - Preis pro 1M Tokens: Prompt ${(m.PromptPrice * 1_000_000):0.00}, Completion ${(m.CompletionPrice * 1_000_000):0.00}");
        }
        else if (m.IsFree)
        {
            sb.AppendLine($"  - Kostenlos (Free Tier)");
        }
    }
}
