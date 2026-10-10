using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Bricht den laufenden CLI-Befehl ab, indem ein SIGINT (STRG+C / \x03) an das ConPTY-Terminal gesendet wird.
    /// </summary>
    private void BtnCancelCommand_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnCancelCommand_Click)} triggered.");
        // Sende SIGINT (STRG+C) an das Terminal, wie in der agy-desktop-app (\x03)
        ActiveTerminal?.ConPTYTerm?.WriteToTerm("\x03");
    }

    private bool _isUpdatingTglAutoReflexion = false;

    private void TglAutoReflexion_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(TglAutoReflexion_Click)} triggered.");
        if (_isUpdatingTglAutoReflexion) return;
        var activeSession = GetActiveTerminalSession();
        if (activeSession != null)
        {
            bool isEnabled = UcPromptEditor?.TglAutoReflexion?.IsChecked == true;
            activeSession.IsAutoReflexionEnabled = isEnabled;
            TelemetryLogger.Log($"[AutoReflexion] Session {activeSession.Id} (convId: '{activeSession.ConversationId}') AutoReflexion toggled to {isEnabled}");
            if (!string.IsNullOrEmpty(activeSession.ConversationId))
            {
                SaveChatAutoReflexion(activeSession.ConversationId, isEnabled);
                AgyChatSession? chat = null;
                lock (_allMasterChats)
                {
                    chat = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, activeSession.ConversationId, StringComparison.OrdinalIgnoreCase));
                }
                chat ??= _chats.FirstOrDefault(c => string.Equals(c.Id, activeSession.ConversationId, StringComparison.OrdinalIgnoreCase));
                if (chat != null)
                {
                    chat.IsAutoReflexionEnabled = isEnabled;
                }
            }
        }
    }

    private void TglAutoReflexion_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(TglAutoReflexion_RightTapped)} triggered.");
        e.Handled = true;

        var (termProjName, termProjPath) = GetActiveTerminalProjectFocus();
        string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder();
        string? activeProjDir = null;
        if (!string.IsNullOrWhiteSpace(termProjPath) && Directory.Exists(termProjPath))
        {
            activeProjDir = termProjPath;
        }
        else
        {
            string currentProjPath = GetCurrentProjectPath();
            if (Directory.Exists(currentProjPath)) activeProjDir = currentProjPath;
        }

        bool isGlobal = string.IsNullOrEmpty(termProjName) || termProjName.Contains("Global", StringComparison.OrdinalIgnoreCase) || string.Equals(activeProjDir?.TrimEnd('\\', '/'), baseFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        if (isGlobal)
        {
            activeProjDir = null;
        }

        string projName = !isGlobal && !string.IsNullOrEmpty(termProjName)
            ? termProjName
            : (!isGlobal && !string.IsNullOrEmpty(activeProjDir) ? Path.GetFileName(activeProjDir.TrimEnd('\\', '/')) : "Projekt");

        var flyout = new MenuFlyout();

        var itemProject = new MenuFlyoutItem
        {
            Text = isGlobal
                ? "📄 Projekt-Ignorierliste (.reflectionignore) – kein Projekt aktiv"
                : $"📄 Projekt-Ignorierliste ({projName}/.reflectionignore) öffnen",
            IsEnabled = !isGlobal && !string.IsNullOrEmpty(activeProjDir)
        };
        itemProject.Click += (s, args) =>
        {
            try
            {
                if (string.IsNullOrEmpty(activeProjDir) || !Directory.Exists(activeProjDir))
                {
                    ShowNotice("Kein aktives Projekt", "Für globale Sessions ist keine projektbezogene .reflectionignore verfügbar. Nutze bitte die globale Ignorierliste.", InfoBarSeverity.Warning);
                    return;
                }

                string targetDir = activeProjDir;
                Helpers.AutoReflectionManager.EnsureProjectIgnoreFileExists(targetDir);
                string ignoreFile = Path.Combine(targetDir, ".reflectionignore");

                try
                {
                    Process.Start(new ProcessStartInfo(ignoreFile) { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", $"\"{ignoreFile}\"") { UseShellExecute = false });
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[TglAutoReflexion_RightTapped] Open project ignore error: {ex.Message}");
                ShowNotice("Fehler beim Öffnen", $"Konnte .reflectionignore nicht öffnen: {ex.Message}", InfoBarSeverity.Error);
            }
        };
        flyout.Items.Add(itemProject);

        var itemGlobal = new MenuFlyoutItem
        {
            Text = "🌐 Globale Ignorierliste (reflection_ignore.json) öffnen"
        };
        itemGlobal.Click += (s, args) =>
        {
            try
            {
                Helpers.AutoReflectionManager.EnsureGlobalIgnoreFileExists();
                string globalPath = Helpers.AutoReflectionManager.GlobalIgnoreFilePath;

                try
                {
                    Process.Start(new ProcessStartInfo(globalPath) { UseShellExecute = true });
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", $"\"{globalPath}\"") { UseShellExecute = false });
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[TglAutoReflexion_RightTapped] Open global ignore error: {ex.Message}");
                ShowNotice("Fehler beim Öffnen", $"Konnte reflection_ignore.json nicht öffnen: {ex.Message}", InfoBarSeverity.Error);
            }
        };
        flyout.Items.Add(itemGlobal);

        FrameworkElement? anchor = sender as FrameworkElement ?? UcPromptEditor?.TglAutoReflexion;
        if (anchor != null)
        {
            flyout.ShowAt(anchor);
        }
    }

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnUndo_Click)} triggered.");
        ActiveTerminal?.ConPTYTerm?.WriteToTerm("/undo\r");
        FocusTerminalAndSetUserPause();
    }

    private void BtnConfigSettings_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnConfigSettings_Click)} triggered.");
        ActiveTerminal?.ConPTYTerm?.WriteToTerm("/config\r");
        FocusTerminalAndSetUserPause();
    }
}
