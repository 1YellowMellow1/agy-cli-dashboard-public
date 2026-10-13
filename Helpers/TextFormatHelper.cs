using System;
using System.Text.RegularExpressions;

namespace AgyCliDashboard.Helpers;

/// <summary>
/// Hilfsklasse zur Bereinigung von Formatierungs-Markern (Markdown, ANSI, HTML/XML),
/// um 100% sauberen, reinen Text ("keine Markdowns") für Druck- und PDF-Ausgaben zu erzeugen.
/// </summary>
public static class TextFormatHelper
{
    public static readonly Regex AnsiEscapeRegex = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);
    private static readonly Regex FencedCodeRegex = new(@"```[a-zA-Z0-9_\-\.\+]*[ \t]*\r?\n([\s\S]*?)\r?\n[ \t]*```", RegexOptions.Compiled);
    private static readonly Regex SingleLineFencedRegex = new(@"```([\s\S]*?)```", RegexOptions.Compiled);
    private static readonly Regex InlineCodeRegex = new(@"`([^`\r\n]+)`", RegexOptions.Compiled);
    private static readonly Regex HeaderRegex = new(@"^[ \t]*#{1,6}[ \t]+(.*?)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BoldItalicRegex = new(@"(\*\*|__)(.*?)\1", RegexOptions.Compiled);
    private static readonly Regex ItalicRegex = new(@"(\*|_)(.*?)\1", RegexOptions.Compiled);
    private static readonly Regex StrikethroughRegex = new(@"~~(.*?)~~", RegexOptions.Compiled);
    private static readonly Regex BlockquoteAlertRegex = new(@"^[ \t]*>[ \t]*\[!.*?\][ \t]*\r?\n?", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BlockquoteRegex = new(@"^[ \t]*>[ \t]?", RegexOptions.Multiline | RegexOptions.Compiled);
    public static readonly Regex ImageRegex = new(@"!\[([^\]\r\n]*)\]\(([^)\r\n]+)\)", RegexOptions.Compiled);
    public static readonly Regex LinkRegex = new(@"\[([^\]\r\n]+)\]\(([^)\r\n]+)\)", RegexOptions.Compiled);
    private static readonly Regex BulletListRegex = new(@"^[ \t]*[-*+][ \t]+", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex NumberedListRegex = new(@"^[ \t]*(\d+)\.[ \t]+", RegexOptions.Multiline | RegexOptions.Compiled);
    public static readonly Regex HorizontalRuleRegex = new(@"^[ \t]*[-*_]{3,}[ \t]*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex UserRequestTagRegex = new(@"<\/?(?:USER_REQUEST|user_rules|rules|rule|scratch)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex XmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex MultipleNewlinesRegex = new(@"(\r?\n){3,}", RegexOptions.Compiled);

    /// <summary>
    /// Bereinigt sämtliche Markdown-, ANSI- und Steuer-Tags aus dem Eingabetext und liefert reinen Text zurück.
    /// </summary>
    public static string StripMarkdown(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // 0. Zeilenumbrüche vorab normalisieren: Alle \r\n und isolierten \r zu \n vereinheitlichen,
        // damit multiline Regex-Anchors (^ und $) an jeder Zeilengrenze zuverlässig matchen
        string text = input.Replace("\r\n", "\n").Replace("\r", "\n");

        // 1. ANSI-Terminal-Escape-Sequenzen entfernen
        text = AnsiEscapeRegex.Replace(text, "");

        // 1.5 Führende Einrückungen in Nicht-Code-Zeilen (z. B. Markdown-Listenfortsetzungen mit 3-4 Leerzeichen) bereinigen
        var rawLines = text.Split('\n');
        bool inFencedCode = false;
        for (int i = 0; i < rawLines.Length; i++)
        {
            string l = rawLines[i];
            string tr = l.Trim();
            bool isSingleLineFence = tr.StartsWith("```") && tr.Length >= 6 && tr.IndexOf("```", 3) >= 3;
            if (isSingleLineFence) continue;
            if (tr.StartsWith("```"))
            {
                inFencedCode = !inFencedCode;
                continue;
            }
            if (!inFencedCode)
            {
                rawLines[i] = tr;
            }
        }
        text = string.Join("\n", rawLines);

        // 2. Code-Blöcke und Inline-Code maskieren, um C#/TypeScript Generics, Unterstriche und Vergleichsoperatoren zu schützen
        var codeBlocks = new List<string>();
        text = FencedCodeRegex.Replace(text, m =>
        {
            int idx = codeBlocks.Count;
            codeBlocks.Add(m.Groups[1].Value);
            return $"\uE000CODEBLOCK{idx}\uE001";
        });
        text = SingleLineFencedRegex.Replace(text, m =>
        {
            int idx = codeBlocks.Count;
            codeBlocks.Add(m.Groups[1].Value);
            return $"\uE000CODEBLOCK{idx}\uE001";
        });
        text = InlineCodeRegex.Replace(text, m =>
        {
            int idx = codeBlocks.Count;
            codeBlocks.Add(m.Groups[1].Value);
            return $"\uE000CODEBLOCK{idx}\uE001";
        });

        // 3. Überschriften-Rauten (#, ##, ###) am Zeilenanfang entfernen
        text = HeaderRegex.Replace(text, "$1");

        // 4. Aufzählungen & nummerierte Listen: Linksbündig formatieren ohne führende Leerzeichen oder Tabs
        text = BulletListRegex.Replace(text, "• ");
        text = NumberedListRegex.Replace(text, "$1. ");

        // 5. Trennlinien durch eine dezente ASCII-Linie ersetzen (kompatibel mit Windows-1252 / PDF)
        text = HorizontalRuleRegex.Replace(text, "----------------------------------------");

        // 6. Auszeichnungen (Fett, Kursiv, Durchgestrichen) entfernen
        text = BoldItalicRegex.Replace(text, "$2");
        text = ItalicRegex.Replace(text, "$2");
        text = StrikethroughRegex.Replace(text, "$1");

        // 7. Blockzitate und Callout-Header (> [!NOTE]) bereinigen
        text = BlockquoteAlertRegex.Replace(text, "");
        text = BlockquoteRegex.Replace(text, "");

        // 8. Bilder und Links: Nur die lesbare Beschreibung behalten, Markdown-Syntax entfernen
        text = ImageRegex.Replace(text, "$1");
        text = LinkRegex.Replace(text, "$1");

        // 9. Interne Antigravity-Tags und HTML/XML-Tags entfernen
        text = UserRequestTagRegex.Replace(text, "");
        text = XmlTagRegex.Replace(text, "");

        // 10. Maskierte Code-Inhalte unversehrt wieder einsetzen
        for (int i = 0; i < codeBlocks.Count; i++)
        {
            text = text.Replace($"\uE000CODEBLOCK{i}\uE001", codeBlocks[i]);
        }

        // 11. Mehrfache Leerzeilen normalisieren und Ränder trimmen
        text = MultipleNewlinesRegex.Replace(text, "\n\n");

        // 12. Finale Zeilenumbrüche deterministisch auf das Windows-Format (\r\n) umstellen
        text = text.Replace("\n", Environment.NewLine);

        return text.Trim();
    }
}
