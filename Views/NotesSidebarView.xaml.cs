using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace AgyCliDashboard.Views;

public sealed partial class NotesSidebarView : UserControl
{
    public event RoutedEventHandler? SaveClick;
    public event RoutedEventHandler? RefreshClick;
    public event RoutedEventHandler? OpenInEditorClick;
    public event TextChangedEventHandler? ContentChanged;

    public NotesSidebarView()
    {
        this.InitializeComponent();
    }

    public string Text
    {
        get => TxtNotesContent?.Text ?? string.Empty;
        set
        {
            if (TxtNotesContent != null)
            {
                TxtNotesContent.TextChanged -= TxtNotesContent_TextChanged;
                try
                {
                    TxtNotesContent.Text = value ?? string.Empty;
                }
                finally
                {
                    TxtNotesContent.TextChanged += TxtNotesContent_TextChanged;
                }
                UpdateStats();
            }
        }
    }

    public void SetProject(string projectName)
    {
        if (TxtProjectBadge != null)
        {
            string display = string.IsNullOrWhiteSpace(projectName) ? "Global" : projectName;
            TxtProjectBadge.Text = display;
            if (BdrProjectBadge != null)
            {
                ToolTipService.SetToolTip(BdrProjectBadge, $"Aktives Projekt: {display}");
            }
        }
    }

    public void SetState(string content, string filePath, string statusMessage, string? projectName = null)
    {
        if (TxtNotesContent != null)
        {
            TxtNotesContent.TextChanged -= TxtNotesContent_TextChanged;
            try
            {
                TxtNotesContent.Text = content ?? string.Empty;
            }
            finally
            {
                TxtNotesContent.TextChanged += TxtNotesContent_TextChanged;
            }
        }

        if (!string.IsNullOrWhiteSpace(projectName))
        {
            SetProject(projectName);
            if (TxtProjectBadge != null && !string.IsNullOrEmpty(filePath))
            {
                ToolTipService.SetToolTip(TxtProjectBadge, $"Projekt: {projectName}\r\nDatei: {filePath}");
            }
        }

        UpdateStats();
    }

    public void SetStatusMessage(string message)
    {
        if (TxtNotesStats != null)
        {
            ToolTipService.SetToolTip(TxtNotesStats, message);
        }
    }

    public void UpdateStats()
    {
        if (TxtNotesStats == null || TxtNotesContent == null) return;
        string text = TxtNotesContent.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            TxtNotesStats.Text = "Leer";
            return;
        }

        var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        int nonEmptyLines = lines.Count(l => !string.IsNullOrWhiteSpace(l));
        TxtNotesStats.Text = nonEmptyLines == 1 ? "1 Zeile" : $"{nonEmptyLines} Zeilen";
    }

    private void TxtNotesContent_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateStats();
        ContentChanged?.Invoke(sender, e);
    }

    private async void TxtNotesContent_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);

        // Strg+V: Intelligentes Terminal-to-Doc Einfügen
        if (e.Key == VirtualKey.V && ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            try
            {
                var package = Clipboard.GetContent();
                if (package != null && package.Contains(StandardDataFormats.Text))
                {
                    string raw = await package.GetTextAsync();
                    if (!string.IsNullOrEmpty(raw))
                    {
                        bool isDirty = TerminalToDocConverter.IsTerminalOrMarkdownContent(raw);
                        string formatted = TerminalToDocConverter.ConvertToCleanDocText(raw);

                        e.Handled = true;
                        int selStart = Math.Clamp(TxtNotesContent.SelectionStart, 0, TxtNotesContent.Text.Length);
                        int selLength = Math.Clamp(TxtNotesContent.SelectionLength, 0, TxtNotesContent.Text.Length - selStart);

                        string before = TxtNotesContent.Text.Substring(0, selStart);
                        string after = TxtNotesContent.Text.Substring(selStart + selLength);

                        TxtNotesContent.Text = before + formatted + after;
                        TxtNotesContent.SelectionStart = selStart + formatted.Length;
                        TxtNotesContent.SelectionLength = 0;

                        SetStatusMessage(isDirty 
                            ? "Terminal-Text als Doc formatiert eingefügt" 
                            : "Eingefügt");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[Notes SmartPaste] Error: {ex.Message}");
            }
        }

        // Strg+S: Sofortiges Speichern
        if (e.Key == VirtualKey.S && ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            SaveClick?.Invoke(this, new RoutedEventArgs());
        }
    }

    private void BtnCleanTerminalText_Click(object sender, RoutedEventArgs e)
    {
        if (TxtNotesContent == null || string.IsNullOrWhiteSpace(TxtNotesContent.Text)) return;
        string cleaned = TerminalToDocConverter.ConvertToCleanDocText(TxtNotesContent.Text);
        if (!string.Equals(cleaned, TxtNotesContent.Text, StringComparison.Ordinal))
        {
            TxtNotesContent.Text = cleaned;
            SaveClick?.Invoke(this, new RoutedEventArgs());
            SetStatusMessage("Markdown bereinigt & als Doc formatiert");
        }
        else
        {
            SetStatusMessage("Text ist bereits im sauberen Doc-Format");
        }
    }

    private void BtnCancelClear_Click(object sender, RoutedEventArgs e)
    {
        FlyoutConfirmClear?.Hide();
    }

    private void BtnConfirmClear_Click(object sender, RoutedEventArgs e)
    {
        FlyoutConfirmClear?.Hide();
        if (TxtNotesContent != null)
        {
            TxtNotesContent.Text = string.Empty;
        }
        SaveClick?.Invoke(this, new RoutedEventArgs());
        SetStatusMessage("Notizen geleert");
    }

    private void BtnOpenInEditor_Click(object sender, RoutedEventArgs e)
    {
        OpenInEditorClick?.Invoke(sender, e);
    }

    private void BtnRefreshNotes_Click(object sender, RoutedEventArgs e)
    {
        RefreshClick?.Invoke(sender, e);
    }
}

/// <summary>
/// Wandelt rohen Terminal-Text (ANSI Escape Codes, ConPTY-Wraps, Shell-Prompts, ASCII-Boxen)
/// sowie Markdown-Syntax (#, ##, **, Backticks, Code-Zäune) eigenständig in sauberen,
/// lesbaren Dokumententext ohne störende Format-Codes um.
/// </summary>
public static class TerminalToDocConverter
{
    private static readonly Regex AnsiRegex = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\].*?(?:\x07|\x1B\\))",
        RegexOptions.Compiled);

    private static readonly Regex ControlCharRegex = new(
        @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]",
        RegexOptions.Compiled);

    private static readonly Regex EmojiRegex = new(
        @"[\uD83C-\uDBFF\uDC00-\uDFFF\u2600-\u27BF\uFE00-\uFE0F\u200D\u20E3]",
        RegexOptions.Compiled);

    private static readonly Regex TerminalPromptRegex = new(
        @"^\s*(?:(?:PS\s+)?[A-Za-z]:\\[^>\r\n]*>|(?:\([^\)]+\)\s+)?[a-zA-Z0-9_.\-]+@[a-zA-Z0-9_.\-]+:[^$#\r\n]*[$#]|(?:❯|\$)\s+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ProgressSpinnerRegex = new(
        @"[⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏■□▪▫▲▼►◄]+\s*",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownHeaderRegex = new(
        @"^[ \t\u00A0\uFEFF]*#{1,6}[ \t\u00A0]*",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MarkdownBoldRegex = new(
        @"\*\*([^\*\r\n]+)\*\*|__([^_\r\n]+)__",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownItalicRegex = new(
        @"(?<!\*)\*([^\*\r\n]+)\*(?!\*)|(?<!_)_([^_\r\n]+)_(?!_)",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownInlineCodeRegex = new(
        @"`([^`\r\n]+)`",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownCodeFenceRegex = new(
        @"^[ \t]*```[a-zA-Z0-9_\-]*[ \t]*\r?$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MarkdownLinkRegex = new(
        @"\[([^\]\r\n]+)\]\([^\)\r\n]*\)",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownImageRegex = new(
        @"!\[([^\]\r\n]*)\]\([^\)\r\n]*\)",
        RegexOptions.Compiled);

    private static readonly Regex MarkdownBlockquoteRegex = new(
        @"^[ \t]*>[ \t]?",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MarkdownCheckboxRegex = new(
        @"^[ \t]*[-*•][ \t]*\[[ xX]\][ \t]*",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MarkdownBulletRegex = new(
        @"^[ \t]*[-*+][ \t]+",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex MarkdownDividerRegex = new(
        @"^[ \t]*[-*_]{3,}[ \t]*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex NumberItemRegex = new(
        @"^\d+\.\s+",
        RegexOptions.Compiled);

    public static bool IsTerminalOrMarkdownContent(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        return AnsiRegex.IsMatch(input) || 
               ControlCharRegex.IsMatch(input) || 
               TerminalPromptRegex.IsMatch(input) || 
               ProgressSpinnerRegex.IsMatch(input) ||
               MarkdownHeaderRegex.IsMatch(input) ||
               MarkdownBoldRegex.IsMatch(input) ||
               MarkdownCodeFenceRegex.IsMatch(input) ||
               MarkdownInlineCodeRegex.IsMatch(input) ||
               MarkdownCheckboxRegex.IsMatch(input) ||
               MarkdownDividerRegex.IsMatch(input);
    }

    public static string ConvertToCleanDocText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // 1. ANSI-Codes, Steuerzeichen, Emojis, BOM und Spinner entfernen
        string text = input.TrimStart('\uFEFF', '\u200B');
        text = AnsiRegex.Replace(text, string.Empty);
        text = ControlCharRegex.Replace(text, string.Empty);
        text = EmojiRegex.Replace(text, string.Empty);
        text = ProgressSpinnerRegex.Replace(text, string.Empty);

        // 2. Normalisiere alle Zeilenumbrüche vorab auf LF
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // 3. Terminal-Prompts am Zeilenanfang säubern
        text = TerminalPromptRegex.Replace(text, string.Empty);

        // 4. Markdown-Syntax restlos zu sauberem Dokumententext transformieren:
        // Code-Zäune entfernen (```csharp ...)
        text = MarkdownCodeFenceRegex.Replace(text, string.Empty);

        // Überschriften-Rauten (#, ##, ###) am Zeilenanfang entfernen
        text = MarkdownHeaderRegex.Replace(text, string.Empty);

        // Fettdruck (**Wort** -> Wort)
        text = MarkdownBoldRegex.Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

        // Kursiv (*Wort* -> Wort)
        text = MarkdownItalicRegex.Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

        // Inline-Code (`code` -> code)
        text = MarkdownInlineCodeRegex.Replace(text, "$1");

        // Links ([Text](url) -> Text)
        text = MarkdownLinkRegex.Replace(text, "$1");

        // Bilder (![alt](url) -> entfernen)
        text = MarkdownImageRegex.Replace(text, string.Empty);

        // Blockzitate (> Zitat -> Zitat)
        text = MarkdownBlockquoteRegex.Replace(text, string.Empty);

        // Checkboxen (- [ ] / - [x] -> • )
        text = MarkdownCheckboxRegex.Replace(text, "• ");

        // Listen-Aufzählungszeichen vereinheitlichen (* oder - -> • )
        text = MarkdownBulletRegex.Replace(text, "• ");

        // Horizontale Linien (--- / ***) zu dezentem Divider
        text = MarkdownDividerRegex.Replace(text, "────────────────────────────────────────");

        // 5. Zeilen durchgehen, Absätze de-wrappen und Einrückungen vereinheitlichen
        var rawLines = text.Split('\n');
        var cleanLines = new List<string>(rawLines.Length);
        string currentParagraph = string.Empty;

        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i].TrimEnd();

            // Leerzeile: Schließt den aktuellen Absatz ab
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!string.IsNullOrEmpty(currentParagraph))
                {
                    cleanLines.Add(currentParagraph);
                    currentParagraph = string.Empty;
                }
                cleanLines.Add(string.Empty);
                continue;
            }

            string trimmed = line.TrimStart();
            int leadingSpaces = line.Length - trimmed.Length;

            bool isBullet = trimmed.StartsWith('•') || trimmed.StartsWith('-') || trimmed.StartsWith('*');
            bool isNumber = NumberItemRegex.IsMatch(trimmed);
            bool isDivider = trimmed.StartsWith('─');

            // Strukturelemente (Aufzählungen, nummerierte Punkte, Linien):
            if (isBullet || isNumber || isDivider)
            {
                if (!string.IsNullOrEmpty(currentParagraph))
                {
                    cleanLines.Add(currentParagraph);
                    currentParagraph = string.Empty;
                }

                // Einrückungs-Normalisierung:
                // Tief eingerückte Unterebenen (>= 4 Spaces oder Tab) auf exakt 2 Spaces einrücken
                // Hauptebene (0-3 Spaces) bündig links (0 Spaces)
                if (isBullet)
                {
                    currentParagraph = (leadingSpaces >= 4 || line.StartsWith('\t'))
                        ? "  " + trimmed
                        : trimmed;
                }
                else
                {
                    currentParagraph = trimmed;
                }
            }
            else
            {
                // Fließtext:
                // Wenn vorheriger Text existiert und NICHT mit Satzende (. : ! ? { } ;) oder Trenner endet,
                // handelt es sich um einen harten ConPTY-Terminal-Wrap -> Zusammenführen!
                bool canMerge = !string.IsNullOrEmpty(currentParagraph) &&
                                !currentParagraph.EndsWith('.') &&
                                !currentParagraph.EndsWith(':') &&
                                !currentParagraph.EndsWith('!') &&
                                !currentParagraph.EndsWith('?') &&
                                !currentParagraph.EndsWith(';') &&
                                !currentParagraph.EndsWith('{') &&
                                !currentParagraph.EndsWith('}') &&
                                !currentParagraph.StartsWith('─');

                if (canMerge)
                {
                    currentParagraph += " " + trimmed;
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentParagraph))
                    {
                        cleanLines.Add(currentParagraph);
                    }
                    // Neuer Absatz: Willkürliche führende Terminal-Spaces entfernen
                    currentParagraph = trimmed;
                }
            }
        }

        if (!string.IsNullOrEmpty(currentParagraph))
        {
            cleanLines.Add(currentParagraph);
        }

        // 6. Rückgabe mit sauberen Windows CRLF-Zeilenumbrüchen und max. 1 Leerzeile
        string output = string.Join("\r\n", cleanLines).TrimEnd();
        output = Regex.Replace(output, @"(\r?\n){3,}", "\r\n\r\n");

        return output;
    }
}
