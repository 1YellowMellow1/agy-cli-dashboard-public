using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgyCliDashboard.Helpers;

/// <summary>
/// Manages agent self-reflection configurations, SSOT prompts, ignore-rule matching,
/// and native Stop-Hook detection for Antigravity CLI.
/// </summary>
public static class AutoReflectionManager
{
    public const string HeaderTag = "🔍 **Auto-Reflexion:**";

    public static readonly string GlobalPromptTemplatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini", "antigravity-cli", "hooks", "reflection_prompt.txt");

    private static string? _cachedPromptTemplate = null;
    private static DateTime _promptTemplateLastWrite = DateTime.MinValue;
    private static readonly object _templateLock = new();

    private const string FallbackPromptTemplate =
        "[Prüfe deine letzte Arbeit adaptiv nach Risiko, Aufwand und erbrachter Verifikation:\n" +
        "- Kein Check (Stufe 1):\n" +
        "  * Ausschließlich bei reinen Dokumentations-, Text-, Kommentar-, Asset- oder Konfigurationsänderungen (0 Quellcode-Dateien geändert).\n" +
        "- Einzelcheck ohne Subagent (Stufe 2 – Schnell-Modus für Kleinigkeiten):\n" +
        "  * Geringer Aufwand (<= 1 Quellcode-Datei und <= 40 Zeilen Diff, ODER reine UI/Styles/Markup bis 60 Zeilen, keine Concurrency-Konstrukte im Diff).\n" +
        "  * Verifiziere per schnellem Build/Syntaxcheck oder präzisem Selbstcheck in 1–3 Sätzen – strikt OHNE Subagent (spart Zeit und Tokens)!\n" +
        "- 1 Subagent (Stufe 3 – Sicherheits-Audit für echte Logik):\n" +
        "  * Mittlerer/größerer Aufwand (mehr als 1 Quellcode-Datei ODER > 40 Zeilen Logik ODER Concurrency/Locks/PInvoke im Diff).\n" +
        "  * VOR der finalen Antwort zwingend genau 1 neutralen Subagenten (Read-Only Code Auditor) beauftragen. Ein reiner Compiler-Build ersetzt bei echter Logik niemals das Vier-Augen-Audit!\n" +
        "  * Diff-Mitgabe: Gib dem Auditor im Prompt direkt die Liste der geänderten Dateien und den kompakten 'git diff' mit (bei sehr großen Diffs die Dateiliste), damit er sofort ohne Dateisystem-Suchaufwand prüfen kann.\n" +
        "  * Zweistufige Fehlerbewertung: Der Auditor bewertet Funde strikt zweistufig: Blocker (Kritisch/Regression/Leak/Logikfehler – zwingend vor Abschluss zu beheben) vs. Hinweis (Kosmetik/Style/Optional – blockiert den Abschluss nicht).\n" +
        "- Schwarm (Stufe 4 – Systemweites Schwarm-Audit):\n" +
        "  * Hoher Aufwand (komplexe Architektur, systemweite Refactorings, >= 5 Dateien oder > 150 Zeilen über mehrere Kernmodule). Beauftrage parallele neutrale Subagents mit geteiltem Fokus, Diff-Mitgabe und zweistufiger Fehlerbewertung.\n\n" +
        "- DIFF-BEWERTUNG STATT DATEINAMEN: Maßgeblich ist ausschließlich, WAS im Diff geändert wurde. Tauchen in den geänderten Zeilen keine echten Concurrency-Primitiven (lock, Semaphore, Interlocked, unmanaged Speicher, P/Invoke) auf, ist bei kleinen Änderungen (<= 1 Datei, <= 40 Zeilen) Stufe 2 die vorgesehene Wahl.\n" +
        "- FEHLER- & RANDFALL-CHECK: Prüfe die Änderungen auf funktionale Fehler, Null-Checks, Randfälle, Exception-Handling und neue Bugs.\n" +
        "- WISSENS- & REGRESSIONS-GUARD: Prüfe zwingend, ob durch die Änderung bestehende Features beeinträchtigt wurden oder dokumentierte historische Fehler aus docs/knowledge.md oder docs/protocol.md erneut aufgetreten sind.\n" +
        "- ASYNCHRONE TASKS / WAKEUPS: Wurdest du lediglich durch einen fertiggestellten Hintergrund-Task (z. B. Build, PM2-Restart) ohne neue Codeänderungen geweckt, entfällt die Auto-Reflexions-Statuszeile und die Zusammenfassung vollständig.\n" +
        "(Kein kosmetisches Nitpicking. Korrigiere berechtigte Fehler sofort (go).)\n\n" +
        "Halte am Ende deiner Antwort zwingend exakt folgende Abschluss-Reihenfolge ein:\n" +
        "1. Auto-Reflexion mit der zutreffenden Aufwandstufe:\n" +
        "   - Stufe 1 (Doku/Analyse): \"🔍 **Auto-Reflexion (Kein Check):** Prüfung übersprungen – keine Codeänderungen (reine Analyse/Konzept oder Dokumentation).\"\n" +
        "   - Stufe 2 (Schnell-Modus): \"🔍 **Auto-Reflexion (Geringer Aufwand – Einzelcheck ohne Subagent):** Code- & Fehlerprüfung durchgeführt – keine Bugs oder Regressionsrisiken.\"\n" +
        "   - Stufe 3 (Sicherheits-Audit): \"🔍 **Auto-Reflexion (Mittlerer Aufwand – 1 Subagent):** Neutrales Code-Audit abgeschlossen – keine Bugs oder Regressionsrisiken.\"\n" +
        "   - Stufe 4 (Schwarm): \"🔍 **Auto-Reflexion (Hoher Aufwand – Schwarm):** Schwarm-Audit abgeschlossen – keine Bugs oder Regressionsrisiken.\"\n" +
        "2. Geänderte Dateien gesondert als Liste: \"**Geänderte Dateien:**\\n- ...\"\n" +
        "3. Einfach gesagt: \"💡  **Einfach gesagt: <Text>**  <Time>\"{MODIFIED_FILES_BLOCK}]";

    /// <summary>
    /// Liest alle Zeilen einer Datei unter Freigabe für parallele Schreibzugriffe (FileShare.ReadWrite),
    /// um Sharing-Violations bei zeitgleichen Append-Operationen der Antigravity CLI zu verhindern.
    /// </summary>
    public static string[] ReadAllLinesShared(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return Array.Empty<string>();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }
            return lines.ToArray();
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoReflectionManager] ReadAllLinesShared error for {path}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Evaluates modified files against deterministic risk rules (git diff numstat, semantic concurrency check, file count)
    /// and generates a binding effort directive based on the 4-tier model.
    /// </summary>
    public static string GetDeterministicRiskDirective(IEnumerable<string>? files, string? projectCwd = null)
    {
        if (files == null) return string.Empty;
        var list = files.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();
        if (list.Count == 0) return string.Empty;

        var codeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".go", ".ts", ".js", ".py", ".rs", ".c", ".cpp", ".java", ".kt", ".swift", ".rb", ".php",
            ".tsx", ".jsx", ".vue", ".svelte", ".razor", ".proto", ".sql", ".prisma"
        };

        int codeCount = 0;
        foreach (var f in list)
        {
            string ext = Path.GetExtension(f);
            if (codeExts.Contains(ext)) codeCount++;
        }

        // Versuche Git-Diff-Zeilen zu ermitteln, falls projectCwd vorhanden
        int totalChangedLines = -1;
        bool hasConcurrency = false;

        if (!string.IsNullOrWhiteSpace(projectCwd) && Directory.Exists(Path.Combine(projectCwd, ".git")))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "diff --numstat HEAD")
                {
                    WorkingDirectory = projectCwd,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                string output = proc != null ? proc.StandardOutput.ReadToEnd() : "";
                proc?.WaitForExit(500);

                if (string.IsNullOrWhiteSpace(output))
                {
                    // Fallback 1: Wurde im aktuellen Turn committet? Prüfe HEAD~1 gegen HEAD
                    try
                    {
                        var psiPrev = new System.Diagnostics.ProcessStartInfo("git", "diff --numstat HEAD~1 HEAD")
                        {
                            WorkingDirectory = projectCwd,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var procPrev = System.Diagnostics.Process.Start(psiPrev);
                        string prevOut = procPrev != null ? procPrev.StandardOutput.ReadToEnd() : "";
                        procPrev?.WaitForExit(500);
                        if (!string.IsNullOrWhiteSpace(prevOut)) output = prevOut;
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    // Fallback 2: Ungestagete Änderungen
                    try
                    {
                        var psiUnstaged = new System.Diagnostics.ProcessStartInfo("git", "diff --numstat")
                        {
                            WorkingDirectory = projectCwd,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var procUnstaged = System.Diagnostics.Process.Start(psiUnstaged);
                        string unstagedOut = procUnstaged != null ? procUnstaged.StandardOutput.ReadToEnd() : "";
                        procUnstaged?.WaitForExit(500);
                        if (!string.IsNullOrWhiteSpace(unstagedOut)) output = unstagedOut;
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    // Fallback 3: Multi-Commit Turn-History (bis HEAD~5)
                    try
                    {
                        var psiMulti = new System.Diagnostics.ProcessStartInfo("git", "diff --numstat HEAD~5 HEAD")
                        {
                            WorkingDirectory = projectCwd,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var procMulti = System.Diagnostics.Process.Start(psiMulti);
                        string multiOut = procMulti != null ? procMulti.StandardOutput.ReadToEnd() : "";
                        procMulti?.WaitForExit(500);
                        if (!string.IsNullOrWhiteSpace(multiOut)) output = multiOut;
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(output))
                {
                    int sum = 0;
                    foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length >= 3)
                        {
                            string rawFile = parts[2].Trim().Replace('\\', '/');
                            string fName = rawFile;
                            if (fName.Contains("=>"))
                            {
                                fName = Regex.Replace(fName, @"\{.*?\s*=>\s*(.*?)\}", "$1");
                                if (fName.Contains("=>"))
                                {
                                    var arrowParts = fName.Split(new[] { "=>" }, StringSplitOptions.RemoveEmptyEntries);
                                    if (arrowParts.Length > 1) fName = arrowParts[1].Trim();
                                }
                            }
                            if (list.Any(x => fName.Equals(x, StringComparison.OrdinalIgnoreCase) ||
                                              fName.EndsWith("/" + x, StringComparison.OrdinalIgnoreCase) ||
                                              x.EndsWith("/" + fName, StringComparison.OrdinalIgnoreCase)))
                            {
                                int a = 0, d = 0;
                                int.TryParse(parts[0], out a);
                                int.TryParse(parts[1], out d);
                                sum += (a + d);
                            }
                        }
                    }
                    if (sum > 0) totalChangedLines = sum;
                    else if (!string.IsNullOrWhiteSpace(output)) totalChangedLines = 0;
                }

                // Check Concurrency in Diff (inklusive gelöschter Synchronisationsprimitive)
                var psiDiff = new System.Diagnostics.ProcessStartInfo("git", "diff -U0 HEAD")
                {
                    WorkingDirectory = projectCwd,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var procDiff = System.Diagnostics.Process.Start(psiDiff);
                string diffOut = procDiff != null ? procDiff.StandardOutput.ReadToEnd() : "";
                procDiff?.WaitForExit(500);

                if (string.IsNullOrWhiteSpace(diffOut))
                {
                    try
                    {
                        var psiDiffPrev = new System.Diagnostics.ProcessStartInfo("git", "diff -U0 HEAD~1 HEAD")
                        {
                            WorkingDirectory = projectCwd,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var pPrev = System.Diagnostics.Process.Start(psiDiffPrev);
                        string pOut = pPrev != null ? pPrev.StandardOutput.ReadToEnd() : "";
                        pPrev?.WaitForExit(500);
                        if (!string.IsNullOrWhiteSpace(pOut)) diffOut = pOut;
                    }
                    catch { }
                }

                foreach (var dLine in diffOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if ((dLine.StartsWith("+") || dLine.StartsWith("-")) && !dLine.StartsWith("+++") && !dLine.StartsWith("---"))
                    {
                        string codeLine = dLine.Substring(1).Trim();
                        if (codeLine.StartsWith("//") || codeLine.StartsWith("/*") || codeLine.StartsWith("*") || codeLine.StartsWith("#")) continue;

                        if (Regex.IsMatch(codeLine, @"(?i)(?:\b(?:lock\s*\(|SemaphoreSlim|Interlocked\.|Monitor\.|ReaderWriterLockSlim|Channel<|BlockingCollection<|ConcurrentDictionary<|DllImport|PInvoke|AllocHGlobal|SetWindowLong|sync\.(?:Mutex|RWMutex|WaitGroup|Once)|SharedArrayBuffer|Atomics\.|async-mutex|(?:threading|asyncio)\.(?:Lock|Semaphore|Event)|tokio::sync::Mutex|BackgroundWorker)\b|\bchan\s*<-\s*|\bchan\s+(?:\w|\*|\[)|\bgo\s+(?:func\b|[a-zA-Z_]\w*\s*\()|\bnew\s+(?:Shared)?Worker\s*\()"))
                        {
                            hasConcurrency = true;
                            break;
                        }
                    }
                }
            }
            catch { }
        }

        // TIER 1: Rein nicht-funktionaler Code oder minimale Änderung (<= 10 Zeilen)
        if (totalChangedLines == 0 || (codeCount == 0 && (totalChangedLines < 0 || totalChangedLines <= 10)))
        {
            return "\n\n[AUFWAND-CHECK: KEIN CHECK NÖTIG (Stufe 1). Grund: Ausschließlich Dokumentations-, Text-, Style- oder Konfigurationsdateien geändert.]";
        }

        // TIER 4: Schwarm
        if (codeCount >= 5 || (totalChangedLines > 150))
        {
            string linesInfo = totalChangedLines > 0 ? $"{totalChangedLines} Zeilen in " : "";
            return $"\n\n[AUFWAND-CHECK: HOHER AUFWAND (Schwarm)\nGrund: Umfangreiche Änderung ({linesInfo}{list.Count} Datei(en)).\nDIREKTIVE: Beauftrage parallele neutrale Subagents mit geteiltem Fokus (Schwarm-Audit) und schließe mit Stufe 4 ab.]";
        }

        // TIER 2: Einzelcheck ohne Subagent (Schnell-Modus für <= 1 Code-Datei und <= 40 Zeilen ohne Concurrency, oder UI/Markup <= 60 Zeilen)
        if ((codeCount <= 1 && (totalChangedLines < 0 || totalChangedLines <= 40) && !hasConcurrency) ||
            (codeCount == 0 && totalChangedLines <= 60 && !hasConcurrency))
        {
            string linesInfo = totalChangedLines > 0 ? $"{totalChangedLines} Zeilen in " : "";
            return $"\n\n[AUFWAND-CHECK: GERINGER AUFWAND (Einzelcheck ohne Subagent)\nGrund: Überschaubare Änderung ({linesInfo}{list.Count} Datei(en), keine Concurrency-Eingriffe im Diff).\nDIREKTIVE: KEIN Subagent erforderlich! Verifiziere die Änderungen im Schnell-Modus (Build-/Syntaxcheck oder 1–3 Sätze Selbstprüfung) und schließe mit Stufe 2 ab.]";
        }

        // TIER 3: 1 Subagent (Sicherheits-Audit für echte Logik)
        string concurrencyReason = hasConcurrency ? " (inkl. Concurrency-Eingriff im Diff)" : "";
        string linesReason = totalChangedLines > 0 ? $"{totalChangedLines} Zeilen in " : "";
        return $"\n\n[AUFWAND-CHECK: MITTLERER AUFWAND (1 Subagent)\nGrund: Signifikanter Änderungsumfang ({linesReason}{list.Count} Datei(en){concurrencyReason}).\nDIREKTIVE: Beauftrage genau 1 neutralen Subagenten (Read-Only Code Auditor) mit dem aktuellen Arbeitsstand und schließe nach dem Audit mit Stufe 3 ab.]";
    }

    /// <summary>
    /// Returns the raw reflection prompt template from disk (SSOT) or built-in fallback.
    /// </summary>
    public static string GetReflectionPromptTemplate()
    {
        lock (_templateLock)
        {
            try
            {
                string? templatePath = null;
                string repoTemplate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "hooks", "reflection_prompt.txt");
                if (File.Exists(repoTemplate))
                {
                    templatePath = repoTemplate;
                }
                else
                {
                    string cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), ".agents", "hooks", "reflection_prompt.txt");
                    if (File.Exists(cwdCandidate))
                    {
                        templatePath = cwdCandidate;
                    }
                    else if (File.Exists(GlobalPromptTemplatePath))
                    {
                        templatePath = GlobalPromptTemplatePath;
                    }
                }

                if (!string.IsNullOrEmpty(templatePath) && File.Exists(templatePath))
                {
                    var lw = File.GetLastWriteTimeUtc(templatePath);
                    if (_cachedPromptTemplate == null || lw > _promptTemplateLastWrite)
                    {
                        _cachedPromptTemplate = File.ReadAllText(templatePath, Encoding.UTF8);
                        _promptTemplateLastWrite = lw;
                    }
                    if (!string.IsNullOrWhiteSpace(_cachedPromptTemplate))
                    {
                        return _cachedPromptTemplate;
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[AutoReflectionManager] GetReflectionPromptTemplate error: {ex.Message}");
            }

            return FallbackPromptTemplate;
        }
    }

    /// <summary>
    /// Builds the unified adaptive reflection prompt (Single Source of Truth) across all reflection triggers.
    /// </summary>
    public static string BuildAdaptiveReflectionPrompt(string? modifiedFiles = null, string? projectCwd = null)
    {
        string template = GetReflectionPromptTemplate();
        string filesBlock = string.Empty;

        if (!string.IsNullOrWhiteSpace(modifiedFiles))
        {
            var fileList = modifiedFiles.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(f => f.Trim())
                                        .Where(f => !string.IsNullOrEmpty(f))
                                        .Distinct()
                                        .ToList();

            string riskDirective = GetDeterministicRiskDirective(fileList, projectCwd);
            filesBlock = $"\n\nGeänderte Dateien: {string.Join(", ", fileList)}{riskDirective}";
        }

        return template.Replace("{MODIFIED_FILES_BLOCK}", filesBlock);
    }


    /// <summary>
    /// Scans the latest turn in the transcript for modified files (ignoring documentation and tracking files).
    /// Returns a comma-separated list of relative file paths.
    /// </summary>
    public static string? GetModifiedFilesFromTranscript(string? transcriptPath, string? projectCwd = null)
    {
        if (string.IsNullOrWhiteSpace(transcriptPath) || !File.Exists(transcriptPath))
            return null;

        try
        {
            var lines = ReadAllLinesShared(transcriptPath);
            if (lines.Length == 0) return null;

            int lastUserIdx = -1;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Contains("\"type\":\"USER_INPUT\"") || line.Contains("\"type\": \"USER_INPUT\"") ||
                    (line.Contains("\"type\":\"SYSTEM_MESSAGE\"") && line.Contains("Stop hook blocked termination")))
                {
                    lastUserIdx = i;
                    break;
                }
            }

            if (lastUserIdx < 0) return null;

            var modTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "replace_file_content", "multi_replace_file_content", "write_to_file",
                "write_file", "new_empty_file", "create_file", "edit_file",
                "apply_patch", "patch_file_content", "save_file", "delete_file",
                "remove_file", "append_file", "append_to_file", "insert_file_content"
            };

            var modifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = lastUserIdx; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                bool hasModTool = false;
                foreach (var mt in modTools)
                {
                    if (line.IndexOf(mt, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasModTool = true;
                        break;
                    }
                }
                if (!hasModTool) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    // Überspringe fehlerhafte Schritte
                    if (doc.RootElement.TryGetProperty("status", out var statusProp) &&
                        statusProp.GetString()?.Equals("ERROR", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        continue;
                    }

                    if (doc.RootElement.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            if (tc.TryGetProperty("name", out var nameProp) && modTools.Contains(nameProp.GetString() ?? "") &&
                                tc.TryGetProperty("args", out var argsProp) && argsProp.ValueKind == JsonValueKind.Object)
                            {
                                string? fPath = null;
                                foreach (var prop in argsProp.EnumerateObject())
                                {
                                    if (prop.Name.Equals("TargetFile", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("TargetFilePath", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("path", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("filePath", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("file", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("AbsolutePath", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("destination", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("file_path", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("target_file", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("target_file_path", StringComparison.OrdinalIgnoreCase) ||
                                        prop.Name.Equals("absolute_path", StringComparison.OrdinalIgnoreCase))
                                    {
                                        fPath = prop.Value.GetString();
                                        if (!string.IsNullOrWhiteSpace(fPath)) break;
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(fPath))
                                {
                                    string cleanPath = fPath.Trim('\"', '\'', ' ');
                                    if (!IsIgnoredFile(cleanPath, projectCwd))
                                    {
                                        string relPath = cleanPath;
                                        if (!string.IsNullOrWhiteSpace(projectCwd))
                                        {
                                            string normClean = cleanPath.Replace('\\', '/');
                                            string normWork = projectCwd.Replace('\\', '/').TrimEnd('/') + '/';
                                            if (normClean.StartsWith(normWork, StringComparison.OrdinalIgnoreCase))
                                            {
                                                relPath = normClean.Substring(normWork.Length);
                                            }
                                            else if (!Path.IsPathRooted(cleanPath))
                                            {
                                                relPath = cleanPath.Replace('\\', '/');
                                            }
                                            else
                                            {
                                                relPath = Path.GetFileName(cleanPath);
                                            }
                                        }
                                        else
                                        {
                                            relPath = Path.GetFileName(cleanPath);
                                        }
                                        modifiedFiles.Add(relPath.Replace('\\', '/'));
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            if (modifiedFiles.Count > 0)
            {
                return string.Join(", ", modifiedFiles);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoReflectionManager] GetModifiedFilesFromTranscript error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Gets recent modified files for a session, checking the transcript first and falling back to uncommitted git files.
    /// </summary>
    public static string? GetRecentModifiedFiles(string? transcriptPath, string? projectCwd)
    {
        var fromTranscript = GetModifiedFilesFromTranscript(transcriptPath, projectCwd);
        if (!string.IsNullOrWhiteSpace(fromTranscript))
            return fromTranscript;

        if (!string.IsNullOrWhiteSpace(projectCwd) && Directory.Exists(projectCwd))
        {
            try
            {
                string gitDir = Path.Combine(projectCwd, ".git");
                if (Directory.Exists(gitDir))
                {
                    DateTime? lastUserInputUtc = null;
                    if (!string.IsNullOrWhiteSpace(transcriptPath) && File.Exists(transcriptPath))
                    {
                        try
                        {
                            var tLines = ReadAllLinesShared(transcriptPath);
                            for (int i = tLines.Length - 1; i >= 0; i--)
                            {
                                var l = tLines[i];
                                if (string.IsNullOrWhiteSpace(l)) continue;
                                if (l.Contains("\"type\":\"USER_INPUT\"") || l.Contains("\"type\": \"USER_INPUT\"") ||
                                    (l.Contains("\"type\":\"SYSTEM_MESSAGE\"") && l.Contains("Stop hook blocked termination")))
                                {
                                    using var doc = JsonDocument.Parse(l);
                                    if (doc.RootElement.TryGetProperty("created_at", out var cat) && cat.GetString() is string catStr)
                                    {
                                        if (DateTime.TryParse(catStr, out var dt))
                                        {
                                            lastUserInputUtc = dt.ToUniversalTime();
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    var psi = new System.Diagnostics.ProcessStartInfo("git", "-c core.quotepath=false status --porcelain")
                    {
                        WorkingDirectory = projectCwd,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc != null)
                    {
                        string output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(1000);
                        var gitFiles = new List<string>();
                        foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (rawLine.Length > 3)
                            {
                                string file = rawLine.Substring(3).Trim('\"', '\'', ' ').Replace('\\', '/');
                                if (file.Contains(" -> "))
                                {
                                    var arrowParts = file.Split(new[] { " -> " }, StringSplitOptions.RemoveEmptyEntries);
                                    if (arrowParts.Length > 1) file = arrowParts[1].Trim('\"', '\'', ' ');
                                }

                                if (!IsIgnoredFile(file, projectCwd))
                                {
                                    string fullPath = Path.Combine(projectCwd, file);
                                    if (File.Exists(fullPath))
                                    {
                                        var lastWrite = File.GetLastWriteTimeUtc(fullPath);
                                        if (lastUserInputUtc != null && lastWrite >= lastUserInputUtc.Value)
                                        {
                                            gitFiles.Add(file);
                                        }
                                        else if (lastUserInputUtc == null && (DateTime.UtcNow - lastWrite).TotalSeconds <= 30)
                                        {
                                            gitFiles.Add(file);
                                        }
                                    }
                                }
                            }
                        }
                        if (gitFiles.Count > 0)
                        {
                            return string.Join(", ", gitFiles.Distinct());
                        }
                    }
                }
            }
            catch { }
        }

        return null;
    }

    public class ReflectionIgnoreConfig
    {
        public List<string> IgnoredFiles { get; set; } = new();
        public List<string> IgnoredPaths { get; set; } = new();
    }

    public static readonly string GlobalIgnoreFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini", "antigravity-cli", "reflection_ignore.json");

    public static readonly string GlobalHookScriptPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini", "antigravity-cli", "hooks", "auto_reflection.ps1");

    public static readonly string GlobalHooksConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini", "config", "hooks.json");

    public static readonly string AntigravityHooksConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini", "antigravity-cli", "hooks.json");

    private static DateTime _globalIgnoreLastWrite = DateTime.MinValue;
    private static ReflectionIgnoreConfig _cachedGlobalConfig = new();
    private static readonly object _globalConfigLock = new();

    private static readonly ConcurrentDictionary<string, (DateTime LastWrite, HashSet<string> Files, List<string> PathPatterns)> _projectIgnoreCache = new(StringComparer.OrdinalIgnoreCase);

    public static void EnsureGlobalIgnoreFileExists()
    {
        try
        {
            if (!File.Exists(GlobalIgnoreFilePath))
            {
                string? dir = Path.GetDirectoryName(GlobalIgnoreFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var template = new
                {
                    description = "Zusätzliche globale Dateien und Pfade, die die Auto-Reflexion nicht als Code-Änderungen werten soll.",
                    ignoredFiles = new[]
                    {
                        "error-tracker.json",
                        "error_tracker.json",
                        "fehlerhistorie.md"
                    },
                    ignoredPaths = new[]
                    {
                        "docs/bugs/",
                        "docs/error-reports/",
                        "docs/error-tracker/"
                    }
                };

                string json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GlobalIgnoreFilePath, json, Encoding.UTF8);
            }

            EnsureGlobalHookConfigured();
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoReflectionManager] EnsureGlobalIgnoreFileExists error: {ex.Message}");
        }
    }

    public static void EnsureProjectIgnoreFileExists(string? projectCwd)
    {
        if (string.IsNullOrWhiteSpace(projectCwd) || !Directory.Exists(projectCwd)) return;
        try
        {
            string ignoreFile = Path.Combine(projectCwd, ".reflectionignore");
            if (!File.Exists(ignoreFile))
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Projekt-spezifische Ignorier-Muster für die Auto-Reflexion (wie .gitignore)");
                sb.AppendLine("# Jede Zeile definiert eine Datei oder einen Pfad, der keine Code-Reflexion auslösen soll.");
                sb.AppendLine();
                sb.AppendLine("# Projekt-spezifische Tracking-Dateien");
                sb.AppendLine("docs/error-tracker.json");
                sb.AppendLine("docs/fehlerhistorie.md");
                sb.AppendLine("docs/error-reports/");
                sb.AppendLine();
                sb.AppendLine("# Temporäre Logs & Auswertungen");
                sb.AppendLine("*.log");
                sb.AppendLine("*.tmp");
                File.WriteAllText(ignoreFile, sb.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoReflectionManager] EnsureProjectIgnoreFileExists error: {ex.Message}");
        }
    }

    private static ReflectionIgnoreConfig GetGlobalIgnoreConfig()
    {
        try
        {
            if (!File.Exists(GlobalIgnoreFilePath))
            {
                return _cachedGlobalConfig;
            }

            var lastWrite = File.GetLastWriteTimeUtc(GlobalIgnoreFilePath);
            if (lastWrite != _globalIgnoreLastWrite)
            {
                lock (_globalConfigLock)
                {
                    if (lastWrite != _globalIgnoreLastWrite)
                    {
                        string json = File.ReadAllText(GlobalIgnoreFilePath);
                        using var doc = JsonDocument.Parse(json);
                        var cfg = new ReflectionIgnoreConfig();

                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in doc.RootElement.EnumerateArray())
                            {
                                string? s = item.GetString();
                                if (!string.IsNullOrWhiteSpace(s))
                                {
                                    if (s.Contains('/') || s.Contains('\\') || s.Contains('*'))
                                        cfg.IgnoredPaths.Add(s.Trim());
                                    else
                                        cfg.IgnoredFiles.Add(s.Trim());
                                }
                            }
                        }
                        else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in doc.RootElement.EnumerateObject())
                            {
                                if (prop.Name.Equals("ignoredFiles", StringComparison.OrdinalIgnoreCase) ||
                                    prop.Name.Equals("ignoredFileNames", StringComparison.OrdinalIgnoreCase) ||
                                    prop.Name.Equals("files", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (prop.Value.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var item in prop.Value.EnumerateArray())
                                        {
                                            string? s = item.GetString();
                                            if (!string.IsNullOrWhiteSpace(s)) cfg.IgnoredFiles.Add(s.Trim());
                                        }
                                    }
                                }
                                else if (prop.Name.Equals("ignoredPaths", StringComparison.OrdinalIgnoreCase) ||
                                         prop.Name.Equals("ignoredPathKeywords", StringComparison.OrdinalIgnoreCase) ||
                                         prop.Name.Equals("paths", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (prop.Value.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var item in prop.Value.EnumerateArray())
                                        {
                                            string? s = item.GetString();
                                            if (!string.IsNullOrWhiteSpace(s)) cfg.IgnoredPaths.Add(s.Trim());
                                        }
                                    }
                                }
                            }
                        }

                        _cachedGlobalConfig = cfg;
                        _globalIgnoreLastWrite = lastWrite;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoReflectionManager] Error loading global ignore config: {ex.Message}");
        }

        return _cachedGlobalConfig;
    }

    private static (HashSet<string> Files, List<string> PathPatterns) GetProjectIgnoreConfig(string? projectCwd)
    {
        if (string.IsNullOrWhiteSpace(projectCwd) || !Directory.Exists(projectCwd))
        {
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new List<string>());
        }

        string ignoreFile = Path.Combine(projectCwd, ".reflectionignore");
        if (!File.Exists(ignoreFile))
        {
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new List<string>());
        }

        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(ignoreFile);
            if (_projectIgnoreCache.TryGetValue(projectCwd, out var cached) && cached.LastWrite == lastWrite)
            {
                return (cached.Files, cached.PathPatterns);
            }

            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();

            foreach (var line in File.ReadAllLines(ignoreFile))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                bool isDirectory = trimmed.EndsWith('/') || trimmed.EndsWith('\\');
                string normalized = trimmed.Replace('\\', '/').TrimEnd('/');
                if (isDirectory || normalized.Contains('/') || normalized.Contains('*'))
                {
                    paths.Add(normalized);
                }
                else
                {
                    files.Add(normalized);
                }
            }

            _projectIgnoreCache[projectCwd] = (lastWrite, files, paths);
            return (files, paths);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoReflectionManager] Error loading .reflectionignore from {projectCwd}: {ex.Message}");
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new List<string>());
        }
    }

    private static readonly HashSet<string> IgnoredFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "protocol.md",
        "knowledge.md",
        "coordination.json",
        "knowledge_buffer.md",
        "TODO.md",
        "NOTES.md",
        "CHANGELOG.md",
        "changelog.json",
        "version.json",
        "versions.json",
        "update_status.json",
        "telemetry.json",
        "walkthrough.md",
        "implementation_plan.md",
        "plan.md",
        "analysis_results.md",
        "research_notes.md",
        "experiment_results.md",
        "reports.json",
        "report.json",
        "bugs.json",
        "bug_reports.json",
        "error-tracker.json",
        "error_tracker.json",
        "fehlerhistorie.md"
    };

    private static readonly string[] IgnoredPathKeywords = new[]
    {
        "custom-knowledge",
        "/.lock/",
        "/.log/",
        ".agents",
        "docs/bugs",
        "docs/error-reports",
        "docs/error-tracker"
    };

    public static bool IsIgnoredFile(string rawPath, string? projectCwd = null)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return true;

        string normalized = rawPath.Trim('\"', '\'', ' ', '\t').Replace('\\', '/');
        string fileName = Path.GetFileName(normalized);

        // 1. Hardcoded Ignored File Names
        if (IgnoredFileNames.Contains(fileName))
            return true;

        // 2. Global custom ignored file names
        var globalCfg = GetGlobalIgnoreConfig();
        if (globalCfg.IgnoredFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            return true;

        // 3. Project-local .reflectionignore
        var (projFiles, projPaths) = GetProjectIgnoreConfig(projectCwd);
        if (projFiles.Contains(fileName))
            return true;

        // 4. Gezielte Ignorierung aller Plan-Markdown-Dateien
        if (fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            if (fileName.EndsWith("plan.md", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("plan.markdown", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("plan_", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("plan-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return true;

        // 5. Gezielte Ignorierung interner Antigravity-Brain/Log-Pfade
        if (normalized.Contains("/.gemini/antigravity-cli/brain/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/.system_generated/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/brain/scratch/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 6. Hardcoded Ignored Path Keywords
        foreach (var keyword in IgnoredPathKeywords)
        {
            if (normalized.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        // 7. Global custom ignored path patterns
        foreach (var p in globalCfg.IgnoredPaths)
        {
            if (MatchesPathPattern(normalized, fileName, p))
                return true;
        }

        // 8. Project-local ignored path patterns
        foreach (var p in projPaths)
        {
            if (MatchesPathPattern(normalized, fileName, p))
                return true;
        }

        return false;
    }

    private static bool MatchesPathPattern(string normalizedPath, string fileName, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        string cleanP = pattern.Trim().Replace('\\', '/').TrimStart('/');
        if (cleanP.Contains('*'))
        {
            string rx = Regex.Escape(cleanP)
                .Replace(@"\*\*/", @"(?:.*/)?")
                .Replace(@"/\*\*", @"(?:/.*)?")
                .Replace(@"\*\*", @".*")
                .Replace(@"\*", @"[^/]*")
                .Replace(@"\?", @"[^/]");
            string regexPattern = "(?:^|/)" + rx + "$";
            return Regex.IsMatch(normalizedPath, regexPattern, RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
        }
        string anchored = cleanP.TrimEnd('/');
        return normalizedPath.Equals(anchored, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(anchored + "/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.Contains("/" + anchored + "/", StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.EndsWith("/" + anchored, StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals(anchored, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures that the specified project has a local .agents/hooks.json configuring the auto-reflection hook.
    /// This provides highest-priority, project-isolated hook execution for any workspace opened in the Dashboard.
    /// </summary>
    public static void EnsureProjectHookConfigured(string? projectCwd)
    {
        if (string.IsNullOrWhiteSpace(projectCwd) || !Directory.Exists(projectCwd)) return;
        try
        {
            string agentsDir = Path.Combine(projectCwd, ".agents");
            if (!Directory.Exists(agentsDir))
            {
                Directory.CreateDirectory(agentsDir);
            }

            string projectHooksPath = Path.Combine(agentsDir, "hooks.json");
            EnsureHookInConfigFile(projectHooksPath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoReflectionManager] EnsureProjectHookConfigured ({projectCwd}) error: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures that the global auto-reflection hook script and hooks.json files are installed and up-to-date.
    /// This guarantees auto-reflection works automatically across all projects and workspaces.
    /// </summary>
    public static void EnsureGlobalHookConfigured()
    {
        try
        {
            string? hooksDir = Path.GetDirectoryName(GlobalHookScriptPath);
            if (!string.IsNullOrEmpty(hooksDir) && !Directory.Exists(hooksDir))
            {
                Directory.CreateDirectory(hooksDir);
            }

            string repoScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "hooks", "auto_reflection.ps1");
            if (!File.Exists(repoScriptPath))
            {
                string? candidate = Path.Combine(Directory.GetCurrentDirectory(), ".agents", "hooks", "auto_reflection.ps1");
                if (File.Exists(candidate)) repoScriptPath = candidate;
            }

            if (File.Exists(repoScriptPath))
            {
                if (!File.Exists(GlobalHookScriptPath) ||
                    File.GetLastWriteTimeUtc(repoScriptPath) > File.GetLastWriteTimeUtc(GlobalHookScriptPath))
                {
                    string scriptContent = File.ReadAllText(repoScriptPath, Encoding.UTF8);
                    File.WriteAllText(GlobalHookScriptPath, scriptContent, new UTF8Encoding(true));
                    TelemetryLogger.Log("[AutoReflectionManager] Global hook script updated from repository.");
                }
            }

            // Sync prompt template to global hooks dir
            string repoTemplatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "hooks", "reflection_prompt.txt");
            if (!File.Exists(repoTemplatePath))
            {
                string? candidate = Path.Combine(Directory.GetCurrentDirectory(), ".agents", "hooks", "reflection_prompt.txt");
                if (File.Exists(candidate)) repoTemplatePath = candidate;
            }

            if (File.Exists(repoTemplatePath))
            {
                if (!File.Exists(GlobalPromptTemplatePath) ||
                    File.GetLastWriteTimeUtc(repoTemplatePath) > File.GetLastWriteTimeUtc(GlobalPromptTemplatePath))
                {
                    string templateContent = File.ReadAllText(repoTemplatePath, Encoding.UTF8);
                    File.WriteAllText(GlobalPromptTemplatePath, templateContent, new UTF8Encoding(true));
                    TelemetryLogger.Log("[AutoReflectionManager] Global prompt template updated from repository.");
                }
            }

            EnsureHookInConfigFile(GlobalHooksConfigPath);
            EnsureHookInConfigFile(AntigravityHooksConfigPath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoReflectionManager] EnsureGlobalHookConfigured error: {ex.Message}");
        }
    }

    public static string GetShellHookCommand()
    {
        bool hasPwsh = false;
        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv != null)
            {
                foreach (var p in pathEnv.Split(Path.PathSeparator))
                {
                    if (File.Exists(Path.Combine(p, "pwsh.exe")) || File.Exists(Path.Combine(p, "pwsh")))
                    {
                        hasPwsh = true;
                        break;
                    }
                }
            }
        }
        catch { }

        string shell = hasPwsh ? "pwsh" : "powershell";
        string scriptPath = GlobalHookScriptPath.Replace('\\', '/');
        return $"{shell} -NoProfile -ExecutionPolicy Bypass -File {scriptPath}";
    }

    private static void EnsureHookInConfigFile(string configPath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string hookCmd = GetShellHookCommand();

            if (!File.Exists(configPath))
            {
                var root = new Dictionary<string, object>
                {
                    ["auto-reflection-gate"] = new Dictionary<string, object>
                    {
                        ["Stop"] = new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["type"] = "command",
                                ["command"] = hookCmd,
                                ["timeout"] = 15
                            }
                        }
                    }
                };

                string json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json, Encoding.UTF8);
                TelemetryLogger.Log($"[AutoReflectionManager] Created hooks config: {configPath}");
            }
            else
            {
                string existing = File.ReadAllText(configPath);
                bool needsUpdate = false;

                if (!existing.Contains("auto-reflection", StringComparison.OrdinalIgnoreCase) &&
                    !existing.Contains("auto_reflection", StringComparison.OrdinalIgnoreCase))
                {
                    needsUpdate = true;
                }
                else if (existing.Contains("%USERPROFILE%", StringComparison.OrdinalIgnoreCase) ||
                         existing.Contains("\\\"%USERPROFILE%", StringComparison.OrdinalIgnoreCase) ||
                         existing.Contains("-File \\\"", StringComparison.OrdinalIgnoreCase) ||
                         existing.Contains("-File \"", StringComparison.OrdinalIgnoreCase))
                {
                    // Fix outdated/broken quoting command
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(existing);
                        var dict = new Dictionary<string, object>();
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            dict[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText())!;
                        }

                        dict["auto-reflection-gate"] = new Dictionary<string, object>
                        {
                            ["Stop"] = new[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["type"] = "command",
                                    ["command"] = hookCmd,
                                    ["timeout"] = 15
                                }
                            }
                        };

                        string mergedJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(configPath, mergedJson, Encoding.UTF8);
                        TelemetryLogger.Log($"[AutoReflectionManager] Updated auto-reflection-gate in {configPath}");
                    }
                    catch (Exception ex)
                    {
                        TelemetryLogger.Log($"[AutoReflectionManager] Failed to update {configPath}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoReflectionManager] EnsureHookInConfigFile ({configPath}) error: {ex.Message}");
        }
    }
}
