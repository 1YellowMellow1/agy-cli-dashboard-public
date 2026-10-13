using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    public TextBox TxtChatInput => UcPromptEditor.TxtChatInput;

    public TextBlock TxtModelName => UcPromptEditor.TxtModelName;
    public Button BtnDeepThink => UcPromptEditor.BtnDeepThink;
    public Microsoft.UI.Xaml.Shapes.Path IconDeepThink => UcPromptEditor.IconDeepThink;
    public Button BtnProjectDocs => UcPromptEditor.BtnProjectDocs;
    public Microsoft.UI.Xaml.Shapes.Path IconProjectDocs => UcPromptEditor.IconProjectDocs;
    public Button BtnGlobalErrorCheck => UcPromptEditor.BtnGlobalErrorCheck;
    public Microsoft.UI.Xaml.Shapes.Path IconGlobalErrorCheck => UcPromptEditor.IconGlobalErrorCheck;
    public Button BtnSubagent => UcPromptEditor.BtnSubagent;
    public Microsoft.UI.Xaml.Shapes.Path IconSubagent => UcPromptEditor.IconSubagent;
    public Button BtnWebResearch => UcPromptEditor.BtnWebResearch;
    public Microsoft.UI.Xaml.Shapes.Path IconWebResearch => UcPromptEditor.IconWebResearch;
    public Button BtnReflection => UcPromptEditor.BtnReflection;
    public Microsoft.UI.Xaml.Shapes.Path IconReflection => UcPromptEditor.IconReflection;
    public Button BtnE2ETest => UcPromptEditor.BtnE2ETest;
    public Microsoft.UI.Xaml.Shapes.Path IconE2ETest => UcPromptEditor.IconE2ETest;
    public Button BtnDeadCodeCheck => UcPromptEditor.BtnDeadCodeCheck;
    public Microsoft.UI.Xaml.Shapes.Path IconDeadCodeCheck => UcPromptEditor.IconDeadCodeCheck;
    public Button BtnIrrwegCleanup => UcPromptEditor.BtnIrrwegCleanup;
    public Microsoft.UI.Xaml.Shapes.Path IconIrrwegCleanup => UcPromptEditor.IconIrrwegCleanup;
    public Button BtnSimplify => UcPromptEditor.BtnSimplify;
    public Microsoft.UI.Xaml.Shapes.Path IconSimplify => UcPromptEditor.IconSimplify;
    public Button BtnZeroBug => UcPromptEditor.BtnZeroBug;
    public Microsoft.UI.Xaml.Shapes.Path IconZeroBug => UcPromptEditor.IconZeroBug;
    public Button BtnDauTest => UcPromptEditor.BtnDauTest;
    public Microsoft.UI.Xaml.Shapes.Path IconDauTest => UcPromptEditor.IconDauTest;
    public Button BtnCustomPrompt => UcPromptEditor.BtnCustomPrompt;
    public Microsoft.UI.Xaml.Shapes.Path IconCustomPrompt => UcPromptEditor.IconCustomPrompt;
    public Button BtnProjectCleanup => BtnProjectCleanupSidebar;
    public Border? BdrProjectFocusBadge => null;
    public TextBlock? TxtCenterActiveProject => null;
    public Button BtnGitStatusBadge => UcPromptEditor.BtnGitStatusBadge;
    public Button BtnProtocolBadge => UcPromptEditor.BtnProtocolBadge;
    public Button BtnKnowledgeBadge => UcPromptEditor.BtnKnowledgeBadge;
    public Border ActionBarProgressBar => UcPromptEditor.ActionBarProgressBar;
    public ItemsControl AttachmentStagingControl => UcPromptEditor.AttachmentStagingControl;
    public Border ChatInputPulseBorder => UcPromptEditor.ChatInputPulseBorder;
    public Button BtnSendMessage => UcPromptEditor.BtnSendMessage;
    public Button BtnVoice => UcPromptEditor.BtnVoice;
    public ImageIcon IconVoice => UcPromptEditor.IconVoice;
    private bool _isVoiceRecordingActive = false;
    private bool _isVoicePendingInTerminal = false;
    private int _isVoiceSubmitting = 0;
    private DateTime _voiceStartTime = DateTime.MinValue;
    private DateTime _voiceStopTime = DateTime.MinValue;
    private int _lastVoiceCalculatedWaitMs = 0;
    private double _peakHoldHeight = 0;
    private DispatcherTimer? _peakDecayTimer;
    private static readonly SolidColorBrush TokenGreenBadgeBgBrush = new(Windows.UI.Color.FromArgb(30, 16, 185, 129));
    private static readonly SolidColorBrush TokenAmberBadgeBgBrush = new(Windows.UI.Color.FromArgb(30, 245, 158, 11));
    private static readonly SolidColorBrush TokenRedBadgeBgBrush = new(Windows.UI.Color.FromArgb(30, 239, 68, 68));
    private readonly ConcurrentDictionary<string, (long Length, DateTime LastWriteUtc, int Tokens, bool IsExactServerUsage, int UserTokens, int AgentTokens, int ToolTokens)> _transcriptTokensCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _transcriptRefreshGates = new(StringComparer.OrdinalIgnoreCase);
    private int _cachedStagedAttachmentTokens = 0;

    private ObservableCollection<StagedAttachment> _stagedAttachments = new();

    private bool _isDeepThinkEnabled = false;
    private bool _isDeepThinkSticky = false;
    private bool _isSubagentModeEnabled = false;
    private bool _isReflectionModeEnabled = false;
    private bool _isProjectDocsEnabled = false;
    private bool _isGlobalErrorCheckEnabled = false;
    private bool _isWebResearchEnabled = false;
    private bool _isE2ETestModeEnabled = false;
    private bool _isDeadCodeCheckModeEnabled = false;
    private bool _isIrrwegCleanupModeEnabled = false;
    private bool _isSimplifyModeEnabled = false;
    private bool _isZeroBugModeEnabled = false;
    private bool _isDauTestModeEnabled = false;
    private bool _isCustomPromptModeEnabled = false;
    private bool _isProjectCleanupModeEnabled = false;
    private bool _isPromptDispatching = false;


    private string _longPressTarget = "deep-think";
    private DateTime _sendBtnPointerDownTime = DateTime.MinValue;
    private DateTime _lastPromptSendTime = DateTime.MinValue;
    private DispatcherTimer? _deepThinkBtnLongPressTimer;
    private bool _isDeepThinkBtnLongPressHandled = false;
    /// <summary>
    /// Bereitet Text für die sichere Übergabe an ConPTY via Bracketed Paste Mode vor.
    /// Filtert ESC-Sequenzen zur Ausbruchsverhinderung, normalisiert Zeilenumbrüche
    /// und ersetzt typografische Unicode-Sonderzeichen (Gedankenstriche, Bullets,
    /// typografische Anführungszeichen/Apostrophe), die im ConPTY-Input-Puffer
    /// ansonsten lautlos verschluckt oder verfälscht werden.
    /// </summary>
    /// <summary>
    /// Bricht überlange Zeilen (> maxLineLength) an Wortgrenzen (Leerzeichen)
    /// sauber um, damit ConPTY-Terminals und die CLI-TUI nicht in den Treppen-Effekt
    /// (Double-Wrap Clash) geraten.
    /// Einzelne lange Tokens (z. B. ununterbrochene URLs oder Pfade) bleiben unzerteilt.
    /// </summary>
    public static string WrapLongLinesForTerminal(string text, int maxLineLength = 75)
    {
        if (string.IsNullOrEmpty(text) || maxLineLength <= 20) return text;

        var rawLines = text.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder(text.Length + 64);

        for (int i = 0; i < rawLines.Length; i++)
        {
            if (i > 0) result.Append('\n');
            string line = rawLines[i];

            if (line.Length <= maxLineLength)
            {
                result.Append(line);
                continue;
            }

            // Ermittle führende Einrückung der Originalzeile
            int indentLen = 0;
            while (indentLen < line.Length && (line[indentLen] == ' ' || line[indentLen] == '\t'))
            {
                indentLen++;
            }
            string baseIndent = line.Substring(0, indentLen);
            string continuationIndent = baseIndent.Length > 0 ? baseIndent + "  " : "  ";

            var words = line.Substring(indentLen).Split(' ');
            var curLine = new StringBuilder();
            curLine.Append(baseIndent);

            bool isFirstWordInLine = true;
            foreach (var word in words)
            {
                if (string.IsNullOrEmpty(word))
                {
                    if (!isFirstWordInLine) curLine.Append(' ');
                    continue;
                }

                int candidateLen = curLine.Length + (isFirstWordInLine ? 0 : 1) + word.Length;
                if (!isFirstWordInLine && candidateLen > maxLineLength)
                {
                    result.Append(curLine.ToString().TrimEnd()).Append('\n');
                    curLine.Clear();
                    curLine.Append(continuationIndent);
                    isFirstWordInLine = true;
                }

                if (!isFirstWordInLine) curLine.Append(' ');
                curLine.Append(word);
                isFirstWordInLine = false;
            }

            if (curLine.Length > 0)
            {
                result.Append(curLine.ToString().TrimEnd());
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Filtert ESC-Sequenzen zur Ausbruchsverhinderung, normalisiert Zeilenumbrüche,
    /// bricht überlange Fließtext-Zeilen per Soft-Wrap um und ersetzt typografische
    /// Unicode-Sonderzeichen (Gedankenstriche, Bullets, typografische Anführungszeichen/Apostrophe),
    /// die im ConPTY-Input-Puffer ansonsten lautlos verschluckt oder verfälscht werden.
    /// </summary>
    public static string PrepareConPtyPayload(string text)
    {
        if (string.IsNullOrEmpty(text)) return "\r";
        string safeText = text.Replace("\x1b", "")
                              .Replace('–', '-')   // En-Dash (U+2013)
                              .Replace('—', '-')   // Em-Dash (U+2014)
                              .Replace('•', '-')   // Bullet (U+2022)
                              .Replace('“', '"')   // Anführungszeichen oben (U+201C)
                              .Replace('”', '"')   // Anführungszeichen oben (U+201D)
                              .Replace('„', '"')   // Anführungszeichen unten (U+201E)
                              .Replace('’', '\'')  // Typografischer Apostroph (U+2019)
                              .Replace('‘', '\''); // Typografischer Apostroph (U+2018)

        string wrapped = WrapLongLinesForTerminal(safeText, maxLineLength: 75);
        string normalized = wrapped.Replace("\r\n", "\r").Replace("\n", "\r");
        return $"\x1b[200~{normalized}\x1b[201~\r";
    }

    /// <summary>
    /// Liefert das Verzeichnis für temporäre Anhänge (Screenshots, Textpuffer).
    /// Liegt im System-Temp-Ordner (außerhalb von ~/.gemini/antigravity-cli/),
    /// damit Antigravity CLI Tools (read_file / view_file) nicht an der internen
    /// System-Protection-Boundary-Sicherheitsgrenze scheitern.
    /// </summary>
    public static string GetTempAttachmentsDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "agy-cli-dashboard", "temp_attachments");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
    }

    private async void SendPromptToTerminal(bool isLongPress = false)
    {
        if (_isPromptDispatching) return;
        _isPromptDispatching = true;

        try
        {
            _initialTerminalId = null;
            string rawInput = TxtChatInput?.Text ?? string.Empty;
            string text = rawInput.Trim();
            bool hasInputText = !string.IsNullOrWhiteSpace(text);
            bool hasAttachments = _stagedAttachments.Count > 0;
            bool isVoicePending = _isVoicePendingInTerminal || _isVoiceRecordingActive;
            bool wasVoiceRecording = _isVoiceRecordingActive;

            // WICHTIG: Wartezeit sofort berechnen, BEVOR ResetModeToggles() oder State-Clears aufgerufen werden!
            int voiceWaitMs = 0;
            if (wasVoiceRecording)
            {
                voiceWaitMs = CalculateVoiceTranscriptionWaitMs();
                _lastVoiceCalculatedWaitMs = voiceWaitMs;
                _voiceStopTime = DateTime.Now;
            }
            else if (isVoicePending && _voiceStopTime != DateTime.MinValue && _lastVoiceCalculatedWaitMs > 0)
            {
                // Benutzer hat vorab manuell per F5/Button gestoppt: Nur noch die eventuelle Restzeit abwarten
                double elapsedMs = (DateTime.Now - _voiceStopTime).TotalMilliseconds;
                int remainingMs = (int)(_lastVoiceCalculatedWaitMs - elapsedMs);
                voiceWaitMs = remainingMs > 0 ? remainingMs : 0;
                TelemetryLogger.Log($"[Voice] Prior manual stop detected. Remaining wait: {voiceWaitMs}ms (elapsed: {elapsedMs:F0}ms of {_lastVoiceCalculatedWaitMs}ms).");
            }

            if (_isVoiceRecordingActive)
            {
                _isVoiceRecordingActive = false;
                UpdateVoiceButtonState(false);
            }
            _isVoicePendingInTerminal = false;

            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);

            // Sicherheitsnetze für leere Eingaben ohne Anhänge / Voice:
            if (!hasInputText && !hasAttachments && !isVoicePending)
            {
                // Schutz: Reines Whitespace/Leerzeilen (z. B. versehentliches Shift+Enter oder Spaces)
                // Wenn Zeichen im Textfeld stehen (rawInput.Length > 0), war der Benutzer am Formatieren/Tippen.
                // Keinesfalls ein versehentliches '[go]' absenden, sondern Eingabe unberührt lassen!
                if (rawInput.Length > 0)
                {
                    TelemetryLogger.Log("[SendPromptToTerminal] Ignored enter on whitespace-only input (prevents accidental [go] on Shift+Enter).");
                    return;
                }

                // Schutz: Doppel-Enter Debounce (500 ms Cooldown)
                // Verhindert doppeltes Feuern bei schnellem Tastenprellen oder Doppelklick.
                if ((DateTime.Now - _lastPromptSendTime).TotalMilliseconds < 500)
                {
                    TelemetryLogger.Log("[SendPromptToTerminal] Ignored rapid double-Enter for empty [go] (500ms debounce).");
                    return;
                }
            }

            bool isFirstDispatch = activeSession != null && !activeSession.HasPromptBeenDispatched;
            _lastPromptSendTime = DateTime.Now;

            if (activeSession != null)
            {
                activeSession.IsUsed = true;
                if ((activeSession.Title == "Neuer Chat" || string.IsNullOrWhiteSpace(activeSession.Title)) && hasInputText && !text.Equals("go", StringComparison.OrdinalIgnoreCase))
                {
                    string promptTitle = Services.DatabaseService.CleanUserInputToTitle(text);
                    if (!string.IsNullOrWhiteSpace(promptTitle))
                    {
                        activeSession.Title = promptTitle;
                        RenderWorkspaceTabs();
                    }
                }
            }

        // Long-Press Target (per Rechtsklick markierter Hotbutton) greift nur, wenn Text im Eingabefeld steht.
        // Ohne Text wird bei lange Enter kein Hotbutton abgefeuert, sondern direkt '/plan /goal'.
        bool deepThinkActive = _isDeepThinkEnabled || (isLongPress && hasInputText && _longPressTarget == "deep-think");
        bool subagentActive = _isSubagentModeEnabled || (isLongPress && hasInputText && _longPressTarget == "subagent");
        bool reflectionActive = _isReflectionModeEnabled || (isLongPress && hasInputText && _longPressTarget == "reflection");
        bool projectDocsActive = _isProjectDocsEnabled || (isLongPress && hasInputText && _longPressTarget == "docs");
        bool globalErrorCheckActive = _isGlobalErrorCheckEnabled || (isLongPress && hasInputText && _longPressTarget == "global-error-check");
        bool webResearchActive = _isWebResearchEnabled || (isLongPress && hasInputText && _longPressTarget == "web-research");
        bool e2eTestActive = _isE2ETestModeEnabled || (isLongPress && hasInputText && _longPressTarget == "e2e-test");
        bool deadCodeCheckActive = _isDeadCodeCheckModeEnabled || (isLongPress && hasInputText && _longPressTarget == "dead-code-check");
        bool irrwegCleanupActive = _isIrrwegCleanupModeEnabled || (isLongPress && hasInputText && _longPressTarget == "irrweg-cleanup");
        bool simplifyActive = _isSimplifyModeEnabled || (isLongPress && hasInputText && _longPressTarget == "simplify");
        bool zeroBugActive = _isZeroBugModeEnabled || (isLongPress && hasInputText && _longPressTarget == "zero-bug");
        bool dauTestActive = _isDauTestModeEnabled || (isLongPress && hasInputText && _longPressTarget == "dau-test");
        bool customPromptActive = _isCustomPromptModeEnabled || (isLongPress && hasInputText && _longPressTarget == "custom-prompt");
        bool projectCleanupActive = _isProjectCleanupModeEnabled || (isLongPress && hasInputText && _longPressTarget == "project-cleanup");

        bool isSubagentWebCombined = subagentActive && webResearchActive;

        // Leeres Eingabefeld:
        // 1. Aus Voice-Modus kommend: KEIN '[go]' injizieren!
        // 2. Bei aktiven Dateianhängen (_stagedAttachments): KEIN '[go]' injizieren,
        if (string.IsNullOrWhiteSpace(text))
        {
            if (isVoicePending || hasAttachments)
            {
                text = isLongPress ? "/plan" : string.Empty;
            }
            else
            {
                text = isLongPress ? "/plan /goal" : "[go]";
            }
        }

        // Append Action-States (1:1 agy-desktop-app Logic)
        var sb = new StringBuilder(text);

        if (deepThinkActive)
        {
            sb.Append(" [Führe einen Deep Think durch.]");
        }

        if (isSubagentWebCombined)
        {
            sb.Append(" [Führe vorab eine intensive Internet-Recherche zu dieser Aufgabe durch. Nutze maximal viele Subagents für diese Aufgabe. Antigravity übernimmt zwingend die Endabnahme der Arbeit aller Subagents und behält das Vetorecht, falls ein Subagenten-Schwarm für diese Aufgabe gefährlich ist.]");
        }
        else
        {
            if (subagentActive)
            {
                sb.Append(" [Nutze maximal viele Subagents für diese Aufgabe. Antigravity übernimmt zwingend die Endabnahme der Arbeit aller Subagents und behält das Vetorecht, falls ein Subagenten-Schwarm für diese Aufgabe gefährlich ist.]");
            }

            if (webResearchActive)
            {
                sb.Append(" [Recherchiere zu diesem Thema im Internet, nutze seriöse Quellen.]");
            }
        }

        if (reflectionActive)
        {
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append(Helpers.AutoReflectionManager.BuildAdaptiveReflectionPrompt());
        }

        if (projectDocsActive)
        {
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append("[Nutze für diese Aufgabe dein Projektwissen aus dem docs-Ordner dieses Projekts.]");
        }

        if (globalErrorCheckActive)
        {
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append("[Überprüfe, ob der Fehler auch noch woanders im Projekt auftritt.]");
        }

        if (e2eTestActive)
        {
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append(E2ETestPromptText);
        }

        if (deadCodeCheckActive)
        {
            RecordProjectMaintenanceTimestamp(GetCurrentTargetProjectForMaintenance(), "dead_code_check");
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append(DeadCodeCheckPromptText);
        }

        if (irrwegCleanupActive)
        {
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append(IrrwegCleanupPromptText);
        }

        if (simplifyActive)
        {
            RecordProjectMaintenanceTimestamp(GetCurrentTargetProjectForMaintenance(), "simplify_audit");
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append(SimplifyPromptText);
        }

        if (zeroBugActive)
        {
            RecordProjectMaintenanceTimestamp(GetCurrentTargetProjectForMaintenance(), "zero_bug_audit");
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append(ZeroBugPromptText);
        }

        if (dauTestActive)
        {
            RecordProjectMaintenanceTimestamp(GetCurrentTargetProjectForMaintenance(), "dau_test");
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append(DauTestPromptText);
        }

        if (customPromptActive)
        {
            string cPrompt = GetCustomPromptForCurrentProject();
            if (!string.IsNullOrWhiteSpace(cPrompt))
            {
                if (!cPrompt.StartsWith("[") || !cPrompt.EndsWith("]")) cPrompt = $"[{cPrompt.Trim('[', ']')}]";
                sb.Append(sb.Length > 0 ? "\n\n" : "").Append(cPrompt);
            }
        }

        if (projectCleanupActive)
        {
            RecordMaintenanceTimestamp("project_cleanup");
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append("[").Append(ProjectCleanupPromptText.Trim()).Append("]");
        }

        if (_stagedAttachments.Count == 1)
        {
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append("[Der Benutzer bezieht sich auf die angehängte Datei: \"").Append(_stagedAttachments[0].FilePath).Append("\"]");
        }
        else if (_stagedAttachments.Count > 1)
        {
            sb.Append(sb.Length > 0 ? "\n\n" : "").Append("[Der Benutzer bezieht sich auf folgende Dateien:\n");
            for (int i = 0; i < _stagedAttachments.Count; i++)
            {
                if (i > 0) sb.Append(",\n");
                sb.Append("  \"").Append(_stagedAttachments[i].FilePath).Append('"');
            }
            sb.Append(']');
        }

        text = sb.ToString().Trim();

        var targetControl = activeSession?.Control ?? ActiveTerminal;
        bool isFreshSessionBooting = isFirstDispatch && activeSession != null && (DateTime.UtcNow - activeSession.CreatedAt).TotalMilliseconds < 3500;
        if (targetControl == null || (targetControl.ConPTYTerm == null && !isFreshSessionBooting))
        {
            ShowNotice("Kein aktives Terminal", "Bitte öffne zuerst einen Terminal-Tab.", InfoBarSeverity.Warning);
            return;
        }

        // UI sofort bereinigen, damit der Benutzer unmittelbares Feedback hat
        if (TxtChatInput != null) TxtChatInput.Text = string.Empty;
        UpdateTokenPeakMeter(true);
        _stagedAttachments.Clear();
        _cachedStagedAttachmentTokens = 0;
        AttachmentStagingControl.Visibility = Visibility.Collapsed;
        if (_lastFilesProjectPath != null)
        {
            UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
        }
        ResetModeToggles();

        // Dynamischer Readiness-Puffer für frische Tabs:
        // Verhindert den Schreibmaschinen-Effekt und Reflow-Abrisse, falls ConPTY den
        // Bracketed-Paste-Modus (\x1b[?2004h) beim CLI-Start noch nicht vollständig scharf geschaltet hat.
        if (isFirstDispatch && activeSession != null)
        {
            double ageMs = (DateTime.UtcNow - activeSession.CreatedAt).TotalMilliseconds;
            if (ageMs < 3500)
            {
                int maxWaitMs = (int)Math.Max(100, 3500 - ageMs);
                int waitedMs = 0;
                TelemetryLogger.Log($"[SendPromptToTerminal] Fresh terminal session detected (age: {ageMs:N0}ms). Buffering prompt for PTY readiness (max wait: {maxWaitMs}ms)...");

                while (waitedMs < maxWaitMs)
                {
                    // Tab-Lifecycle Guard: Tab wurde während des Wartens geschlossen/gelöscht
                    if (!_terminalSessions.Contains(activeSession))
                    {
                        TelemetryLogger.Log("[SendPromptToTerminal] Active session was closed during boot buffer wait. Aborting dispatch.");
                        return;
                    }

                    if (targetControl.ConPTYTerm != null)
                    {
                        string? outText = Helpers.TerminalBufferManager.GetOutputLogText(targetControl.ConPTYTerm);
                        if (!string.IsNullOrEmpty(outText))
                        {
                            // Präzise TUI-Marker (ohne ungenauen "agy"-Submatch, der auf Pfade oder Startbefehl matchen würde)
                            if ((outText.Contains("Antigravity", StringComparison.OrdinalIgnoreCase) ||
                                 outText.Contains("Tips:", StringComparison.OrdinalIgnoreCase) ||
                                 outText.Contains("? for shortcuts", StringComparison.OrdinalIgnoreCase) ||
                                 outText.Contains("model:", StringComparison.OrdinalIgnoreCase)) &&
                                (DateTime.UtcNow - activeSession.CreatedAt).TotalMilliseconds >= 1200)
                            {
                                TelemetryLogger.Log($"[SendPromptToTerminal] CLI boot ready early after {waitedMs}ms (total age: {(DateTime.UtcNow - activeSession.CreatedAt).TotalMilliseconds:N0}ms).");
                                break;
                            }
                        }
                    }

                    await Task.Delay(100);
                    waitedMs += 100;
                }
            }
        }

        // Tab-Lifecycle Guard vor dem PTY-Schreiben
        if (activeSession != null && !_terminalSessions.Contains(activeSession))
        {
            TelemetryLogger.Log("[SendPromptToTerminal] Session was closed before PTY write. Aborting dispatch.");
            return;
        }

        if (targetControl?.ConPTYTerm == null) return;

        if (activeSession != null)
        {
            activeSession.HasPromptBeenDispatched = true;
        }

        if (string.IsNullOrEmpty(text))
        {
            if (isVoicePending)
            {
                if (wasVoiceRecording)
                {
                    targetControl.ConPTYTerm.WriteToTerm("\x1b[15~");
                    TelemetryLogger.Log("[Voice] Dispatched F5 (\\x1b[15~) to stop recording in CLI (from SendPromptToTerminal).");
                    if (voiceWaitMs > 0)
                    {
                        await Task.Delay(voiceWaitMs);
                    }
                }
                else if (voiceWaitMs > 0)
                {
                    await Task.Delay(voiceWaitMs);
                }
                // Im Voice-Modus ohne zusätzlichen getippten Text: Reines Enter (\r) an ConPTY senden,
                // um die bereits im CLI-Terminal transkribierte Spracheingabe sauber auszuführen!
                targetControl.ConPTYTerm.WriteToTerm("\r");
                TelemetryLogger.Log("[Voice] Dispatched \\r to ConPTY for voice submission (from SendPromptToTerminal).");
            }
        }
        else
        {
            if (wasVoiceRecording)
            {
                targetControl.ConPTYTerm.WriteToTerm("\x1b[15~");
                TelemetryLogger.Log("[Voice] Dispatched F5 (\\x1b[15~) to stop recording in CLI (from SendPromptToTerminal with text).");
                if (voiceWaitMs > 0)
                {
                    await Task.Delay(voiceWaitMs);
                }
            }
            else if (voiceWaitMs > 0)
            {
                await Task.Delay(voiceWaitMs);
            }
            // Bracketed Paste Mode mit Vorbereitung & Bereinigung typografischer Sonderzeichen
            string payloadText = isVoicePending && !text.StartsWith(" ") ? " " + text : text;
            targetControl.ConPTYTerm.WriteToTerm(PrepareConPtyPayload(payloadText));
            TelemetryLogger.Log($"[SendPromptToTerminal] Dispatched prompt ({payloadText.Length} chars) via Bracketed Paste to ConPTY.");
        }
        _lastPromptSendTime = DateTime.Now;

        string? targetSessionId = activeSession?.Id ?? _activeTerminalId;
        if (!string.IsNullOrEmpty(targetSessionId))
        {
            var targetSession = _terminalSessions.FirstOrDefault(t => t.Id == targetSessionId);
            if (targetSession != null)
            {
                targetSession.LastPromptDispatchedUtc = DateTime.UtcNow;
            }
            SetTerminalWorkingState(targetSessionId, true);
        }

        // Fokus nur zurückgeben, wenn der Benutzer noch auf demselben Tab ist
        if (activeSession == null || _activeTerminalId == activeSession.Id)
        {
            TxtChatInput?.Focus(FocusState.Programmatic);
        }

        // Trigger AGY CLI to update chat titles in SQLite
        TriggerPromptResumeSync();
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Error in SendPromptToTerminal: {ex.Message}");
        TelemetryLogger.Log($"[SendPromptToTerminal Global ERROR] {ex.Message}");
    }
    finally
    {
        _isPromptDispatching = false;
    }
}

    private void SendDirectReflection()
    {
        var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
        string? transcriptPath = !string.IsNullOrEmpty(activeSession?.ConversationId)
            ? ResolveActiveTranscriptPath(activeSession.ConversationId)
            : null;

        string? modifiedFiles = Helpers.AutoReflectionManager.GetRecentModifiedFiles(
            transcriptPath,
            activeSession?.ProjectPath);

        string text = Helpers.AutoReflectionManager.BuildAdaptiveReflectionPrompt(modifiedFiles, activeSession?.ProjectPath);
        PrepareAndDispatchDirectCommand(text, null, "Reflexion");
    }
    private static readonly string E2ETestPromptText =
        "[E2E-End-to-End-Test]\n" +
        "Führe einen E2E-Test für das Projekt durch:\n" +
        "- Web-Projekt / Frontend: Nutze zwingend den integrierten Dashboard KI-Co-Browser\n" +
        "  (automatisch im Split-Screen öffnen und über Co-Browser MCP-Tools testen).\n" +
        "- Desktop-App / Backend-API: Führe den E2E-Test über PowerShell oder das\n" +
        "  projektspezifische Test-Tool aus.";

    private static readonly string DeadCodeCheckPromptText =
        "[Dead Code & UI-Dataflow Bereinigung]\n" +
        "Führe eine mehrstufige Bereinigung für das gesamte Projekt durch:\n\n" +
        "1. Deterministischer Tool-Scan:\n" +
        "   - Starte passende Tools (knip, tsc, linter, roslyn) für ungenutzte Dateien,\n" +
        "     tote Exports und unbenutzte Dependencies (bei Bedarf Installation erfragen).\n" +
        "2. Semantische Top-Down Zombie-Analyse:\n" +
        "   - Prüfe Einstiegspunkte auf formal verknüpften, aber faktisch toten Code\n" +
        "     (Routen/Views ohne UI-Links, veraltete Feature-Flags, ungenutzte Handler).\n" +
        "3. UI- & Reverse-Dataflow-Tracking (Tote Senken):\n" +
        "   - Prüfe Daten-Pipelines von der Bildschirmoberfläche rückwärts:\n" +
        "     Werden berechnete Werte jemals im UI dynamisch gerendert oder landen sie\n" +
        "     in 'Write-Only Sinks' (ungebundene Properties, überdeckte Controls)?\n" +
        "   - Werden schwere I/O- oder Datei-Scans für Ergebnisse ausgeführt, die im\n" +
        "     UI oder Output gar nicht ankommen?\n" +
        "4. Kaskaden-Prüfung:\n" +
        "   - Ermittle, welche Abhängigkeiten durch das Beseitigen toter Einstiegspunkte\n" +
        "     und Pipelines automatisch frei werden.\n" +
        "5. Strukturierter Plan:\n" +
        "   - Präsentiere das Ergebnis kategorisiert in:\n" +
        "     * [Sicher] Direkt entfernbar\n" +
        "     * [Zombie] Wahrscheinlich tot, manuelle Prüfung empfohlen\n" +
        "     * [Vorsicht] Unklare Seiteneffekte / Reflexion erforderlich\n" +
        "   - Warte vor jeder Löschung oder Codeänderung auf meine Freigabe ('go').";

    private static readonly string IrrwegCleanupPromptText =
        "[Irrweg-Cleanup & Post-Fix-Bereinigung]\n" +
        "Da die eigentliche Ursache des Problems nun ermittelt ist:\n" +
        "- Gibt es noch obsolete temporäre Fixes oder ungenutzte Workarounds?\n" +
        "- Gibt es aufgeblähte Codeteile, die durch vorherige Lösungsversuche\n" +
        "  entstanden sind und jetzt sauber aufgeräumt werden können?";

    private bool PrepareAndDispatchDirectCommand(string text, string? maintenanceKey = null, string? tabTitle = null)
    {
        if (ActiveTerminal?.ConPTYTerm == null)
        {
            ShowNotice("Kein aktives Terminal", "Bitte öffne zuerst einen Terminal-Tab.", InfoBarSeverity.Warning);
            return false;
        }

        var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
        if (activeSession != null)
        {
            bool isBooting = (DateTime.UtcNow - activeSession.CreatedAt).TotalMilliseconds < 2500
                && !activeSession.HasPromptBeenDispatched
                && string.IsNullOrEmpty(activeSession.ConversationId);

            if (isBooting)
            {
                ShowNotice("Terminal wird vorbereitet", "Bitte warte einen kurzen Moment, bis die CLI bereit ist, oder nutze den normalen Klick zum Einfügen ins Chatfeld.", InfoBarSeverity.Informational);
                return false;
            }
        }

        if (!string.IsNullOrEmpty(maintenanceKey))
        {
            RecordProjectMaintenanceTimestamp(GetCurrentTargetProjectForMaintenance(), maintenanceKey);
        }

        try
        {
            ActiveTerminal.ConPTYTerm.WriteToTerm(PrepareConPtyPayload(text));

            _initialTerminalId = null;
            if (activeSession != null)
            {
                activeSession.IsUsed = true;
                activeSession.HasPromptBeenDispatched = true;
                if (!string.IsNullOrEmpty(tabTitle) && (activeSession.Title == "Neuer Chat" || string.IsNullOrWhiteSpace(activeSession.Title)))
                {
                    activeSession.Title = tabTitle;
                    RenderWorkspaceTabs();
                }
            }
            _lastPromptSendTime = DateTime.Now;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending direct command: {ex.Message}");
            return false;
        }
    }

    private void SendDirectE2ETest()
    {
        PrepareAndDispatchDirectCommand(E2ETestPromptText, null, "E2E-Test");
    }

    private void SendDirectDeadCodeCheck()
    {
        PrepareAndDispatchDirectCommand(DeadCodeCheckPromptText, "dead_code_check", "Dead-Code-Check");
    }

    private void SendDirectIrrwegCleanup()
    {
        PrepareAndDispatchDirectCommand(IrrwegCleanupPromptText, null, "Irrweg-Cleanup");
    }

    private static readonly string SimplifyPromptText =
        "[Overengineering, KISS-Audit & Regressions-Check]\n" +
        "Führe eine strukturierte Code-Vereinfachung in zwei getrennten Phasen durch:\n\n" +
        "Phase 1 (Analyse via Read-Only Subagents):\n" +
        "- Setze neutrale Read-Only Subagents ein, um das Projekt bzw. die relevanten Module auf Overengineering, akzidentelle Komplexität und Rube-Goldberg-Logik zu scannen.\n" +
        "- Identifiziere redundante Abstraktionen, überflüssige Wrapper, tote Codepfade und unverhältnismäßig aufgeblähte Datenstrukturen.\n" +
        "- REGRESSIONS-GUARD: Gleiche jeden Vorschlag zwingend mit docs/knowledge.md und docs/protocol.md ab! Prüfe, ob scheinbar 'überflüssige' Prüfungen in Wahrheit historische Bugfixes für Race Conditions, ConPTY-Deadlocks, UI-Threading oder File-Locks sind. Solche Guards dürfen keinesfalls entfernt werden!\n" +
        "- WICHTIG: Führe in Phase 1 noch KEINERLEI Codeänderungen durch!\n" +
        "- Präsentiere eine nummerierte Liste der Vereinfachungskandidaten mit konkretem Nutzen (z. B. 'spart X Zeilen', 'beseitigt unnötige Indirektion') und warte auf meine Freigabe ('go').\n\n" +
        "Phase 2 (Beweispflichtige Umsetzung nach Freigabe):\n" +
        "- Sobald ich die Umsetzung freigebe: Refaktoriere die genehmigten Stellen schrittweise auf die einfachste, robuste Lösung, ohne alte Fehler erneut einzuführen.\n" +
        "- BEWEISPFLICHT: Führe nach der Änderung die Test-Suite und Verifikations-Checks aus. Beweise anhand der Testausgaben zweifelsfrei, dass alle Funktionen voll intakt sind und keinerlei Seiteneffekte oder Regressionen entstanden sind.";

    private void SendDirectSimplify()
    {
        PrepareAndDispatchDirectCommand(SimplifyPromptText, "simplify_audit", "KISS-Audit");
    }

    private static readonly string ZeroBugPromptText =
        "[Zero-Bug-Audit & Reliability-Exterminator: Vollständige Codebase-Prüfung]\n" +
        "Führe einen systematischen, deterministischen Bug-Beseitigungs-Lauf für das aktuelle Projekt durch.\n" +
        "Arbeite streng phasenbasiert nach dem TDD- und Verification-in-the-Loop-Prinzip:\n\n" +
        "PHASE 1: Read-Only Schwachstellen-Audit (Isolierte Subagents)\n" +
        "- Setze neutrale Read-Only Subagents ein, um die Module des Projekts parallel und unabhängig auf folgende 5 Fehlerkategorien zu analysieren:\n" +
        "  1. Concurrency & Thread-Safety: Race Conditions, Deadlocks, Thread-Konflikte (z.B. UI-Thread-Verletzungen), ungesicherte Timer-/Watcher-Lifecycles.\n" +
        "  2. Grenzwerte & Nullability: NullReference/NullPointer-Vektoren, Off-by-one, ungültige Collections-Zugriffe, fehlende Bounds-Checks.\n" +
        "  3. Resource Leaks: Nicht freigegebene Streams, Pipes, File-Handles, Event-Subscriptions oder FileSystemWatcher.\n" +
        "  4. State- & Logikbrüche: Ungültige Zustandsübergänge, asynchrone Race Conditions bei User-Inputs, Broken Invariants.\n" +
        "  5. Historische Regressionen: Gleiche gefundene Stellen zwingend mit docs/knowledge.md und docs/protocol.md ab! Prüfe, ob bekannte alte Fehler oder Anti-Patterns erneut eingebaut wurden.\n" +
        "- SCHUTZ VOR FEHLINTERPRETATION: Bestehende Guards (Thread-Locks, Exception-Handler, Fallbacks, Scroll-Guards) dürfen keinesfalls als 'überflüssig' eingestuft werden!\n" +
        "- WICHTIG: FÜHRE IN PHASE 1 NOCH KEINERLEI CODE-ÄNDERUNGEN DURCH!\n" +
        "- Präsentiere eine vertikal strukturierte Liste (keine Markdown-Tabellen wegen Terminal-Kollaps) aller Fundstellen:\n" +
        "  • Priorität (P0 Blocker bis P3 Minor)\n" +
        "  • Datei & Zeilennummer\n" +
        "  • Konkrete Fehlerursache & Fehlerszenario\n" +
        "  • Vorgeschlagener Fix & Benötigter Repro-Test\n" +
        "- Warte nach Phase 1 auf meine Freigabe ('go') für die Umsetzung!\n\n" +
        "PHASE 2: Failure Reproduction (TDD Red-Phase – nach Freigabe)\n" +
        "- Erstelle vor dem Ändern von Quellcode für jeden genehmigten Fehler einen isolierten, automatisierten Unit- oder Integrationstest (oder ein minimales Repro-Skript).\n" +
        "- Führe den Test aus und belege, dass er im aktuellen Zustand fehlschlägt (Red).\n\n" +
        "PHASE 3: Minimal-Invasive Behebung (Green-Phase)\n" +
        "- Behebe die Ursache mit dem kleinstmöglichen chirurgischen Eingriff (KISS).\n" +
        "- Bestehende öffentliche Schnittstellen, Verträge und das Gesamtverhalten müssen vollständig erhalten bleiben.\n" +
        "- Führe keine kosmetischen Stiländerungen oder ungefragten Refactorings durch.\n\n" +
        "PHASE 4: Deterministische Verifikation & Regressions-Beweis\n" +
        "- Führe die gesamte Test-Suite und den projektspezifischen Compiler/Build-Check aus (z. B. dotnet build, npm test, pytest).\n" +
        "- Verifiziere:\n" +
        "  1. Der neue Test aus Phase 2 läuft erfolgreich durch (Green).\n" +
        "  2. Alle bestehenden Regressionstests bleiben intakt (0 Fehler).\n" +
        "  3. Es entstehen keinerlei neue Linter- oder Compiler-Warnungen (Exit-Code 0).\n" +
        "- Fasse das Ergebnis in einer kurzen vertikalen Liste mit Vorher-Nachher-Status zusammen.";

    private void SendDirectZeroBug()
    {
        PrepareAndDispatchDirectCommand(ZeroBugPromptText, "zero_bug_audit", "Zero-Bug-Audit");
    }

    private static readonly string DauTestPromptText =
        "[DAU-Simulationstest: Multi-Agenten Real-User & Chaos-Auditing]\n" +
        "Führe einen umfassenden, zielgruppen-bewussten DAU-Simulationstest mit einem hochgradig parallelisierten Subagent-Schwarm durch.\n" +
        "Arbeite streng phasenbasiert, um alle Stellen aufzudecken, an denen ein Mensch Eingaben machen kann oder muss:\n\n" +
        "SCHRITT 1: Kontext- & Zielgruppen-Klassifikation\n" +
        "- Scanne die betroffenen Masken, Routen, Dialoge und Komponenten des Projekts und klassifiziere sie strikt:\n" +
        "  A) Öffentliche Benutzer-UI (B2C / Bürger / Kunden): Hohe Diversität! Simuliere Sprachbarrieren (RTL / Arabisch ٠-٩, Kyrillisch, Diakritika), mobile Eingaben, 200% Zoom, Autocomplete-Fallen, Zeitverzögerungen bei E-Mail-OTP, Baby-/Senioren-Grenzwerte (Alter 0 bis 120), wackliges Mobilfunknetz.\n" +
        "  B) Interne Mitarbeiter-UI (B2E / Verwaltung / Dashboards): Realistische Anwender! Simuliere unerfahrene Gelegenheitsnutzer (Doppelklicks auf normale Links, versehentliches Löschen ohne Warnung, Fokusverlust), ungeduldige Nutzer im Akkord (abgebrochene Dialoge, geschlossene Tabs), aktive Filter vergessen und Einträge vermissen, Trennzeichen- und Datenformat-Konflikte (Semikolon vs. Komma, Datumsformate).\n" +
        "  C) Partner- & Firmen-Portale (B2B): Simuliere Firmenkoordinatoren, Teil-Kontingente, versehentliche Überbuchung, Stornierung nach Zusage, Barcode-/Druckansichten-Skalierung, Mandantentrennung (sieht Firma A Daten von B?).\n\n" +
        "SCHRITT 2: Schwarm-Größe & Persona-Orchestrierung\n" +
        "- Ermittle dynamisch die erforderliche Anzahl an Subagents nach: N = Masken × Rollen × DAU-Archetypen (Standard: 10 bis 20 spezialisierte Subagents).\n" +
        "- Starte parallele Subagents mit scharf abgegrenzten Personas und konkreten Aufgaben.\n" +
        "- Jeder Subagent prüft seine spezifische Persona auf: Validierungsfallen, UI-Deadlocks, Datenverlust bei Fehlern, unklare Fehlermeldungen und Race Conditions.\n\n" +
        "SCHRITT 3: Praktische Live-Verifikation (Praxistest-Beweis im echten Frontend)\n" +
        "- Keine reinen theoretischen Vermutungen! Handelt es sich um ein Web-Projekt oder Frontend, nutze zwingend den integrierten Dashboard KI-Co-Browser:\n" +
        "  * Öffne den Co-Browser automatisch im Split-Screen und navigiere zur laufenden App (via Co-Browser MCP-Tools wie cobrowser_navigate / cobrowser_open oder co-browser-api.ps1).\n" +
        "  * Steuere die Masken mit echten DAU-Aktionen an: Nutze cobrowser_interactive_map (beachte inDialog bei Modals), cobrowser_type / cobrowser_fill_form für fehlerhafte Payloads (arabische Ziffern, Diakritika, Whitespace, Grenzwerte) und cobrowser_click für Rage-Clicks auf Buttons oder inaktive Bereiche.\n" +
        "  * Überwache unhandled Errors via cobrowser_console und dokumentiere optische Glitches mit cobrowser_screenshot.\n" +
        "- Handelt es sich um ein reines Backend / Desktop-App ohne Web-UI: Erstelle ein isoliertes temporäres Repro-Skript (z. B. in scratch/) und führe es gegen die Applikation aus.\n" +
        "- PFLICHT - CLEANUP: Testdaten am Ende rückstandslos aus der Datenbank bzw. dem System bereinigen!\n\n" +
        "SCHRITT 4: Priorisiertes Qualitäts-Protokoll\n" +
        "- Fasse die Ergebnisse vertikal strukturiert zusammen (keine Markdown-Tabellen wegen ConPTY-Kollaps):\n" +
        "  • P0 Blocker: Crashes, 500er, unbehandelte Exceptions, Deadlocks\n" +
        "  • P1 Datenverlust: Eingaben gehen bei Validierungsfehlern oder Tab-Wechseln verloren\n" +
        "  • P2 Frustrationsfalle: Submit gesperrt ohne ersichtlichen Grund, irreführende Meldungen\n" +
        "  • P3 Usability: Skalierungsprobleme, Textüberlauf, unklare Buttons\n" +
        "  • Praxistest-Ergebnis: [X Bestanden, Y Fehlgeschlagen]";

    private void SendDirectDauTest()
    {
        PrepareAndDispatchDirectCommand(DauTestPromptText, "dau_test", "DAU-Simulationstest");
    }

    private static readonly string ProjectCleanupPromptText =
        "Führe einen vollständigen Projekt-Hygiene-Audit über alle Projekte durch:\n\n" +
        "Setze für jedes Projekt im Workspace/Projekte-Ordner einen eigenen Subagenten ein, um die folgenden Schritte isoliert und parallel durchzuführen:\n\n" +
        "Phase 1 (Struktur-Check pro Projekt):\n" +
        "- Prüfe in jedem Projekt das Vorhandensein von:\n" +
        "  1. `_archive/` Ordner\n" +
        "  2. `.gitignore` Datei\n" +
        "  3. `.geminiignore` Datei\n" +
        "- Lege fehlende Ordner/Dateien mit sinnvollen Standard-Einträgen an.\n\n" +
        "Phase 2 (Mülldateien & Aufräumen pro Projekt):\n" +
        "- Scanne das Projekt nach Mülldateien (*.tmp, *.bak, *.log, alten Dumps/Artefakten).\n" +
        "- Verschiebe identifizierte Mülldateien sicher in den `_archive/` Ordner des jeweiligen Projekts.\n\n" +
        "Phase 3 (Ignore-Pflege pro Projekt):\n" +
        "- WICHTIG: Trage den `_archive/`-Ordner ZWINGEND in die `.gitignore` UND `.geminiignore` ein!\n" +
        "- Prüfe, welche weiteren Dateien/Ordner auf die `.geminiignore` oder `.gitignore` gehören.\n" +
        "- Aktualisiere beide Ignore-Dateien präzise.\n\n" +
        "Phase 4 (Regel-Hygiene & Wildwuchs-Audit pro Projekt):\n" +
        "- Das Antigravity CLI Dashboard ist die einzige erlaubte Quelle für Projektregeln (Single Source of Truth).\n" +
        "- Spüre wilde, veraltete oder nicht vom Dashboard injizierte Regeldateien auf und bereinige sie sicher:\n" +
        "  1. Veraltete `.gemini/GEMINI.md` in Unterordnern (ins Archiv verschieben oder nach Bestätigung löschen).\n" +
        "  2. Nicht vom Dashboard verwaltete Duplikate oder veraltete Artefakte im Projekt prüfen und auflisten.\n" +
        "  3. Manuelle Legacy-Blöcke in Root-`GEMINI.md` (nur die vom Dashboard synchronisierten `<rules>`-Tags sind erlaubt).\n" +
        "- Stelle sicher, dass ausschließlich die vom Dashboard verwalteten Regeln aktiv sind.\n\n" +
        "Phase 5 (Globale AGY-System- & Chat-Hygiene):\n" +
        "- Führe eine gründliche, sichere Bereinigung unter ~/.gemini/antigravity-cli und im Temp-Verzeichnis durch:\n" +
        "  1. Temporäre Anhänge & Screenshots (`temp_attachments/`):\n" +
        "     - Scanne die Ordner `$env:TEMP/agy-cli-dashboard/temp_attachments/` sowie veraltete Legacy-Dateien unter `~/.gemini/antigravity-cli/temp_attachments/`.\n" +
        "     - SICHERHEITS-FILTER: Nur Dateien erfassen, die älter als 7 Tage sind (aktuelle Screenshots/Puffer bleiben 100 % erhalten).\n" +
        "     - Liste gefundene Alt-Dateien und die freizugebende Megabyte-Zahl auf.\n" +
        "  2. Verwaiste Chat-Einträge (`conversation_summaries.db`):\n" +
        "     - Gleiche die SQLite-Datenbank `~/.gemini/antigravity-cli/conversation_summaries.db` gegen das Brain-Verzeichnis `~/.gemini/antigravity-cli/brain/` ab.\n" +
        "     - SICHERHEITS-FILTER: Nur Datensätze erfassen, zu denen kein physischer Brain-Ordner existiert UND deren `last_modified_time` älter als 1 Stunde ist (Schutz vor frisch startenden Chats!).\n" +
        "     - BACKUP-PFLICHT: Erstelle vor jedem Schreibzugriff zwingend ein Backup: `conversation_summaries.db.bak_<timestamp>`.\n" +
        "     - Entferne nach Freigabe die verwaisten IDs und führe ein `VACUUM;` zur Defragmentierung aus.\n" +
        "  3. Verwaiste Locks & Annotationen (`presence/` und `annotations/`):\n" +
        "     - Entferne verwaiste `.lock`- und `.pbtxt`-Dateien, zu denen kein Brain-Ordner mehr existiert.\n\n" +
        "SICHERHEITS-SCHRANKE VOR AUSFÜHRUNG:\n" +
        "- Jeder Subagent meldet seine Ergebnisse zurück. Zeige vor dem Verschieben, Anlegen oder Löschen eine strukturierte, vertikale Gesamtzusammenfassung als Liste (keine Markdown-Tabellen; Phase 1 bis 5) und warte auf meine ausdrückliche Freigabe ('go').";

    private void SendDirectProjectCleanup()
    {
        PrepareAndDispatchDirectCommand($"[{ProjectCleanupPromptText}]", "project_cleanup", "Projekt-Hygiene");
    }

    public static string BuildSingleProjectCleanupPrompt(string projectName)
    {
        string projStr = !string.IsNullOrWhiteSpace(projectName) ? $"für das aktuelle Projekt ('{projectName}')" : "für das aktuelle Projekt";
        return
            $"Führe einen vollständigen Projekt-Hygiene-Audit {projStr} durch:\n\n" +
            "Phase 1 (Struktur-Check):\n" +
            "- Prüfe im Projekt das Vorhandensein von:\n" +
            "  1. `_archive/` Ordner\n" +
            "  2. `.gitignore` Datei\n" +
            "  3. `.geminiignore` Datei\n" +
            "- Lege fehlende Ordner/Dateien mit sinnvollen Standard-Einträgen an.\n\n" +
            "Phase 2 (Mülldateien & Aufräumen):\n" +
            "- Scanne das Projekt nach Mülldateien (*.tmp, *.bak, *.log, alten Dumps/Artefakten).\n" +
            "- Verschiebe identifizierte Mülldateien sicher in den `_archive/` Ordner des Projekts.\n\n" +
            "Phase 3 (Ignore-Pflege):\n" +
            "- WICHTIG: Trage den `_archive/`-Ordner ZWINGEND in die `.gitignore` UND `.geminiignore` ein!\n" +
            "- Prüfe, welche weiteren Dateien/Ordner auf die `.geminiignore` oder `.gitignore` gehören.\n" +
            "- Aktualisiere beide Ignore-Dateien präzise.\n\n" +
            "Phase 4 (Regel-Hygiene & Wildwuchs-Audit):\n" +
            "- Das Antigravity CLI Dashboard ist die einzige erlaubte Quelle für Projektregeln (Single Source of Truth).\n" +
            "- Spüre wilde, veraltete oder nicht vom Dashboard injizierte Regeldateien auf und bereinige sie sicher:\n" +
            "  1. Veraltete `.gemini/GEMINI.md` in Unterordnern (ins Archiv verschieben oder nach Bestätigung löschen).\n" +
            "  2. Nicht vom Dashboard verwaltete Duplikate oder veraltete Artefakte im Projekt prüfen und auflisten.\n" +
            "  3. Manuelle Legacy-Blöcke in Root-`GEMINI.md` (nur die vom Dashboard synchronisierten `<rules>`-Tags sind erlaubt).\n" +
            "- Stelle sicher, dass ausschließlich die vom Dashboard verwalteten Regeln aktiv sind.\n\n" +
            "Phase 5 (Projektbezogene Chat- & System-Hygiene):\n" +
            "- Bereinige verwaiste Einträge in ~/.gemini/antigravity-cli und im Temp-Verzeichnis mit Bezug zu diesem Projekt:\n" +
            "  1. Verwaiste Chat-Einträge in `conversation_summaries.db` (Backup anlegen, Datensätze ohne Brain-Ordner tilgen, VACUUM).\n" +
            "  2. Temporäre Anhänge & Screenshots in `$env:TEMP/agy-cli-dashboard/temp_attachments/` und Legacy-Dateien in `~/.gemini/antigravity-cli/temp_attachments/`, die älter als 7 Tage sind.\n" +
            "  3. Verwaiste `.lock`-Dateien in `presence/` und `.pbtxt` in `annotations/`.\n\n" +
            "SICHERHEITS-SCHRANKE VOR AUSFÜHRUNG:\n" +
            "- Zeige vor dem Verschieben, Anlegen oder Löschen eine strukturierte, vertikale Zusammenfassung als Liste (keine Markdown-Tabellen; Phase 1 bis 5) und warte auf meine ausdrückliche Freigabe ('go').";
    }

    private void SendDirectSingleProjectCleanup(string promptText, string projectName)
    {
        RecordProjectMaintenanceTimestamp(projectName, "project_cleanup");
        PrepareAndDispatchDirectCommand($"[{promptText}]", null, "Projekt-Hygiene");
    }

    private void SendDirectGlobalErrorCheck()
    {
        PrepareAndDispatchDirectCommand("[Überprüfe, ob der Fehler auch noch woanders im Projekt auftritt.]", null, "Fehlerprüfung");
    }

    private async void BtnAttachFiles_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnAttachFiles_Click)} triggered.");
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    if (!_stagedAttachments.Any(a => a.FilePath.Equals(file.Path, StringComparison.OrdinalIgnoreCase)))
                    {
                        _stagedAttachments.Add(new StagedAttachment
                        {
                            FileName = file.Name,
                            FilePath = file.Path
                        });
                    }
                }
                AttachmentStagingControl.Visibility = Visibility.Visible;
                RecalculateStagedAttachmentTokens();
                UpdateTokenPeakMeter(false);
                if (_lastFilesProjectPath != null)
                {
                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnAttachFiles_Click error: {ex.Message}");
            ShowNotice("Anhang-Fehler", $"Fehler beim Auswählen von Dateien: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void TxtChatInput_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Text))
        {
            e.AcceptedOperation = (e.AllowedOperations & DataPackageOperation.Copy) != 0
                ? DataPackageOperation.Copy
                : (e.AllowedOperations & DataPackageOperation.Link) != 0
                    ? DataPackageOperation.Link
                    : DataPackageOperation.Move;
            e.DragUIOverride.Caption = "Als Anhang an den Chat übergeben";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
    }

    private async void TxtChatInput_Drop(object? sender, DragEventArgs e)
    {
        try
        {
            var filesToAdd = new List<string>();

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                try
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            if (!string.IsNullOrEmpty(item.Path) && (File.Exists(item.Path) || Directory.Exists(item.Path)))
                            {
                                filesToAdd.Add(item.Path);
                            }
                        }
                    }
                }
                catch (Exception exStorage)
                {
                    TelemetryLogger.Log($"[TxtChatInput_Drop Storage Error] {exStorage.Message}");
                }
            }

            if (filesToAdd.Count == 0 && e.DataView.Contains(StandardDataFormats.Text))
            {
                try
                {
                    string rawText = await e.DataView.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(rawText))
                    {
                        var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            string candidate = line.Trim().Trim('"');
                            if (File.Exists(candidate) || Directory.Exists(candidate))
                            {
                                filesToAdd.Add(candidate);
                            }
                        }
                    }
                }
                catch (Exception exText)
                {
                    TelemetryLogger.Log($"[TxtChatInput_Drop Text Error] {exText.Message}");
                }
            }

            if (filesToAdd.Count > 0)
            {
                e.Handled = true;
                bool anyAdded = false;
                foreach (var filePath in filesToAdd)
                {
                    if (!_stagedAttachments.Any(a => a.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        string name = Path.GetFileName(filePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (string.IsNullOrEmpty(name)) name = filePath;
                        _stagedAttachments.Add(new StagedAttachment
                        {
                            FileName = name,
                            FilePath = filePath
                        });
                        anyAdded = true;
                    }
                }

                if (anyAdded)
                {
                    if (AttachmentStagingControl != null)
                    {
                        AttachmentStagingControl.Visibility = Visibility.Visible;
                    }
                    RecalculateStagedAttachmentTokens();
                    UpdateTokenPeakMeter(false);
                    if (_lastFilesProjectPath != null)
                    {
                        UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                    }
                }

                TxtChatInput?.Focus(FocusState.Programmatic);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[TxtChatInput_Drop Error] {ex.Message}");
        }
    }

    private async void TxtChatInput_Paste(object? sender, TextControlPasteEventArgs e)
    {
        try
        {
            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView == null) return;

            // 1. Files / StorageItems aus Zwischenablage (z. B. aus Windows Explorer kopiert)
            if (dataPackageView.Contains(StandardDataFormats.StorageItems))
            {
                e.Handled = true;
                var items = await dataPackageView.GetStorageItemsAsync();
                if (items != null && items.Count > 0)
                {
                    foreach (var item in items)
                    {
                        if (item is Windows.Storage.StorageFile file)
                        {
                            if (!_stagedAttachments.Any(a => a.FilePath.Equals(file.Path, StringComparison.OrdinalIgnoreCase)))
                            {
                                _stagedAttachments.Add(new StagedAttachment
                                {
                                    FileName = file.Name,
                                    FilePath = file.Path
                                });
                            }
                        }
                    }
                    if (_stagedAttachments.Count > 0)
                    {
                        AttachmentStagingControl.Visibility = Visibility.Visible;
                        RecalculateStagedAttachmentTokens();
                        UpdateTokenPeakMeter(false);
                        if (_lastFilesProjectPath != null)
                        {
                            UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                        }
                    }
                }
                return;
            }

            // 2. Textinhalte aus der Zwischenablage
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                e.Handled = true; // Verhindere synchrones, blockierendes WinUI 3 Layouting auf dem UI-Thread

                // Synchronen Zustand VOR dem asynchronen Clipboard-Lesen sichern
                int selStart = TxtChatInput.SelectionStart;
                int selLength = TxtChatInput.SelectionLength;
                string current = TxtChatInput.Text ?? string.Empty;

                if (selStart < 0 || selStart > current.Length) selStart = current.Length;
                if (selLength < 0 || selStart + selLength > current.Length) selLength = 0;

                string text = await dataPackageView.GetTextAsync();
                if (string.IsNullOrEmpty(text)) return;

                // Null-Bytes entfernen, um DirectWrite/PTY Parsing-Fehler zu verhindern
                text = text.Replace("\0", string.Empty);
                if (string.IsNullOrEmpty(text)) return;

                const int MaxDirectPasteChars = 4000;

                if (text.Length <= MaxDirectPasteChars)
                {
                    // Text automatisch in Anführungszeichen setzen, falls nicht bereits umschlossen
                    string textToInsert = text;
                    string trimmed = textToInsert.Trim();
                    if (!string.IsNullOrWhiteSpace(textToInsert) && !(trimmed.Length >= 2 && trimmed.StartsWith("\"") && trimmed.EndsWith("\"")))
                    {
                        textToInsert = $"\"{textToInsert}\"";
                    }

                    int targetCursor = selStart + textToInsert.Length;

                    // Direkte Texteinfügung: Zuerst natives SelectedText probieren (verhindert vollen Text-Reset in WinUI 3)
                    try
                    {
                        TxtChatInput.Focus(FocusState.Programmatic);
                        TxtChatInput.SelectionStart = selStart;
                        TxtChatInput.SelectionLength = selLength;
                        TxtChatInput.SelectedText = textToInsert;
                    }
                    catch
                    {
                        string newText = current.Remove(selStart, selLength).Insert(selStart, textToInsert);
                        TxtChatInput.Text = newText;
                    }

                    int currentLen = (TxtChatInput.Text ?? string.Empty).Length;
                    int safePos = Math.Clamp(targetCursor, 0, currentLen);
                    TxtChatInput.Select(safePos, 0);

                    // WinUI 3 Layout-Pass Absicherung: Dispatcher-Queue stellt sicher,
                    // dass der Cursor auch nach asynchronem WinUI DirectWrite-Layout am Ende des eingefügten Textes bleibt
                    DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                    {
                        if (TxtChatInput == null) return;
                        TxtChatInput.Focus(FocusState.Programmatic);
                        int maxLen = (TxtChatInput.Text ?? string.Empty).Length;
                        TxtChatInput.Select(Math.Clamp(targetCursor, 0, maxLen), 0);
                    });
                }
                else
                {
                    // Sehr langer Text / Code-Dump / Logfile: Automatisch als temporäre Anhang-Datei ablegen
                    try
                    {
                        string tempDir = GetTempAttachmentsDirectory();
                        string fileName = $"pasted_text_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..4]}.txt";
                        string filePath = Path.Combine(tempDir, fileName);
                        await File.WriteAllTextAsync(filePath, text);

                        if (!_stagedAttachments.Any(a => a.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            _stagedAttachments.Add(new StagedAttachment
                            {
                                FileName = fileName,
                                FilePath = filePath
                            });
                        }
                        AttachmentStagingControl.Visibility = Visibility.Visible;
                        RecalculateStagedAttachmentTokens();
                        UpdateTokenPeakMeter(false);
                        if (_lastFilesProjectPath != null)
                        {
                            UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                        }
                        ShowNotice("Großer Text als Anhang bereitgestellt", $"Der eingefügte Inhalt ({text.Length:N0} Zeichen) wurde automatisch als Anhang '{fileName}' abgelegt.", InfoBarSeverity.Informational);
                        TxtChatInput.Focus(FocusState.Programmatic);
                        TxtChatInput.Select(selStart, 0);
                    }
                    catch (Exception exFile)
                    {
                        TelemetryLogger.Log($"[TxtChatInput_Paste StagedAttachment ERROR] {exFile.Message}");
                        TxtChatInput.Text = text;
                        int textLen = (TxtChatInput.Text ?? string.Empty).Length;
                        TxtChatInput.Select(textLen, 0);
                        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                        {
                            if (TxtChatInput == null) return;
                            TxtChatInput.Focus(FocusState.Programmatic);
                            TxtChatInput.Select((TxtChatInput.Text ?? string.Empty).Length, 0);
                        });
                    }
                }
                return;
            }

            // 3. Reines Bild (Bitmap) in Zwischenablage
            if (dataPackageView.Contains(StandardDataFormats.Bitmap))
            {
                e.Handled = true; // Verhindere Standard-Paste im Chatfeld

                try
                {
                    var bitmapStreamRef = await dataPackageView.GetBitmapAsync();
                    if (bitmapStreamRef != null)
                    {
                        using var imageStream = await bitmapStreamRef.OpenReadAsync();
                        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(imageStream);
                        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);

                        string tempDir = GetTempAttachmentsDirectory();
                        string fileName = $"clipboard_image_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..4]}.png";
                        string filePath = Path.Combine(tempDir, fileName);

                        using (var fileStream = File.Create(filePath))
                        using (var winrtStream = fileStream.AsRandomAccessStream())
                        {
                            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, winrtStream);
                            encoder.SetSoftwareBitmap(softwareBitmap);
                            await encoder.FlushAsync();
                        }

                        if (!_stagedAttachments.Any(a => a.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            _stagedAttachments.Add(new StagedAttachment
                            {
                                FileName = fileName,
                                FilePath = filePath
                            });
                        }
                        AttachmentStagingControl.Visibility = Visibility.Visible;
                        RecalculateStagedAttachmentTokens();
                        UpdateTokenPeakMeter(false);
                        if (_lastFilesProjectPath != null)
                        {
                            UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                        }
                        ShowNotice("Bild als Anhang bereitgestellt", $"Bild aus der Zwischenablage wurde als '{fileName}' im Chat bereitgestellt.", InfoBarSeverity.Informational);
                        TxtChatInput.Focus(FocusState.Programmatic);
                    }
                }
                catch (Exception exImg)
                {
                    TelemetryLogger.Log($"[TxtChatInput_Paste Bitmap ERROR] {exImg.Message}");
                    ShowNotice("Bild-Fehler", $"Bild konnte nicht aus der Zwischenablage verarbeitet werden: {exImg.Message}", InfoBarSeverity.Warning);
                }
                return;
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[TxtChatInput_Paste ERROR] {ex.Message}");
        }
    }

    private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnRemoveAttachment_Click)} triggered.");
        if (sender is Button btn && btn.Tag is string filePath)
        {
            var item = _stagedAttachments.FirstOrDefault(a => a.FilePath == filePath);
            if (item != null)
            {
                _stagedAttachments.Remove(item);
                if (_stagedAttachments.Count == 0)
                {
                    AttachmentStagingControl.Visibility = Visibility.Collapsed;
                }
                RecalculateStagedAttachmentTokens();
                UpdateTokenPeakMeter(false);
                if (_lastFilesProjectPath != null)
                {
                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                }
            }
        }
    }

    private void Global_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key;
        var currentFocusedObj = FocusManager.GetFocusedElement(this.Content?.XamlRoot);

        if (key == VirtualKey.Enter)
        {
            if (object.ReferenceEquals(currentFocusedObj, TxtChatInput))
            {
                // Fokus liegt im Chat-Eingabefeld: TxtChatInput_KeyDown / KeyUp wickelt die Verarbeitung und das Senden ab
                return;
            }

            if (_isVoiceRecordingActive || _isVoicePendingInTerminal)
            {
                e.Handled = true;
                HandleVoiceSubmissionOnEnter();
                return;
            }

            if (currentFocusedObj is TextBox otherTb && !object.ReferenceEquals(otherTb, TxtChatInput))
            {
                return;
            }
            if (currentFocusedObj is AutoSuggestBox || currentFocusedObj is PasswordBox)
            {
                return;
            }

            var enterShiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool enterIsShiftPressed = enterShiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (object.ReferenceEquals(currentFocusedObj, TxtChatInput) && enterIsShiftPressed)
            {
                return;
            }

            TxtChatInput?.Focus(FocusState.Programmatic);
            return;
        }



        if (key == VirtualKey.B)
        {
            var ctrlStateB = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            var shiftStateB = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            if (ctrlStateB.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) &&
                shiftStateB.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                ToggleCoBrowser();
                e.Handled = true;
                return;
            }
        }

        if (key == VirtualKey.D)
        {
            var ctrlStateD = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            var shiftStateD = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            if (ctrlStateD.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) &&
                !shiftStateD.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                ToggleTerminalDocumentMode();
                e.Handled = true;
                return;
            }
        }

        if (key == VirtualKey.F5)
        {
            if (currentFocusedObj is not TextBox && currentFocusedObj is not AutoSuggestBox)
            {
                e.Handled = true;
                _ = TriggerTerminalMicroResizeRedrawAsync(showNotice: true);
                return;
            }
        }

        if (key != VirtualKey.NumberPad0 && key != VirtualKey.NumberPad1 &&
            key != VirtualKey.NumberPad2 && key != VirtualKey.NumberPad3 &&
            key != VirtualKey.NumberPad4 && key != VirtualKey.NumberPad5 &&
            key != VirtualKey.NumberPad7 &&
            key != VirtualKey.NumberPad8 && key != VirtualKey.NumberPad9)
        {
            return;
        }

        bool isInputFocused = currentFocusedObj is TextBox || currentFocusedObj is TextBlock || currentFocusedObj is EasyWindowsTerminalControl.EasyTerminalControl;

        var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        bool isCtrlPressed = ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isInputFocused && !isCtrlPressed)
        {
            return;
        }

        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        switch (key)
        {
            case VirtualKey.NumberPad0:
                BtnModelSelect_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case VirtualKey.NumberPad1:
                if (_isDeepThinkSticky) { e.Handled = true; break; }
                _isDeepThinkEnabled = !_isDeepThinkEnabled;
                UpdateModeButtonVisuals();
                e.Handled = true;
                break;
            case VirtualKey.NumberPad2:
                _isProjectDocsEnabled = !_isProjectDocsEnabled;
                UpdateModeButtonVisuals();
                e.Handled = true;
                break;
            case VirtualKey.NumberPad3:
                _isSubagentModeEnabled = !_isSubagentModeEnabled;
                UpdateModeButtonVisuals();
                e.Handled = true;
                break;
            case VirtualKey.NumberPad4:
                if (isShiftPressed)
                {
                    _isReflectionModeEnabled = !_isReflectionModeEnabled;
                    UpdateModeButtonVisuals();
                }
                else
                {
                    SendDirectReflection();
                }
                e.Handled = true;
                break;
            case VirtualKey.NumberPad5:
                _isWebResearchEnabled = !_isWebResearchEnabled;
                UpdateModeButtonVisuals();
                e.Handled = true;
                break;
            case VirtualKey.NumberPad7:
                if (isShiftPressed)
                {
                    _isE2ETestModeEnabled = !_isE2ETestModeEnabled;
                    UpdateModeButtonVisuals();
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(TxtChatInput.Text))
                    {
                        SendDirectE2ETest();
                    }
                    else
                    {
                        _isE2ETestModeEnabled = !_isE2ETestModeEnabled;
                        UpdateModeButtonVisuals();
                    }
                }
                e.Handled = true;
                break;
            case VirtualKey.NumberPad8:
                if (isShiftPressed)
                {
                    _isDeadCodeCheckModeEnabled = !_isDeadCodeCheckModeEnabled;
                    UpdateModeButtonVisuals();
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(TxtChatInput.Text))
                    {
                        SendDirectDeadCodeCheck();
                    }
                    else
                    {
                        _isDeadCodeCheckModeEnabled = !_isDeadCodeCheckModeEnabled;
                        UpdateModeButtonVisuals();
                    }
                }
                e.Handled = true;
                break;
            case VirtualKey.NumberPad9:
                if (isShiftPressed)
                {
                    _isIrrwegCleanupModeEnabled = !_isIrrwegCleanupModeEnabled;
                    UpdateModeButtonVisuals();
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(TxtChatInput.Text))
                    {
                        SendDirectIrrwegCleanup();
                    }
                    else
                    {
                        _isIrrwegCleanupModeEnabled = !_isIrrwegCleanupModeEnabled;
                        UpdateModeButtonVisuals();
                    }
                }
                e.Handled = true;
                break;
        }
    }



    private void ToggleStickyDeepThinkForCurrentModel()
    {
        _isDeepThinkSticky = !_isDeepThinkSticky;
        _isDeepThinkEnabled = _isDeepThinkSticky;
        _uiStates.IsDeepThinkSticky = _isDeepThinkSticky;

        SaveUiStates();
        UpdateModeButtonVisuals();

        if (_isDeepThinkSticky)
            ShowNotice("[PIN] Deep Think Dauer-Modus AKTIVIERT", "Deep Think bleibt jetzt dauerhaft bei jeder Nachricht aktiv. Erneut Longpress zum Deaktivieren.", InfoBarSeverity.Success);
        else
            ShowNotice("Deep Think Dauer-Modus deaktiviert", "Deep Think ist jetzt wieder im normalen One-Shot-Modus.", InfoBarSeverity.Informational);
    }

    private void BtnDeepThink_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDeepThinkBtnLongPressHandled = false;
        if (_deepThinkBtnLongPressTimer == null)
        {
            _deepThinkBtnLongPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _deepThinkBtnLongPressTimer.Tick += (s, ev) =>
            {
                _deepThinkBtnLongPressTimer?.Stop();
                _isDeepThinkBtnLongPressHandled = true;
                ToggleStickyDeepThinkForCurrentModel();
            };
        }
        _deepThinkBtnLongPressTimer.Stop();
        _deepThinkBtnLongPressTimer.Start();
    }

    private void BtnDeepThink_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _deepThinkBtnLongPressTimer?.Stop();
    }

    private void BtnDeepThink_Click(object sender, RoutedEventArgs e)
    {
        if (_isDeepThinkBtnLongPressHandled) { _isDeepThinkBtnLongPressHandled = false; return; }
        if (_isDeepThinkSticky) return; // Sticky-Modus nur per Longpress deaktivierbar

        TelemetryLogger.Log($"[Click Trace] {nameof(BtnDeepThink_Click)} triggered.");
        _isDeepThinkEnabled = !_isDeepThinkEnabled;
        UpdateModeButtonVisuals();
    }

    private void BtnDeepThink_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnDeepThink_RightTapped)} triggered.");
        SetLongPressTarget("deep-think");
        e.Handled = true;
    }

    private void BtnProjectDocs_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnProjectDocs_Click)} triggered.");
        _isProjectDocsEnabled = !_isProjectDocsEnabled;
        UpdateModeButtonVisuals();
    }

    private void BtnProjectDocs_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnProjectDocs_RightTapped)} triggered.");
        SetLongPressTarget("docs");
        e.Handled = true;
    }

    private void BtnGlobalErrorCheck_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnGlobalErrorCheck_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            _isGlobalErrorCheckEnabled = !_isGlobalErrorCheckEnabled;
            UpdateModeButtonVisuals();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TxtChatInput.Text))
            {
                SendDirectGlobalErrorCheck();
            }
            else
            {
                _isGlobalErrorCheckEnabled = !_isGlobalErrorCheckEnabled;
                UpdateModeButtonVisuals();
            }
        }
    }

    private void BtnGlobalErrorCheck_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnGlobalErrorCheck_RightTapped)} triggered.");
        SetLongPressTarget("global-error-check");
        e.Handled = true;
    }

    private void BtnSubagent_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnSubagent_Click)} triggered.");
        _isSubagentModeEnabled = !_isSubagentModeEnabled;
        UpdateModeButtonVisuals();
    }

    private void BtnSubagent_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnSubagent_RightTapped)} triggered.");
        SetLongPressTarget("subagent");
        e.Handled = true;
    }

    private void BtnReflection_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnReflection_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            _isReflectionModeEnabled = !_isReflectionModeEnabled;
            UpdateModeButtonVisuals();
        }
        else
        {
            SendDirectReflection();
        }
    }

    private void BtnReflection_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnReflection_RightTapped)} triggered.");
        SetLongPressTarget("reflection");
        e.Handled = true;
    }

    private void BtnWebResearch_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnWebResearch_Click)} triggered.");
        _isWebResearchEnabled = !_isWebResearchEnabled;
        UpdateModeButtonVisuals();
    }

    private void BtnWebResearch_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnWebResearch_RightTapped)} triggered.");
        SetLongPressTarget("web-research");
        e.Handled = true;
    }

    private void BtnE2ETest_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnE2ETest_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            _isE2ETestModeEnabled = !_isE2ETestModeEnabled;
            UpdateModeButtonVisuals();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TxtChatInput.Text))
            {
                SendDirectE2ETest();
            }
            else
            {
                _isE2ETestModeEnabled = !_isE2ETestModeEnabled;
                UpdateModeButtonVisuals();
            }
        }
    }

    private void BtnE2ETest_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnE2ETest_RightTapped)} triggered.");
        SetLongPressTarget("e2e-test");
        e.Handled = true;
    }

    private void BtnDeadCodeCheck_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnDeadCodeCheck_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            _isDeadCodeCheckModeEnabled = !_isDeadCodeCheckModeEnabled;
            UpdateModeButtonVisuals();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TxtChatInput.Text))
            {
                SendDirectDeadCodeCheck();
            }
            else
            {
                _isDeadCodeCheckModeEnabled = !_isDeadCodeCheckModeEnabled;
                UpdateModeButtonVisuals();
            }
        }
    }

    private void BtnDeadCodeCheck_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnDeadCodeCheck_RightTapped)} triggered.");
        SetLongPressTarget("dead-code-check");
        e.Handled = true;
    }

    private void BtnIrrwegCleanup_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnIrrwegCleanup_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            _isIrrwegCleanupModeEnabled = !_isIrrwegCleanupModeEnabled;
            UpdateModeButtonVisuals();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TxtChatInput.Text))
            {
                SendDirectIrrwegCleanup();
            }
            else
            {
                _isIrrwegCleanupModeEnabled = !_isIrrwegCleanupModeEnabled;
                UpdateModeButtonVisuals();
            }
        }
    }

    private void BtnIrrwegCleanup_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnIrrwegCleanup_RightTapped)} triggered.");
        SetLongPressTarget("irrweg-cleanup");
        e.Handled = true;
    }

    private void BtnSimplify_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnSimplify_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            _isSimplifyModeEnabled = !_isSimplifyModeEnabled;
            UpdateModeButtonVisuals();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TxtChatInput?.Text))
            {
                SendDirectSimplify();
            }
            else
            {
                _isSimplifyModeEnabled = !_isSimplifyModeEnabled;
                UpdateModeButtonVisuals();
            }
        }
    }

    private void BtnSimplify_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnSimplify_RightTapped)} triggered.");
        SetLongPressTarget("simplify");
        e.Handled = true;
    }

    private void BtnZeroBug_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnZeroBug_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            _isZeroBugModeEnabled = !_isZeroBugModeEnabled;
            UpdateModeButtonVisuals();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TxtChatInput?.Text))
            {
                SendDirectZeroBug();
            }
            else
            {
                _isZeroBugModeEnabled = !_isZeroBugModeEnabled;
                UpdateModeButtonVisuals();
            }
        }
    }

    private void BtnZeroBug_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnZeroBug_RightTapped)} triggered.");
        SetLongPressTarget("zero-bug");
        e.Handled = true;
    }

    private void BtnDauTest_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnDauTest_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            _isDauTestModeEnabled = !_isDauTestModeEnabled;
            UpdateModeButtonVisuals();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TxtChatInput?.Text))
            {
                SendDirectDauTest();
            }
            else
            {
                _isDauTestModeEnabled = !_isDauTestModeEnabled;
                UpdateModeButtonVisuals();
            }
        }
    }

    private void BtnDauTest_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnDauTest_RightTapped)} triggered.");
        SetLongPressTarget("dau-test");
        e.Handled = true;
    }

    private void BtnProjectCleanup_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnProjectCleanup_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            SendDirectProjectCleanup();
        }
        else
        {
            RecordMaintenanceTimestamp("project_cleanup");
            TxtChatInput.Text = $"[{ProjectCleanupPromptText}]";
            TxtChatInput.SelectionStart = TxtChatInput.Text.Length;
            TxtChatInput.Focus(FocusState.Programmatic);
        }
    }

    private void BtnProjectCleanup_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnProjectCleanup_RightTapped)} triggered.");
        e.Handled = true;

        string currentProj = GetCurrentTargetProjectForMaintenance();
        string singlePrompt = BuildSingleProjectCleanupPrompt(currentProj);

        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            SendDirectSingleProjectCleanup(singlePrompt, currentProj);
        }
        else
        {
            RecordProjectMaintenanceTimestamp(currentProj, "project_cleanup");
            TxtChatInput.Text = $"[{singlePrompt}]";
            TxtChatInput.SelectionStart = TxtChatInput.Text.Length;
            TxtChatInput.Focus(FocusState.Programmatic);
        }
    }



    private void SetLongPressTarget(string targetId)
    {
        if (_longPressTarget == targetId)
        {
            _longPressTarget = string.Empty;
        }
        else
        {
            _longPressTarget = targetId;
        }

        _uiStates.LongPressTarget = _longPressTarget;
        SaveUiStates();
        UpdateModeButtonVisuals();
    }

    private void ResetModeToggles()
    {
        _isDeepThinkEnabled = _isDeepThinkSticky;
        _isProjectDocsEnabled = false;
        _isGlobalErrorCheckEnabled = false;
        _isSubagentModeEnabled = false;
        _isReflectionModeEnabled = false;
        _isWebResearchEnabled = false;
        _isE2ETestModeEnabled = false;
        _isDeadCodeCheckModeEnabled = false;
        _isIrrwegCleanupModeEnabled = false;
        _isSimplifyModeEnabled = false;
        _isZeroBugModeEnabled = false;
        _isDauTestModeEnabled = false;
        _isCustomPromptModeEnabled = false;
        _isProjectCleanupModeEnabled = false;
        _isVoiceRecordingActive = false;
        _isVoicePendingInTerminal = false;
        _voiceStartTime = DateTime.MinValue;
        _voiceStopTime = DateTime.MinValue;
        _lastVoiceCalculatedWaitMs = 0;
        UpdateVoiceButtonState(false);
        UpdateModeButtonVisuals();
    }

    private void ApplyButtonVisual(
        Button? btn,
        Microsoft.UI.Xaml.Shapes.Path? icon,
        bool isActive,
        string targetId,
        Windows.UI.Color bgActive,
        Windows.UI.Color borderActive,
        Windows.UI.Color strokeActive,
        Brush borderLight,
        Brush textSlate300,
        bool transparentInactiveBg = false,
        int inactiveBorderThickness = 1)
    {
        if (btn == null || icon == null) return;
        bool isTarget = (_longPressTarget == targetId);

        if (isActive)
        {
            btn.Background = new SolidColorBrush(bgActive);
            btn.BorderBrush = new SolidColorBrush(borderActive);
            btn.BorderThickness = new Thickness(isTarget ? 2 : 1);
            icon.Stroke = new SolidColorBrush(strokeActive);
        }
        else
        {
            btn.Background = transparentInactiveBg
                ? new SolidColorBrush(Colors.Transparent)
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 42, 52));
            btn.BorderBrush = isTarget ? new SolidColorBrush(strokeActive) : borderLight;
            btn.BorderThickness = new Thickness(isTarget ? 2 : inactiveBorderThickness);
            icon.Stroke = textSlate300;
        }
    }

    private void UpdateModeButtonVisuals()
    {
        if (BtnDeepThink == null || BtnProjectDocs == null || BtnGlobalErrorCheck == null || BtnSubagent == null || BtnReflection == null || BtnWebResearch == null || BtnE2ETest == null || BtnDeadCodeCheck == null || BtnIrrwegCleanup == null || BtnSimplify == null || BtnZeroBug == null || BtnDauTest == null || BtnCustomPrompt == null || BtnProjectCleanup == null) return;

        var borderLight = (Brush)Application.Current.Resources["AgyBorderDefault"];
        var textSlate300 = (Brush)Application.Current.Resources["AgyTextSecondary"];

        // 1. Deep Think (Indigo)
        if (_isDeepThinkSticky)
        {
            BtnDeepThink.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 79, 70, 229));
            BtnDeepThink.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 129, 140, 248));
            BtnDeepThink.BorderThickness = new Thickness(2);
            IconDeepThink.Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 165, 180, 252));
            IconDeepThink.Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 129, 140, 248));
            var dtToolTip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            dtToolTip.Children.Add(new Microsoft.UI.Xaml.Controls.ImageIcon { Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/pin.svg")), Width = 14, Height = 14 });
            dtToolTip.Children.Add(new TextBlock { Text = "Deep Think DAUERHAFT AKTIV (Longpress zum Deaktivieren)", VerticalAlignment = VerticalAlignment.Center });
            ToolTipService.SetToolTip(BtnDeepThink, dtToolTip);
        }
        else
        {
            ApplyButtonVisual(
                BtnDeepThink, IconDeepThink, _isDeepThinkEnabled, "deep-think",
                Windows.UI.Color.FromArgb(51, 79, 70, 229),
                Windows.UI.Color.FromArgb(255, 79, 70, 229),
                Windows.UI.Color.FromArgb(255, 129, 140, 248),
                borderLight, textSlate300, transparentInactiveBg: true);
            IconDeepThink.Fill = new SolidColorBrush(Colors.Transparent);
            ToolTipService.SetToolTip(BtnDeepThink, _isDeepThinkEnabled
                ? "Deep Think (einmalig aktiv | Longpress gedrückt halten: dauerhaft an)"
                : "Deep Think (Numpad 1 | Longpress gedrückt halten: dauerhaft an)");
        }

        // 2. Project Docs (Emerald)
        ApplyButtonVisual(BtnProjectDocs, IconProjectDocs, _isProjectDocsEnabled, "docs",
            Windows.UI.Color.FromArgb(51, 5, 150, 105), Windows.UI.Color.FromArgb(255, 16, 185, 129), Windows.UI.Color.FromArgb(255, 52, 211, 153),
            borderLight, textSlate300, transparentInactiveBg: true);

        // 2b. Global Error Check (Amber / Orange)
        ApplyButtonVisual(BtnGlobalErrorCheck, IconGlobalErrorCheck, _isGlobalErrorCheckEnabled, "global-error-check",
            Windows.UI.Color.FromArgb(51, 217, 119, 6), Windows.UI.Color.FromArgb(255, 245, 158, 11), Windows.UI.Color.FromArgb(255, 251, 191, 36),
            borderLight, textSlate300);

        // 3. Subagent & 5. Web Research (combined support: Magenta)
        bool isSubWebCombined = _isSubagentModeEnabled && _isWebResearchEnabled;

        if (isSubWebCombined)
        {
            ApplyButtonVisual(BtnSubagent, IconSubagent, true, "subagent",
                Windows.UI.Color.FromArgb(51, 236, 72, 153), Windows.UI.Color.FromArgb(255, 236, 72, 153), Windows.UI.Color.FromArgb(255, 244, 114, 182),
                borderLight, textSlate300, transparentInactiveBg: true);
            ApplyButtonVisual(BtnWebResearch, IconWebResearch, true, "web-research",
                Windows.UI.Color.FromArgb(51, 236, 72, 153), Windows.UI.Color.FromArgb(255, 236, 72, 153), Windows.UI.Color.FromArgb(255, 244, 114, 182),
                borderLight, textSlate300, transparentInactiveBg: true);
        }
        else
        {
            // 3. Subagent (Blue)
            ApplyButtonVisual(BtnSubagent, IconSubagent, _isSubagentModeEnabled, "subagent",
                Windows.UI.Color.FromArgb(51, 37, 99, 235), Windows.UI.Color.FromArgb(255, 59, 130, 246), Windows.UI.Color.FromArgb(255, 96, 165, 250),
                borderLight, textSlate300, transparentInactiveBg: true);

            // 5. Web Research (Cyan)
            ApplyButtonVisual(BtnWebResearch, IconWebResearch, _isWebResearchEnabled, "web-research",
                Windows.UI.Color.FromArgb(51, 8, 145, 178), Windows.UI.Color.FromArgb(255, 6, 182, 214), Windows.UI.Color.FromArgb(255, 34, 211, 238),
                borderLight, textSlate300, transparentInactiveBg: true);
        }

        // 4. Reflection (Amber)
        ApplyButtonVisual(BtnReflection, IconReflection, _isReflectionModeEnabled, "reflection",
            Windows.UI.Color.FromArgb(51, 217, 119, 6), Windows.UI.Color.FromArgb(255, 245, 158, 11), Windows.UI.Color.FromArgb(255, 251, 191, 36),
            borderLight, textSlate300);

        // 7. E2E Test (Emerald)
        ApplyButtonVisual(BtnE2ETest, IconE2ETest, _isE2ETestModeEnabled, "e2e-test",
            Windows.UI.Color.FromArgb(51, 16, 185, 129), Windows.UI.Color.FromArgb(255, 16, 185, 129), Windows.UI.Color.FromArgb(255, 52, 211, 153),
            borderLight, textSlate300);

        // 8. Dead Code Check (Rose)
        ApplyButtonVisual(BtnDeadCodeCheck, IconDeadCodeCheck, _isDeadCodeCheckModeEnabled, "dead-code-check",
            Windows.UI.Color.FromArgb(51, 244, 63, 94), Windows.UI.Color.FromArgb(255, 244, 63, 94), Windows.UI.Color.FromArgb(255, 251, 113, 133),
            borderLight, textSlate300);

        // 9. Irrweg Cleanup (Amber / Orange)
        ApplyButtonVisual(BtnIrrwegCleanup, IconIrrwegCleanup, _isIrrwegCleanupModeEnabled, "irrweg-cleanup",
            Windows.UI.Color.FromArgb(51, 245, 158, 11), Windows.UI.Color.FromArgb(255, 245, 158, 11), Windows.UI.Color.FromArgb(255, 251, 191, 36),
            borderLight, textSlate300);

        // 9b. Simplify / Overengineering Check (Violet / Purple)
        ApplyButtonVisual(BtnSimplify, IconSimplify, _isSimplifyModeEnabled, "simplify",
            Windows.UI.Color.FromArgb(51, 168, 85, 247), Windows.UI.Color.FromArgb(255, 168, 85, 247), Windows.UI.Color.FromArgb(255, 192, 132, 252),
            borderLight, textSlate300);

        // 9c. Zero-Bug-Audit / Reliability Exterminator (Crimson / Red)
        ApplyButtonVisual(BtnZeroBug, IconZeroBug, _isZeroBugModeEnabled, "zero-bug",
            Windows.UI.Color.FromArgb(51, 239, 68, 68), Windows.UI.Color.FromArgb(255, 239, 68, 68), Windows.UI.Color.FromArgb(255, 248, 113, 113),
            borderLight, textSlate300);

        // 9d. DAU-Simulationstest / Real-User Chaos-Auditing (Banana Yellow / Gold)
        ApplyButtonVisual(BtnDauTest, IconDauTest, _isDauTestModeEnabled, "dau-test",
            Windows.UI.Color.FromArgb(51, 234, 179, 8), Windows.UI.Color.FromArgb(255, 234, 179, 8), Windows.UI.Color.FromArgb(255, 250, 204, 21),
            borderLight, textSlate300);

        // 10. Custom Prompt (Dynamic 1. Prompt Color)
        if (BtnCustomPrompt != null && IconCustomPrompt != null)
        {
            var firstItem = GetFirstCustomPrompt();
            bool hasValidPrompt = (firstItem != null && !string.IsNullOrWhiteSpace(firstItem.Prompt));
            var activeBrush = hasValidPrompt ? HexToBrush(firstItem!.Color) : textSlate300;
            var activeBgColor = hasValidPrompt ? HexToColor(firstItem!.Color, 51) : Windows.UI.Color.FromArgb(51, 139, 92, 246);

            if (_isCustomPromptModeEnabled)
            {
                BtnCustomPrompt.Background = new SolidColorBrush(activeBgColor);
                BtnCustomPrompt.BorderBrush = activeBrush;
                BtnCustomPrompt.BorderThickness = new Thickness(1);
                IconCustomPrompt.Stroke = activeBrush;
                IconCustomPrompt.Fill = new SolidColorBrush(hasValidPrompt ? HexToColor(firstItem!.Color, 60) : Windows.UI.Color.FromArgb(60, 167, 139, 250));
            }
            else
            {
                BtnCustomPrompt.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 38, 42, 52));
                BtnCustomPrompt.BorderBrush = hasValidPrompt ? activeBrush : borderLight;
                BtnCustomPrompt.BorderThickness = new Thickness(hasValidPrompt ? 1.5 : 1);
                IconCustomPrompt.Stroke = hasValidPrompt ? activeBrush : textSlate300;
                IconCustomPrompt.Fill = new SolidColorBrush(Colors.Transparent);
            }
        }

        // 11. Projekt Cleanup & Audit (Cyan/Teal - Sidebar Footer Button)
        if (BtnProjectCleanupSidebar != null && IconProjectCleanupSidebar != null)
        {
            ApplyButtonVisual(BtnProjectCleanupSidebar, IconProjectCleanupSidebar, _isProjectCleanupModeEnabled, "project-cleanup",
                Windows.UI.Color.FromArgb(51, 6, 182, 212), Windows.UI.Color.FromArgb(255, 6, 182, 212), Windows.UI.Color.FromArgb(255, 34, 211, 238),
                borderLight, textSlate300, transparentInactiveBg: true, inactiveBorderThickness: 0);
        }
    }

    private bool _isPulseActive = false;

    private void TxtChatInput_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeTokenPeakMeterFlyout();
        RecalculateStagedAttachmentTokens();
        UpdateChatInputPulse();
        UpdateTokenPeakMeter(false);
    }

    private void TxtChatInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = TxtChatInput?.Text ?? string.Empty;
        if (text.Length > 0)
        {
            _isVoicePendingInTerminal = false;
        }
        UpdateChatInputPulse(string.IsNullOrWhiteSpace(text));
        UpdateTokenPeakMeter(false);
    }

    private void RecalculateStagedAttachmentTokens()
    {
        int total = 0;
        foreach (var att in _stagedAttachments)
        {
            if (string.IsNullOrEmpty(att.FilePath)) continue;
            try
            {
                string ext = Path.GetExtension(att.FilePath).ToLowerInvariant();
                if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif")
                {
                    total += 300;
                }
                else if (File.Exists(att.FilePath))
                {
                    var fi = new FileInfo(att.FilePath);
                    if (fi.Length > 0 && fi.Length < 2_000_000)
                    {
                        total += (int)Math.Ceiling(fi.Length / 3.6);
                    }
                    else if (fi.Length >= 2_000_000)
                    {
                        total += 2000;
                    }
                }
                else
                {
                    total += 150;
                }
            }
            catch
            {
                total += 150;
            }
        }
        _cachedStagedAttachmentTokens = total;
    }

    public void UpdateTokenPeakMeter(bool isSending = false)
    {
        try
        {
            if (TokenPeakMeterContainer == null || TokenPeakMeterActiveBar == null || TokenPeakMeterBarBorder == null || TokenPeakHoldMarker == null) return;

            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
            if (activeSession != null && string.Equals(activeSession.AgentType, "clawcode", StringComparison.OrdinalIgnoreCase))
            {
                TokenPeakMeterContainer.Visibility = Visibility.Collapsed;
                return;
            }
            TokenPeakMeterContainer.Visibility = Visibility.Visible;

            // Kalibrierte Baseline aus Antigravity CLI /context TUI:
            // System prompt (6.2k) + System tools (13.7k) + Skills (0.8k) + Subagents (0.7k) = ~21.4k Tokens
            const int SystemBaseTokens = 21400;
            
            // 1. Obtain History Tokens from In-Memory Cache (0ms latency, zero blocking I/O on UI thread)
            int historyTokens = 0;
            bool isExactServerUsage = false;
            int cachedUserTokens = 0;
            int cachedAgentTokens = 0;
            int cachedToolTokens = 0;
            if (activeSession != null && !string.IsNullOrEmpty(activeSession.ConversationId))
            {
                string brainPath = ResolveActiveTranscriptPath(activeSession.ConversationId);
                if (_transcriptTokensCache.TryGetValue(brainPath, out var cached))
                {
                    historyTokens = cached.Tokens;
                    isExactServerUsage = cached.IsExactServerUsage;
                    cachedUserTokens = cached.UserTokens;
                    cachedAgentTokens = cached.AgentTokens;
                    cachedToolTokens = cached.ToolTokens;
                }
                else if (File.Exists(brainPath))
                {
                    // Asynchronously load history tokens in background without blocking UI thread
                    _ = RefreshTranscriptTokensAsync(brainPath);
                }
            }

            // 2. Calculate Current Prompt Tokens
            string inputText = TxtChatInput?.Text ?? "";
            var sbPrompt = new StringBuilder(inputText);
            if (_isDeepThinkEnabled) sbPrompt.Append(" [Führe einen Deep Think durch.]");
            if (_isSubagentModeEnabled) sbPrompt.Append(" [Nutze maximal viele Subagents für diese Aufgabe...]");
            if (_isWebResearchEnabled) sbPrompt.Append(" [Recherchiere zu diesem Thema im Internet...]");
            if (_isReflectionModeEnabled) sbPrompt.Append(" [Prüfe deine letzte Arbeit adaptiv nach Risiko und Aufwand...]");
            if (_isProjectDocsEnabled) sbPrompt.Append(" [Nutze für diese Aufgabe dein Projektwissen...]");
            if (_isGlobalErrorCheckEnabled) sbPrompt.Append(" [Überprüfe, ob der Fehler auch noch woanders im Projekt auftritt.]");
            if (_isE2ETestModeEnabled) sbPrompt.Append(" ").Append(E2ETestPromptText);
            if (_isDeadCodeCheckModeEnabled) sbPrompt.Append(" ").Append(DeadCodeCheckPromptText);
            if (_isIrrwegCleanupModeEnabled) sbPrompt.Append(" ").Append(IrrwegCleanupPromptText);
            if (_isSimplifyModeEnabled) sbPrompt.Append(" ").Append(SimplifyPromptText);
            if (_isZeroBugModeEnabled) sbPrompt.Append(" ").Append(ZeroBugPromptText);
            if (_isDauTestModeEnabled) sbPrompt.Append(" ").Append(DauTestPromptText);
            if (_isProjectCleanupModeEnabled) sbPrompt.Append("\n\n[").Append(ProjectCleanupPromptText.Trim()).Append("]");
            if (_isCustomPromptModeEnabled)
            {
                string cPrompt = GetCustomPromptForCurrentProject();
                if (!string.IsNullOrWhiteSpace(cPrompt)) sbPrompt.Append(" ").Append(cPrompt);
            }

            if (_stagedAttachments.Count == 1)
            {
                sbPrompt.Append(" [Der Benutzer bezieht sich auf die angehängte Datei: \"").Append(_stagedAttachments[0].FilePath).Append("\"]");
            }
            else if (_stagedAttachments.Count > 1)
            {
                sbPrompt.Append(" [Der Benutzer bezieht sich auf folgende Dateien: ");
                for (int i = 0; i < _stagedAttachments.Count; i++)
                {
                    if (i > 0) sbPrompt.Append(", ");
                    sbPrompt.Append('"').Append(_stagedAttachments[i].FilePath).Append('"');
                }
                sbPrompt.Append(']');
            }

            int promptTextTokens = string.IsNullOrWhiteSpace(sbPrompt.ToString()) ? 0 : (int)Math.Ceiling(sbPrompt.Length / 3.6);
            int promptTokens = promptTextTokens + _cachedStagedAttachmentTokens;
            
            // If exact server usage was returned, historyTokens already contains the base system prompt & tool declarations
            int totalTokens = (isExactServerUsage && historyTokens > 0)
                ? historyTokens + promptTokens
                : SystemBaseTokens + historyTokens + promptTokens;

            // 3. Resolve Dynamic Model Specifications
            var (modelName, maxTokens, greenThreshold, yellowThreshold) = GetActiveModelContextSpecs();
            if (maxTokens <= 0) maxTokens = 1048576.0;
            double rawRatio = totalTokens / maxTokens;
            double ratio = (double.IsNaN(rawRatio) || double.IsInfinity(rawRatio)) ? 0.0 : Math.Clamp(rawRatio, 0.0, 1.0);
            
            double containerHeight = TokenPeakMeterContainer.ActualHeight;
            if (containerHeight <= 0) containerHeight = 600;

            double targetHeight = Math.Max(4, ratio * containerHeight);
            if (double.IsNaN(targetHeight) || double.IsInfinity(targetHeight)) targetHeight = 4;
            TokenPeakMeterActiveBar.Height = targetHeight;

            // 4. Dynamic LED Color Coding based on model-specific thresholds (using dedicated static brushes to avoid shared resource mutation)
            SolidColorBrush targetFgBrush;
            SolidColorBrush targetBadgeBgBrush;

            if (totalTokens <= greenThreshold)
            {
                targetFgBrush = HealthGreenBrush;
                targetBadgeBgBrush = TokenGreenBadgeBgBrush;
            }
            else if (totalTokens <= yellowThreshold)
            {
                targetFgBrush = HealthAmberBrush;
                targetBadgeBgBrush = TokenAmberBadgeBgBrush;
            }
            else
            {
                targetFgBrush = HealthRedBrush;
                targetBadgeBgBrush = TokenRedBadgeBgBrush;
            }

            // Restore global AgyStatusSuccess if it was previously mutated by legacy code
            if (Application.Current.Resources.TryGetValue("AgyStatusSuccess", out var sharedSuccessObj)
                && sharedSuccessObj is SolidColorBrush sharedSuccessBrush
                && (sharedSuccessBrush.Color.R != 16 || sharedSuccessBrush.Color.G != 185 || sharedSuccessBrush.Color.B != 129))
            {
                sharedSuccessBrush.Color = Windows.UI.Color.FromArgb(255, 16, 185, 129);
            }

            if (TokenPeakMeterBarBorder != null && TokenPeakMeterBarBorder.Background != targetFgBrush)
            {
                TokenPeakMeterBarBorder.Background = targetFgBrush;
            }

            // 5. Peak-Hold Behavior (Vertical)
            if (isSending)
            {
                _peakHoldHeight = Math.Max(_peakHoldHeight, targetHeight);
                TokenPeakHoldMarker.Margin = new Thickness(0, 0, 0, Math.Max(0, _peakHoldHeight - 1));
                TokenPeakHoldMarker.Opacity = 1.0;

                if (_peakDecayTimer == null)
                {
                    _peakDecayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                    _peakDecayTimer.Tick += PeakDecayTimer_Tick;
                }
                _peakDecayTimer.Stop();
                _peakDecayTimer.Start();
            }
            else if (TokenPeakHoldMarker.Opacity > 0 && targetHeight > _peakHoldHeight)
            {
                _peakHoldHeight = targetHeight;
                TokenPeakHoldMarker.Margin = new Thickness(0, 0, 0, Math.Max(0, _peakHoldHeight - 1));
            }

            // 6. Flyout Daten aktualisieren (Kalibriert nach Antigravity CLI /context Metriken)
            if (FlyoutTokenDetails?.IsOpen == true || isSending)
            {
                string statusLevel = totalTokens switch
                {
                    _ when totalTokens <= greenThreshold => $"Optimal (≤ {greenThreshold / 1000:N0}k)",
                    _ when totalTokens <= yellowThreshold => $"Erweitert ({greenThreshold / 1000:N0}k - {yellowThreshold / 1000:N0}k)",
                    _ => $"Hohe Last (> {yellowThreshold / 1000:N0}k)"
                };

                if (TxtTokenFlyoutModelName != null) TxtTokenFlyoutModelName.Text = modelName;
                if (TxtTokenFlyoutStatusLevel != null)
                {
                    TxtTokenFlyoutStatusLevel.Text = statusLevel;
                    if (TxtTokenFlyoutStatusLevel.Foreground != targetFgBrush)
                        TxtTokenFlyoutStatusLevel.Foreground = targetFgBrush;
                }
                if (BdrTokenFlyoutStatusBadge != null)
                {
                    if (BdrTokenFlyoutStatusBadge.Background != targetBadgeBgBrush)
                        BdrTokenFlyoutStatusBadge.Background = targetBadgeBgBrush;
                }
                if (TxtTokenFlyoutUsageNumbers != null) TxtTokenFlyoutUsageNumbers.Text = $"{totalTokens:N0} / {maxTokens:N0} Tokens";
                if (TxtTokenFlyoutRatio != null) TxtTokenFlyoutRatio.Text = $"{ratio:P1}";
                if (PrgTokenFlyoutRatio != null)
                {
                    PrgTokenFlyoutRatio.Value = Math.Clamp(ratio * 100.0, 0.0, 100.0);
                    if (PrgTokenFlyoutRatio.Foreground != targetFgBrush)
                        PrgTokenFlyoutRatio.Foreground = targetFgBrush;
                }

                int baseSys = isExactServerUsage ? 21400 : SystemBaseTokens;
                var sbDetails = new StringBuilder();
                sbDetails.Append("Kategorien (nach Antigravity /context):");
                sbDetails.Append($"\n• System & Tools: ~{baseSys:N0} Tokens (Baseline)");
                if (cachedAgentTokens > 0) sbDetails.Append($"\n• Agent responses: ~{cachedAgentTokens:N0} Tokens");
                if (cachedToolTokens > 0) sbDetails.Append($"\n• Tool calls & Output: ~{cachedToolTokens:N0} Tokens");
                if (cachedUserTokens > 0) sbDetails.Append($"\n• User messages: ~{cachedUserTokens:N0} Tokens");
                if (promptTokens > 0) sbDetails.Append($"\n• Prompt-Entwurf: ~{promptTokens:N0} Tokens");

                double freeSpace = Math.Max(0, maxTokens - totalTokens);
                sbDetails.Append($"\n• Free space: ~{freeSpace:N0} Tokens ({(freeSpace / maxTokens):P1})");

                if (TxtTokenFlyoutDetails != null) TxtTokenFlyoutDetails.Text = sbDetails.ToString();
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"UpdateTokenPeakMeter Error: {ex.Message}");
        }
    }

    private void PeakDecayTimer_Tick(object? sender, object e)
    {
        _peakDecayTimer?.Stop();
        var anim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(600)
        };
        var sb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        sb.Children.Add(anim);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(anim, TokenPeakHoldMarker);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(anim, "Opacity");
        sb.Completed += (s2, e2) => _peakHoldHeight = 0;
        sb.Begin();
    }

    public async Task RefreshTranscriptTokensAsync(string brainPath)
    {
        if (string.IsNullOrEmpty(brainPath) || !File.Exists(brainPath)) return;

        var gate = _transcriptRefreshGates.GetOrAdd(brainPath, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0))
        {
            // Background refresh already in flight for this transcript file, avoid redundant concurrent work
            return;
        }

        try
        {
            bool hasChanged = false;
            await Task.Run(() =>
            {
                try
                {
                    var fi = new FileInfo(brainPath);
                    if (_transcriptTokensCache.TryGetValue(brainPath, out var cached) &&
                        cached.Length == fi.Length &&
                        cached.LastWriteUtc == fi.LastWriteTimeUtc)
                    {
                        return;
                    }

                    var (tokens, isExact, userTokens, agentTokens, toolTokens) = ParseHistoryTokensFromDisk(brainPath);
                    _transcriptTokensCache[brainPath] = (fi.Length, fi.LastWriteTimeUtc, tokens, isExact, userTokens, agentTokens, toolTokens);
                    hasChanged = true;
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[RefreshTranscriptTokensAsync Task.Run ERROR] {ex.Message}");
                }
            });

            if (hasChanged)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
                    if (activeSession != null && !string.IsNullOrEmpty(activeSession.ConversationId))
                    {
                        string activeBrainPath = ResolveActiveTranscriptPath(activeSession.ConversationId);
                        if (string.Equals(activeBrainPath, brainPath, StringComparison.OrdinalIgnoreCase))
                        {
                            UpdateTokenPeakMeter(false);
                        }
                    }
                });
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"RefreshTranscriptTokensAsync Error: {ex.Message}");
        }
        finally
        {
            gate.Release();
        }
    }

    public static string ResolveActiveTranscriptPath(string convId)
    {
        if (string.IsNullOrEmpty(convId)) return string.Empty;
        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain", convId, ".system_generated", "logs");
        string fullPath = Path.Combine(logDir, "transcript_full.jsonl");
        if (File.Exists(fullPath) && new FileInfo(fullPath).Length > 0)
        {
            return fullPath;
        }
        return Path.Combine(logDir, "transcript.jsonl");
    }

    private static List<string> ReadTranscriptLinesWindow(string filePath, long maxTailBytes, out bool wasTailSeeked)
    {
        wasTailSeeked = false;
        var lines = new List<string>(256);
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536);
        if (maxTailBytes > 0 && fs.Length > maxTailBytes)
        {
            fs.Seek(-maxTailBytes, SeekOrigin.End);
            wasTailSeeked = true;
        }
        using var reader = new StreamReader(fs, Encoding.UTF8);
        if (wasTailSeeked)
        {
            // Mögliche angeschnittene erste Zeile nach Seek überspringen
            reader.ReadLine();
        }
        string? l;
        while ((l = reader.ReadLine()) != null)
        {
            if (!string.IsNullOrWhiteSpace(l))
            {
                lines.Add(l);
            }
        }
        return lines;
    }

    private static (int Tokens, bool IsExact, int UserTokens, int AgentTokens, int ToolTokens) ParseHistoryTokensFromDisk(string brainPath)
    {
        try
        {
            // Prefer transcript_full.jsonl if available to avoid undercounting truncated tool contents
            string fileToRead = brainPath;
            if (brainPath.EndsWith("transcript.jsonl", StringComparison.OrdinalIgnoreCase))
            {
                string fullCandidate = brainPath.Substring(0, brainPath.Length - "transcript.jsonl".Length) + "transcript_full.jsonl";
                if (File.Exists(fullCandidate) && new FileInfo(fullCandidate).Length > 0)
                {
                    fileToRead = fullCandidate;
                }
            }

            const long TailSeekWindowBytes = 2 * 1024 * 1024; // 2 MB Tail-Window verhindert 80-MB-Full-Scans
            var lines = ReadTranscriptLinesWindow(fileToRead, TailSeekWindowBytes, out bool wasTailSeeked);
            if (lines.Count == 0)
            {
                return (0, false, 0, 0, 0);
            }

            // 1. Check if the latest turn(s) contain exact API usageMetadata / usage tokens
            int latestUsageIndex = -1;
            int? latestUsageTokens = null;

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                int? usageTokens = ExtractExactUsageTokens(lines[i]);
                if (usageTokens.HasValue && usageTokens.Value > 0)
                {
                    latestUsageIndex = i;
                    latestUsageTokens = usageTokens.Value;
                    break;
                }
            }

            // Fallback / Slice detection: Scan backwards to locate the latest Compaction / Summary checkpoint
            int lastCompactionIndex = -1;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (IsCompactionOrSummaryLine(lines[i]))
                {
                    lastCompactionIndex = i;
                    break;
                }
            }

            // Falls im 2-MB-Tail weder exakte Server-Tokens noch ein Compaction-Checkpoint lagen,
            // einmalig auf Voll-Scan ab Byte 0 zurückfallen, damit die Heuristik vollständig bleibt.
            if (wasTailSeeked && latestUsageIndex < 0 && lastCompactionIndex < 0)
            {
                lines = ReadTranscriptLinesWindow(fileToRead, 0, out _);
                if (lines.Count == 0)
                {
                    return (0, false, 0, 0, 0);
                }

                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    int? usageTokens = ExtractExactUsageTokens(lines[i]);
                    if (usageTokens.HasValue && usageTokens.Value > 0)
                    {
                        latestUsageIndex = i;
                        latestUsageTokens = usageTokens.Value;
                        break;
                    }
                }

                for (int i = lines.Count - 1; i >= 0; i--)
                {
                    if (IsCompactionOrSummaryLine(lines[i]))
                    {
                        lastCompactionIndex = i;
                        break;
                    }
                }
            }

            double accumulatedTokens = 0;
            double userTokensAccum = 0;
            double agentTokensAccum = 0;
            double toolTokensAccum = 0;
            bool isExact = false;

            int startIndex = (lastCompactionIndex >= 0) ? lastCompactionIndex : 0;
            for (int i = startIndex; i < lines.Count; i++)
            {
                var (t, u, a, tl) = CategorizeAndEstimateTokensForJsonLine(lines[i]);
                userTokensAccum += u;
                agentTokensAccum += a;
                toolTokensAccum += tl;

                // Only accumulate heuristic total if no server exact usage was found or if step occurred after server usage
                if (latestUsageIndex < 0 || i > latestUsageIndex)
                {
                    accumulatedTokens += t;
                }
            }

            if (latestUsageIndex >= 0 && latestUsageTokens.HasValue)
            {
                isExact = true;
                accumulatedTokens += latestUsageTokens.Value;
            }

            int computedTokens = (int)Math.Max(0, Math.Ceiling(accumulatedTokens));
            int computedUser = (int)Math.Max(0, Math.Ceiling(userTokensAccum));
            int computedAgent = (int)Math.Max(0, Math.Ceiling(agentTokensAccum));
            int computedTool = (int)Math.Max(0, Math.Ceiling(toolTokensAccum));
            return (computedTokens, isExact, computedUser, computedAgent, computedTool);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"ParseHistoryTokensFromDisk Error: {ex.Message}");
            return (0, false, 0, 0, 0);
        }
    }

    private static int? ExtractExactUsageTokens(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        // Fast string pre-filter: skip expensive JSON parsing if no token-related keywords exist
        if (!line.Contains("usage", StringComparison.OrdinalIgnoreCase) && 
            !line.Contains("tokens", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("totalTokenCount", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            // 0. Native Antigravity CLI step metrics (top-level input_tokens + cache_read_tokens + output_tokens)
            int agyIn = (root.TryGetProperty("input_tokens", out var inTok) && inTok.TryGetInt32(out int inVal)) ? inVal : 0;
            int agyCache = (root.TryGetProperty("cache_read_tokens", out var cTok) && cTok.TryGetInt32(out int cVal)) ? cVal : 0;
            int agyOut = (root.TryGetProperty("output_tokens", out var oTok) && oTok.TryGetInt32(out int oVal)) ? oVal : 0;
            if (agyIn + agyCache > 0)
            {
                return agyIn + agyCache + agyOut;
            }

            // 1. Google Gemini: usageMetadata (camelCase) or usage_metadata (snake_case)
            if (root.TryGetProperty("usageMetadata", out var um) || root.TryGetProperty("usage_metadata", out um))
            {
                if (um.ValueKind == JsonValueKind.Object)
                {
                    if (um.TryGetProperty("totalTokenCount", out var tt) && tt.TryGetInt32(out int total)) return total;
                    if (um.TryGetProperty("total_token_count", out var tt2) && tt2.TryGetInt32(out int total2)) return total2;
                    if (um.TryGetProperty("promptTokenCount", out var pt) && pt.TryGetInt32(out int prompt)) return prompt;
                    if (um.TryGetProperty("prompt_token_count", out var pt2) && pt2.TryGetInt32(out int prompt2)) return prompt2;
                }
            }

            // 2. OpenAI / OpenRouter / Claude: usage
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("total_tokens", out var tt) && tt.TryGetInt32(out int total)) return total;
                if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out int prompt))
                {
                    int comp = (usage.TryGetProperty("completion_tokens", out var ct) && ct.TryGetInt32(out int c)) ? c : 0;
                    return prompt + comp;
                }
                if (usage.TryGetProperty("input_tokens", out var it) && it.TryGetInt32(out int input))
                {
                    int output = (usage.TryGetProperty("output_tokens", out var ot) && ot.TryGetInt32(out int o)) ? o : 0;
                    return input + output;
                }
            }

            // 3. Generic tokens object / field
            if (root.TryGetProperty("tokens", out var tokens))
            {
                if (tokens.ValueKind == JsonValueKind.Number && tokens.TryGetInt32(out int directCount)) return directCount;
                if (tokens.ValueKind == JsonValueKind.Object)
                {
                    if (tokens.TryGetProperty("total", out var tt) && tt.TryGetInt32(out int total)) return total;
                    if (tokens.TryGetProperty("input", out var inp) && inp.TryGetInt32(out int i))
                    {
                        int outp = (tokens.TryGetProperty("output", out var o) && o.TryGetInt32(out int outVal)) ? outVal : 0;
                        return i + outp;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static bool IsCompactionOrSummaryLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        // 1. Direct /compact slash command in transcript
        if (line.Contains("/compact", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Skip normal user inputs
        if (line.Contains("\"type\":\"USER_INPUT\"", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 3. Explicit JSON type markers for compactions
        if (line.Contains("\"type\":\"SUMMARY\"", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("\"type\":\"COMPACT\"", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("\"type\":\"COMPACTION\"", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("\"type\":\"CONTEXT_SUMMARY\"", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("\"type\":\"CONTEXT_RESET\"", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 4. CHECKPOINT: In AGY, CHECKPOINT is used for git/state snapshots AND for resumes from compactions.
        // We must ONLY treat it as a compaction boundary if it explicitly carries compaction/truncation content!
        if (line.Contains("\"type\":\"CHECKPOINT\"", StringComparison.OrdinalIgnoreCase))
        {
            return line.Contains("Resuming from a compaction", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("lost access to the full conversation history", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("<summary>", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("<context_summary>", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("<!-- context_compacted -->", StringComparison.OrdinalIgnoreCase);
        }

        // 5. Context summary markers in content (exclude bare HTML5 <summary> tags to avoid false positives in web code)
        if (line.Contains("<context_summary>", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("<conversation_summary>", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Auto-Compacted", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Context compacted", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Conversation compacted", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Context truncated", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Chat komprimiert", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("Chatverlauf komprimiert", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("<!-- context_compacted -->", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static (double Total, double User, double Agent, double Tool) CategorizeAndEstimateTokensForJsonLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return (0, 0, 0, 0);

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            string source = root.TryGetProperty("source", out var sProp) ? (sProp.GetString() ?? "") : "";
            string type = root.TryGetProperty("type", out var tProp) ? (tProp.GetString() ?? "") : "";

            double user = 0;
            double agent = 0;
            double tool = 0;

            if (root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
            {
                string text = contentProp.GetString() ?? "";
                if (!string.IsNullOrEmpty(text))
                {
                    double cTokens = text.Length / 3.6;

                    // If content was truncated in transcript.jsonl (e.g. "<truncated 1234 bytes>"), add the truncated byte volume
                    if (text.Contains("<truncated ", StringComparison.OrdinalIgnoreCase))
                    {
                        var truncMatch = System.Text.RegularExpressions.Regex.Match(text, @"<truncated\s+(\d+)\s+bytes>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (truncMatch.Success && int.TryParse(truncMatch.Groups[1].Value, out int truncatedBytes))
                        {
                            cTokens += truncatedBytes / 3.6;
                        }
                    }

                    if (string.Equals(source, "USER_EXPLICIT", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(source, "USER", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(type, "USER_INPUT", StringComparison.OrdinalIgnoreCase))
                    {
                        user += cTokens;
                    }
                    else if (string.Equals(source, "MODEL", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(type, "PLANNER_RESPONSE", StringComparison.OrdinalIgnoreCase))
                    {
                        agent += cTokens;
                    }
                    else
                    {
                        tool += cTokens;
                    }
                }
            }

            if (root.TryGetProperty("thinking", out var thinkingProp) && thinkingProp.ValueKind == JsonValueKind.String)
            {
                string thinkingText = thinkingProp.GetString() ?? "";
                if (!string.IsNullOrEmpty(thinkingText))
                {
                    agent += thinkingText.Length / 3.6;
                }
            }

            if (root.TryGetProperty("tool_calls", out var toolCallsProp) && toolCallsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in toolCallsProp.EnumerateArray())
                {
                    tool += 45; // Overhead per tool call
                    if (tc.TryGetProperty("arguments", out var argsProp) || tc.TryGetProperty("args", out argsProp) || tc.TryGetProperty("parameters", out argsProp))
                    {
                        string rawArgs = argsProp.GetRawText();
                        if (!string.IsNullOrEmpty(rawArgs))
                        {
                            tool += rawArgs.Length / 3.6;
                        }
                    }
                    else
                    {
                        string rawTc = tc.GetRawText();
                        if (!string.IsNullOrEmpty(rawTc) && rawTc.Length > 50)
                        {
                            tool += rawTc.Length / 3.6;
                        }
                    }
                }
            }

            double total = 35 + user + agent + tool; // 35 envelope overhead per message step
            return (total, user, agent, tool);
        }
        catch
        {
            // Fallback estimation for non-standard or truncated JSON line
            double fallback = line.Length / 9.0;
            return (fallback, 0, fallback, 0);
        }
    }

    private (string DisplayName, double MaxTokens, double GreenThreshold, double YellowThreshold) GetActiveModelContextSpecs()
    {
        string model = "";
        var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
        if (activeSession != null && !string.IsNullOrEmpty(activeSession.ConversationId))
        {
            model = LoadChatModel(activeSession.ConversationId);
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            model = (CurrentModelId ?? "").Trim();
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            model = LoadAgyModelFromSettings();
        }
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gemini-4-argon-high";
        }

        string lower = model.ToLowerInvariant();
        string norm = lower.Replace("-", " ").Replace("_", " ");
        string reasoningSuffix = "";
        if (lower.EndsWith("-high") || lower.Contains("-high") || lower.Contains(" (high)") || lower.EndsWith(" high")) reasoningSuffix = " (High)";
        else if (lower.EndsWith("-medium") || lower.Contains("-medium") || lower.Contains(" (medium)") || lower.EndsWith(" medium")) reasoningSuffix = " (Medium)";
        else if (lower.EndsWith("-low") || lower.Contains("-low") || lower.Contains(" (low)") || lower.EndsWith(" low")) reasoningSuffix = " (Low)";
        else if (lower.Contains("thinking") || lower.Contains("-thinking")) reasoningSuffix = " (Thinking)";

        // 1. Gemini 4 Argon (Aktuelles Frontier Flagship) -> 1.048.576 Tokens (1M Input & 1M Long Decode Output)
        // Kalibriert auf Long-Horizon-Stabilität von Argon: Grün bis 150k, Gelb bis 400k, Rot ab 400k
        if (norm.Contains("gemini 4") || norm.Contains("argon"))
        {
            return ($"Gemini 4 Argon{reasoningSuffix}", 1048576.0, 150000.0, 400000.0);
        }

        // 2. Gemini 3.8 Flash (Aktuelles Flagship Flash) -> 1.048.576 Tokens (1M)
        // Kalibriert auf reale Google Server-Stabilität: Grün bis 80k, Gelb bis 250k, Rot ab 250k (503 & Timeout-Gefahr)
        if (norm.Contains("gemini 3.8") || norm.Contains("3.8 flash"))
        {
            return ($"Gemini 3.8 Flash{reasoningSuffix}", 1048576.0, 80000.0, 250000.0);
        }

        // 2b. Dynamic lookup from OpenRouter cache if available (including normalized CLI base keys)
        string baseKey = NormalizeModelBaseKey(model);
        var orMatch = _cachedOpenRouterModels?.FirstOrDefault(m =>
            string.Equals(m.Id, model, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.Name, model, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(m.DisplayName, model, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(m.Id) && model.StartsWith(m.Id, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(m.Name) && model.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(baseKey) && string.Equals(NormalizeModelBaseKey(m.Id), baseKey, StringComparison.OrdinalIgnoreCase)));
        if (orMatch != null && orMatch.ContextLength > 0)
        {
            double max = orMatch.ContextLength;
            string disp = orMatch.DisplayName;
            if (!string.IsNullOrEmpty(reasoningSuffix) && !disp.Contains(reasoningSuffix, StringComparison.OrdinalIgnoreCase))
            {
                disp += reasoningSuffix;
            }
            return (disp, max, max * 0.15, max * 0.50);
        }

        // 3. Gemini 3.7 Flash -> 1.048.576 Tokens (1M)
        if (norm.Contains("gemini 3.7") || norm.Contains("3.7 flash"))
        {
            return ($"Gemini 3.7 Flash{reasoningSuffix}", 1048576.0, 80000.0, 250000.0);
        }

        // 4. Gemini 3.6 Flash -> 1.048.576 Tokens (1M)
        if (norm.Contains("gemini 3.6") || norm.Contains("3.6 flash"))
        {
            return ($"Gemini 3.6 Flash{reasoningSuffix}", 1048576.0, 70000.0, 200000.0);
        }

        // 5. Gemini 3.1 Pro -> 1.048.576 Tokens (1M)
        if (norm.Contains("gemini 3.1 pro") || (norm.Contains("3.1") && norm.Contains("pro")))
        {
            return ($"Gemini 3.1 Pro{reasoningSuffix}", 1048576.0, 120000.0, 350000.0);
        }

        // 6. Legacy Gemini Pro Models (Gemini 2.5 Pro, 1.5 Pro) -> 2.000.000 Tokens (2M)
        if (norm.Contains("gemini 2.5 pro") || norm.Contains("gemini 1.5 pro") || (norm.Contains("gemini") && norm.Contains("pro")))
        {
            string proName = "Gemini Pro";
            if (norm.Contains("2.5")) proName = "Gemini 2.5 Pro";
            else if (norm.Contains("1.5")) proName = "Gemini 1.5 Pro";
            return ($"{proName}{reasoningSuffix}", 2000000.0, 150000.0, 500000.0);
        }

        // 7. Claude 5.x & 4.6+ Models (Opus 5.5, Sonnet 5.5, Fable 5.1, Opus 4.8/4.7/4.6, Sonnet 4.6) -> 1.000.000 Tokens (1M Extended Context)
        if (norm.Contains("claude") && (norm.Contains("5.5") || norm.Contains("5.1") || norm.Contains("5 5") || norm.Contains("5 1") || norm.Contains("opus 5") || norm.Contains("sonnet 5") || norm.Contains("fable 5") ||
            norm.Contains("4.8") || norm.Contains("4.7") || norm.Contains("4.6") || norm.Contains("4 8") || norm.Contains("4 7") || norm.Contains("4 6")))
        {
            string claudeName = "Claude";
            if (norm.Contains("opus 5") || (norm.Contains("5.5") && norm.Contains("opus"))) claudeName = "Claude Opus 5.5";
            else if (norm.Contains("sonnet 5") || (norm.Contains("5.5") && norm.Contains("sonnet"))) claudeName = "Claude Sonnet 5.5";
            else if (norm.Contains("fable")) claudeName = "Claude Fable 5.1";
            else if (norm.Contains("opus 4.6") || (norm.Contains("4.6") && norm.Contains("opus")) || (norm.Contains("4 6") && norm.Contains("opus"))) claudeName = "Claude Opus 4.6";
            else if (norm.Contains("sonnet 4.6") || (norm.Contains("4.6") && norm.Contains("sonnet")) || (norm.Contains("4 6") && norm.Contains("sonnet"))) claudeName = "Claude Sonnet 4.6";
            else if (norm.Contains("opus")) claudeName = "Claude Opus (Extended)";
            else if (norm.Contains("sonnet")) claudeName = "Claude Sonnet (Extended)";
            return ($"{claudeName}{reasoningSuffix}", 1000000.0, 150000.0, 450000.0);
        }

        // 8. Claude 3.7 Sonnet -> 200.000 Tokens
        if (norm.Contains("claude 3.7") || norm.Contains("3.7 sonnet") || (norm.Contains("claude") && norm.Contains("3 7")))
        {
            return ($"Claude 3.7 Sonnet{reasoningSuffix}", 200000.0, 64000.0, 160000.0);
        }

        // 9. Standard Claude Models (3.5 Sonnet / Haiku / Opus legacy) -> 200.000 Tokens (200k)
        if (norm.Contains("claude"))
        {
            string claudeName = "Claude";
            if (norm.Contains("3.5") || norm.Contains("3 5")) claudeName = "Claude 3.5 Sonnet";
            else if (norm.Contains("haiku")) claudeName = "Claude 3.5 Haiku";
            return ($"{claudeName}{reasoningSuffix}", 200000.0, 40000.0, 140000.0);
        }

        // 10. GPT-OSS 120B -> 131.072 Tokens (128k)
        if (norm.Contains("gpt oss") || norm.Contains("120b"))
        {
            return ($"GPT-OSS 120B{reasoningSuffix}", 131072.0, 32768.0, 98304.0);
        }

        // 11. OpenAI GPT-4o / GPT-4.5 / o1 / o3 -> 128.000 Tokens
        if (norm.Contains("gpt 4o") || norm.Contains("gpt 4") || norm.Contains("o1") || norm.Contains("o3"))
        {
            string gptName = "GPT-4o";
            if (norm.Contains("o1")) gptName = "OpenAI o1";
            else if (norm.Contains("o3")) gptName = "OpenAI o3";
            else if (norm.Contains("4.5")) gptName = "GPT-4.5";
            return ($"{gptName}{reasoningSuffix}", 128000.0, 32000.0, 90000.0);
        }

        // 12. DeepSeek V3 / R1 -> 128.000 Tokens
        if (norm.Contains("deepseek"))
        {
            string dsName = norm.Contains("r1") ? "DeepSeek R1" : "DeepSeek V3";
            return ($"{dsName}{reasoningSuffix}", 128000.0, 32000.0, 90000.0);
        }

        // 13. Gemini Flash Family Fallback (Gemini 3.5 Flash, 2.5 Flash, 1.5 Flash) -> 1.048.576 Tokens (1M)
        string displayName = "Gemini Flash";
        if (norm.Contains("3.5")) displayName = "Gemini 3.5 Flash";
        else if (norm.Contains("2.5")) displayName = "Gemini 2.5 Flash";
        else if (norm.Contains("1.5")) displayName = "Gemini 1.5 Flash";

        return ($"{displayName}{reasoningSuffix}", 1048576.0, 128000.0, 500000.0);
    }

    private void TokenPeakMeterContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateTokenPeakMeter(false);
    }

    private DispatcherTimer? _tokenMeterHoverTimer;

    private void TokenPeakMeterContainer_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (TokenPeakMeterActiveBar != null)
            {
                TokenPeakMeterActiveBar.Opacity = 1.0;
                TokenPeakMeterActiveBar.Width = 6;
            }
            if (TokenPeakMeterTrackBorder != null)
            {
                TokenPeakMeterTrackBorder.Width = 6;
            }
            if (TokenPeakHoldMarker != null)
            {
                TokenPeakHoldMarker.Width = 8;
            }

            // Entprell-Timer für stabiles Hover (200ms) ohne Zeitbegrenzung lazily wiederverwenden
            if (_tokenMeterHoverTimer == null)
            {
                _tokenMeterHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _tokenMeterHoverTimer.Tick += (s, args) =>
                {
                    _tokenMeterHoverTimer?.Stop();
                    ShowTokenFlyout(isHover: true);
                };
            }
            _tokenMeterHoverTimer.Stop();
            _tokenMeterHoverTimer.Start();
        }
        catch { }
    }

    private void TokenPeakMeterContainer_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            _tokenMeterHoverTimer?.Stop();

            if (TokenPeakMeterActiveBar != null)
            {
                TokenPeakMeterActiveBar.Opacity = 0.55;
                TokenPeakMeterActiveBar.Width = 4;
            }
            if (TokenPeakMeterTrackBorder != null)
            {
                TokenPeakMeterTrackBorder.Width = 4;
            }
            if (TokenPeakHoldMarker != null)
            {
                TokenPeakHoldMarker.Width = 6;
            }
        }
        catch { }
    }

    private void TokenPeakMeterContainer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        _tokenMeterHoverTimer?.Stop();
        ShowTokenFlyout(isHover: false);
    }

    private void ShowTokenFlyout(bool isHover)
    {
        try
        {
            if (FlyoutTokenDetails == null || TokenPeakMeterContainer == null) return;
            if (FlyoutTokenDetails.IsOpen && isHover) return;
            if (TokenPeakMeterContainer.ContextFlyout?.IsOpen == true) return;

            UpdateTokenPeakMeter(false);

            FlyoutTokenDetails.ShouldConstrainToRootBounds = false;

            var options = new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.LeftEdgeAlignedBottom,
                ShowMode = isHover ? FlyoutShowMode.TransientWithDismissOnPointerMoveAway : FlyoutShowMode.Standard
            };
            FlyoutTokenDetails.ShowAt(TokenPeakMeterContainer, options);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"ShowTokenFlyout Error: {ex.Message}");
        }
    }

    private void BtnTokenFlyoutContext_Click(object sender, RoutedEventArgs e)
    {
        FlyoutTokenDetails?.Hide();
        if (ActiveTerminal?.ConPTYTerm != null)
        {
            ActiveTerminal.ConPTYTerm.WriteToTerm("/context\r");
            ActiveTerminal.Focus(FocusState.Programmatic);
        }
    }

    private void BtnTokenFlyoutCompact_Click(object sender, RoutedEventArgs e)
    {
        FlyoutTokenDetails?.Hide();
        if (ActiveTerminal?.ConPTYTerm != null)
        {
            ActiveTerminal.ConPTYTerm.WriteToTerm("/compact\r");
            ActiveTerminal.Focus(FocusState.Programmatic);
        }
    }

    private async void BtnTokenFlyoutRefresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
            if (activeSession != null && !string.IsNullOrEmpty(activeSession.ConversationId))
            {
                string brainPath = ResolveActiveTranscriptPath(activeSession.ConversationId);
                _transcriptTokensCache.TryRemove(brainPath, out _);
                await RefreshTranscriptTokensAsync(brainPath);
                UpdateTokenPeakMeter(false);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnTokenFlyoutRefresh_Click Error: {ex.Message}");
        }
    }

    private void InitializeTokenPeakMeterFlyout()
    {
        if (TokenPeakMeterContainer == null || TokenPeakMeterContainer.ContextFlyout != null) return;

        var flyout = new MenuFlyout();

        var itemContext = new MenuFlyoutItem
        {
            Text = "/context im Terminal öffnen (TUI-Inspektion)",
            Icon = new FontIcon { Glyph = "\uE9D9" }
        };
        itemContext.Click += (s, e) =>
        {
            if (ActiveTerminal?.ConPTYTerm != null)
            {
                ActiveTerminal.ConPTYTerm.WriteToTerm("/context\r");
                ActiveTerminal.Focus(FocusState.Programmatic);
            }
        };

        var itemCompact = new MenuFlyoutItem
        {
            Text = "/compact im Terminal ausführen (Kontext komprimieren)",
            Icon = new FontIcon { Glyph = "\uE749" }
        };
        itemCompact.Click += (s, e) =>
        {
            if (ActiveTerminal?.ConPTYTerm != null)
            {
                ActiveTerminal.ConPTYTerm.WriteToTerm("/compact\r");
                ActiveTerminal.Focus(FocusState.Programmatic);
            }
        };

        var itemRefresh = new MenuFlyoutItem
        {
            Text = "Token-Zähler neu einlesen",
            Icon = new FontIcon { Glyph = "\uE72C" }
        };
        itemRefresh.Click += async (s, e) =>
        {
            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
            if (activeSession != null && !string.IsNullOrEmpty(activeSession.ConversationId))
            {
                string brainPath = ResolveActiveTranscriptPath(activeSession.ConversationId);
                _transcriptTokensCache.TryRemove(brainPath, out _);
                await RefreshTranscriptTokensAsync(brainPath);
            }
        };

        flyout.Items.Add(itemContext);
        flyout.Items.Add(itemCompact);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(itemRefresh);

        TokenPeakMeterContainer.ContextFlyout = flyout;
    }

    private void UpdateChatInputPulse(bool? isEmptyOverride = null)
    {
        bool isEmpty = isEmptyOverride ?? string.IsNullOrWhiteSpace(TxtChatInput?.Text);
        if (isEmpty)
        {
            if (!_isPulseActive)
            {
                if (ChatInputPulseBorder.Resources.TryGetValue("ChatInputPulseStoryboard", out object storyboardObj) && storyboardObj is Microsoft.UI.Xaml.Media.Animation.Storyboard storyboard)
                {
                    ChatInputPulseBorder.Visibility = Visibility.Visible;
                    storyboard.Begin();
                    _isPulseActive = true;
                }
            }
        }
        else
        {
            if (_isPulseActive)
            {
                if (ChatInputPulseBorder.Resources.TryGetValue("ChatInputPulseStoryboard", out object storyboardObj) && storyboardObj is Microsoft.UI.Xaml.Media.Animation.Storyboard storyboard)
                {
                    storyboard.Stop();
                    ChatInputPulseBorder.Visibility = Visibility.Collapsed;
                    _isPulseActive = false;
                }
            }
        }
    }

    private void TxtChatInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.F5)
        {
            e.Handled = true;
            ToggleVoiceDictation();
            return;
        }

        if (e.Key == VirtualKey.Enter)
        {
            var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (isShiftPressed)
            {
                TxtChatInput.SelectedText = "\r\n";
                e.Handled = true;
            }
            else
            {
                e.Handled = true;
                if (_enterKeyDownTime == DateTime.MinValue)
                {
                    _enterKeyDownTime = DateTime.Now;
                    StartLongPressTimer();
                }
            }
        }
    }

    private void TxtChatInput_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (!isShiftPressed && _enterKeyDownTime != DateTime.MinValue)
            {
                e.Handled = true;
                double elapsedMs = (DateTime.Now - _enterKeyDownTime).TotalMilliseconds;
                _enterKeyDownTime = DateTime.MinValue;

                bool isLongPress = elapsedMs >= 600 || _isLongPressThresholdReached;
                StopLongPressTimer();
                SendPromptToTerminal(isLongPress);
            }
        }
    }

    private void BtnSendMessage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _sendBtnPointerDownTime = DateTime.Now;
        StartLongPressTimer();
    }

    private void BtnSendMessage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
    }

    private void BtnSendMessage_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        StopLongPressTimer();
        _sendBtnPointerDownTime = DateTime.MinValue;
    }

    private void BtnVoice_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnVoice_Click)} triggered.");
        ToggleVoiceDictation();
    }

    public void ToggleVoiceDictation()
    {
        try
        {
            if (ActiveTerminal == null || ActiveTerminal.ConPTYTerm == null)
            {
                ShowNotice("Kein Terminal aktiv", "Bitte öffne oder wähle zuerst einen aktiven Chat aus.", InfoBarSeverity.Warning);
                return;
            }

            _isVoiceRecordingActive = !_isVoiceRecordingActive;
            if (_isVoiceRecordingActive)
            {
                _voiceStartTime = DateTime.Now;
                _isVoicePendingInTerminal = true;
                UpdateVoiceButtonState(true);
                ActiveTerminal.ConPTYTerm.WriteToTerm("/voice\r");
                FocusTerminalAndSetUserPause();
                TelemetryLogger.Log("[Voice] /voice sent to terminal to start dictation. Active state: True");
            }
            else
            {
                UpdateVoiceButtonState(false);
                _voiceStopTime = DateTime.Now;
                _lastVoiceCalculatedWaitMs = CalculateVoiceTranscriptionWaitMs();
                // Beim manuellen Stoppen niemals '/voice\r' senden, da dies bei bereits vorhandenem
                // transkribiertem Text '/voice' an den Satz anhängen würde. Stattdessen das native F5-Steuersignal (\x1b[15~) nutzen.
                ActiveTerminal.ConPTYTerm.WriteToTerm("\x1b[15~");
                FocusTerminalAndSetUserPause();
                TelemetryLogger.Log("[Voice] F5 (\\x1b[15~) sent to terminal to stop dictation cleanly. Active state: False");
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Voice ERROR] {ex.Message}");
        }
    }

    private void UpdateVoiceButtonState(bool isRecording)
    {
        if (BtnVoice == null) return;

        if (isRecording)
        {
            BtnVoice.Background = (Brush)Application.Current.Resources["AgyCategoryRose"];
            BtnVoice.BorderBrush = (Brush)Application.Current.Resources["AgyCategoryRose"];
            if (IconVoice != null)
            {
                IconVoice.Source = CreateHighResSvg("ms-appx:///Assets/Icons/mic-white.svg");
            }
            ToolTipService.SetToolTip(BtnVoice, "Sprachaufnahme aktiv! Erneut klicken oder F5 drücken zum Beenden.");
        }
        else
        {
            BtnVoice.Background = (Brush)Application.Current.Resources["AgyCardBg"];
            BtnVoice.BorderBrush = (Brush)Application.Current.Resources["AgyBorderDefault"];
            if (IconVoice != null)
            {
                IconVoice.Source = CreateHighResSvg("ms-appx:///Assets/Icons/mic.svg");
            }
            ToolTipService.SetToolTip(BtnVoice, "Sprach-Diktat (F5 oder /voice)");
        }
    }

    /// <summary>
    /// Berechnet die robuste Wartezeit für die Sprachtranskription anhand der tatsächlichen Aufnahmedauer.
    /// Je länger gesprochen wurde, desto größer ist die Audiodatei und desto länger benötigt Whisper/GenAI.
    /// Skaliert sauber auch bei langen Aufnahmen (1–3 Minuten) mit ausreichend Puffer für Upload und Inferenz.
    /// </summary>
    private int CalculateVoiceTranscriptionWaitMs()
    {
        double recordSec = _voiceStartTime != DateTime.MinValue ? (DateTime.Now - _voiceStartTime).TotalSeconds : 5.0;
        _voiceStartTime = DateTime.MinValue;
        // Basis: 1300 ms Mindestwartezeit + 90 ms pro Sekunde Aufnahme, begrenzt auf 1300 bis 25000 ms (25s)
        int waitMs = (int)Math.Clamp(1300 + (recordSec * 90), 1300, 25000);
        TelemetryLogger.Log($"[Voice] Calculated adaptive wait time: {waitMs}ms (based on {recordSec:F1}s recording duration).");
        return waitMs;
    }

    public void HandleVoiceSubmissionOnEnter()
    {
        if (!_isVoiceRecordingActive && !_isVoicePendingInTerminal) return;

        var currentFocused = FocusManager.GetFocusedElement(this.Content?.XamlRoot);
        if (object.ReferenceEquals(currentFocused, TxtChatInput))
        {
            // Focus ist bereits im Eingabefeld; TxtChatInput_KeyUp bzw. SendPromptToTerminal wickelt das Senden ab.
            return;
        }

        if (System.Threading.Interlocked.CompareExchange(ref _isVoiceSubmitting, 1, 0) != 0)
        {
            return;
        }

        try
        {
            TelemetryLogger.Log("[Voice] Enter pressed during voice dictation (terminal/global): delegating to SendPromptToTerminal.");
            SendPromptToTerminal(false);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Voice ERROR] Failed to delegate voice prompt to SendPromptToTerminal: {ex.Message}");
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isVoiceSubmitting, 0);
        }
    }

    private void BtnSendMessage_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnSendMessage_Click)} triggered.");
        double elapsedMs = _sendBtnPointerDownTime != DateTime.MinValue ? (DateTime.Now - _sendBtnPointerDownTime).TotalMilliseconds : 0;
        _sendBtnPointerDownTime = DateTime.MinValue;

        bool isLongPress = elapsedMs >= 600 || _isLongPressThresholdReached;
        StopLongPressTimer();
        SendPromptToTerminal(isLongPress);
    }

    private void StartLongPressTimer()
    {
        _isLongPressThresholdReached = false;
        _pressStartTime = DateTime.Now;

        var magentaBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 236, 72, 153));
        var blueBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246));
        var activeBrush = (_longPressTarget == "web-research" || _longPressTarget == "subagent") ? magentaBrush : blueBrush;

        if (ActionBarProgressBar != null)
        {
            ActionBarProgressBar.Width = 0;
            ActionBarProgressBar.Background = activeBrush;
        }

        if (_progressAnimationTimer == null)
        {
            _progressAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _progressAnimationTimer.Tick += (s, e) =>
            {
                if (_pressStartTime == DateTime.MinValue) return;
                double elapsed = (DateTime.Now - _pressStartTime).TotalMilliseconds;
                double progress = Math.Clamp(elapsed / 600.0, 0.0, 1.0);

                if (ActionBarProgressBar != null)
                {
                    double fullWidth = Math.Max(this.Content?.XamlRoot?.Size.Width ?? 800, 400);
                    ActionBarProgressBar.Width = progress * fullWidth;
                }

                if (progress >= 1.0)
                {
                    _progressAnimationTimer?.Stop();
                }
            };
        }
        _progressAnimationTimer.Stop();
        _progressAnimationTimer.Start();

        if (_longPressTimer == null)
        {
            _longPressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _longPressTimer.Tick += (s, e) =>
            {
                _longPressTimer?.Stop();
                _isLongPressThresholdReached = true;
                OnLongPressThresholdReached();
            };
        }
        _longPressTimer.Stop();
        _longPressTimer.Start();
    }

    private void StopLongPressTimer()
    {
        _pressStartTime = DateTime.MinValue;
        _longPressTimer?.Stop();
        _progressAnimationTimer?.Stop();

        if (ActionBarProgressBar != null)
        {
            ActionBarProgressBar.Width = 0;
        }

        if (BtnSendMessage != null)
        {
            BtnSendMessage.Background = (Brush)Application.Current.Resources["AgyAccent"];
        }
    }

    private void OnLongPressThresholdReached()
    {
        var magentaBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 236, 72, 153));
        var blueBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 99, 235));
        var activeBrush = (_longPressTarget == "web-research" || _longPressTarget == "subagent") ? magentaBrush : blueBrush;

        if (ActionBarProgressBar != null)
        {
            double fullWidth = Math.Max(this.Content.XamlRoot?.Size.Width ?? 800, 400);
            ActionBarProgressBar.Width = fullWidth;
            ActionBarProgressBar.Background = activeBrush;
        }

        if (BtnSendMessage != null)
        {
            BtnSendMessage.Background = activeBrush;
        }
    }

    private void FocusTerminalAndSetUserPause()
    {

        if (ActiveTerminal != null)
        {
            ActiveTerminal.Focus(FocusState.Programmatic);
        }
    }


    private void BtnModelSelect_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnModelSelect_Click)} triggered.");
        try
        {
            var activeSession = GetActiveTerminalSession();
            if (activeSession?.AgentType == "clawcode")
            {
                _ = ShowModelPickerPanel();
            }
            else
            {
                try
                {
                    if (ActiveTerminal?.ConPTYTerm != null)
                    {
                        ActiveTerminal.ConPTYTerm.WriteToTerm("/model\r");
                        FocusTerminalAndSetUserPause();
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"BtnModelSelect_Click Error: {ex.Message}");
        }
    }

    private void BtnGitStatusBadge_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnGitStatusBadge_Click)} triggered.");
        if (TabGitBtn != null)
        {
            TabNav_Click(TabGitBtn, new RoutedEventArgs());
        }
    }

    private void BtnProtocolBadge_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnProtocolBadge_Click)} triggered.");
        var focus = GetActiveTerminalProjectFocus();
        if (!string.IsNullOrWhiteSpace(focus.ProjectPath))
        {
            string filePath = Path.Combine(focus.ProjectPath, "docs", "protocol.md");
            if (File.Exists(filePath))
            {
                OpenFileInEditor(filePath);
            }
            else
            {
                ShowNotice("Datei nicht gefunden", $"Protokoll nicht gefunden: {filePath}", InfoBarSeverity.Warning);
            }
        }
    }

    private void BtnKnowledgeBadge_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnKnowledgeBadge_Click)} triggered.");
        var focus = GetActiveTerminalProjectFocus();
        if (!string.IsNullOrWhiteSpace(focus.ProjectPath))
        {
            string filePath = Path.Combine(focus.ProjectPath, "docs", "knowledge.md");
            if (File.Exists(filePath))
            {
                OpenFileInEditor(filePath);
            }
            else
            {
                ShowNotice("Datei nicht gefunden", $"Wissensdatenbank nicht gefunden: {filePath}", InfoBarSeverity.Warning);
            }
        }
    }
}

