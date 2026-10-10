using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AgyCliDashboard.Helpers;

/// <summary>
/// Konvertiert Markdown-Text (z. B. aus KI-Antworten oder .md-Dateien)
/// in standardkonformes RTF 1.5, sodass WinUI 3 RichEditBox und WordPad
/// den Text sofort typografisch formatiert ohne Markdown-Sonderzeichen darstellen.
/// </summary>
public static class MarkdownToRtfConverter
{
    private static Regex AnsiEscapeRegex => TextFormatHelper.AnsiEscapeRegex;
    private static readonly Regex HeaderLineRegex = new(@"^[ \t]*(#{1,4})[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletLineRegex = new(@"^[ \t]*[-*+][ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex NumberedLineRegex = new(@"^[ \t]*(\d+)\.[ \t]+(.*)$", RegexOptions.Compiled);
    private static readonly Regex BlockquoteAlertRegex = new(@"^[ \t]*>[ \t]*\[!.*?\][ \t]*", RegexOptions.Compiled);
    private static readonly Regex BlockquoteLineRegex = new(@"^[ \t]*>[ \t]*(.*)$", RegexOptions.Compiled);
    private static Regex HorizontalRuleRegex => TextFormatHelper.HorizontalRuleRegex;
    private static Regex LinkRegex => TextFormatHelper.LinkRegex;
    private static Regex ImageRegex => TextFormatHelper.ImageRegex;

    /// <summary>
    /// Konvertiert Markdown in einen vollständigen RTF 1.5 Dokument-String.
    /// </summary>
    public static string ConvertToRtf(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}}{\colortbl ;\red15\green23\blue42;}\viewkind4\uc1\pard\f0\fs22\cf1\par}";
        }

        // 1. Bereinige ANSI-Sequenzen
        string cleanMd = AnsiEscapeRegex.Replace(markdown, "");

        var sb = new StringBuilder();
        // RTF Header mit Segoe UI (Standard) und Consolas (Code)
        sb.AppendLine(@"{\rtf1\ansi\ansicpg1252\deff0\nouicompat");
        sb.AppendLine(@"{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}{\f1\fnil\fcharset0 Consolas;}}");
        sb.AppendLine(@"{\colortbl ;\red15\green23\blue42;\red71\green85\blue105;\red2\green132\blue199;\red203\green213\blue225;\red0\green0\blue0;}");
        sb.AppendLine(@"{\*\generator AgyWordPad;}");
        sb.AppendLine(@"\viewkind4\uc1");

        var lines = cleanMd.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        bool inCodeBlock = false;
        bool inMarkdownBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            // Code-Block Start/Ende (```)
            if (trimmed.StartsWith("```"))
            {
                if (inMarkdownBlock)
                {
                    inMarkdownBlock = false;
                    continue;
                }

                if (inCodeBlock)
                {
                    inCodeBlock = false;
                    continue;
                }

                string tag = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : "";
                if (string.Equals(tag, "markdown", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag, "md", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tag, "prompt", StringComparison.OrdinalIgnoreCase))
                {
                    inMarkdownBlock = true;
                    continue;
                }

                inCodeBlock = true;
                continue;
            }

            if (inCodeBlock)
            {
                // Monospace Codezeile (linksbündig, interne Code-Einrückung bleibt erhalten)
                sb.Append(@"\pard\sl240\slmult1\f1\fs20\cf2 ");
                sb.Append(EscapeRtfText(line));
                sb.AppendLine(@"\par");
                continue;
            }

            // Horizontale Trennlinie (---, ***, ___)
            if (HorizontalRuleRegex.IsMatch(line))
            {
                sb.AppendLine(@"\pard\sa140\sl240\slmult1\f0\fs18\cf4 ------------------------------------------------------------\cf1\fs22\par");
                continue;
            }

            // Überschriften (#, ##, ###, ####)
            var headerMatch = HeaderLineRegex.Match(line);
            if (headerMatch.Success)
            {
                int level = headerMatch.Groups[1].Value.Length;
                string title = headerMatch.Groups[2].Value.Trim();
                int fontSize = level switch
                {
                    1 => 32, // 16pt
                    2 => 28, // 14pt
                    3 => 24, // 12pt
                    _ => 22  // 11pt
                };
                int spaceAfter = level switch
                {
                    1 => 180,
                    2 => 140,
                    3 => 120,
                    _ => 100
                };

                sb.Append($@"\pard\sa{spaceAfter}\sl280\slmult1\f0\b\fs{fontSize}\cf5 ");
                sb.Append(FormatInline(title, baseFontSize: fontSize, isHeading: true));
                sb.AppendLine(@"\b0\fs22\par");
                continue;
            }

            // Aufzählungsliste (- oder * oder +): Linksbündig ohne Randversatz und ohne harten Tabulator
            var bulletMatch = BulletLineRegex.Match(line);
            if (bulletMatch.Success)
            {
                string itemText = bulletMatch.Groups[1].Value.Trim();
                sb.Append(@"\pard\sa80\sl260\slmult1\f0\fs22\cf1\bullet  ");
                sb.Append(FormatInline(itemText));
                sb.AppendLine(@"\par");
                continue;
            }

            // Nummerierte Liste (1. 2. 3.): Linksbündig ohne Randversatz und mit Leerzeichen statt Tabulator
            var numMatch = NumberedLineRegex.Match(line);
            if (numMatch.Success)
            {
                string num = numMatch.Groups[1].Value;
                string itemText = numMatch.Groups[2].Value.Trim();
                sb.Append($@"\pard\sa80\sl260\slmult1\f0\fs22\cf1 {num}. ");
                sb.Append(FormatInline(itemText));
                sb.AppendLine(@"\par");
                continue;
            }

            // Blockzitat (> [!NOTE] oder > Zitat): Linksbündig kursiv
            if (BlockquoteAlertRegex.IsMatch(line))
            {
                line = Regex.Replace(line, @"^[ \t]*>[ \t]*\[!(?:NOTE|TIP|IMPORTANT|WARNING|CAUTION)\][ \t]*", "> ", RegexOptions.IgnoreCase);
            }
            var quoteMatch = BlockquoteLineRegex.Match(line);
            if (quoteMatch.Success)
            {
                string quoteText = quoteMatch.Groups[1].Value.Trim();
                sb.Append(@"\pard\sa100\sl260\slmult1\f0\i\fs22\cf2 ");
                sb.Append(FormatInline(quoteText));
                sb.AppendLine(@"\i0\par");
                continue;
            }

            // Leerzeile
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.AppendLine(@"\pard\sa80\sl240\slmult1\f0\fs22\cf1\par");
                continue;
            }

            // Normaler Fließtext-Absatz: Führende Markdown-Einrückungen (z. B. 3-4 Leerzeichen bei Listen-Folgezeilen) restlos entfernen
            sb.Append(@"\pard\sa140\sl276\slmult1\f0\fs22\cf1 ");
            sb.Append(FormatInline(trimmed));
            sb.AppendLine(@"\par");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Formatiert Inline-Elemente wie **fett**, *kursiv*, `code`, Links.
    /// </summary>
    private static string FormatInline(string input, int baseFontSize = 22, bool isHeading = false)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // Links & Bilder vereinfachen [Titel](url) -> Titel
        string text = ImageRegex.Replace(input, "$1");
        text = LinkRegex.Replace(text, "$1");

        // Wir zerlegen den Text tokenbasiert, um RTF-Tags sicher einzubauen
        var sb = new StringBuilder();
        int pos = 0;
        int len = text.Length;

        while (pos < len)
        {
            // Inline Code `...`
            if (text[pos] == '`')
            {
                int end = text.IndexOf('`', pos + 1);
                if (end > pos)
                {
                    string code = text.Substring(pos + 1, end - pos - 1);
                    sb.Append(@"\f1\fs20\cf3 ");
                    sb.Append(EscapeRtfText(code));
                    sb.Append($@"\f0\fs{baseFontSize}\cf1 ");
                    pos = end + 1;
                    continue;
                }
            }

            // Bold & Italic ***...***
            if (pos + 2 < len && text.Substring(pos, 3) == "***")
            {
                int end = text.IndexOf("***", pos + 3, StringComparison.Ordinal);
                if (end > pos)
                {
                    string inner = text.Substring(pos + 3, end - pos - 3);
                    sb.Append(@"\b\i ");
                    sb.Append(FormatInline(inner, baseFontSize, isHeading));
                    sb.Append(isHeading ? @"\i0 " : @"\i0\b0 ");
                    pos = end + 3;
                    continue;
                }
            }

            // Bold **...**
            if (pos + 1 < len && text.Substring(pos, 2) == "**")
            {
                int end = text.IndexOf("**", pos + 2, StringComparison.Ordinal);
                if (end > pos)
                {
                    string inner = text.Substring(pos + 2, end - pos - 2);
                    sb.Append(@"\b ");
                    sb.Append(FormatInline(inner, baseFontSize, isHeading));
                    sb.Append(isHeading ? @" " : @"\b0 ");
                    pos = end + 2;
                    continue;
                }
            }

            // Italic *...*
            if (text[pos] == '*' && (pos + 1 >= len || text[pos + 1] != '*'))
            {
                int end = text.IndexOf('*', pos + 1);
                if (end > pos)
                {
                    string inner = text.Substring(pos + 1, end - pos - 1);
                    sb.Append(@"\i ");
                    sb.Append(FormatInline(inner, baseFontSize, isHeading));
                    sb.Append(@"\i0 ");
                    pos = end + 1;
                    continue;
                }
            }

            // Strikethrough ~~...~~
            if (pos + 1 < len && text.Substring(pos, 2) == "~~")
            {
                int end = text.IndexOf("~~", pos + 2, StringComparison.Ordinal);
                if (end > pos)
                {
                    string inner = text.Substring(pos + 2, end - pos - 2);
                    sb.Append(@"\strike ");
                    sb.Append(FormatInline(inner));
                    sb.Append(@"\strike0 ");
                    pos = end + 2;
                    continue;
                }
            }

            // Normales Zeichen
            char c = text[pos];
            AppendEscapedChar(sb, c);
            pos++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escaped Steuerzeichen und Unicode-Zeichen für RTF.
    /// </summary>
    private static string EscapeRtfText(string str)
    {
        var sb = new StringBuilder();
        foreach (char c in str)
        {
            AppendEscapedChar(sb, c);
        }
        return sb.ToString();
    }

    private static void AppendEscapedChar(StringBuilder sb, char c)
    {
        switch (c)
        {
            case '\\': sb.Append(@"\\"); break;
            case '{': sb.Append(@"\{"); break;
            case '}': sb.Append(@"\}"); break;
            case '\t': sb.Append(@"\tab "); break;
            default:
                if (c <= 127)
                {
                    sb.Append(c);
                }
                else
                {
                    // RTF Unicode Escape: \uN? wobei N short (signed 16-bit) ist
                    short val = (short)c;
                    sb.Append($@"\u{val}?");
                }
                break;
        }
    }
}
