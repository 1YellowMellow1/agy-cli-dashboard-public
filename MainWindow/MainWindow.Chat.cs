using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private static readonly SolidColorBrush SotaCyanHeaderBrush = new(Color.FromArgb(255, 56, 189, 248));
    private static readonly SolidColorBrush FavAmberHeaderBrush = new(Color.FromArgb(255, 234, 179, 8));
    private static readonly SolidColorBrush Slate148HeaderBrush = new(Color.FromArgb(255, 148, 163, 184));


    private async Task SaveChatModelAsync(string convId, string modelName)
    {
        try
        {
            string brainPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
            string path = Path.Combine(brainPath, convId, "dashboard_chat.json");
            string? dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);

            string? projName = null;
            lock (_allMasterChats)
            {
                var ch = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase));
                if (ch != null && !string.IsNullOrWhiteSpace(ch.Workspace))
                {
                    projName = Path.GetFileName(ch.Workspace.Trim().TrimEnd('\\', '/'));
                }
            }
            bool isAutoReflexion = GetProjectAutoReflexionDefault(projName);
            if (File.Exists(path))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(path);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("IsAutoReflexionEnabled", out JsonElement autoElem) &&
                        (autoElem.ValueKind == JsonValueKind.True || autoElem.ValueKind == JsonValueKind.False))
                    {
                        isAutoReflexion = autoElem.GetBoolean();
                    }
                }
                catch { }
            }

            var data = new { SelectedModel = modelName, IsAutoReflexionEnabled = isAutoReflexion };
            await WriteAllTextAtomicAsync(path, JsonSerializer.Serialize(data));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SaveChatModel Error: {ex.Message}"); }
    }

    private void SaveChatModel(string convId, string modelName)
    {
        _ = SaveChatModelAsync(convId, modelName);
    }

    public async Task SaveChatAutoReflexionAsync(string convId, bool isEnabled)
    {
        try
        {
            string brainPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
            string path = Path.Combine(brainPath, convId, "dashboard_chat.json");
            string? dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);

            string selectedModel = string.Empty;
            if (File.Exists(path))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(path);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("SelectedModel", out JsonElement modelElem))
                    {
                        selectedModel = modelElem.GetString() ?? string.Empty;
                    }
                }
                catch { }
            }

            var data = new { SelectedModel = selectedModel, IsAutoReflexionEnabled = isEnabled };
            await WriteAllTextAtomicAsync(path, JsonSerializer.Serialize(data));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SaveChatAutoReflexion Error: {ex.Message}"); }
    }

    public void SaveChatAutoReflexion(string convId, bool isEnabled)
    {
        _ = SaveChatAutoReflexionAsync(convId, isEnabled);
    }

    public bool LoadChatAutoReflexion(string? convId, string? projectName = null)
    {
        if (!string.IsNullOrWhiteSpace(convId))
        {
            try
            {
                string brainPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
                string path = Path.Combine(brainPath, convId, "dashboard_chat.json");
                string? val = Services.FileTimestampCache.GetOrLoadSync(path, filePath =>
                {
                    if (File.Exists(filePath))
                    {
                        string json = File.ReadAllText(filePath);
                        using JsonDocument doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("IsAutoReflexionEnabled", out JsonElement elem) &&
                            (elem.ValueKind == JsonValueKind.True || elem.ValueKind == JsonValueKind.False))
                        {
                            return elem.GetBoolean().ToString();
                        }
                    }
                    return null;
                });
                if (val != null && bool.TryParse(val, out bool res))
                {
                    return res;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadChatAutoReflexion Error: {ex.Message}"); }
        }

        if (string.IsNullOrWhiteSpace(projectName) && !string.IsNullOrEmpty(convId))
        {
            AgyChatSession? chat = null;
            lock (_allMasterChats)
            {
                chat = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase));
            }
            if (chat != null && !string.IsNullOrWhiteSpace(chat.Workspace))
            {
                projectName = Path.GetFileName(chat.Workspace.Trim().TrimEnd('\\', '/'));
            }
        }

        return GetProjectAutoReflexionDefault(projectName);
    }

    private string LoadChatModel(string convId)
    {
        try
        {
            string brainPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
            string path = Path.Combine(brainPath, convId, "dashboard_chat.json");
            return Services.FileTimestampCache.GetOrLoadSync(path, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("SelectedModel", out JsonElement elem))
                    {
                        return elem.GetString() ?? string.Empty;
                    }
                }
                return string.Empty;
            }) ?? string.Empty;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LoadChatModel Error: {ex.Message}"); }
        return string.Empty;
    }

    private string LoadAgyModelFromSettings()
    {
        try
        {
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "settings.json");
            return Services.FileTimestampCache.GetOrLoadSync(settingsPath, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("model", out JsonElement modelElem))
                    {
                        string? val = modelElem.GetString();
                        if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
                    }
                }
                return string.Empty;
            }) ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    private string LoadClawCodeModelFromSettings()
    {
        try
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string clawSettingsPath = Path.Combine(userHome, ".claw", "settings.json");
            return Services.FileTimestampCache.GetOrLoadSync(clawSettingsPath, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("model", out JsonElement modelElem))
                    {
                        string? val = modelElem.GetString();
                        if (!string.IsNullOrEmpty(val) && val.Contains('/')) return val;
                    }
                }
                return string.Empty;
            }) ?? string.Empty;
        }
        catch { }
        return string.Empty;
    }

    private async Task ShowModelPickerPanel()
    {
        HideCustomPromptEditorPanel();
        if (UcModelPicker != null) UcModelPicker.Visibility = Visibility.Visible;
        if (PnlModelPicker != null) PnlModelPicker.Visibility = Visibility.Visible;
        if (TxtModelSearch != null) TxtModelSearch.Text = string.Empty;
        _modelFilterProvider = "";
        _modelFilterFreeOnly = false;
        _modelFilterFavoritesOnly = false;
        _showPriceScore = false;
        if (BtnTogglePriceScore != null)
        {
            BtnTogglePriceScore.IsChecked = false;
            BtnTogglePriceScore.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184));
            BtnTogglePriceScore.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 51, 65, 85));
            BtnTogglePriceScore.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 15, 23, 42));
        }
        var models = await FetchOpenRouterModelsAsync(forceRefresh: false);
        PopulateModelProviderComboBox();
        RenderModelPickerList(models, string.Empty);
    }

    private void HideModelPickerPanel()
    {
        if (UcModelPicker != null) UcModelPicker.Visibility = Visibility.Collapsed;
        if (PnlModelPicker != null) PnlModelPicker.Visibility = Visibility.Collapsed;
    }

    private void BtnModelPickerBack_Click(object sender, RoutedEventArgs e)
    {
        HideModelPickerPanel();
    }

    private async void BtnRefreshModels_Click(object sender, RoutedEventArgs e)
    {
        var models = await FetchOpenRouterModelsAsync(forceRefresh: true);
        RenderModelPickerList(models, TxtModelSearch?.Text ?? string.Empty);
    }

    private void TxtModelSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenderModelPickerList(_cachedOpenRouterModels, TxtModelSearch?.Text ?? string.Empty);
    }

    private void RenderModelPickerList(List<OpenRouterModelItem> allModels, string query)
    {
        if (UcModelPicker == null) return;
        query = query.Trim().ToLowerInvariant();

        foreach (var m in allModels)
        {
            m.IsFavorite = _favoriteModelIds.Contains(m.Id);
        }

        var filtered = string.IsNullOrEmpty(query)
            ? allModels.ToList()
            : allModels.Where(m => m.Id.ToLowerInvariant().Contains(query) || m.Name.ToLowerInvariant().Contains(query)).ToList();

        if (!string.IsNullOrEmpty(_modelFilterProvider))
        {
            filtered = filtered.Where(m => string.Equals(m.Provider, _modelFilterProvider, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (_modelFilterFreeOnly)
        {
            filtered = filtered.Where(m => m.IsFree).ToList();
        }

        if (_modelFilterFavoritesOnly)
        {
            filtered = filtered.Where(m => m.IsFavorite).ToList();
        }

        if (TxtModelCount != null)
        {
            TxtModelCount.Text = $"({filtered.Count} Modelle)";
        }

        string currentModel = CurrentModelId;

        double maxPrice = 0;
        if (filtered.Count > 0)
        {
            maxPrice = filtered.Max(m => m.PromptPrice + m.CompletionPrice);
        }

        IOrderedEnumerable<OpenRouterModelItem> SortModels(IEnumerable<OpenRouterModelItem> models)
        {
            return _modelSortMode switch
            {
                "price_asc" => models.OrderBy(m => m.PromptPrice).ThenBy(m => m.DisplayName),
                "price_desc" => models.OrderByDescending(m => m.PromptPrice).ThenBy(m => m.DisplayName),
                "ctx_desc" => models.OrderByDescending(m => m.ContextLength).ThenBy(m => m.DisplayName),
                _ => models.OrderBy(m => m.DisplayName)
            };
        }

        // 1. SOTA (State of the Art Frontier-Modelle: 1 Top-Modell pro AI-Lab)
        List<OpenRouterModelItem> sotaModels = new();
        if (!_modelFilterFavoritesOnly)
        {
            sotaModels = GetFrontierModels(filtered);
        }
        var sotaIds = new HashSet<string>(sotaModels.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

        // 2. Favoriten (persönlich markierte Modelle)
        var favorites = SortModels(filtered.Where(m => m.IsFavorite)).ToList();

        // 3. Alle übrigen Modelle (weder Favorit noch bereits in SOTA enthalten)
        var others = SortModels(filtered.Where(m => !m.IsFavorite && !sotaIds.Contains(m.Id))).ToList();

        var list = new List<ModelPickerListItemViewModel>();

        // SOTA Section rendern
        if (sotaModels.Count > 0)
        {
            list.Add(new ModelPickerHeaderViewModel
            {
                Title = "⚡ STATE OF THE ART",
                HeaderBrush = SotaCyanHeaderBrush,
                ShowTopDivider = false
            });

            foreach (var model in sotaModels)
            {
                list.Add(CreateModelRowViewModel(model, currentModel, maxPrice));
            }
        }

        // Favoriten Section rendern
        if (favorites.Count > 0)
        {
            list.Add(new ModelPickerHeaderViewModel
            {
                Title = "★ FAVORITEN",
                HeaderBrush = FavAmberHeaderBrush,
                ShowTopDivider = sotaModels.Count > 0
            });

            foreach (var model in favorites)
            {
                list.Add(CreateModelRowViewModel(model, currentModel, maxPrice));
            }
        }

        // Restliche Modelle Section rendern
        if (others.Count > 0)
        {
            if (sotaModels.Count > 0 || favorites.Count > 0)
            {
                list.Add(new ModelPickerHeaderViewModel
                {
                    Title = "ALLE OPENROUTER MODELLE",
                    HeaderBrush = Slate148HeaderBrush,
                    ShowTopDivider = true
                });
            }

            foreach (var model in others)
            {
                list.Add(CreateModelRowViewModel(model, currentModel, maxPrice));
            }
        }

        if (filtered.Count == 0)
        {
            list.Add(new ModelPickerHeaderViewModel
            {
                Title = "Keine passenden Modelle gefunden.",
                HeaderBrush = Slate148HeaderBrush,
                ShowTopDivider = false
            });
        }

        VisibleModels.Clear();
        foreach (var item in list)
        {
            VisibleModels.Add(item);
        }
    }

    private static readonly string[] FrontierProviders = new[]
    {
        "anthropic",
        "openai",
        "google",
        "deepseek",
        "x-ai",
        "qwen",
        "meta-llama",
        "mistralai",
        "moonshotai",
        "minimax"
    };

    private List<OpenRouterModelItem> GetFrontierModels(IEnumerable<OpenRouterModelItem> sourceModels)
    {
        var result = new List<OpenRouterModelItem>();
        var modelsList = sourceModels.ToList();

        foreach (var provider in FrontierProviders)
        {
            var candidates = modelsList.Where(m =>
            {
                if (!string.Equals(m.Provider, provider, StringComparison.OrdinalIgnoreCase))
                    return false;

                string id = m.Id.ToLowerInvariant();
                string name = (m.Name ?? string.Empty).ToLowerInvariant();

                // Keine speziellen Varianten (:free, :batch, :exact)
                if (id.EndsWith(":free") || id.EndsWith(":batch") || id.EndsWith(":exact"))
                    return false;

                // Keine Guard- oder Moderationsmodelle
                if (id.Contains("guard") || id.Contains("moderation") || id.Contains("safety"))
                    return false;

                // Keine reinen Bild-, Audio- oder Video-Generatoren
                if (m.IsImageModel || id.Contains("banana") || id.Contains("imagen") ||
                    id.Contains("diffusion") || id.Contains("lyria") ||
                    name.Contains("image") || name.Contains("audio") || name.Contains("clip"))
                    return false;

                return true;
            }).ToList();

            if (candidates.Count == 0) continue;

            // Google: Gemini bevorzugen vor Gemma Open-Weights
            if (string.Equals(provider, "google", StringComparison.OrdinalIgnoreCase))
            {
                var geminis = candidates.Where(m => m.Id.Contains("gemini", StringComparison.OrdinalIgnoreCase)).ToList();
                if (geminis.Count > 0) candidates = geminis;
            }

            // Leichtgewichts- / Mini-Modelle herausfiltern, falls vollwertige Flagships existieren
            var nonLightweight = candidates.Where(m =>
            {
                string id = m.Id.ToLowerInvariant();
                return !(id.Contains("haiku") || id.Contains("flash-lite") || id.Contains("nano") ||
                         id.Contains("scout") || id.Contains("small") || id.Contains("-lite") ||
                         id.Contains("mini") || id.Contains("micro") || id.Contains("ministral") ||
                         id.Contains("devstral"));
            }).ToList();

            if (nonLightweight.Count > 0)
            {
                candidates = nonLightweight;
            }

            // Ranking nach Recency (Created), Flagship-Tier & Benchmark (IntelligenceIndex)
            var topModel = candidates
                .OrderByDescending(m =>
                {
                    string id = m.Id.ToLowerInvariant();
                    double score = m.Created;

                    // Oberstes Flagship-Tier
                    if (id.Contains("opus") || id.Contains("sol-pro") || id.Contains("max-prime") ||
                        id.Contains("maverick") || id.Contains("mistral-large"))
                    {
                        score += 30.0 * 86400.0 * 10;
                    }
                    else if (id.Contains("max") || id.Contains("ultra") || id.Contains("pro"))
                    {
                        score += 15.0 * 86400.0 * 10;
                    }
                    else if (id.Contains("sonnet") || id.Contains("sol"))
                    {
                        score += 7.0 * 86400.0 * 10;
                    }

                    if (m.IntelligenceIndex > 0)
                    {
                        score += m.IntelligenceIndex * 86400.0 * 2.0;
                    }

                    return score;
                })
                .FirstOrDefault();

            if (topModel != null)
            {
                result.Add(topModel);
            }
        }

        return result;
    }

    private ModelPickerRowViewModel CreateModelRowViewModel(OpenRouterModelItem model, string currentModel, double maxPrice)
    {
        bool isActive = string.Equals(model.Id, currentModel, StringComparison.OrdinalIgnoreCase);

        string priceDisplay;
        if (_showPriceScore)
        {
            if (model.IsFree) priceDisplay = "Score: 0/100 (Kostenlos)";
            else if (maxPrice > 0)
            {
                double score = ((model.PromptPrice + model.CompletionPrice) / maxPrice) * 100.0;
                int scoreInt = Math.Max(1, (int)Math.Round(score));
                priceDisplay = $"Relative Kosten: {scoreInt}/100";
            }
            else priceDisplay = "Score: 0/100";
        }
        else
        {
            if (model.IsFree)
            {
                priceDisplay = "Kostenlos";
            }
            else
            {
                double inPer1M = model.PromptPrice * 1_000_000;
                double outPer1M = model.CompletionPrice * 1_000_000;
                priceDisplay = $"In: ${inPer1M:0.##} / 1M  •  Out: ${outPer1M:0.##} / 1M";
            }
        }

        return new ModelPickerRowViewModel(model)
        {
            IsActive = isActive,
            PriceDisplay = priceDisplay
        };
    }

    private void ToggleFavoriteModel(string modelId)
    {
        if (_favoriteModelIds.Contains(modelId))
            _favoriteModelIds.Remove(modelId);
        else
            _favoriteModelIds.Add(modelId);

        SaveFavoriteModels();

        RenderModelPickerList(_cachedOpenRouterModels, TxtModelSearch?.Text ?? string.Empty);
    }

    private TerminalTabSession? GetActiveTerminalSession()
    {
        return _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId) ?? _terminalSessions.FirstOrDefault();
    }

    private void SelectModel(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return;

        CurrentModelId = modelId;

        var activeTermSession = GetActiveTerminalSession();
        if (activeTermSession != null && !string.IsNullOrEmpty(activeTermSession.ConversationId))
        {
            SaveChatModel(activeTermSession.ConversationId, modelId);
            AgyChatSession? chat = null;
            lock (_allMasterChats)
            {
                chat = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, activeTermSession.ConversationId, StringComparison.OrdinalIgnoreCase));
            }
            chat ??= _chats.FirstOrDefault(c => string.Equals(c.Id, activeTermSession.ConversationId, StringComparison.OrdinalIgnoreCase));
            if (chat != null) chat.SelectedModel = modelId;
        }

        WriteModelToClawSettings(modelId);

        try
        {
            if (ActiveTerminal != null && ActiveTerminal.ConPTYTerm != null)
            {
                ActiveTerminal.ConPTYTerm.WriteToTerm($"/model {modelId}\r");
            }
        }
        catch { }

        HideModelPickerPanel();
    }

    private async Task WriteModelToClawSettingsAsync(string modelName)
    {
        if (string.IsNullOrEmpty(modelName) || !modelName.Contains('/') || modelName.Contains(' ')) return;
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string clawSettingsDir = Path.Combine(userHome, ".claw");
        string clawSettingsPath = Path.Combine(clawSettingsDir, "settings.json");

        try
        {
            Directory.CreateDirectory(clawSettingsDir);
            System.Text.Json.Nodes.JsonObject jsonNode;

            if (File.Exists(clawSettingsPath))
            {
                string json = await File.ReadAllTextAsync(clawSettingsPath);
                jsonNode = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
            }
            else
            {
                jsonNode = new System.Text.Json.Nodes.JsonObject();
            }

            jsonNode["model"] = modelName;
            await WriteAllTextAtomicAsync(clawSettingsPath, jsonNode.ToJsonString(_jsonOptions));
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"WriteModelToClawSettings Error: {ex.Message}");
        }
    }

    private void WriteModelToClawSettings(string modelName)
    {
        _ = WriteModelToClawSettingsAsync(modelName);
    }
}
