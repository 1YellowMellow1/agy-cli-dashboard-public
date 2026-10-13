using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private FileSystemWatcher? _globalKnowledgeWatcher;
    private FileSystemWatcher? _projectFileWatcher;
    private System.Threading.Timer? _badgeDebounceTimer;
    private System.Threading.Timer? _flashResetTimer;
    private readonly object _syncLockBadgeTimer = new object();
    private System.Threading.Timer? _filesTabDebounceTimer;
    private readonly object _syncLockFilesTabTimer = new object();

    private volatile bool _flashGitPending = false;
    private volatile bool _flashProtocolPending = false;
    private volatile bool _flashKnowledgePending = false;

    private DispatcherTimer? _noticeAutoCloseTimer;

    private void InitStatusBadgesWatcher()
    {
        try
        {
            if (_globalKnowledgeWatcher != null)
            {
                _globalKnowledgeWatcher.Changed -= OnFileSystemEventTriggered;
                _globalKnowledgeWatcher.Created -= OnFileSystemEventTriggered;
                _globalKnowledgeWatcher.Dispose();
                _globalKnowledgeWatcher = null;
            }

            // Global knowledge directory watcher
            string globalKnowledgeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "skills", "knowledge-base", "custom-knowledge");
            if (Directory.Exists(globalKnowledgeDir))
            {
                _globalKnowledgeWatcher = new FileSystemWatcher(globalKnowledgeDir)
                {
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };
                _globalKnowledgeWatcher.Changed += OnFileSystemEventTriggered;
                _globalKnowledgeWatcher.Created += OnFileSystemEventTriggered;
            }

            UpdateProjectFileWatcher();
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"InitStatusBadgesWatcher error: {ex.Message}");
        }
    }

    private void UpdateProjectFileWatcher()
    {
        try
        {
            if (_projectFileWatcher != null)
            {
                _projectFileWatcher.Changed -= OnFileSystemEventTriggered;
                _projectFileWatcher.Created -= OnFileSystemEventTriggered;
                _projectFileWatcher.Deleted -= OnFileSystemEventTriggered;
                _projectFileWatcher.Renamed -= OnFileSystemEventTriggered;
                _projectFileWatcher.Dispose();
                _projectFileWatcher = null;
            }

            string projectPath = GetCurrentProjectPath();
            if (!string.IsNullOrEmpty(projectPath) && Directory.Exists(projectPath))
            {
                _projectFileWatcher = new FileSystemWatcher(projectPath)
                {
                    InternalBufferSize = 64 * 1024,
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true
                };
                _projectFileWatcher.Changed += OnFileSystemEventTriggered;
                _projectFileWatcher.Created += OnFileSystemEventTriggered;
                _projectFileWatcher.Deleted += OnFileSystemEventTriggered;
                _projectFileWatcher.Renamed += OnFileSystemEventTriggered;
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"UpdateProjectFileWatcher error: {ex.Message}");
        }
    }

    private void OnFileSystemEventTriggered(object sender, FileSystemEventArgs e)
    {
        if (string.IsNullOrEmpty(e.FullPath)) return;

        string path = e.FullPath;

        // 1. Echtzeitaktualisierung des Dateibaums bei strukturellen Änderungen oder Ignore-Dateien
        bool isStructuralTreeChange = e.ChangeType == WatcherChangeTypes.Created ||
                                      e.ChangeType == WatcherChangeTypes.Deleted ||
                                      e.ChangeType == WatcherChangeTypes.Renamed ||
                                      path.EndsWith(".gitignore", StringComparison.OrdinalIgnoreCase) ||
                                      path.EndsWith(".geminiignore", StringComparison.OrdinalIgnoreCase) ||
                                      path.EndsWith(".reflectionignore", StringComparison.OrdinalIgnoreCase);

        if (isStructuralTreeChange)
        {
            bool isNoise = (path.Contains(".git", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".gitignore", StringComparison.OrdinalIgnoreCase)) ||
                           path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("\\.vs\\", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("/.vs/", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("\\node_modules\\", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase);

            if (!isNoise)
            {
                lock (_syncLockFilesTabTimer)
                {
                    if (_projectFileWatcher == null && _globalKnowledgeWatcher == null) return;

                    _filesTabDebounceTimer ??= new System.Threading.Timer(_ =>
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            try
                            {
                                if (!string.IsNullOrEmpty(_lastFilesProjectPath) && Directory.Exists(_lastFilesProjectPath))
                                {
                                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                                }
                            }
                            catch (Exception ex)
                            {
                                TelemetryLogger.Log($"[FilesTab LiveWatcher Debounce Error] {ex.Message}");
                            }
                        });
                    }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

                    _filesTabDebounceTimer.Change(400, System.Threading.Timeout.Infinite);
                }
            }
        }

        // 2. Status-Badges (Git, protocol.md, knowledge.md)
        bool isGit = path.Contains(".git", StringComparison.OrdinalIgnoreCase) &&
                     (path.EndsWith("head", StringComparison.OrdinalIgnoreCase) ||
                      path.EndsWith("index", StringComparison.OrdinalIgnoreCase) ||
                      path.Contains("refs", StringComparison.OrdinalIgnoreCase));
        bool isProtocol = path.EndsWith("protocol.md", StringComparison.OrdinalIgnoreCase);
        bool isKnowledge = path.EndsWith("knowledge.md", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains("custom-knowledge", StringComparison.OrdinalIgnoreCase);

        if (!isGit && !isProtocol && !isKnowledge) return;

        if (isGit) _flashGitPending = true;
        if (isProtocol) _flashProtocolPending = true;
        if (isKnowledge) _flashKnowledgePending = true;

        lock (_syncLockBadgeTimer)
        {
            if (_projectFileWatcher == null && _globalKnowledgeWatcher == null) return;

            _badgeDebounceTimer ??= new System.Threading.Timer(_ =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    RefreshStatusBadgesAndFlash();
                });
            }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

            _badgeDebounceTimer.Change(300, System.Threading.Timeout.Infinite);
        }
    }

    private void RefreshStatusBadgesAndFlash()
    {
        try
        {
            var amberBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11));
            var emeraldBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
            var fuchsiaBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 217, 70, 239));

            if (_flashGitPending && BtnGitStatusBadge != null)
            {
                BtnGitStatusBadge.BorderBrush = amberBrush;
                BtnGitStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 245, 158, 11));
            }
            if (_flashProtocolPending && BtnProtocolBadge != null)
            {
                BtnProtocolBadge.BorderBrush = emeraldBrush;
                BtnProtocolBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 16, 185, 129));
            }
            if (_flashKnowledgePending && BtnKnowledgeBadge != null)
            {
                BtnKnowledgeBadge.BorderBrush = fuchsiaBrush;
                BtnKnowledgeBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 217, 70, 239));
            }

            lock (_syncLockBadgeTimer)
            {
                _flashResetTimer ??= new System.Threading.Timer(_ =>
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        ResetBadgeVisuals();
                    });
                }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

                _flashResetTimer.Change(1500, System.Threading.Timeout.Infinite);
            }
        }
        catch { }
    }

    private void ResetBadgeVisuals()
    {
        try
        {
            var defaultBorder = (Brush)Application.Current.Resources["AgyBorderDefault"];
            var defaultBg = (Brush)Application.Current.Resources["AgySurfaceBg"];

            if (BtnGitStatusBadge != null)
            {
                BtnGitStatusBadge.BorderBrush = defaultBorder;
                BtnGitStatusBadge.Background = defaultBg;
            }
            if (BtnProtocolBadge != null)
            {
                BtnProtocolBadge.BorderBrush = defaultBorder;
                BtnProtocolBadge.Background = defaultBg;
            }
            if (BtnKnowledgeBadge != null)
            {
                BtnKnowledgeBadge.BorderBrush = defaultBorder;
                BtnKnowledgeBadge.Background = defaultBg;
            }

            _flashGitPending = false;
            _flashProtocolPending = false;
            _flashKnowledgePending = false;
        }
        catch { }
    }

    private string GetCurrentProjectPath()
    {
        string selected = GetSelectedProjectName();
            string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder)
                ? _projectSettings.BaseFolder
                : ProjectSettingsData.GetDefaultBaseFolder();

        if (string.IsNullOrWhiteSpace(selected) || selected.Contains("Global", StringComparison.OrdinalIgnoreCase) || selected.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return baseFolder;
        }
        return Path.Combine(baseFolder, selected);
    }



    private void ShowNotice(string title, string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowNotice(title, message, severity));
            return;
        }

        if (PnlHeaderNotification != null)
        {
            if (TxtHeaderNoticeTitle != null) TxtHeaderNoticeTitle.Text = title;
            if (TxtHeaderNoticeMessage != null) TxtHeaderNoticeMessage.Text = message;
            if (DotHeaderNotice != null)
            {
                Windows.UI.Color color = severity switch
                {
                    InfoBarSeverity.Success => Windows.UI.Color.FromArgb(255, 16, 185, 129),
                    InfoBarSeverity.Warning => Windows.UI.Color.FromArgb(255, 245, 158, 11),
                    InfoBarSeverity.Error => Windows.UI.Color.FromArgb(255, 239, 68, 68),
                    _ => Windows.UI.Color.FromArgb(255, 59, 130, 246)
                };
                DotHeaderNotice.Fill = new SolidColorBrush(color);
            }

            string fullTooltip = string.IsNullOrEmpty(title) ? message : $"{title}:\n{message}";
            ToolTipService.SetToolTip(PnlHeaderNotification, fullTooltip);
            if (TxtHeaderNoticeMessage != null)
            {
                ToolTipService.SetToolTip(TxtHeaderNoticeMessage, fullTooltip);
            }

            PnlHeaderNotification.Visibility = Visibility.Visible;
            if (UcHeader?.TitleBarDragArea != null)
            {
                UcHeader.TitleBarDragArea.Visibility = Visibility.Collapsed;
            }

            if (_noticeAutoCloseTimer == null)
            {
                _noticeAutoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _noticeAutoCloseTimer.Tick += (s, e) =>
                {
                    _noticeAutoCloseTimer?.Stop();
                    if (PnlHeaderNotification != null)
                    {
                        PnlHeaderNotification.Visibility = Visibility.Collapsed;
                    }
                    if (UcHeader?.TitleBarDragArea != null)
                    {
                        UcHeader.TitleBarDragArea.Visibility = Visibility.Visible;
                    }
                };
            }
            _noticeAutoCloseTimer.Stop();
            _noticeAutoCloseTimer.Start();
        }
    }

    private void BtnCloseHeaderNotice_Click(object sender, RoutedEventArgs e)
    {
        _noticeAutoCloseTimer?.Stop();
        if (PnlHeaderNotification != null)
        {
            PnlHeaderNotification.Visibility = Visibility.Collapsed;
        }
        if (UcHeader?.TitleBarDragArea != null)
        {
            UcHeader.TitleBarDragArea.Visibility = Visibility.Visible;
        }
    }



    private void ShowMockupNotice(string featureName)
    {
        ShowNotice($"UI-Attrappe: {featureName}", $"Die Funktion '{featureName}' ist im derzeitigen UI-Prototyp noch ein Mockup (Attrappe).", InfoBarSeverity.Informational);
    }
}
