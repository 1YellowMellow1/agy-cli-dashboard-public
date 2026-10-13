using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private FileSystemWatcher? _notesWatcher;
    private DispatcherTimer? _notesDebounceTimer;
    private string? _currentNotesFilePath;
    private bool _isNotesUserEditing;
    private string _lastLoadedNotesText = string.Empty;

    private void InitNotesSidebar()
    {
        if (UcNotesSidebar != null)
        {
            UcNotesSidebar.SaveClick += UcNotesSidebar_SaveClick;
            UcNotesSidebar.RefreshClick += UcNotesSidebar_RefreshClick;
            UcNotesSidebar.OpenInEditorClick += UcNotesSidebar_OpenInEditorClick;
            UcNotesSidebar.ContentChanged += UcNotesSidebar_ContentChanged;
        }

        _notesDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _notesDebounceTimer.Tick += (s, e) =>
        {
            _notesDebounceTimer.Stop();
            SaveNotesAndSync();
        };
    }

    private void UcNotesSidebar_SaveClick(object sender, RoutedEventArgs e)
    {
        _notesDebounceTimer?.Stop();
        SaveNotesAndSync();
    }

    private void UcNotesSidebar_RefreshClick(object sender, RoutedEventArgs e)
    {
        _notesDebounceTimer?.Stop();
        LoadNotesFromDisk();
    }

    private void UcNotesSidebar_OpenInEditorClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentNotesFilePath) && File.Exists(_currentNotesFilePath))
        {
            OpenFileInEditor(_currentNotesFilePath);
        }
    }

    private void UcNotesSidebar_ContentChanged(object sender, TextChangedEventArgs e)
    {
        _isNotesUserEditing = true;
        UcNotesSidebar?.SetStatusMessage("Änderungen ungespeichert...");
        _notesDebounceTimer?.Stop();
        _notesDebounceTimer?.Start();
    }

    private string? _currentProjectName;

    public void UpdateNotesTab(bool isGlobal, string projectPath, string projectName)
    {
        try
        {
            _notesDebounceTimer?.Stop();
            _isNotesUserEditing = false;

            string targetPath;
            string displayProj;
            if (isGlobal || string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string globalDir = Path.Combine(userProfile, ".gemini", "antigravity-cli");
                if (!Directory.Exists(globalDir)) Directory.CreateDirectory(globalDir);
                targetPath = Path.Combine(globalDir, "TODO.md");
                displayProj = "Global";
            }
            else
            {
                targetPath = Path.Combine(projectPath, "TODO.md");
                if (!File.Exists(targetPath) && File.Exists(Path.Combine(projectPath, "NOTES.md")))
                {
                    targetPath = Path.Combine(projectPath, "NOTES.md");
                }
                else if (!File.Exists(targetPath) && File.Exists(Path.Combine(projectPath, "notes.md")))
                {
                    targetPath = Path.Combine(projectPath, "notes.md");
                }
                else if (!File.Exists(targetPath) && File.Exists(Path.Combine(projectPath, "todo.md")))
                {
                    targetPath = Path.Combine(projectPath, "todo.md");
                }
                displayProj = !string.IsNullOrWhiteSpace(projectName) ? projectName : Path.GetFileName(projectPath.TrimEnd('\\', '/'));
            }

            _currentProjectName = displayProj;
            _currentNotesFilePath = targetPath;
            SetupNotesFileWatcher(targetPath);
            LoadNotesFromDisk(displayProj);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"UpdateNotesTab ERROR: {ex.Message}");
        }
    }

    private void SetupNotesFileWatcher(string filePath)
    {
        try
        {
            if (_notesWatcher != null)
            {
                _notesWatcher.EnableRaisingEvents = false;
                _notesWatcher.Dispose();
                _notesWatcher = null;
            }

            string? dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            string fileName = Path.GetFileName(filePath);

            _notesWatcher = new FileSystemWatcher(dir, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _notesWatcher.Changed += (s, e) => OnExternalNotesFileChanged();
            _notesWatcher.Created += (s, e) => OnExternalNotesFileChanged();
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SetupNotesFileWatcher ERROR: {ex.Message}");
        }
    }

    private void OnExternalNotesFileChanged()
    {
        // Wenn der Benutzer gerade aktiv tippt, nicht überschreiben (außer das Feld ist noch leer)
        if (_isNotesUserEditing && UcNotesSidebar != null && !string.IsNullOrWhiteSpace(UcNotesSidebar.Text)) return;

        DispatcherQueue?.TryEnqueue(() =>
        {
            if (_isNotesUserEditing && UcNotesSidebar != null && !string.IsNullOrWhiteSpace(UcNotesSidebar.Text)) return;
            LoadNotesFromDisk(_currentProjectName);
        });
    }

    private void LoadNotesFromDisk(string? projectName = null)
    {
        if (string.IsNullOrEmpty(_currentNotesFilePath) || UcNotesSidebar == null) return;

        try
        {
            _notesDebounceTimer?.Stop();

            string content;
            if (File.Exists(_currentNotesFilePath))
            {
                content = File.ReadAllText(_currentNotesFilePath, Encoding.UTF8);
            }
            else
            {
                string projName = !string.IsNullOrWhiteSpace(projectName) 
                    ? projectName 
                    : Path.GetFileName(Path.GetDirectoryName(_currentNotesFilePath) ?? "Projekt");
                if (string.IsNullOrWhiteSpace(projName)) projName = "Projekt";

                content = $"# {projName} Notizen\r\n";
                WriteAllTextAtomic(_currentNotesFilePath, content);
            }

            // Normalisiere Zeilenumbrüche für die UI
            content = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

            _lastLoadedNotesText = content;
            _isNotesUserEditing = false;

            string displayProj = !string.IsNullOrWhiteSpace(projectName) 
                ? projectName 
                : (_currentProjectName ?? Path.GetFileName(Path.GetDirectoryName(_currentNotesFilePath) ?? "Projekt"));

            UcNotesSidebar.SetState(content, _currentNotesFilePath, "Gespeichert", displayProj);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"LoadNotesFromDisk ERROR: {ex.Message}");
            UcNotesSidebar.SetStatusMessage("Fehler beim Laden: " + ex.Message);
        }
    }

    private void SaveNotesAndSync()
    {
        if (string.IsNullOrEmpty(_currentNotesFilePath) || UcNotesSidebar == null) return;

        try
        {
            string content = UcNotesSidebar.Text;

            if (string.IsNullOrWhiteSpace(content))
            {
                string projName = _currentProjectName ?? Path.GetFileName(Path.GetDirectoryName(_currentNotesFilePath) ?? "Projekt");
                content = $"# {projName} Notizen\r\n";
            }
            else
            {
                // Standardisiere Zeilenumbrüche zu echtem Windows-CRLF
                content = content.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd() + "\r\n";
            }

            WriteAllTextAtomic(_currentNotesFilePath, content);
            _lastLoadedNotesText = content;
            _isNotesUserEditing = false;

            UcNotesSidebar.SetStatusMessage($"Gespeichert ({DateTime.Now:HH:mm:ss})");
            TelemetryLogger.Log($"[Notes] Saved {_currentNotesFilePath}");
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SaveNotesAndSync ERROR: {ex.Message}");
            UcNotesSidebar.SetStatusMessage("Fehler beim Speichern: " + ex.Message);
        }
    }
}
