using System;
using System.Reflection;
using System.Text;
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
}
