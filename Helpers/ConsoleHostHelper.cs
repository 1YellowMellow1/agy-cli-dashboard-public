using Microsoft.Win32;
using System;

namespace AgyCliDashboard.Helpers;

/// <summary>
/// Verwalter für die Windows 11 Standard-Terminal-App Registrierung.
/// Schaltet zwischen dem stillen Windows-Konsolenhost (conhost) und dem Standard-Terminal (Windows Terminal) um.
/// </summary>
public static class ConsoleHostHelper
{
    private const string StartupSubKey = @"Console\%%Startup";
    private const string DelegationConsoleValue = "DelegationConsole";
    private const string DelegationTerminalValue = "DelegationTerminal";

    // GUID für klassischen Windows-Konsolenhost (ConHost) -> verhindert Flackern/Aufblitzen von Konsolenfenstern
    public const string WindowsConsoleHostGuid = "{B23D10C0-E52E-411E-9D5B-C09FDF709C7D}";

    // GUID für Windows Standard (Vom System entscheiden lassen / Windows Terminal)
    public const string DefaultTerminalGuid = "{00000000-0000-0000-0000-000000000000}";

    /// <summary>
    /// Prüft, ob aktuell der Windows-Konsolenhost als Standard registriert ist.
    /// </summary>
    public static bool IsConsoleHostEnforced()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupSubKey, writable: false);
            if (key == null) return false;

            var consoleVal = key.GetValue(DelegationConsoleValue) as string;
            var termVal = key.GetValue(DelegationTerminalValue) as string;

            return string.Equals(consoleVal, WindowsConsoleHostGuid, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(termVal, WindowsConsoleHostGuid, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[ConsoleHostHelper] Fehler beim Lesen der Delegation-Registry: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Setzt oder entfernt den Windows-Konsolenhost als Standard-Terminal.
    /// </summary>
    /// <param name="enable">true = Stiller Windows-Konsolenhost, false = Standard-Terminal-App</param>
    public static bool SetConsoleHostEnforced(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StartupSubKey, writable: true);
            if (key == null) return false;

            if (enable)
            {
                key.SetValue(DelegationConsoleValue, WindowsConsoleHostGuid, RegistryValueKind.String);
                key.SetValue(DelegationTerminalValue, WindowsConsoleHostGuid, RegistryValueKind.String);
                TelemetryLogger.Log("[ConsoleHostHelper] Stiller Windows-Konsolenhost (conhost) als Standard-Terminal gesetzt.");
            }
            else
            {
                key.SetValue(DelegationConsoleValue, DefaultTerminalGuid, RegistryValueKind.String);
                key.SetValue(DelegationTerminalValue, DefaultTerminalGuid, RegistryValueKind.String);
                TelemetryLogger.Log("[ConsoleHostHelper] Standard-Terminal-App (Windows Terminal) wiederhergestellt.");
            }
            return true;
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[ConsoleHostHelper] Fehler beim Schreiben der Delegation-Registry: {ex.Message}");
            return false;
        }
    }
}
