using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using AgyCliDashboard.Helpers;

namespace AgyCliDashboard.Services;

public class LocalizationService
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    public event Action? LanguageChanged;

    private Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _fallbackStrings = new(StringComparer.OrdinalIgnoreCase);

    public string CurrentLanguage { get; private set; } = "de";
    public string ConfiguredLanguage { get; private set; } = "auto";

    private LocalizationService()
    {
        LoadStringsFromDisk("de", _fallbackStrings);
    }

    public void Initialize(string configuredLanguage)
    {
        SetLanguage(configuredLanguage, notify: false);
    }

    public static string ResolveEffectiveLanguage(string configured)
    {
        string norm = (configured ?? "auto").Trim().ToLowerInvariant();
        if (norm == "de" || norm == "en") return norm;

        try
        {
            var sysLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
            return sysLang == "de" ? "de" : "en";
        }
        catch
        {
            return "de";
        }
    }

    public void SetLanguage(string lang, bool notify = true)
    {
        ConfiguredLanguage = string.IsNullOrWhiteSpace(lang) ? "auto" : lang.Trim().ToLowerInvariant();
        CurrentLanguage = ResolveEffectiveLanguage(ConfiguredLanguage);

        var newStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        LoadStringsFromDisk(CurrentLanguage, newStrings);
        _strings = newStrings;

        if (notify)
        {
            LanguageChanged?.Invoke();
        }
    }

    public string Get(string key, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback ?? string.Empty;

        if (_strings.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        if (_fallbackStrings.TryGetValue(key, out var fb) && !string.IsNullOrWhiteSpace(fb))
        {
            return fb;
        }

        return fallback ?? key;
    }

    public string Format(string key, params object[] args)
    {
        string template = Get(key);
        if (args == null || args.Length == 0) return template;
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private void LoadStringsFromDisk(string lang, Dictionary<string, string> target)
    {
        try
        {
            string baseDir = AppContext.BaseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            string localePath = Path.Combine(baseDir, "Assets", "Locales", $"{lang}.json");

            if (!File.Exists(localePath))
            {
                var dir = new DirectoryInfo(baseDir);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Assets", "Locales", $"{lang}.json");
                    if (File.Exists(candidate))
                    {
                        localePath = candidate;
                        break;
                    }
                    dir = dir.Parent;
                }
            }

            if (File.Exists(localePath))
            {
                string json = File.ReadAllText(localePath);
                using var doc = JsonDocument.Parse(json);
                FlattenJsonElement(doc.RootElement, "", target);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[LocalizationService] Error loading locale '{lang}': {ex.Message}");
        }
    }

    private static void FlattenJsonElement(JsonElement element, string prefix, Dictionary<string, string> target)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                string key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                FlattenJsonElement(prop.Value, key, target);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            target[prefix] = element.GetString() ?? "";
        }
    }
}
