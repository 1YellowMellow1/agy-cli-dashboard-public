using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgyCliDashboard.Helpers;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private string? _lastLoadedPromptsConvId = null;
    private DateTime _lastPromptsTranscriptModified = DateTime.MinValue;

    private void InitPromptsSidebar()
    {
        if (UcPromptsSidebar != null)
        {
            UcPromptsSidebar.PromptSelectedForInput += UcPromptsSidebar_PromptSelectedForInput;
            UcPromptsSidebar.PromptOpenInEditorRequested += UcPromptsSidebar_PromptOpenInEditorRequested;
            UcPromptsSidebar.RefreshRequested += UcPromptsSidebar_RefreshRequested;
        }
    }

    private void DisposePromptsSidebar()
    {
        if (UcPromptsSidebar != null)
        {
            UcPromptsSidebar.PromptSelectedForInput -= UcPromptsSidebar_PromptSelectedForInput;
            UcPromptsSidebar.PromptOpenInEditorRequested -= UcPromptsSidebar_PromptOpenInEditorRequested;
            UcPromptsSidebar.RefreshRequested -= UcPromptsSidebar_RefreshRequested;
        }
    }

    private void UcPromptsSidebar_PromptSelectedForInput(object? sender, string promptText)
    {
        if (TxtChatInput != null)
        {
            TxtChatInput.Text = promptText;
            TxtChatInput.Focus(FocusState.Programmatic);
            TxtChatInput.SelectionStart = promptText.Length;
            ShowNotice("Prompt geladen", "Der ausgewählte Prompt wurde in das Chat-Eingabefeld übernommen.", InfoBarSeverity.Informational);
        }
    }

    private void UcPromptsSidebar_PromptOpenInEditorRequested(object? sender, UserPromptItem item)
    {
        try
        {
            string tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "temp");
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, $"prompt_{item.Index}_{DateTime.Now:yyyyMMdd_HHmmss}.md");
            var sb = new StringBuilder();
            sb.AppendLine($"# Benutzer-Prompt {item.DisplayIndex}");
            sb.AppendLine();
            sb.AppendLine($"**Datum/Uhrzeit:** {item.FormattedDateTime} Uhr");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine(item.RawText);
            if (!string.IsNullOrWhiteSpace(item.AssistantSummary))
            {
                sb.AppendLine();
                sb.AppendLine("## 💡 KI-Antwort (Einfach gesagt)");
                sb.AppendLine();
                sb.AppendLine(item.AssistantSummary);
            }
            File.WriteAllText(tempFile, sb.ToString(), Encoding.UTF8);
            OpenFileInEditor(tempFile);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[PromptsOpenInEditor Error] {ex.Message}");
            ShowNotice("Fehler", $"Prompt konnte nicht im Editor geöffnet werden: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void UcPromptsSidebar_RefreshRequested(object? sender, EventArgs e)
    {
        UpdatePromptsTab(force: true);
    }

    public async void UpdatePromptsTab(bool force = false)
    {
        try
        {
            var activeSession = GetActiveTerminalSession();
            string? convId = activeSession?.ConversationId;
            string chatTitle = activeSession?.Title ?? "Aktueller Chat";

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string brainBase = Path.Combine(userProfile, ".gemini", "antigravity-cli", "brain");

            if (string.IsNullOrEmpty(convId))
            {
                _lastLoadedPromptsConvId = null;
                _lastPromptsTranscriptModified = DateTime.MinValue;
                UcPromptsSidebar?.SetPrompts("Kein Chat aktiv", Enumerable.Empty<UserPromptItem>());
                return;
            }

            string logDir = Path.Combine(brainBase, convId, ".system_generated", "logs");
            string fullLogFile = Path.Combine(logDir, "transcript_full.jsonl");
            string compactLogFile = Path.Combine(logDir, "transcript.jsonl");
            string? targetFile = null;

            if (File.Exists(fullLogFile) && File.Exists(compactLogFile))
            {
                var fiFull = new FileInfo(fullLogFile);
                var fiCompact = new FileInfo(compactLogFile);
                targetFile = fiFull.LastWriteTimeUtc >= fiCompact.LastWriteTimeUtc ? fullLogFile : compactLogFile;
            }
            else if (File.Exists(fullLogFile)) targetFile = fullLogFile;
            else if (File.Exists(compactLogFile)) targetFile = compactLogFile;

            if (targetFile == null)
            {
                _lastLoadedPromptsConvId = null;
                _lastPromptsTranscriptModified = DateTime.MinValue;
                UcPromptsSidebar?.SetPrompts(chatTitle, Enumerable.Empty<UserPromptItem>());
                return;
            }

            var fileInfo = new FileInfo(targetFile);
            if (!force && convId == _lastLoadedPromptsConvId && fileInfo.LastWriteTimeUtc == _lastPromptsTranscriptModified)
            {
                return; // Keine Änderungen, unnötiges Re-Parsing vermeiden
            }

            var items = await Task.Run(() => ParseUserPromptsFromTranscript(targetFile));

            // Guard: Hat der Benutzer während des asynchronen Parsens zu einem anderen Tab gewechselt?
            if (GetActiveTerminalSession()?.ConversationId != convId)
            {
                return;
            }

            _lastLoadedPromptsConvId = convId;
            _lastPromptsTranscriptModified = fileInfo.LastWriteTimeUtc;

            if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => UcPromptsSidebar?.SetPrompts(chatTitle, items));
            }
            else
            {
                UcPromptsSidebar?.SetPrompts(chatTitle, items);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[UpdatePromptsTab Error] {ex.Message}");
        }
    }

    private static readonly Regex AssistantSummaryRegex = new(
        @"💡\s*\*{0,2}\s*Einfach gesagt:?\*{0,2}\s*(?<text>[^\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static List<UserPromptItem> ParseUserPromptsFromTranscript(string filePath)
    {
        var result = new List<UserPromptItem>();
        if (!File.Exists(filePath)) return result;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);

            string? line;
            int promptCounter = 1;
            UserPromptItem? currentItem = null;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    string source = root.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                    string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

                    bool isUserInput = string.Equals(source, "USER_INPUT", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(source, "USER_EXPLICIT", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(type, "USER_INPUT", StringComparison.OrdinalIgnoreCase);

                    if (isUserInput)
                    {
                        if (!root.TryGetProperty("content", out var cProp) || cProp.ValueKind != JsonValueKind.String) continue;
                        string content = cProp.GetString() ?? "";
                        if (string.IsNullOrWhiteSpace(content)) continue;

                        DateTime createdAt = DateTime.UtcNow;
                        if (root.TryGetProperty("created_at", out var caProp) && DateTime.TryParse(caProp.GetString(), out var dt))
                        {
                            createdAt = dt;
                        }

                        // Extrahiere <USER_REQUEST>...</USER_REQUEST>
                        var reqMatch = Regex.Match(content, @"<USER_REQUEST>([\s\S]*?)</USER_REQUEST>", RegexOptions.IgnoreCase);
                        string requestText = reqMatch.Success ? reqMatch.Groups[1].Value.Trim() : content.Trim();

                        // Prüfe auf Anhänge
                        bool hasAttachment = requestText.Contains("[Der Benutzer bezieht sich auf die angehängte Datei:") ||
                                             requestText.Contains("[Der Benutzer bezieht sich auf folgende Dateien:");

                        string attachmentBadge = "";
                        if (hasAttachment)
                        {
                            attachmentBadge = (requestText.Contains(".png", StringComparison.OrdinalIgnoreCase) || requestText.Contains(".jpg", StringComparison.OrdinalIgnoreCase))
                                ? "Screenshot"
                                : "Anhang";
                        }

                        // Bereinige Injektionen für CleanText
                        string cleanText = CleanPromptForDisplay(requestText);

                        // Nur aufnehmen, wenn nach Bereinigung relevanter Text übrig ist oder Anhang vorliegt
                        if (string.IsNullOrWhiteSpace(cleanText) && !hasAttachment) continue;

                        string displayPrompt = !string.IsNullOrWhiteSpace(cleanText)
                            ? cleanText
                            : (attachmentBadge == "Screenshot" ? "[Screenshot]" : "[Datei-Anhang]");

                        currentItem = new UserPromptItem
                        {
                            Index = promptCounter++,
                            CleanText = displayPrompt,
                            RawText = requestText,
                            CreatedAt = createdAt,
                            HasAttachment = hasAttachment,
                            AttachmentBadgeText = attachmentBadge
                        };
                        result.Add(currentItem);
                    }
                    else if (currentItem != null)
                    {
                        bool isModelResponse = string.Equals(source, "MODEL", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(type, "PLANNER_RESPONSE", StringComparison.OrdinalIgnoreCase);

                        if (isModelResponse && root.TryGetProperty("content", out var cProp) && cProp.ValueKind == JsonValueKind.String)
                        {
                            string mContent = cProp.GetString() ?? "";
                            string summary = ExtractAssistantSummary(mContent);
                            if (!string.IsNullOrWhiteSpace(summary))
                            {
                                currentItem.AssistantSummary = summary;
                            }
                        }
                    }
                }
                catch
                {
                    // Zeilen-Parsing-Toleranz bei Json-Fragmenten
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[ParseUserPromptsFromTranscript Error] {ex.Message}");
        }

        return result;
    }

    private static string ExtractAssistantSummary(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;

        var match = AssistantSummaryRegex.Match(content);
        if (match.Success)
        {
            string text = match.Groups["text"].Value.Trim();
            text = Regex.Replace(text, @"\s*\(\s*\d{2}\.\d{2}\.\d{4}[\s\S]*?\)\s*$", "");
            text = text.Trim('*', ' ', '\r', '\n');
            return text;
        }

        return string.Empty;
    }

    private static string CleanPromptForDisplay(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string cleaned = raw;
        // Entferne System- und Modus-Injektionen
        cleaned = Regex.Replace(cleaned, @"\[Recherchiere zu diesem Thema im Internet[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Führe vorab eine intensive Internet-Recherche[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Der Benutzer bezieht sich auf[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[DeepThink:[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Führe einen Deep Think durch[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Führe eine gründliche Code-Prüfung[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Nutze maximal viele Subagents[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Nutze für diese Aufgabe dein Projektwissen[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Überprüfe, ob der Fehler auch noch woanders[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Hier sind die aktuellen Telemetrie-Daten[\s\S]*?\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[(?:Gib als allererste Zeile[\s\S]*?Prüfe|(?:(?:Auto-Reflexion\]\s*|Auto-Reflexion:\s*)?)?(?:Prüfe|Reflektiere)) deine letzte Arbeit[\s\S]*?\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Führe vorab die Verifizierungs-[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Führe einen E2E-Test[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Führe einen Dead-Code-Check[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Führe einen vollständigen Projekt-Hygiene-Audit[\s\S]*?\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Overengineering, KISS-Audit[\s\S]*?\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Zero-Bug-Audit & Reliability-Exterminator[\s\S]*?\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[DAU-Simulationstest[\s\S]*?\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[(Reflektiere deine letzte Arbeit|Test-Guard|E2E-Test|Dead-Code|Toter Code|Irrweg|Vereinfache|Simplify|Zero-Bug|DAU-Simulationstest|DAU-Test|Projekt-Aufräumarbeit|Global-Error)[^\]]*\]", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\[Da die eigentliche Ursache[^\]]*\]", "", RegexOptions.IgnoreCase);

        return cleaned.Trim();
    }
}
