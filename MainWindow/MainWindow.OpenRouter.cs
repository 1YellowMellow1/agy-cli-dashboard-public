using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private async Task FetchOpenRouterCreditsAsync()
    {
        try
        {
            string apiKey = GetOpenRouterApiKey()?.Trim() ?? "";
            if (string.IsNullOrEmpty(apiKey)) return;
            if (apiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) apiKey = apiKey.Substring(7).Trim();

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/credits");
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            using var response = await _healthHttpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object)
                {
                    double totalCredits = 0;
                    double totalUsage = 0;

                    if (dataElement.TryGetProperty("total_credits", out var tcProp))
                    {
                        if (tcProp.ValueKind == JsonValueKind.Number) totalCredits = tcProp.GetDouble();
                        else if (tcProp.ValueKind == JsonValueKind.String && double.TryParse(tcProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedTc)) totalCredits = parsedTc;
                    }

                    if (dataElement.TryGetProperty("total_usage", out var tuProp))
                    {
                        if (tuProp.ValueKind == JsonValueKind.Number) totalUsage = tuProp.GetDouble();
                        else if (tuProp.ValueKind == JsonValueKind.String && double.TryParse(tuProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedTu)) totalUsage = parsedTu;
                    }

                    double remaining = totalCredits - totalUsage;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (TxtOpenRouterBalance != null)
                        {
                            TxtOpenRouterBalance.Text = remaining.ToString("0.00", CultureInfo.InvariantCulture);
                        }
                        UpdateOpenRouterBalanceVisibility();
                    });
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"FetchOpenRouterCreditsAsync error: {ex.Message}");
        }
    }

    public void UpdateOpenRouterBalanceVisibility()
    {
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => UpdateOpenRouterBalanceVisibility());
            return;
        }

        if (BdrOpenRouterBalance == null) return;

        bool isClawCode = false;
        if (string.IsNullOrEmpty(_currentEditingFilePath) && !string.IsNullOrEmpty(_activeTerminalId))
        {
            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
            isClawCode = activeSession != null && string.Equals(activeSession.AgentType, "clawcode", StringComparison.OrdinalIgnoreCase);
        }

        BdrOpenRouterBalance.Visibility = isClawCode ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<OpenRouterModelItem> ParseOpenRouterModelsJson(string json)
    {
        List<OpenRouterModelItem> list = new();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out JsonElement dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement elem in dataArray.EnumerateArray())
                {
                    string id = elem.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String ? idProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(id)) continue;

                    string name = elem.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String ? nameProp.GetString() ?? id : id;
                    string desc = elem.TryGetProperty("description", out var descProp) && descProp.ValueKind == JsonValueKind.String ? descProp.GetString() ?? "" : "";
                    
                    int context = 0;
                    if (elem.TryGetProperty("context_length", out var ctxProp) && ctxProp.ValueKind == JsonValueKind.Number)
                    {
                        ctxProp.TryGetInt32(out context);
                    }

                    // Pricing parsen (Preis pro Token in $)
                    double promptPrice = 0, completionPrice = 0;
                    if (elem.TryGetProperty("pricing", out var pricingProp) && pricingProp.ValueKind == JsonValueKind.Object)
                    {
                        if (pricingProp.TryGetProperty("prompt", out var ppProp))
                        {
                            if (ppProp.ValueKind == JsonValueKind.String)
                                double.TryParse(ppProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out promptPrice);
                            else if (ppProp.ValueKind == JsonValueKind.Number)
                                promptPrice = ppProp.GetDouble();
                        }
                        if (pricingProp.TryGetProperty("completion", out var cpProp))
                        {
                            if (cpProp.ValueKind == JsonValueKind.String)
                                double.TryParse(cpProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out completionPrice);
                            else if (cpProp.ValueKind == JsonValueKind.Number)
                                completionPrice = cpProp.GetDouble();
                        }
                    }

                    long created = 0;
                    if (elem.TryGetProperty("created", out var crProp) && crProp.ValueKind == JsonValueKind.Number)
                    {
                        crProp.TryGetInt64(out created);
                    }

                    double intelligenceIndex = 0;
                    if (elem.TryGetProperty("benchmarks", out var bmProp) && bmProp.ValueKind == JsonValueKind.Object)
                    {
                        if (bmProp.TryGetProperty("artificial_analysis", out var aaProp) && aaProp.ValueKind == JsonValueKind.Object)
                        {
                            if (aaProp.TryGetProperty("intelligence_index", out var iiProp))
                            {
                                if (iiProp.ValueKind == JsonValueKind.Number) intelligenceIndex = iiProp.GetDouble();
                                else if (iiProp.ValueKind == JsonValueKind.String) double.TryParse(iiProp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out intelligenceIndex);
                            }
                        }
                    }

                    string modality = "";
                    List<string> outputModalities = new();
                    if (elem.TryGetProperty("architecture", out var archProp) && archProp.ValueKind == JsonValueKind.Object)
                    {
                        if (archProp.TryGetProperty("modality", out var modProp) && modProp.ValueKind == JsonValueKind.String)
                        {
                            modality = modProp.GetString() ?? "";
                        }
                        if (archProp.TryGetProperty("output_modalities", out var outModProp) && outModProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var om in outModProp.EnumerateArray())
                            {
                                if (om.ValueKind == JsonValueKind.String)
                                {
                                    string s = om.GetString() ?? "";
                                    if (!string.IsNullOrEmpty(s)) outputModalities.Add(s);
                                }
                            }
                        }
                    }

                    list.Add(new OpenRouterModelItem
                    {
                        Id = id,
                        Name = name,
                        Description = desc,
                        ContextLength = context,
                        PromptPrice = promptPrice,
                        CompletionPrice = completionPrice,
                        Created = created,
                        IntelligenceIndex = intelligenceIndex,
                        Modality = modality,
                        OutputModalities = outputModalities,
                        IsFavorite = _favoriteModelIds.Contains(id)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"ParseOpenRouterModelsJson Error: {ex.Message}");
        }
        return list;
    }

    private List<OpenRouterModelItem> GetDefaultFallbackModels()
    {
        var defaults = new List<string>
        {
            "google/gemini-3.8-flash", "google/gemini-3.7-flash", "google/gemini-3.6-flash", "google/gemini-2.5-flash-thinking", "anthropic/claude-sonnet-5",
            "anthropic/claude-opus-5", "anthropic/claude-sonnet-4.6", "openai/gpt-5.6-sol",
            "openai/gpt-5.6-terra", "openai/gpt-4o", "x-ai/grok-4.5", "meta-llama/llama-3.3-70b-instruct"
        };
        return defaults.Select(m => new OpenRouterModelItem
        {
            Id = m,
            Name = m,
            ContextLength = 200000,
            IsFavorite = _favoriteModelIds.Contains(m)
        }).ToList();
    }
}
