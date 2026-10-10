using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AgyCliDashboard.Helpers;

public record IgnoreStatus(bool IsGitIgnored, bool IsGeminiIgnored, bool IsReflectionIgnored)
{
    public static readonly IgnoreStatus Empty = new(false, false, false);
    public bool IsAnyIgnored => IsGitIgnored || IsGeminiIgnored || IsReflectionIgnored;
}

/// <summary>
/// Erkennt und evaluiert Regeln aus .gitignore, .geminiignore und .reflectionignore
/// mit blitzschnellem In-Memory-, TTL- und Zeitstempel-Caching.
/// </summary>
public static class IgnoreRuleMatcher
{
    private sealed class IgnoreRule
    {
        public string Pattern { get; }
        public bool IsNegated { get; }
        public bool IsDirectoryOnly { get; }
        public Regex RegexPattern { get; }

        public IgnoreRule(string rawLine)
        {
            string pattern = rawLine.Trim();
            if (pattern.StartsWith('!'))
            {
                IsNegated = true;
                pattern = pattern[1..].Trim();
            }

            if (pattern.EndsWith('/'))
            {
                IsDirectoryOnly = true;
                pattern = pattern.TrimEnd('/');
            }

            bool isRootOnly = false;
            if (pattern.StartsWith('/'))
            {
                isRootOnly = true;
                pattern = pattern.TrimStart('/');
            }
            else if (pattern.Contains('/'))
            {
                isRootOnly = true;
            }

            Pattern = pattern;

            // In .NET Regex.Escape wird '/' nicht escaped (da kein Regex-Metazeichen).
            // Daher ersetzen wir @"\*\*/" statt @"\*\*\/".
            string rx = Regex.Escape(pattern)
                .Replace(@"\*\*/", @"(?:.*/)?")
                .Replace(@"/\*\*", @"(?:/.*)?")
                .Replace(@"\*\*", @".*")
                .Replace(@"\*", @"[^/]*")
                .Replace(@"\?", @"[^/]")
                .Replace(@"\[", "[")
                .Replace(@"\]", "]");

            string regexStr = isRootOnly ? "^" + rx + "(?:/|$)" : "(?:^|/)" + rx + "(?:/|$)";
            RegexPattern = new Regex(regexStr, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        public bool Matches(string relPath, bool isDirectory)
        {
            var m = RegexPattern.Match(relPath);
            if (!m.Success) return false;

            if (IsDirectoryOnly)
            {
                // Wenn der Treffer vor dem Pfadende endet, wurde ein Eltern-Ordnersegment getroffen
                if (m.Index + m.Length < relPath.Length) return true;
                // Andernfalls muss das Ziel selbst ein Ordner sein
                return isDirectory;
            }
            return true;
        }
    }

    private sealed class IgnoreRuleSet
    {
        private readonly List<IgnoreRule> _rules;
        private readonly HashSet<string> _explicitEntries;
        private readonly HashSet<string> _negatedEntries;

        public static readonly IgnoreRuleSet Empty = new(new List<IgnoreRule>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public IgnoreRuleSet(List<IgnoreRule> rules, HashSet<string> explicitEntries, HashSet<string> negatedEntries)
        {
            _rules = rules;
            _explicitEntries = explicitEntries;
            _negatedEntries = negatedEntries;
        }

        public bool IsExplicit(string relPath) => _explicitEntries.Contains(relPath.Trim('/', '\\'));
        public bool IsNegated(string relPath) => _negatedEntries.Contains(relPath.Trim('/', '\\'));

        public bool IsIgnored(string relPath, bool isDirectory)
        {
            if (_rules.Count == 0) return false;
            bool ignored = false;
            foreach (var rule in _rules)
            {
                if (rule.Matches(relPath, isDirectory))
                {
                    ignored = !rule.IsNegated;
                }
            }
            return ignored;
        }
    }

    private sealed class ProjectIgnoreCacheEntry
    {
        public DateTime LastCheckedUtc { get; set; } = DateTime.MinValue;

        public DateTime GitLastWrite { get; set; } = DateTime.MinValue;
        public IgnoreRuleSet GitRules { get; set; } = IgnoreRuleSet.Empty;

        public DateTime GeminiLastWrite { get; set; } = DateTime.MinValue;
        public IgnoreRuleSet GeminiRules { get; set; } = IgnoreRuleSet.Empty;

        public DateTime ReflectionLastWrite { get; set; } = DateTime.MinValue;
        public IgnoreRuleSet ReflectionRules { get; set; } = IgnoreRuleSet.Empty;
    }

    private static readonly ConcurrentDictionary<string, ProjectIgnoreCacheEntry> _projectCaches = new(StringComparer.OrdinalIgnoreCase);

    public static void InvalidateCache(string? projectRoot = null)
    {
        if (string.IsNullOrEmpty(projectRoot))
        {
            _projectCaches.Clear();
        }
        else
        {
            _projectCaches.TryRemove(projectRoot, out _);
        }
    }

    private static ProjectIgnoreCacheEntry GetOrUpdateProjectCache(string projectRoot)
    {
        var entry = _projectCaches.GetOrAdd(projectRoot, _ => new ProjectIgnoreCacheEntry());

        // Performance-Guard: 500ms TTL für Dateisystem-Checks, um während eines einzelnen Tree-Renders
        // hunderte redundante GetFileAttributesEx-Aufrufe zu vermeiden
        var nowUtc = DateTime.UtcNow;
        if (nowUtc - entry.LastCheckedUtc > TimeSpan.FromMilliseconds(500))
        {
            entry.LastCheckedUtc = nowUtc;

            // 1. .gitignore
            string gitIgnoreFile = Path.Combine(projectRoot, ".gitignore");
            if (File.Exists(gitIgnoreFile))
            {
                var lastWrite = File.GetLastWriteTimeUtc(gitIgnoreFile);
                if (lastWrite != entry.GitLastWrite)
                {
                    var loaded = LoadRuleSet(gitIgnoreFile);
                    if (loaded != null)
                    {
                        entry.GitRules = loaded;
                        entry.GitLastWrite = lastWrite;
                    }
                }
            }
            else if (entry.GitLastWrite != DateTime.MinValue)
            {
                entry.GitRules = IgnoreRuleSet.Empty;
                entry.GitLastWrite = DateTime.MinValue;
            }

            // 2. .geminiignore
            string geminiIgnoreFile = Path.Combine(projectRoot, ".geminiignore");
            if (File.Exists(geminiIgnoreFile))
            {
                var lastWrite = File.GetLastWriteTimeUtc(geminiIgnoreFile);
                if (lastWrite != entry.GeminiLastWrite)
                {
                    var loaded = LoadRuleSet(geminiIgnoreFile);
                    if (loaded != null)
                    {
                        entry.GeminiRules = loaded;
                        entry.GeminiLastWrite = lastWrite;
                    }
                }
            }
            else if (entry.GeminiLastWrite != DateTime.MinValue)
            {
                entry.GeminiRules = IgnoreRuleSet.Empty;
                entry.GeminiLastWrite = DateTime.MinValue;
            }

            // 3. .reflectionignore
            string reflectionIgnoreFile = Path.Combine(projectRoot, ".reflectionignore");
            if (File.Exists(reflectionIgnoreFile))
            {
                var lastWrite = File.GetLastWriteTimeUtc(reflectionIgnoreFile);
                if (lastWrite != entry.ReflectionLastWrite)
                {
                    var loaded = LoadRuleSet(reflectionIgnoreFile);
                    if (loaded != null)
                    {
                        entry.ReflectionRules = loaded;
                        entry.ReflectionLastWrite = lastWrite;
                    }
                }
            }
            else if (entry.ReflectionLastWrite != DateTime.MinValue)
            {
                entry.ReflectionRules = IgnoreRuleSet.Empty;
                entry.ReflectionLastWrite = DateTime.MinValue;
            }
        }

        return entry;
    }

    public static bool IsExplicitlyIgnored(string projectRoot, string ignoreFileName, string relPath)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(relPath)) return false;
        var entry = GetOrUpdateProjectCache(projectRoot);

        if (ignoreFileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase))
            return entry.GitRules.IsExplicit(relPath);
        if (ignoreFileName.Equals(".geminiignore", StringComparison.OrdinalIgnoreCase))
            return entry.GeminiRules.IsExplicit(relPath);
        if (ignoreFileName.Equals(".reflectionignore", StringComparison.OrdinalIgnoreCase))
            return entry.ReflectionRules.IsExplicit(relPath);
        return false;
    }

    public static bool IsExplicitlyNegated(string projectRoot, string ignoreFileName, string relPath)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(relPath)) return false;
        var entry = GetOrUpdateProjectCache(projectRoot);

        if (ignoreFileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase))
            return entry.GitRules.IsNegated(relPath);
        if (ignoreFileName.Equals(".geminiignore", StringComparison.OrdinalIgnoreCase))
            return entry.GeminiRules.IsNegated(relPath);
        if (ignoreFileName.Equals(".reflectionignore", StringComparison.OrdinalIgnoreCase))
            return entry.ReflectionRules.IsNegated(relPath);
        return false;
    }

    public static IgnoreStatus CheckIgnoreStatus(string projectRoot, string fullPath, bool isFolder)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(fullPath))
        {
            return IgnoreStatus.Empty;
        }

        try
        {
            string relPath = Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrEmpty(relPath) || relPath == "." || relPath.StartsWith("..") || Path.IsPathRooted(relPath))
            {
                return IgnoreStatus.Empty;
            }

            var entry = GetOrUpdateProjectCache(projectRoot);

            bool isGitIgnored = entry.GitRules.IsIgnored(relPath, isFolder);
            bool isGeminiIgnored = entry.GeminiRules.IsIgnored(relPath, isFolder);
            bool isReflectionIgnored = entry.ReflectionRules.IsIgnored(relPath, isFolder) || AutoReflectionManager.IsIgnoredFile(fullPath, projectRoot);

            return new IgnoreStatus(isGitIgnored, isGeminiIgnored, isReflectionIgnored);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[IgnoreRuleMatcher] Error evaluating '{fullPath}': {ex.Message}");
            return IgnoreStatus.Empty;
        }
    }

    private static IgnoreRuleSet? LoadRuleSet(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);
            var rules = new List<IgnoreRule>();
            var explicitEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var negatedEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                rules.Add(new IgnoreRule(trimmed));

                string normalized = trimmed.Replace('\\', '/');
                if (normalized.StartsWith('!'))
                {
                    string neg = normalized[1..].Trim().Trim('/');
                    if (!string.IsNullOrEmpty(neg) && !neg.Contains('*') && !neg.Contains('?'))
                    {
                        negatedEntries.Add(neg);
                    }
                }
                else
                {
                    string exp = normalized.Trim('/');
                    if (!exp.Contains('*') && !exp.Contains('?'))
                    {
                        explicitEntries.Add(exp);
                    }
                }
            }
            return new IgnoreRuleSet(rules, explicitEntries, negatedEntries);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[IgnoreRuleMatcher] Error reading '{filePath}': {ex.Message}");
            return null;
        }
    }
}
