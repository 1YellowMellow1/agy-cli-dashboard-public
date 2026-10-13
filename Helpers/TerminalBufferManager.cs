using System;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using EasyWindowsTerminalControl;

namespace AgyCliDashboard.Helpers;

public static class TerminalBufferManager
{
    private const int MaxCharacterLimit = 300_000; // ~300 KB max buffer (~5,000 lines)
    private const int TrimTargetLength = 150_000;  // Retain newest 150 KB when limit reached
    private static PropertyInfo? _consoleOutputLogProp;
    private static volatile bool _propertySearched;

    public static PropertyInfo? GetConsoleOutputLogProperty(object conPty)
    {
        if (!_propertySearched)
        {
            _consoleOutputLogProp = conPty.GetType().GetProperty("ConsoleOutputLog", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _propertySearched = true;
        }
        return _consoleOutputLogProp;
    }

    /// <summary>
    /// Thread-safely extracts the current text from the ConPTY console output log buffer.
    /// </summary>
    public static string? GetOutputLogText(object? conPty)
    {
        if (conPty == null) return null;
        try
        {
            PropertyInfo? prop = GetConsoleOutputLogProperty(conPty);
            if (prop?.GetValue(conPty) is StringBuilder sb)
            {
                lock (sb)
                {
                    return sb.ToString();
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Restricts the ConPTY console output log buffer to prevent unbounded RAM growth.
    /// Call this periodically or during terminal output events / tab switches.
    /// </summary>
    public static void EnforceBufferLimit(EasyTerminalControl? termControl)
    {
        if (termControl == null) return;

        try
        {
            object? conPty = termControl.ConPTYTerm;
            if (conPty == null) return;

            PropertyInfo? prop = GetConsoleOutputLogProperty(conPty);
            if (prop?.GetValue(conPty) is StringBuilder sb)
            {
                lock (sb)
                {
                    if (sb.Length > MaxCharacterLimit)
                    {
                        int removeCount = sb.Length - TrimTargetLength;
                        if (removeCount > 0 && removeCount < sb.Length)
                        {
                            if (char.IsLowSurrogate(sb[removeCount]))
                            {
                                removeCount++;
                            }
                            if (removeCount < sb.Length)
                            {
                                sb.Remove(0, removeCount);
                                TelemetryLogger.Log($"[Terminal RAM Safeguard] Trimmed {removeCount} chars from ConPTY output buffer. Remaining: {sb.Length} chars.");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Terminal RAM Safeguard Error] {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the ConPTY console output log buffer completely upon terminal session disposal.
    /// </summary>
    public static void ClearBuffer(EasyTerminalControl? termControl)
    {
        if (termControl == null) return;

        try
        {
            object? conPty = termControl.ConPTYTerm;
            if (conPty == null) return;

            PropertyInfo? prop = GetConsoleOutputLogProperty(conPty);
            if (prop?.GetValue(conPty) is StringBuilder sb)
            {
                lock (sb)
                {
                    sb.Clear();
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Thread-sicherer Abruf ausschließlich des Puffer-Endes (Tail), ohne den gesamten Puffer in RAM zu duplizieren.
    /// </summary>
    public static string? GetOutputLogTail(object? conPty, int maxChars = 2000)
    {
        if (conPty == null) return null;
        try
        {
            PropertyInfo? prop = GetConsoleOutputLogProperty(conPty);
            if (prop?.GetValue(conPty) is StringBuilder sb)
            {
                lock (sb)
                {
                    if (sb.Length == 0) return string.Empty;
                    int startIndex = Math.Max(0, sb.Length - maxChars);
                    int length = sb.Length - startIndex;
                    return sb.ToString(startIndex, length);
                }
            }
        }
        catch { }
        return null;
    }

    private static readonly Regex AnsiRegex = new(@"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])", RegexOptions.Compiled);

    /// <summary>
    /// Prüft strikt anhand der letzten Zeilen am ConPTY-Puffer-Ende, ob die CLI aktuell am interaktiven Eingabeprompt
    /// wartet oder unmittelbar abgebrochen wurde.
    /// Verhindert Falsch-Positive durch vorherige Turns oder Textausgaben und bereinigt ANSI-Escape-Sequenzen.
    /// </summary>
    public static bool IsTerminalAtIdlePrompt(object? conPty)
    {
        string? rawTail = GetOutputLogTail(conPty, 1000);
        if (string.IsNullOrWhiteSpace(rawTail)) return false;

        string tail = AnsiRegex.Replace(rawTail, "");
        string[] lines = tail.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return false;

        // Prüfe ausschließlich die letzten 1 bis 3 Zeilen am tatsächlichen Puffer-Ende
        string lastLine = lines[^1].Trim();
        string secondLastLine = lines.Length >= 2 ? lines[^2].Trim() : string.Empty;
        string thirdLastLine = lines.Length >= 3 ? lines[^3].Trim() : string.Empty;

        // 1. Wenn die allerletzte Zeile der Standard-Footer "? for shortcuts" ist:
        if (lastLine.Equals("? for shortcuts", StringComparison.OrdinalIgnoreCase) ||
            lastLine.EndsWith("? for shortcuts", StringComparison.OrdinalIgnoreCase))
        {
            // Steht direkt darüber der Eingabeprompt ">" (evtl. mit Abbruch-Hinweis darüber)?
            if (secondLastLine.StartsWith(">") || secondLastLine.Equals(">") ||
                thirdLastLine.StartsWith(">") ||
                secondLastLine.Contains("Interrupted", StringComparison.OrdinalIgnoreCase) ||
                thirdLastLine.Contains("Interrupted", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 2. Wenn die allerletzte Zeile selbst der leere Prompt ">" ist (z.B. vor Rendering des Footers):
        if (lastLine.Equals(">") || lastLine.Equals(">?") || lastLine.Equals("?"))
        {
            return true;
        }

        return false;
    }
}
