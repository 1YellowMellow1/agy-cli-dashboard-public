using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private string _maintenanceTimestampsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "maintenance_timestamps.json");
    private MaintenanceTimestampsData _maintenanceData = new();

    private void LoadMaintenanceTimestamps()
    {
        try
        {
            if (File.Exists(_maintenanceTimestampsFilePath))
            {
                string json = File.ReadAllText(_maintenanceTimestampsFilePath);
                var loaded = JsonSerializer.Deserialize<MaintenanceTimestampsData>(json, _jsonOptions);
                if (loaded != null)
                {
                    _maintenanceData = loaded;
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Maintenance] Error loading maintenance timestamps: {ex.Message}");
        }

        _maintenanceData ??= new MaintenanceTimestampsData();
        _maintenanceData.Global ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _maintenanceData.Projects ??= new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveMaintenanceTimestamps()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_maintenanceTimestampsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(_maintenanceData, _jsonOptions);
            WriteAllTextAtomic(_maintenanceTimestampsFilePath, json);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Maintenance] Error saving maintenance timestamps: {ex.Message}");
        }
    }

    public void RecordMaintenanceTimestamp(string key, DateTime? dt = null)
    {
        try
        {
            _maintenanceData.Global[key] = (dt ?? DateTime.Now).ToString("o");
            SaveMaintenanceTimestamps();
            UpdateMaintenanceToolTips();
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Maintenance] Error recording maintenance timestamp for '{key}': {ex.Message}");
        }
    }

    public void RecordProjectMaintenanceTimestamp(string projectName, string key, DateTime? dt = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = GetCurrentTargetProjectForMaintenance();
            }

            if (!_maintenanceData.Projects.TryGetValue(projectName, out var projDict))
            {
                projDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _maintenanceData.Projects[projectName] = projDict;
            }

            projDict[key] = (dt ?? DateTime.Now).ToString("o");
            SaveMaintenanceTimestamps();
            UpdateMaintenanceToolTips();
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Maintenance] Error recording project maintenance timestamp for '{projectName}/{key}': {ex.Message}");
        }
    }

    public DateTime? GetMaintenanceTimestamp(string key)
    {
        if (_maintenanceData.Global.TryGetValue(key, out var str) && DateTime.TryParse(str, out var dt))
        {
            return dt;
        }
        return null;
    }

    public DateTime? GetProjectMaintenanceTimestamp(string projectName, string key)
    {
        if (string.IsNullOrWhiteSpace(projectName)) return null;
        if (_maintenanceData.Projects.TryGetValue(projectName, out var projDict) &&
            projDict.TryGetValue(key, out var str) &&
            DateTime.TryParse(str, out var dt))
        {
            return dt;
        }
        return null;
    }

    public string FormatMaintenanceTimestamp(DateTime? dt)
    {
        if (!dt.HasValue) return "Noch nie";
        return dt.Value.ToString("dd.MM.yyyy, HH:mm:ss") + " Uhr";
    }

    public string GetCurrentTargetProjectForMaintenance()
    {
        var (termProjName, _) = GetActiveTerminalProjectFocus();
        if (!string.IsNullOrWhiteSpace(termProjName) && !termProjName.Contains("Global"))
        {
            return termProjName;
        }
        string sel = GetSelectedProjectName();
        if (!string.IsNullOrWhiteSpace(sel) && !sel.Contains("Global") && !sel.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return sel;
        }
        string? last = _projectSettings?.LastSelectedProject;
        if (!string.IsNullOrWhiteSpace(last) && !last.Contains("Global") && !last.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return last;
        }
        var firstProj = _allProjects?.FirstOrDefault(p => p.IsVisible && !p.Name.Contains("Global"));
        return firstProj?.Name ?? "Projekt";
    }

    public void UpdateMaintenanceToolTips()
    {
        try
        {
            // 1. Projekt-Hygiene & Audit (Mop-Icon)
            if (BtnProjectCleanupSidebar != null)
            {
                string baseTip = "Projekt-Hygiene & Audit (Klick: Alle Projekte | Rechtsklick: Nur aktuelles Projekt | Shift+Klick: Sofort absenden)";
                string allTime = FormatMaintenanceTimestamp(GetMaintenanceTimestamp("project_cleanup"));
                string currentProj = GetCurrentTargetProjectForMaintenance();
                string projTime = FormatMaintenanceTimestamp(GetProjectMaintenanceTimestamp(currentProj, "project_cleanup"));
                string extra = projTime != "Noch nie" ? $"\nZuletzt ausgeführt ({currentProj}): {projTime}" : string.Empty;
                ToolTipService.SetToolTip(BtnProjectCleanupSidebar, $"{baseTip}\n\nZuletzt ausgeführt (Alle Projekte): {allTime}{extra}");
            }

            // 2. Auf Updates prüfen (Radio-Tower Icon)
            if (BtnCheckGlobalUpdates != null)
            {
                string baseTip = "Auf Updates prüfen (Wissens-Update-Prompt in Chat-Eingabe einfügen)";
                string timeStr = FormatMaintenanceTimestamp(GetMaintenanceTimestamp("check_global_updates"));
                ToolTipService.SetToolTip(BtnCheckGlobalUpdates, $"{baseTip}\n\nZuletzt ausgeführt am: {timeStr}");
            }

            // 3. KI-Modelle & Skala updaten (Chip-Icon)
            if (BtnUpdateModelSpecs != null)
            {
                string baseTip = "KI-Modelle & Token-Skala synchronisieren ('agy models' auslesen, Grenzwerte im Web prüfen & Skala aktualisieren)";
                string timeStr = FormatMaintenanceTimestamp(GetMaintenanceTimestamp("update_model_specs"));
                ToolTipService.SetToolTip(BtnUpdateModelSpecs, $"{baseTip}\n\nZuletzt ausgeführt am: {timeStr}");
            }

            // 4. System-Voraussetzungen (Monitor-Check Icon)
            if (BtnSystemCheckSidebar != null)
            {
                string baseTip = "System-Voraussetzungen & Live-Versionen prüfen";
                string timeStr = FormatMaintenanceTimestamp(GetMaintenanceTimestamp("system_check"));
                ToolTipService.SetToolTip(BtnSystemCheckSidebar, $"{baseTip}\n\nZuletzt ausgeführt am: {timeStr}");
            }

            // 4b. Refresh-Button im System-Check Panel
            if (BtnRefreshSystemCheck != null)
            {
                string baseTip = "Prüfung erneut ausführen";
                string timeStr = FormatMaintenanceTimestamp(GetMaintenanceTimestamp("system_check"));
                ToolTipService.SetToolTip(BtnRefreshSystemCheck, $"{baseTip}\n\nZuletzt ausgeführt am: {timeStr}");
            }

            // 5. Dead Code Check (Totenkopf-Icon, projektspezifisch!)
            if (BtnDeadCodeCheck != null)
            {
                string baseTip = "Dead Code Check (Numpad 8 | Klick: Sofort testen | Text vorhanden: Anhängen | Rechtsklick: als Longpress-Ziel festlegen)";
                string currentProj = GetCurrentTargetProjectForMaintenance();
                string timeStr = FormatMaintenanceTimestamp(GetProjectMaintenanceTimestamp(currentProj, "dead_code_check"));
                string projLabel = !string.IsNullOrWhiteSpace(currentProj) ? $" ({currentProj})" : "";
                ToolTipService.SetToolTip(BtnDeadCodeCheck, $"{baseTip}\n\nZuletzt ausgeführt am: {timeStr}{projLabel}");
            }

            // 6. Simplify / Overengineering Check (projektspezifisch!)
            if (BtnSimplify != null)
            {
                string baseTip = "Simplify & KISS-Audit (Klick: 2-Phasen-Audit mit Subagents | Text vorhanden: Anhängen | Shift+Klick: Umschalten | Rechtsklick: als Longpress-Ziel festlegen)";
                string currentProj = GetCurrentTargetProjectForMaintenance();
                string timeStr = FormatMaintenanceTimestamp(GetProjectMaintenanceTimestamp(currentProj, "simplify_audit"));
                string projLabel = !string.IsNullOrWhiteSpace(currentProj) ? $" ({currentProj})" : "";
                ToolTipService.SetToolTip(BtnSimplify, $"{baseTip}\n\nZuletzt ausgeführt am: {timeStr}{projLabel}");
            }

            // 7. Zero-Bug-Audit & Reliability Exterminator (projektspezifisch!)
            if (BtnZeroBug != null)
            {
                string baseTip = "Zero-Bug-Audit (Klick: Sofort ausführen | Text vorhanden: Anhängen | Shift+Klick: Umschalten | Rechtsklick: als Longpress-Ziel festlegen)";
                string currentProj = GetCurrentTargetProjectForMaintenance();
                string timeStr = FormatMaintenanceTimestamp(GetProjectMaintenanceTimestamp(currentProj, "zero_bug_audit"));
                string projLabel = !string.IsNullOrWhiteSpace(currentProj) ? $" ({currentProj})" : "";
                ToolTipService.SetToolTip(BtnZeroBug, $"{baseTip}\n\nZuletzt ausgeführt am: {timeStr}{projLabel}");
            }

            // 8. Robustheits-Audit & Real-User Chaos-Auditing (projektspezifisch!)
            if (BtnDauTest != null)
            {
                string baseTip = "Robustheits-Audit (Klick: Sofort ausführen | Text vorhanden: Anhängen | Shift+Klick: Umschalten | Rechtsklick: als Longpress-Ziel festlegen)";
                string currentProj = GetCurrentTargetProjectForMaintenance();
                string timeStr = FormatMaintenanceTimestamp(GetProjectMaintenanceTimestamp(currentProj, "dau_test"));
                string projLabel = !string.IsNullOrWhiteSpace(currentProj) ? $" ({currentProj})" : "";
                ToolTipService.SetToolTip(BtnDauTest, $"{baseTip}\n\nZuletzt ausgeführt am: {timeStr}{projLabel}");
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[Maintenance] Error updating maintenance tooltips: {ex.Message}");
        }
    }
}