using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private string? _currentEditingFilePath;
    private static readonly SolidColorBrush TreeTransparentBrush = new(Microsoft.UI.Colors.Transparent);
    private static readonly SolidColorBrush TreeSlate203Brush = new(Microsoft.UI.ColorHelper.FromArgb(255, 203, 213, 225));
    private static readonly SolidColorBrush TreeStagedBgBrush = new(Microsoft.UI.ColorHelper.FromArgb(48, 59, 130, 246));
    private static readonly SolidColorBrush TreeStagedBorderBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 59, 130, 246));
    private static readonly SolidColorBrush TreeWhiteBrush = new(Microsoft.UI.Colors.White);
    private static readonly SolidColorBrush IgnoreBadgeBgBrush = new(Microsoft.UI.ColorHelper.FromArgb(24, 255, 255, 255));
    private static readonly SolidColorBrush IgnoreBadgeBorderBrush = new(Microsoft.UI.ColorHelper.FromArgb(20, 255, 255, 255));
    private static readonly SolidColorBrush TreeSortBgBrush = new(Microsoft.UI.ColorHelper.FromArgb(36, 245, 158, 11));
    private static readonly SolidColorBrush TreeSortBorderBrush = new(Microsoft.UI.ColorHelper.FromArgb(160, 245, 158, 11));
    private static readonly SolidColorBrush TreeSortTextBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 251, 191, 36));

    private static Border CreateIgnoreIconBadge(string iconPath, string tooltipText)
    {
        var icon = new ImageIcon
        {
            Source = CreateHighResSvg(iconPath, 32.0),
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        var border = new Border
        {
            Background = IgnoreBadgeBgBrush,
            BorderThickness = new Thickness(1),
            BorderBrush = IgnoreBadgeBorderBrush,
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(3, 1, 3, 1),
            Margin = new Thickness(1, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = icon
        };
        ToolTipService.SetToolTip(border, tooltipText);
        return border;
    }
    private readonly List<string> _openTabFilePaths = new();
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);
    private string? _lastFilesProjectPath;
    private string? _lastFilesProjectName;
    private bool _lastFilesIsGlobal;
    private bool _showHiddenFiles;
    private DispatcherTimer? _fileSearchDebounceTimer;
    private string _fileSearchQuery = string.Empty;
    private bool _filesSearchInitialized;

    private bool IsFolderSortedRecentFirst(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || _uiStates?.RecentFirstFolders == null) return false;
        string norm = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return _uiStates.RecentFirstFolders.Any(p => string.Equals(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), norm, StringComparison.OrdinalIgnoreCase));
    }

    private void ToggleFolderSortOrder(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return;
        _uiStates.RecentFirstFolders ??= new();
        string norm = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        bool exists = _uiStates.RecentFirstFolders.Any(p => string.Equals(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), norm, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            _uiStates.RecentFirstFolders.RemoveAll(p => string.Equals(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), norm, StringComparison.OrdinalIgnoreCase));
            ShowNotice("Standardsortierung aktiv", $"Ordner '{Path.GetFileName(norm)}' wird wieder alphabetisch sortiert.", InfoBarSeverity.Informational);
        }
        else
        {
            _uiStates.RecentFirstFolders.Add(norm);
            if (!_expandedFolders.Contains(fullPath))
            {
                _expandedFolders.Add(fullPath);
            }
            ShowNotice("Nach Datum sortiert", $"Ordner '{Path.GetFileName(norm)}' sortiert die neuesten Inhalte jetzt nach oben.", InfoBarSeverity.Success);
        }

        SaveUiStates();

        if (_lastFilesProjectPath != null)
        {
            UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
        }
    }

    private static DateTime GetItemLatestTimestamp(string fullPath, bool isDir)
    {
        try
        {
            if (isDir)
            {
                var di = new DirectoryInfo(fullPath);
                return di.LastWriteTimeUtc > di.CreationTimeUtc ? di.LastWriteTimeUtc : di.CreationTimeUtc;
            }
            else
            {
                var fi = new FileInfo(fullPath);
                return fi.LastWriteTimeUtc > fi.CreationTimeUtc ? fi.LastWriteTimeUtc : fi.CreationTimeUtc;
            }
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private void UpdatePersistentFolderSortPaths(string oldPath, string newPath)
    {
        if (_uiStates?.RecentFirstFolders == null || _uiStates.RecentFirstFolders.Count == 0) return;
        string normOld = oldPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normNew = newPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string oldPrefix = normOld + Path.DirectorySeparatorChar;
        string newPrefix = normNew + Path.DirectorySeparatorChar;

        bool changed = false;
        for (int i = 0; i < _uiStates.RecentFirstFolders.Count; i++)
        {
            string p = _uiStates.RecentFirstFolders[i];
            if (p.Equals(normOld, StringComparison.OrdinalIgnoreCase))
            {
                _uiStates.RecentFirstFolders[i] = normNew;
                changed = true;
            }
            else if (p.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _uiStates.RecentFirstFolders[i] = newPrefix + p.Substring(oldPrefix.Length);
                changed = true;
            }
        }
        if (changed) SaveUiStates();
    }

    private void RemovePersistentFolderSortPath(string path)
    {
        if (_uiStates?.RecentFirstFolders == null || _uiStates.RecentFirstFolders.Count == 0) return;
        string norm = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string prefix = norm + Path.DirectorySeparatorChar;

        int removed = _uiStates.RecentFirstFolders.RemoveAll(p => p.Equals(norm, StringComparison.OrdinalIgnoreCase) || p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) SaveUiStates();
    }

    private void UpdateFilesTab(bool isGlobal, string projectPath, string projectName)
    {
        bool isProjectSwitch = !string.Equals(_lastFilesProjectPath, projectPath, StringComparison.OrdinalIgnoreCase);
        _lastFilesIsGlobal = isGlobal;
        _lastFilesProjectPath = projectPath;
        _lastFilesProjectName = projectName;

        EnsureFilesDragDropInitialized();
        EnsureFilesSearchInitialized();

        if (FilesPanel == null) return;
        if (isProjectSwitch)
        {
            FilesPanelScrollViewer?.ChangeView(null, 0, null, true);
            if (TxtFileSearch != null)
            {
                _fileSearchQuery = string.Empty;
                TxtFileSearch.Text = string.Empty;
            }
        }
        FilesPanel.Children.Clear();

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        headerGrid.AllowDrop = true;
        headerGrid.DragOver += (s, e) =>
        {
            if (e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = (e.AllowedOperations & DataPackageOperation.Copy) != 0
                    ? DataPackageOperation.Copy
                    : (e.AllowedOperations & DataPackageOperation.Move) != 0
                        ? DataPackageOperation.Move
                        : DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "In Projekt-Wurzel ablegen";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
            }
        };
        headerGrid.Drop += async (s, e) =>
        {
            if (!string.IsNullOrEmpty(_lastFilesProjectPath) && Directory.Exists(_lastFilesProjectPath))
            {
                e.Handled = true;
                await HandleItemDroppedIntoFolderAsync(e.DataView, _lastFilesProjectPath);
            }
        };

        var header = new TextBlock
        {
            Text = Helpers.Loc.Tr("Sidebars.Files.Header", "PROJEKT-DATEIEN"),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (Application.Current.Resources.TryGetValue("AgySidebarSectionHeaderStyle", out var fileHeaderStyle) && fileHeaderStyle is Style fs)
        {
            header.Style = fs;
        }
        else
        {
            header.FontSize = 12;
            header.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            header.Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"];
            header.CharacterSpacing = 10;
        }
        Grid.SetColumn(header, 0);
        headerGrid.Children.Add(header);

        var rightStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (!isGlobal && Directory.Exists(projectPath) && _expandedFolders.Count > 0)
        {
            var btnCollapseAll = new Button
            {
                Content = Helpers.Loc.Tr("Sidebars.Files.CollapseAll", "Einklappen"),
                FontSize = 11.5,
                Padding = new Thickness(4, 1, 4, 1),
                Height = 20,
                Background = (Brush)Application.Current.Resources["AgySurfaceBg"],
                BorderBrush = (Brush)Application.Current.Resources["AgyBorderDefault"],
                Foreground = (Brush)Application.Current.Resources["AgyTextMuted"],
                CornerRadius = new CornerRadius(4)
            };
            ToolTipService.SetToolTip(btnCollapseAll, Helpers.Loc.Tr("Sidebars.Files.CollapseAllTooltip", "Alle aufgeklappten Ordner einklappen"));
            btnCollapseAll.Click += (s, e) =>
            {
                _expandedFolders.Clear();
                UpdateFilesTab(isGlobal, projectPath, projectName);
            };
            rightStack.Children.Add(btnCollapseAll);
        }

        string? targetFolder = !isGlobal && Directory.Exists(projectPath)
            ? projectPath
            : (!string.IsNullOrEmpty(_projectSettings.BaseFolder) && Directory.Exists(_projectSettings.BaseFolder) ? _projectSettings.BaseFolder : null);

        if (!string.IsNullOrEmpty(targetFolder))
        {
            if (!isGlobal && Directory.Exists(projectPath))
            {
                var btnNewFolder = new Button
                {
                    AllowFocusOnInteraction = true,
                    Width = 22,
                    Height = 22,
                    Padding = new Thickness(0),
                    Background = TreeTransparentBrush,
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(4)
                };
                var iconPlus = new ImageIcon
                {
                    Source = CreateHighResSvg("ms-appx:///Assets/Icons/plus.svg"),
                    Width = 12,
                    Height = 12
                };
                btnNewFolder.Content = iconPlus;
                ToolTipService.SetToolTip(btnNewFolder, Helpers.Loc.Tr("Sidebars.Files.NewFolderTooltip", "Neuen Ordner im Projekt erstellen"));

                var newFolderFlyout = CreateNewFolderFlyout(projectPath);
                btnNewFolder.Flyout = newFolderFlyout;
                rightStack.Children.Add(btnNewFolder);
            }

            var btnOpenExplorer = new Button
            {
                AllowFocusOnInteraction = false,
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Background = TreeTransparentBrush,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4)
            };
            var icon = new ImageIcon
            {
                Source = CreateHighResSvg("ms-appx:///Assets/Icons/folder-open.svg"),
                Width = 14,
                Height = 14
            };
            btnOpenExplorer.Content = icon;
            ToolTipService.SetToolTip(btnOpenExplorer, Helpers.Loc.Tr("Sidebars.Files.OpenExplorerTooltip", "Projektordner im Windows Explorer öffnen"));
            btnOpenExplorer.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = targetFolder,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[Open Explorer Error] {ex.Message}");
                }
            };
            rightStack.Children.Add(btnOpenExplorer);

            var btnToggleHidden = new Button
            {
                AllowFocusOnInteraction = false,
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Background = _showHiddenFiles ? TreeStagedBgBrush : TreeTransparentBrush,
                BorderBrush = _showHiddenFiles ? TreeStagedBorderBrush : TreeTransparentBrush,
                BorderThickness = _showHiddenFiles ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(4)
            };
            var iconEye = new ImageIcon
            {
                Source = CreateHighResSvg("ms-appx:///Assets/Icons/eye.svg"),
                Width = 13,
                Height = 13
            };
            btnToggleHidden.Content = iconEye;
            ToolTipService.SetToolTip(btnToggleHidden, Helpers.Loc.Tr("Sidebars.Files.HiddenFilesTooltip", "Versteckte Dateien ein-/ausblenden"));
            btnToggleHidden.Click += (s, e) =>
            {
                _showHiddenFiles = !_showHiddenFiles;
                UpdateFilesTab(isGlobal, projectPath, projectName);
            };
            rightStack.Children.Add(btnToggleHidden);
        }

        Grid.SetColumn(rightStack, 1);
        headerGrid.Children.Add(rightStack);

        if (FilesHeaderContainer != null)
        {
            FilesHeaderContainer.Content = headerGrid;
        }
        else
        {
            FilesPanel.Children.Add(headerGrid);
        }

        if (isGlobal)
        {
            if (PnlFileSearch != null) PnlFileSearch.Visibility = Visibility.Collapsed;
            var msg = new TextBlock
            {
                Text = "Bitte wähle oben ein konkretes Projekt aus, um dessen Dateien anzuzeigen.",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["AgyTextMuted"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            FilesPanel.Children.Add(msg);
            return;
        }

        if (Directory.Exists(projectPath))
        {
            if (PnlFileSearch != null) PnlFileSearch.Visibility = Visibility.Visible;

            if (!string.IsNullOrEmpty(_fileSearchQuery))
            {
                RenderFileSearchResults(projectPath, _fileSearchQuery);
            }
            else
            {
                try
                {
                    RenderDirectoryLevel(projectPath, level: 0);
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[FilesTab Error] {ex.Message}");
                }
            }
        }
        else
        {
            if (PnlFileSearch != null) PnlFileSearch.Visibility = Visibility.Collapsed;
            var msg = new TextBlock
            {
                Text = $"Projektordner '{projectName}' wurde nicht gefunden.",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                Margin = new Thickness(0, 8, 0, 0)
            };
            FilesPanel.Children.Add(msg);
        }
    }

    private void RenderDirectoryLevel(string dirPath, int level)
    {
        if (!Directory.Exists(dirPath) || level > 8) return;

        try
        {
            var dirs = _fileService.GetDirectories(dirPath, _showHiddenFiles);
            var files = _fileService.GetFiles(dirPath, _showHiddenFiles);

            if (IsFolderSortedRecentFirst(dirPath))
            {
                dirs = dirs.OrderByDescending(d => GetItemLatestTimestamp(Path.Combine(dirPath, d), isDir: true)).ToList();
                files = files.OrderByDescending(f => GetItemLatestTimestamp(Path.Combine(dirPath, f), isDir: false)).ToList();
            }

            foreach (var d in dirs)
            {
                try
                {
                    string fullPath = Path.Combine(dirPath, d);
                    bool isExpanded = _expandedFolders.Contains(fullPath);

                    var btn = CreateFileTreeButton(d, isFolder: true, fullPath: fullPath, level: level, isExpanded: isExpanded);
                    FilesPanel.Children.Add(btn);

                    if (isExpanded)
                    {
                        RenderDirectoryLevel(fullPath, level + 1);
                    }
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[FilesTab Folder Error] {d}: {ex.Message}");
                }
            }

            foreach (var f in files)
            {
                try
                {
                    string fullPath = Path.Combine(dirPath, f);
                    var btn = CreateFileTreeButton(f, isFolder: false, fullPath: fullPath, level: level, isExpanded: false);
                    FilesPanel.Children.Add(btn);
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[FilesTab File Error] {f}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[FilesTab Render Level Error] {dirPath}: {ex.Message}");
        }
    }

    private void EnsureFilesSearchInitialized()
    {
        if (_filesSearchInitialized || TxtFileSearch == null) return;
        _filesSearchInitialized = true;

        _fileSearchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _fileSearchDebounceTimer.Tick += (s, e) =>
        {
            _fileSearchDebounceTimer.Stop();
            PerformFileSearch();
        };

        TxtFileSearch.TextChanged += (s, e) =>
        {
            string newQuery = TxtFileSearch.Text?.Trim() ?? string.Empty;
            if (BtnClearFileSearch != null)
            {
                BtnClearFileSearch.Visibility = string.IsNullOrEmpty(newQuery) ? Visibility.Collapsed : Visibility.Visible;
            }

            if (string.IsNullOrEmpty(newQuery))
            {
                _fileSearchDebounceTimer?.Stop();
                PerformFileSearch();
            }
            else
            {
                _fileSearchDebounceTimer?.Stop();
                _fileSearchDebounceTimer?.Start();
            }
        };

        TxtFileSearch.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                TxtFileSearch.Text = string.Empty;
                e.Handled = true;
            }
        };

        if (BtnClearFileSearch != null)
        {
            BtnClearFileSearch.Click += (s, e) =>
            {
                TxtFileSearch.Text = string.Empty;
                TxtFileSearch.Focus(FocusState.Programmatic);
            };
        }
    }

    private void PerformFileSearch()
    {
        string query = TxtFileSearch?.Text?.Trim() ?? string.Empty;
        if (string.Equals(_fileSearchQuery, query, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _fileSearchQuery = query;

        if (string.IsNullOrEmpty(_fileSearchQuery))
        {
            UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath ?? "", _lastFilesProjectName ?? "");
            return;
        }

        if (!string.IsNullOrEmpty(_lastFilesProjectPath) && Directory.Exists(_lastFilesProjectPath))
        {
            RenderFileSearchResults(_lastFilesProjectPath, _fileSearchQuery);
        }
    }

    private void RenderFileSearchResults(string rootPath, string query)
    {
        FilesPanel.Children.Clear();

        var matches = new List<string>();
        var folderMatches = new List<string>();
        const int maxMatches = 150;

        try
        {
            var queue = new Queue<string>();
            queue.Enqueue(rootPath);

            while (queue.Count > 0 && matches.Count + folderMatches.Count < maxMatches)
            {
                string currentDir = queue.Dequeue();

                var subDirs = _fileService.GetDirectories(currentDir, _showHiddenFiles);
                foreach (var d in subDirs)
                {
                    string subDirPath = Path.Combine(currentDir, d);
                    string relPath = Path.GetRelativePath(rootPath, subDirPath).Replace('\\', '/');

                    if (d.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        relPath.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        folderMatches.Add(subDirPath);
                    }

                    queue.Enqueue(subDirPath);
                }

                var files = _fileService.GetFiles(currentDir, _showHiddenFiles);
                foreach (var f in files)
                {
                    string filePath = Path.Combine(currentDir, f);
                    string relPath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');

                    if (f.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        relPath.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(filePath);
                        if (matches.Count + folderMatches.Count >= maxMatches)
                        {
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[FilesTab Search Error] {ex.Message}");
        }

        int totalMatches = folderMatches.Count + matches.Count;
        var infoPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 4) };
        var infoText = new TextBlock
        {
            Text = totalMatches == 0
                ? $"Keine Treffer für \"{query}\""
                : (totalMatches >= maxMatches
                    ? $"{totalMatches}+ Treffer für \"{query}\" (Suche verfeinern)"
                    : $"{totalMatches} Treffer für \"{query}\""),
            FontSize = 11.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = totalMatches == 0
                ? (Brush)Application.Current.Resources["AgyTextSubtle"]
                : (Brush)Application.Current.Resources["AgyTextSecondary"],
            VerticalAlignment = VerticalAlignment.Center
        };
        infoPanel.Children.Add(infoText);
        FilesPanel.Children.Add(infoPanel);

        if (totalMatches == 0)
        {
            var hint = new TextBlock
            {
                Text = "Tipp: Drücke Esc oder leere das Suchfeld, um den gesamten Dateibaum anzuzeigen.",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["AgyTextMuted"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            FilesPanel.Children.Add(hint);
            return;
        }

        foreach (var folderPath in folderMatches)
        {
            string folderName = Path.GetFileName(folderPath);
            string relDir = Path.GetRelativePath(rootPath, folderPath).Replace('\\', '/');
            string? sub = relDir != folderName ? Path.GetDirectoryName(relDir)?.Replace('\\', '/') + "/" : null;

            var btn = CreateFileTreeButton(folderName, isFolder: true, fullPath: folderPath, level: 0, isExpanded: false, subtitle: sub);
            FilesPanel.Children.Add(btn);
        }

        foreach (var filePath in matches)
        {
            string fileName = Path.GetFileName(filePath);
            string relPath = Path.GetRelativePath(rootPath, filePath).Replace('\\', '/');
            string? sub = Path.GetDirectoryName(relPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(sub)) sub += "/";

            var btn = CreateFileTreeButton(fileName, isFolder: false, fullPath: filePath, level: 0, isExpanded: false, subtitle: sub);
            FilesPanel.Children.Add(btn);
        }
    }

    private static SvgImageSource CreateHighResSvg(string uriPath, double rasterSize = 64.0)
    {
        return new SvgImageSource(new Uri(uriPath))
        {
            RasterizePixelWidth = rasterSize,
            RasterizePixelHeight = rasterSize
        };
    }

    private static void OptimizeImageIconRasterization(DependencyObject parent)
    {
        if (parent == null) return;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ImageIcon imgIcon && imgIcon.Source is SvgImageSource svg)
            {
                svg.RasterizePixelWidth = 64.0;
                svg.RasterizePixelHeight = 64.0;
            }
            else if (child is Image img && img.Source is SvgImageSource imgSvg)
            {
                imgSvg.RasterizePixelWidth = 64.0;
                imgSvg.RasterizePixelHeight = 64.0;
            }
            OptimizeImageIconRasterization(child);
        }
    }

    private Button CreateFileTreeButton(string name, bool isFolder, string fullPath, int level = 0, bool isExpanded = false, string? subtitle = null)
    {
        string normFullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        bool isStaged = _stagedAttachments.Any(a => a.FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(normFullPath, StringComparison.OrdinalIgnoreCase));

        string? projectRoot = !string.IsNullOrEmpty(_lastFilesProjectPath) && Directory.Exists(_lastFilesProjectPath)
            ? _lastFilesProjectPath
            : (!string.IsNullOrEmpty(_projectSettings.BaseFolder) && Directory.Exists(_projectSettings.BaseFolder) ? _projectSettings.BaseFolder : null);

        var ignoreStatus = !string.IsNullOrEmpty(projectRoot)
            ? Helpers.IgnoreRuleMatcher.CheckIgnoreStatus(projectRoot, fullPath, isFolder)
            : Helpers.IgnoreStatus.Empty;

        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = isStaged ? TreeStagedBgBrush : TreeTransparentBrush,
            BorderThickness = isStaged ? new Thickness(1.5) : new Thickness(0),
            BorderBrush = isStaged ? TreeStagedBorderBrush : TreeTransparentBrush,
            CornerRadius = isStaged ? new CornerRadius(6) : new CornerRadius(0),
            Padding = new Thickness(4, 3, 4, 3),
            Margin = new Thickness(level * 12, 0, 0, 2),
            Tag = fullPath
        };

        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };

        if (isFolder)
        {
            var txtArrow = new TextBlock
            {
                Text = isExpanded ? "▼" : "▶",
                FontSize = 10,
                Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                VerticalAlignment = VerticalAlignment.Center,
                Width = 10
            };
            sp.Children.Add(txtArrow);

            string iconPath = isExpanded ? "ms-appx:///Assets/Icons/folder-open.svg" : "ms-appx:///Assets/Icons/folder.svg";
            var icon = new ImageIcon
            {
                Source = CreateHighResSvg(iconPath),
                Width = 14,
                Height = 14
            };
            sp.Children.Add(icon);

            Brush textBrush = isStaged
                ? TreeStagedBorderBrush
                : (ignoreStatus.IsAnyIgnored
                    ? (Brush)Application.Current.Resources["AgyTextSubtle"]
                    : (Application.Current.Resources.ContainsKey("AgyTextSecondary")
                        ? (Brush)Application.Current.Resources["AgyTextSecondary"]
                        : TreeSlate203Brush));

            var txt = new TextBlock
            {
                Text = name + "/",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = textBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sp.Children.Add(txt);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var txtSub = new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11.5,
                    Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                sp.Children.Add(txtSub);
            }

            bool isRecentFirst = IsFolderSortedRecentFirst(fullPath);
            if (isRecentFirst)
            {
                var sortBadge = new Border
                {
                    Background = TreeSortBgBrush,
                    BorderBrush = TreeSortBorderBrush,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(4, 1, 4, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "🕒 Neueste",
                        FontSize = 9.5,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = TreeSortTextBrush
                    }
                };
                ToolTipService.SetToolTip(sortBadge, "Ordnerinhalt ist nach Datum sortiert (neueste oben). Rechtsklick zum Umschalten.");
                sp.Children.Add(sortBadge);
            }

            if (ignoreStatus.IsGitIgnored)
            {
                sp.Children.Add(CreateIgnoreIconBadge("ms-appx:///Assets/Icons/hat-glasses-amber.svg", "In .gitignore ignoriert"));
            }
            if (ignoreStatus.IsGeminiIgnored)
            {
                sp.Children.Add(CreateIgnoreIconBadge("ms-appx:///Assets/Icons/hat-glasses-violet.svg", "In .geminiignore ignoriert"));
            }
            if (ignoreStatus.IsReflectionIgnored)
            {
                sp.Children.Add(CreateIgnoreIconBadge("ms-appx:///Assets/Icons/hat-glasses-cyan.svg", "In .reflectionignore ignoriert"));
            }

            if (isStaged)
            {
                var badge = new Border
                {
                    Background = TreeStagedBorderBrush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "Kontext",
                        FontSize = 9.5,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = TreeWhiteBrush
                    }
                };
                ToolTipService.SetToolTip(badge, "Ordner ist im Prompt-Kontext eingebunden");
                sp.Children.Add(badge);
            }

            var flyout = new MenuFlyout();

            var referenceFolderItem = new MenuFlyoutItem
            {
                Text = isStaged ? "Bezug im Chat aufheben" : "Im Chat darauf beziehen",
                Icon = new FontIcon { Glyph = "\uE8BD" }
            };
            referenceFolderItem.Click += (s, e) => StageFileAttachment(fullPath);
            flyout.Items.Add(referenceFolderItem);

            var openExplorerItem = new MenuFlyoutItem
            {
                Text = "Im Explorer öffnen",
                Icon = new FontIcon { Glyph = "\uE8DA" }
            };
            openExplorerItem.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = fullPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[Open Explorer Error] {ex.Message}");
                }
            };
            flyout.Items.Add(openExplorerItem);

            var sortFolderItem = new ToggleMenuFlyoutItem
            {
                Text = "Nach Datum sortieren (neueste oben)",
                Icon = new FontIcon { Glyph = "\uE8CB" },
                IsChecked = isRecentFirst
            };
            sortFolderItem.Click += (s, e) => ToggleFolderSortOrder(fullPath);
            flyout.Items.Add(sortFolderItem);

            var newSubfolderItem = new MenuFlyoutItem
            {
                Text = "Neuer Unterordner...",
                Icon = new FontIcon { Glyph = "\uE8F4" }
            };
            newSubfolderItem.Click += (s, e) =>
            {
                var f = CreateNewFolderFlyout(fullPath);
                f.ShowAt(btn);
            };
            flyout.Items.Add(newSubfolderItem);

            var renameFolderItem = new MenuFlyoutItem
            {
                Text = "Umbenennen...",
                Icon = new FontIcon { Glyph = "\uE8AC" }
            };
            renameFolderItem.Click += (s, e) =>
            {
                var f = CreateRenameFlyout(fullPath, isFolder: true);
                f.ShowAt(btn);
            };
            flyout.Items.Add(renameFolderItem);

            var moveFolderItem = new MenuFlyoutItem
            {
                Text = "Verschieben nach...",
                Icon = new FontIcon { Glyph = "\uE8DE" }
            };
            moveFolderItem.Click += (s, e) =>
            {
                var f = CreateMoveFlyout(fullPath, isFolder: true);
                f.ShowAt(btn);
            };
            flyout.Items.Add(moveFolderItem);

            var copyRelPathItem = new MenuFlyoutItem
            {
                Text = "Relativen Pfad kopieren",
                Icon = new FontIcon { Glyph = "\uE8C8" }
            };
            copyRelPathItem.Click += (s, e) => CopyFilePathToClipboard(fullPath, isRelative: true);
            flyout.Items.Add(copyRelPathItem);

            var copyAbsPathItem = new MenuFlyoutItem
            {
                Text = "Absoluten Pfad kopieren",
                Icon = new FontIcon { Glyph = "\uE8C8" }
            };
            copyAbsPathItem.Click += (s, e) => CopyFilePathToClipboard(fullPath, isRelative: false);
            flyout.Items.Add(copyAbsPathItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var ignoreFolderItem = CreateIgnoreSubItem(fullPath, isFolder: true, ignoreStatus);
            flyout.Items.Add(ignoreFolderItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var deleteFolderItem = new MenuFlyoutItem
            {
                Text = "Ordner löschen",
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            deleteFolderItem.Click += (s, e) =>
            {
                var f = CreateDeleteFlyout(fullPath, isFolder: true);
                f.ShowAt(btn);
            };
            flyout.Items.Add(deleteFolderItem);

            btn.ContextFlyout = flyout;

            btn.CanDrag = true;
            btn.DragStarting += (s, e) =>
            {
                e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
                e.Data.SetText(fullPath);
                e.Data.Properties.Title = name + "/";
                try
                {
                    if (Directory.Exists(fullPath))
                    {
                        var storageItem = Windows.Storage.StorageFolder.GetFolderFromPathAsync(fullPath).AsTask().GetAwaiter().GetResult();
                        if (storageItem != null) e.Data.SetStorageItems(new[] { storageItem });
                    }
                }
                catch { }
            };

            btn.AllowDrop = true;
            btn.DragOver += (s, e) =>
            {
                if (e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    e.AcceptedOperation = (e.AllowedOperations & DataPackageOperation.Copy) != 0
                        ? DataPackageOperation.Copy
                        : (e.AllowedOperations & DataPackageOperation.Move) != 0
                            ? DataPackageOperation.Move
                            : DataPackageOperation.Copy;
                    e.DragUIOverride.Caption = $"In '{name}/' ablegen";
                    e.DragUIOverride.IsCaptionVisible = true;
                    e.DragUIOverride.IsGlyphVisible = true;
                    btn.Background = TreeStagedBgBrush;
                }
            };
            btn.DragLeave += (s, e) =>
            {
                btn.Background = isStaged ? TreeStagedBgBrush : TreeTransparentBrush;
            };
            btn.Drop += async (s, e) =>
            {
                e.Handled = true;
                btn.Background = isStaged ? TreeStagedBgBrush : TreeTransparentBrush;
                await HandleItemDroppedIntoFolderAsync(e.DataView, fullPath);
            };

            string folderTooltip = fullPath;
            if (isRecentFirst)
            {
                folderTooltip += "\n[Sortierung: Neueste oben (nach Datum)]";
            }
            if (ignoreStatus.IsAnyIgnored)
            {
                var listNames = new List<string>();
                if (ignoreStatus.IsGitIgnored) listNames.Add(".gitignore");
                if (ignoreStatus.IsGeminiIgnored) listNames.Add(".geminiignore");
                if (ignoreStatus.IsReflectionIgnored) listNames.Add(".reflectionignore");
                folderTooltip += $"\n[Ignoriert in: {string.Join(", ", listNames)}]";
            }
            folderTooltip += "\n(Klick: Auf/Zuklappen | Rechtsklick: Aktionen)";
            ToolTipService.SetToolTip(btn, folderTooltip);

            btn.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_fileSearchQuery))
                {
                    _expandedFolders.Add(fullPath);
                    if (TxtFileSearch != null) TxtFileSearch.Text = string.Empty;
                    return;
                }

                if (_expandedFolders.Contains(fullPath))
                {
                    _expandedFolders.Remove(fullPath);
                }
                else
                {
                    _expandedFolders.Add(fullPath);
                }

                if (_lastFilesProjectPath != null)
                {
                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                }
            };
        }
        else
        {
            var spacer = new TextBlock { Width = 10 };
            sp.Children.Add(spacer);

            string ext = Path.GetExtension(name).ToLowerInvariant();
            string iconPath = ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tiff" or ".tif" => "ms-appx:///Assets/Icons/eye.svg",
                _ => "ms-appx:///Assets/Icons/file-text.svg"
            };

            var icon = new ImageIcon
            {
                Source = CreateHighResSvg(iconPath),
                Width = 13,
                Height = 13
            };
            sp.Children.Add(icon);

            Brush fileTextBrush = isStaged
                ? TreeStagedBorderBrush
                : (ignoreStatus.IsAnyIgnored
                    ? (Brush)Application.Current.Resources["AgyTextSubtle"]
                    : (Brush)Application.Current.Resources["AgyTextMuted"]);

            var txt = new TextBlock
            {
                Text = name,
                FontSize = 14,
                Foreground = fileTextBrush,
                FontWeight = isStaged ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sp.Children.Add(txt);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var txtSub = new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11.5,
                    Foreground = (Brush)Application.Current.Resources["AgyTextSubtle"],
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                sp.Children.Add(txtSub);
            }

            if (ignoreStatus.IsGitIgnored)
            {
                sp.Children.Add(CreateIgnoreIconBadge("ms-appx:///Assets/Icons/hat-glasses-amber.svg", "In .gitignore ignoriert"));
            }
            if (ignoreStatus.IsGeminiIgnored)
            {
                sp.Children.Add(CreateIgnoreIconBadge("ms-appx:///Assets/Icons/hat-glasses-violet.svg", "In .geminiignore ignoriert"));
            }
            if (ignoreStatus.IsReflectionIgnored)
            {
                sp.Children.Add(CreateIgnoreIconBadge("ms-appx:///Assets/Icons/hat-glasses-cyan.svg", "In .reflectionignore ignoriert"));
            }

            if (isStaged)
            {
                var badge = new Border
                {
                    Background = TreeStagedBorderBrush,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 1, 5, 1),
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "Kontext",
                        FontSize = 9.5,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = TreeWhiteBrush
                    }
                };
                ToolTipService.SetToolTip(badge, "Datei ist im Prompt-Kontext eingebunden");
                sp.Children.Add(badge);
            }

            var flyout = new MenuFlyout();

            var referenceFileItem = new MenuFlyoutItem
            {
                Text = isStaged ? "Bezug im Chat aufheben" : "Im Chat darauf beziehen",
                Icon = new FontIcon { Glyph = "\uE8BD" }
            };
            referenceFileItem.Click += (s, e) => StageFileAttachment(fullPath);
            flyout.Items.Add(referenceFileItem);

            var openExplorerItem = new MenuFlyoutItem
            {
                Text = "Im Explorer anzeigen",
                Icon = new FontIcon { Glyph = "\uE8DA" }
            };
            openExplorerItem.Click += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{fullPath}\"",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[Open Explorer Select Error] {ex.Message}");
                }
            };
            flyout.Items.Add(openExplorerItem);

            var renameFileItem = new MenuFlyoutItem
            {
                Text = "Umbenennen...",
                Icon = new FontIcon { Glyph = "\uE8AC" }
            };
            renameFileItem.Click += (s, e) =>
            {
                var f = CreateRenameFlyout(fullPath, isFolder: false);
                f.ShowAt(btn);
            };
            flyout.Items.Add(renameFileItem);

            var moveFileItem = new MenuFlyoutItem
            {
                Text = "Verschieben nach...",
                Icon = new FontIcon { Glyph = "\uE8DE" }
            };
            moveFileItem.Click += (s, e) =>
            {
                var f = CreateMoveFlyout(fullPath, isFolder: false);
                f.ShowAt(btn);
            };
            flyout.Items.Add(moveFileItem);

            var copyRelPathItem = new MenuFlyoutItem
            {
                Text = "Relativen Pfad kopieren",
                Icon = new FontIcon { Glyph = "\uE8C8" }
            };
            copyRelPathItem.Click += (s, e) => CopyFilePathToClipboard(fullPath, isRelative: true);
            flyout.Items.Add(copyRelPathItem);

            var copyAbsPathItem = new MenuFlyoutItem
            {
                Text = "Absoluten Pfad kopieren",
                Icon = new FontIcon { Glyph = "\uE8C8" }
            };
            copyAbsPathItem.Click += (s, e) => CopyFilePathToClipboard(fullPath, isRelative: false);
            flyout.Items.Add(copyAbsPathItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var ignoreFileItem = CreateIgnoreSubItem(fullPath, isFolder: false, ignoreStatus);
            flyout.Items.Add(ignoreFileItem);

            flyout.Items.Add(new MenuFlyoutSeparator());

            var deleteFileItem = new MenuFlyoutItem
            {
                Text = "Datei löschen",
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            deleteFileItem.Click += (s, e) =>
            {
                var f = CreateDeleteFlyout(fullPath, isFolder: false);
                f.ShowAt(btn);
            };
            flyout.Items.Add(deleteFileItem);

            btn.ContextFlyout = flyout;

            btn.CanDrag = true;
            btn.DragStarting += (s, e) =>
            {
                e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
                e.Data.SetText(fullPath);
                e.Data.Properties.Title = name;
                try
                {
                    if (File.Exists(fullPath))
                    {
                        var storageItem = Windows.Storage.StorageFile.GetFileFromPathAsync(fullPath).AsTask().GetAwaiter().GetResult();
                        if (storageItem != null) e.Data.SetStorageItems(new[] { storageItem });
                    }
                }
                catch { }
            };

            btn.AllowDrop = true;
            btn.DragOver += (s, e) =>
            {
                if (e.DataView.Contains(StandardDataFormats.Text) || e.DataView.Contains(StandardDataFormats.StorageItems))
                {
                    string parentDir = Path.GetDirectoryName(fullPath) ?? _lastFilesProjectPath ?? "";
                    string parentDisplay = !string.IsNullOrEmpty(_lastFilesProjectPath) && string.Equals(parentDir, _lastFilesProjectPath, StringComparison.OrdinalIgnoreCase)
                        ? "Projekt-Wurzel"
                        : Path.GetFileName(parentDir) + "/";

                    e.AcceptedOperation = (e.AllowedOperations & DataPackageOperation.Copy) != 0
                        ? DataPackageOperation.Copy
                        : (e.AllowedOperations & DataPackageOperation.Move) != 0
                            ? DataPackageOperation.Move
                            : DataPackageOperation.Copy;
                    e.DragUIOverride.Caption = $"In '{parentDisplay}' ablegen";
                    e.DragUIOverride.IsCaptionVisible = true;
                    e.DragUIOverride.IsGlyphVisible = true;
                    btn.Background = TreeStagedBgBrush;
                }
            };
            btn.DragLeave += (s, e) =>
            {
                btn.Background = isStaged ? TreeStagedBgBrush : TreeTransparentBrush;
            };
            btn.Drop += async (s, e) =>
            {
                e.Handled = true;
                btn.Background = isStaged ? TreeStagedBgBrush : TreeTransparentBrush;
                string parentDir = Path.GetDirectoryName(fullPath) ?? _lastFilesProjectPath ?? "";
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    await HandleItemDroppedIntoFolderAsync(e.DataView, parentDir);
                }
            };

            string fileTooltip = fullPath;
            if (ignoreStatus.IsAnyIgnored)
            {
                var listNames = new List<string>();
                if (ignoreStatus.IsGitIgnored) listNames.Add(".gitignore");
                if (ignoreStatus.IsGeminiIgnored) listNames.Add(".geminiignore");
                if (ignoreStatus.IsReflectionIgnored) listNames.Add(".reflectionignore");
                fileTooltip += $"\n[Ignoriert in: {string.Join(", ", listNames)}]";
            }
            fileTooltip += "\n(Klick: Öffnen | Rechtsklick: Aktionen)";
            ToolTipService.SetToolTip(btn, fileTooltip);

            btn.Click += (s, e) =>
            {
                if (File.Exists(fullPath))
                {
                    OpenFileItem(fullPath);
                }
            };
        }

        btn.Content = sp;
        return btn;
    }

    private void OpenFileItem(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".pdf")
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "chrome.exe",
                        Arguments = $"\"{filePath}\"",
                        UseShellExecute = true
                    });
                    TelemetryLogger.Log($"[OpenFileItem] Opened PDF in Chrome: {filePath}");
                    return;
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[OpenFileItem Chrome Error] {ex.Message} - Falling back to default app");
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        });
                        return;
                    }
                    catch (Exception fallbackEx)
                    {
                        TelemetryLogger.Log($"[OpenFileItem Fallback Error] {fallbackEx.Message}");
                    }
                }
            }

            var externalExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Images
                ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif",
                // Audio & Video
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a",
                // Office & Documents
                ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt",
                // Archives & Executables
                ".zip", ".rar", ".7z", ".tar", ".gz", ".exe", ".msi", ".dll", ".bin", ".iso"
            };

            if (externalExtensions.Contains(ext))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                    TelemetryLogger.Log($"[OpenFileItem] Opened external file with default application: {filePath}");
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[OpenFileItem Error] {ex.Message}");
                }
                return;
            }

            // Code & Text Files -> Built-in Text Editor
            OpenFileInEditor(filePath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[OpenFileItem Global Error] {ex.Message}");
        }
    }

    private async void OpenFileInEditor(string filePath, string? preRenderedRtf = null)
    {
        try
        {
            var editor = UcEditor;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) || editor == null) return;

            // Vorherige Datei bei direktem Wechsel zwischen zwei Dateien auto-sichern
            if (!string.IsNullOrEmpty(_currentEditingFilePath) &&
                !string.Equals(_currentEditingFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (_currentEditingFilePath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteAllTextAtomic(_currentEditingFilePath, editor.GetRtf());
                    }
                    else
                    {
                        WriteAllTextAtomic(_currentEditingFilePath, editor.GetText());
                    }
                }
                catch { }
            }

            try
            {
                if (!string.IsNullOrEmpty(preRenderedRtf))
                {
                    editor.SetEditorMode(isDocumentMode: true);
                    editor.SetRtf(preRenderedRtf);
                }
                else if (filePath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
                {
                    editor.SetEditorMode(isDocumentMode: true);
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
                    string content = await sr.ReadToEndAsync();
                    editor.SetRtf(content);
                }
                else
                {
                    editor.SetEditorMode(isDocumentMode: false);
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var sr = new StreamReader(fs, System.Text.Encoding.UTF8);
                    string content = await sr.ReadToEndAsync();
                    editor.SetText(content);
                }

                if (!_openTabFilePaths.Contains(filePath, StringComparer.OrdinalIgnoreCase))
                {
                    _openTabFilePaths.Add(filePath);
                }
                _currentEditingFilePath = filePath;
            }
            catch (Exception readEx)
            {
                TelemetryLogger.Log($"[OpenFileInEditor] Fehler beim Lesen von '{filePath}': {readEx.Message}");
                ShowNotice("Lesefehler", $"Die Datei '{Path.GetFileName(filePath)}' konnte nicht gelesen werden (evtl. durch anderen Prozess gesperrt).", InfoBarSeverity.Error);
                return;
            }

            if (TxtEditorTitle != null) TxtEditorTitle.Text = Path.GetFileName(filePath);
            if (TxtEditorPath != null) TxtEditorPath.Text = filePath;

            SwitchToTab(filePath);
            try { editor.RichFileEditor?.Focus(FocusState.Programmatic); } catch { }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[OpenFileInEditor Error] {ex.Message}");
        }
    }

    private void CloseFileTab(string filePath)
    {
        _openTabFilePaths.RemoveAll(p => string.Equals(p, filePath, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_currentEditingFilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            if (_openTabFilePaths.Count > 0)
            {
                OpenFileInEditor(_openTabFilePaths[_openTabFilePaths.Count - 1]);
            }
            else
            {
                SwitchToTerminalTab();
            }
        }
        else
        {
            RenderWorkspaceTabs();
        }
    }

    private Flyout CreateNewFolderFlyout(string parentDir)
    {
        var flyout = new Flyout();
        var sp = new StackPanel { Spacing = 8, Width = 220, Padding = new Thickness(4) };
        var txtTitle = new TextBlock
        {
            Text = "Neuen Ordner erstellen",
            FontSize = 12.5,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AgyTextSecondary"]
        };
        var txtBox = new TextBox
        {
            PlaceholderText = "Ordnername...",
            FontSize = 12.5,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["AgyMainBg"],
            Foreground = (Brush)Application.Current.Resources["AgyTextPrimary"]
        };
        var btnCreate = new Button
        {
            Content = "Erstellen",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = (Brush)Application.Current.Resources["AgyAccent"],
            Foreground = (Brush)Application.Current.Resources["AgyTextPrimary"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(0, 4, 0, 4)
        };

        void ExecuteCreate()
        {
            string folderName = txtBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(folderName)) return;

            try
            {
                string targetPath = Path.Combine(parentDir, folderName);
                if (Directory.Exists(targetPath))
                {
                    ShowNotice("Hinweis", $"Ordner '{folderName}' existiert bereits.", InfoBarSeverity.Warning);
                    return;
                }

                Directory.CreateDirectory(targetPath);
                _expandedFolders.Add(parentDir);
                _expandedFolders.Add(targetPath);
                flyout.Hide();

                if (_lastFilesProjectPath != null)
                {
                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                }
                ShowNotice("Ordner erstellt", $"Ordner '{folderName}' wurde erfolgreich erstellt.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[CreateFolder Error] {ex.Message}");
                ShowNotice("Fehler", $"Ordner konnte nicht erstellt werden: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        btnCreate.Click += (s, e) => ExecuteCreate();
        txtBox.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                ExecuteCreate();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                flyout.Hide();
            }
        };

        sp.Children.Add(txtTitle);
        sp.Children.Add(txtBox);
        sp.Children.Add(btnCreate);
        flyout.Content = sp;

        flyout.Opened += (s, e) =>
        {
            txtBox.Text = string.Empty;
            txtBox.Focus(FocusState.Programmatic);
            txtBox.SelectAll();
        };

        return flyout;
    }

    private Flyout CreateRenameFlyout(string fullPath, bool isFolder)
    {
        var flyout = new Flyout();
        var sp = new StackPanel { Spacing = 8, Width = 230, Padding = new Thickness(4) };
        string currentName = isFolder ? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : Path.GetFileName(fullPath);
        var txtTitle = new TextBlock
        {
            Text = isFolder ? "Ordner umbenennen" : "Datei umbenennen",
            FontSize = 12.5,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AgyTextSecondary"]
        };
        var txtBox = new TextBox
        {
            Text = currentName,
            FontSize = 12.5,
            CornerRadius = new CornerRadius(4),
            Background = (Brush)Application.Current.Resources["AgyMainBg"],
            Foreground = (Brush)Application.Current.Resources["AgyTextPrimary"]
        };
        var btnRename = new Button
        {
            Content = "Umbenennen",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = (Brush)Application.Current.Resources["AgyAccent"],
            Foreground = (Brush)Application.Current.Resources["AgyTextPrimary"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(0, 4, 0, 4)
        };

        void ExecuteRename()
        {
            string newName = txtBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, currentName, StringComparison.Ordinal)) return;

            try
            {
                string cleanFullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string? parentDir = Path.GetDirectoryName(cleanFullPath);
                if (parentDir == null) return;
                string newPath = Path.Combine(parentDir, newName);

                if (isFolder)
                {
                    if (Directory.Exists(newPath))
                    {
                        ShowNotice("Hinweis", $"Ein Ordner mit dem Namen '{newName}' existiert bereits.", InfoBarSeverity.Warning);
                        return;
                    }
                    Directory.Move(fullPath, newPath);

                    // Offene Dateitabs und aufgeklappte Unterordner aktualisieren, die innerhalb des umbenannten Ordners liegen
                    string oldPrefix = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string newPrefix = newPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                    var affectedExpanded = _expandedFolders.Where(f => f.Equals(fullPath, StringComparison.OrdinalIgnoreCase) || f.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var exp in affectedExpanded)
                    {
                        _expandedFolders.Remove(exp);
                        if (exp.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                            _expandedFolders.Add(newPath);
                        else
                            _expandedFolders.Add(newPrefix + exp.Substring(oldPrefix.Length));
                    }
                    UpdatePersistentFolderSortPaths(fullPath, newPath);
                    bool tabChanged = false;
                    for (int i = 0; i < _openTabFilePaths.Count; i++)
                    {
                        if (_openTabFilePaths[i].StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            _openTabFilePaths[i] = newPrefix + _openTabFilePaths[i].Substring(oldPrefix.Length);
                            tabChanged = true;
                        }
                    }
                    if (_currentEditingFilePath != null && _currentEditingFilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentEditingFilePath = newPrefix + _currentEditingFilePath.Substring(oldPrefix.Length);
                        if (TxtEditorPath != null) TxtEditorPath.Text = _currentEditingFilePath;
                        tabChanged = true;
                    }
                    if (tabChanged)
                    {
                        RenderWorkspaceTabs();
                    }
                }
                else
                {
                    if (File.Exists(newPath))
                    {
                        ShowNotice("Hinweis", $"Eine Datei mit dem Namen '{newName}' existiert bereits.", InfoBarSeverity.Warning);
                        return;
                    }
                    File.Move(fullPath, newPath);

                    // Update open tabs if file was open
                    if (_openTabFilePaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                    {
                        _openTabFilePaths.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
                        _openTabFilePaths.Add(newPath);
                        if (string.Equals(_currentEditingFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _currentEditingFilePath = newPath;
                            if (TxtEditorTitle != null) TxtEditorTitle.Text = newName;
                            if (TxtEditorPath != null) TxtEditorPath.Text = newPath;
                        }
                        RenderWorkspaceTabs();
                    }
                }

                flyout.Hide();
                if (_lastFilesProjectPath != null)
                {
                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                }
                ShowNotice("Umbenannt", $"'{currentName}' wurde in '{newName}' umbenannt.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[Rename Error] {ex.Message}");
                ShowNotice("Fehler", $"Umbenennen fehlgeschlagen: {ex.Message}", InfoBarSeverity.Error);
            }
        }

        btnRename.Click += (s, e) => ExecuteRename();
        txtBox.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                ExecuteRename();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                flyout.Hide();
            }
        };

        sp.Children.Add(txtTitle);
        sp.Children.Add(txtBox);
        sp.Children.Add(btnRename);
        flyout.Content = sp;

        flyout.Opened += (s, e) =>
        {
            txtBox.Text = currentName;
            txtBox.Focus(FocusState.Programmatic);
            txtBox.SelectAll();
        };

        return flyout;
    }

    private Flyout CreateDeleteFlyout(string fullPath, bool isFolder)
    {
        var flyout = new Flyout();
        var sp = new StackPanel { Spacing = 8, Width = 220, Padding = new Thickness(4) };
        string name = isFolder ? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : Path.GetFileName(fullPath);
        var txtTitle = new TextBlock
        {
            Text = isFolder ? "Ordner wirklich löschen?" : "Datei wirklich löschen?",
            FontSize = 12.5,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AgyTextPrimary"]
        };
        var txtDesc = new TextBlock
        {
            Text = isFolder ? $"Ordner '{name}' und alle enthaltenen Dateien werden gelöscht." : $"'{name}' wird endgültig gelöscht.",
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["AgyTextMuted"],
            TextWrapping = TextWrapping.Wrap
        };
        var btnConfirm = new Button
        {
            Content = "Endgültig löschen",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)),
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(0, 4, 0, 4)
        };

        btnConfirm.Click += (s, e) =>
        {
            try
            {
                if (isFolder)
                {
                    if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, recursive: true);
                        string folderPrefix = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        _expandedFolders.RemoveWhere(f => f.Equals(fullPath, StringComparison.OrdinalIgnoreCase) || f.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
                        RemovePersistentFolderSortPath(fullPath);
                        _openTabFilePaths.RemoveAll(p => p.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
                        if (_currentEditingFilePath != null && _currentEditingFilePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            if (_openTabFilePaths.Count > 0)
                                OpenFileInEditor(_openTabFilePaths[_openTabFilePaths.Count - 1]);
                            else
                                SwitchToTerminalTab();
                        }
                    }
                }
                else
                {
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        CloseFileTab(fullPath);
                    }
                }

                flyout.Hide();
                if (_lastFilesProjectPath != null)
                {
                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                }
                ShowNotice(isFolder ? "Ordner gelöscht" : "Datei gelöscht", $"'{name}' wurde gelöscht.", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[Delete Error] {ex.Message}");
                ShowNotice("Fehler beim Löschen", ex.Message, InfoBarSeverity.Error);
            }
        };

        sp.Children.Add(txtTitle);
        sp.Children.Add(txtDesc);
        sp.Children.Add(btnConfirm);
        flyout.Content = sp;
        return flyout;
    }

    private Flyout CreateMoveFlyout(string fullPath, bool isFolder)
    {
        var flyout = new Flyout();
        var sp = new StackPanel { Spacing = 8, Width = 260, Padding = new Thickness(4) };
        string currentName = isFolder
            ? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileName(fullPath);

        var txtTitle = new TextBlock
        {
            Text = isFolder ? "Ordner verschieben" : "Datei verschieben",
            FontSize = 12.5,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = (Brush)Application.Current.Resources["AgyTextSecondary"]
        };

        var txtCurrent = new TextBlock
        {
            Text = $"Objekt: {currentName}",
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["AgyTextMuted"],
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var lblTarget = new TextBlock
        {
            Text = "Zielordner auswählen:",
            FontSize = 11.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["AgyTextMuted"]
        };

        var cmbFolders = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12,
            MaxDropDownHeight = 240,
            Background = (Brush)Application.Current.Resources["AgyMainBg"],
            Foreground = (Brush)Application.Current.Resources["AgyTextPrimary"]
        };

        string? projectRoot = !string.IsNullOrEmpty(_lastFilesProjectPath) && Directory.Exists(_lastFilesProjectPath)
            ? _lastFilesProjectPath
            : (!string.IsNullOrEmpty(_projectSettings.BaseFolder) && Directory.Exists(_projectSettings.BaseFolder) ? _projectSettings.BaseFolder : null);

        if (!string.IsNullOrEmpty(projectRoot) && Directory.Exists(projectRoot))
        {
            cmbFolders.Items.Add(new ComboBoxItem { Content = "/ (Projekt-Wurzel)", Tag = projectRoot });

            var allDirs = GetAllProjectDirectories(projectRoot);
            string sourceParent = isFolder ? fullPath : (Path.GetDirectoryName(fullPath) ?? projectRoot);
            int selectIdx = 0;
            int curIdx = 1;

            string cleanFullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var dir in allDirs)
            {
                string fullDir = Path.Combine(projectRoot, dir.Replace('/', Path.DirectorySeparatorChar));
                string cleanFullDir = fullDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (isFolder && (string.Equals(cleanFullDir, cleanFullPath, StringComparison.OrdinalIgnoreCase) ||
                                 cleanFullDir.StartsWith(cleanFullPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var item = new ComboBoxItem { Content = dir + "/", Tag = fullDir };
                cmbFolders.Items.Add(item);

                if (string.Equals(fullDir, sourceParent, StringComparison.OrdinalIgnoreCase))
                {
                    selectIdx = curIdx;
                }
                curIdx++;
            }
            cmbFolders.SelectedIndex = selectIdx < cmbFolders.Items.Count ? selectIdx : 0;
        }

        var btnMove = new Button
        {
            Content = "Verschieben",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Background = (Brush)Application.Current.Resources["AgyAccent"],
            Foreground = (Brush)Application.Current.Resources["AgyTextPrimary"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(0, 4, 0, 4)
        };

        void ExecuteMove()
        {
            if (cmbFolders.SelectedItem is not ComboBoxItem selectedItem || selectedItem.Tag is not string targetFolder)
            {
                return;
            }

            try
            {
                string targetPath = Path.Combine(targetFolder, currentName);
                if (string.Equals(fullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    ShowNotice("Hinweis", "Das Objekt befindet sich bereits in diesem Ordner.", InfoBarSeverity.Informational);
                    flyout.Hide();
                    return;
                }

                if (isFolder)
                {
                    string cleanSource = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string cleanTarget = targetFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (string.Equals(cleanTarget, cleanSource, StringComparison.OrdinalIgnoreCase) ||
                        cleanTarget.StartsWith(cleanSource, StringComparison.OrdinalIgnoreCase))
                    {
                        ShowNotice("Hinweis", "Ein Ordner kann nicht in sich selbst verschoben werden.", InfoBarSeverity.Warning);
                        return;
                    }

                    if (Directory.Exists(targetPath))
                    {
                        ShowNotice("Hinweis", $"Ein Ordner mit dem Namen '{currentName}' existiert bereits im Zielverzeichnis.", InfoBarSeverity.Warning);
                        return;
                    }

                    Directory.Move(fullPath, targetPath);

                    // Offene Dateitabs und aufgeklappte Unterordner aktualisieren, die innerhalb des verschobenen Ordners liegen
                    string oldPrefix = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string newPrefix = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                    var affectedExpanded = _expandedFolders.Where(f => f.Equals(fullPath, StringComparison.OrdinalIgnoreCase) || f.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var exp in affectedExpanded)
                    {
                        _expandedFolders.Remove(exp);
                        if (exp.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                            _expandedFolders.Add(targetPath);
                        else
                            _expandedFolders.Add(newPrefix + exp.Substring(oldPrefix.Length));
                    }
                    _expandedFolders.Add(targetFolder);
                    UpdatePersistentFolderSortPaths(fullPath, targetPath);
                    bool tabChanged = false;
                    for (int i = 0; i < _openTabFilePaths.Count; i++)
                    {
                        if (_openTabFilePaths[i].StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            _openTabFilePaths[i] = newPrefix + _openTabFilePaths[i].Substring(oldPrefix.Length);
                            tabChanged = true;
                        }
                    }
                    if (_currentEditingFilePath != null && _currentEditingFilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        _currentEditingFilePath = newPrefix + _currentEditingFilePath.Substring(oldPrefix.Length);
                        if (TxtEditorPath != null) TxtEditorPath.Text = _currentEditingFilePath;
                        tabChanged = true;
                    }
                    if (tabChanged)
                    {
                        RenderWorkspaceTabs();
                    }
                }
                else
                {
                    if (File.Exists(targetPath))
                    {
                        ShowNotice("Hinweis", $"Eine Datei mit dem Namen '{currentName}' existiert bereits im Zielverzeichnis.", InfoBarSeverity.Warning);
                        return;
                    }

                    File.Move(fullPath, targetPath);

                    if (_openTabFilePaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                    {
                        _openTabFilePaths.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
                        _openTabFilePaths.Add(targetPath);
                        if (string.Equals(_currentEditingFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _currentEditingFilePath = targetPath;
                            if (TxtEditorTitle != null) TxtEditorTitle.Text = currentName;
                            if (TxtEditorPath != null) TxtEditorPath.Text = targetPath;
                        }
                        RenderWorkspaceTabs();
                    }
                    _expandedFolders.Add(targetFolder);
                }

                flyout.Hide();
                if (_lastFilesProjectPath != null)
                {
                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                }

                string displayTarget = projectRoot != null ? Path.GetRelativePath(projectRoot, targetFolder).Replace('\\', '/') : targetFolder;
                if (displayTarget == ".") displayTarget = "/ (Projekt-Wurzel)";
                ShowNotice("Erfolgreich verschoben", $"'{currentName}' wurde nach '{displayTarget}' verschoben.", InfoBarSeverity.Success);
                TelemetryLogger.Log($"[MoveTree] Moved '{fullPath}' to '{targetPath}'");
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[MoveTree Error] {ex.Message}");
                ShowNotice("Fehler beim Verschieben", ex.Message, InfoBarSeverity.Error);
            }
        }

        btnMove.Click += (s, e) => ExecuteMove();
        cmbFolders.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                ExecuteMove();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                flyout.Hide();
            }
        };

        sp.Children.Add(txtTitle);
        sp.Children.Add(txtCurrent);
        sp.Children.Add(lblTarget);
        sp.Children.Add(cmbFolders);
        sp.Children.Add(btnMove);

        flyout.Content = sp;
        return flyout;
    }

    private List<string> GetAllProjectDirectories(string rootDir)
    {
        var result = new List<string>();
        try
        {
            void Collect(string currentDir, string relPrefix, int depth = 0)
            {
                if (depth > 8) return;
                var subs = _fileService.GetDirectories(currentDir, _showHiddenFiles);
                foreach (var sub in subs)
                {
                    string fullSub = Path.Combine(currentDir, sub);
                    string rel = string.IsNullOrEmpty(relPrefix) ? sub : $"{relPrefix}/{sub}";
                    result.Add(rel);
                    Collect(fullSub, rel, depth + 1);
                }
            }
            Collect(rootDir, "", 0);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[GetAllProjectDirectories Error] {ex.Message}");
        }
        return result;
    }

    private MenuFlyoutSubItem CreateIgnoreSubItem(string fullPath, bool isFolder, Helpers.IgnoreStatus ignoreStatus)
    {
        var subItem = new MenuFlyoutSubItem
        {
            Text = ignoreStatus.IsAnyIgnored ? "Ignorieren / Freigeben" : "Ignorieren",
            Icon = new FontIcon { Glyph = ignoreStatus.IsAnyIgnored ? "\uE8BB" : "\uE7BA" }
        };

        string? projectRoot = !string.IsNullOrEmpty(_lastFilesProjectPath) && Directory.Exists(_lastFilesProjectPath)
            ? _lastFilesProjectPath
            : (!string.IsNullOrEmpty(_projectSettings.BaseFolder) && Directory.Exists(_projectSettings.BaseFolder) ? _projectSettings.BaseFolder : null);

        if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
        {
            subItem.IsEnabled = false;
            return subItem;
        }

        string relPath = Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
        string cleanRel = relPath.TrimStart('/');
        if (string.IsNullOrEmpty(cleanRel) || cleanRel == "." || cleanRel == "./" || cleanRel.StartsWith("..") || Path.IsPathRooted(cleanRel))
        {
            subItem.IsEnabled = false;
            return subItem;
        }

        if (isFolder && !relPath.EndsWith('/')) relPath += "/";
        string itemName = isFolder ? Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + "/" : Path.GetFileName(fullPath);

        bool isGitExplicit = Helpers.IgnoreRuleMatcher.IsExplicitlyIgnored(projectRoot, ".gitignore", relPath);
        bool isGitNegated = Helpers.IgnoreRuleMatcher.IsExplicitlyNegated(projectRoot, ".gitignore", relPath);
        bool isGitIgnored = ignoreStatus.IsGitIgnored;

        bool isGeminiExplicit = Helpers.IgnoreRuleMatcher.IsExplicitlyIgnored(projectRoot, ".geminiignore", relPath);
        bool isGeminiNegated = Helpers.IgnoreRuleMatcher.IsExplicitlyNegated(projectRoot, ".geminiignore", relPath);
        bool isGeminiIgnored = ignoreStatus.IsGeminiIgnored;

        bool isReflectionExplicit = Helpers.IgnoreRuleMatcher.IsExplicitlyIgnored(projectRoot, ".reflectionignore", relPath);
        bool isReflectionNegated = Helpers.IgnoreRuleMatcher.IsExplicitlyNegated(projectRoot, ".reflectionignore", relPath);
        bool isReflectionIgnored = ignoreStatus.IsReflectionIgnored;

        // --- 1. .gitignore ---
        if (isGitExplicit)
        {
            var itemGitRemove = new MenuFlyoutItem
            {
                Text = "Aus .gitignore entfernen",
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            itemGitRemove.Click += (s, e) => RemoveFromIgnoreFile(projectRoot, ".gitignore", relPath, itemName);
            subItem.Items.Add(itemGitRemove);

            if (isGitIgnored)
            {
                var itemGitWhitelist = new MenuFlyoutItem
                {
                    Text = "In .gitignore freigeben (! Ausnahme)",
                    Icon = new FontIcon { Glyph = "\uE73E" }
                };
                itemGitWhitelist.Click += (s, e) => WhitelistInIgnoreFile(projectRoot, ".gitignore", relPath, itemName);
                subItem.Items.Add(itemGitWhitelist);
            }
        }
        else if (isGitNegated)
        {
            var itemGitNegRemove = new MenuFlyoutItem
            {
                Text = "Freigabe-Ausnahme (!) aus .gitignore entfernen",
                Icon = new FontIcon { Glyph = "\uE711" }
            };
            itemGitNegRemove.Click += (s, e) => RemoveFromIgnoreFile(projectRoot, ".gitignore", relPath, itemName);
            subItem.Items.Add(itemGitNegRemove);
        }
        else
        {
            var itemGitAdd = new MenuFlyoutItem
            {
                Text = "Zu .gitignore hinzufügen",
                Icon = new FontIcon { Glyph = "\uE8A5" }
            };
            itemGitAdd.Click += (s, e) => AppendToIgnoreFile(projectRoot, ".gitignore", relPath, itemName);
            subItem.Items.Add(itemGitAdd);

            if (isGitIgnored)
            {
                var itemGitWhitelist = new MenuFlyoutItem
                {
                    Text = "In .gitignore freigeben (! Ausnahme)",
                    Icon = new FontIcon { Glyph = "\uE73E" }
                };
                itemGitWhitelist.Click += (s, e) => WhitelistInIgnoreFile(projectRoot, ".gitignore", relPath, itemName);
                subItem.Items.Add(itemGitWhitelist);
            }
        }

        // --- 2. .geminiignore ---
        if (isGeminiExplicit)
        {
            var itemGeminiRemove = new MenuFlyoutItem
            {
                Text = "Aus .geminiignore entfernen",
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            itemGeminiRemove.Click += (s, e) => RemoveFromIgnoreFile(projectRoot, ".geminiignore", relPath, itemName);
            subItem.Items.Add(itemGeminiRemove);

            if (isGeminiIgnored)
            {
                var itemGeminiWhitelist = new MenuFlyoutItem
                {
                    Text = "In .geminiignore freigeben (! Ausnahme)",
                    Icon = new FontIcon { Glyph = "\uE73E" }
                };
                itemGeminiWhitelist.Click += (s, e) => WhitelistInIgnoreFile(projectRoot, ".geminiignore", relPath, itemName);
                subItem.Items.Add(itemGeminiWhitelist);
            }
        }
        else if (isGeminiNegated)
        {
            var itemGeminiNegRemove = new MenuFlyoutItem
            {
                Text = "Freigabe-Ausnahme (!) aus .geminiignore entfernen",
                Icon = new FontIcon { Glyph = "\uE711" }
            };
            itemGeminiNegRemove.Click += (s, e) => RemoveFromIgnoreFile(projectRoot, ".geminiignore", relPath, itemName);
            subItem.Items.Add(itemGeminiNegRemove);
        }
        else
        {
            var itemGeminiAdd = new MenuFlyoutItem
            {
                Text = "Zu .geminiignore hinzufügen",
                Icon = new FontIcon { Glyph = "\uE9CE" }
            };
            itemGeminiAdd.Click += (s, e) => AppendToIgnoreFile(projectRoot, ".geminiignore", relPath, itemName);
            subItem.Items.Add(itemGeminiAdd);

            if (isGeminiIgnored)
            {
                var itemGeminiWhitelist = new MenuFlyoutItem
                {
                    Text = "In .geminiignore freigeben (! Ausnahme)",
                    Icon = new FontIcon { Glyph = "\uE73E" }
                };
                itemGeminiWhitelist.Click += (s, e) => WhitelistInIgnoreFile(projectRoot, ".geminiignore", relPath, itemName);
                subItem.Items.Add(itemGeminiWhitelist);
            }
        }

        // --- 3. Beide (.gitignore & .geminiignore) ---
        if (isGitExplicit && isGeminiExplicit)
        {
            subItem.Items.Add(new MenuFlyoutSeparator());
            var itemBothRemove = new MenuFlyoutItem
            {
                Text = "Aus beiden entfernen (.gitignore & .geminiignore)",
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            itemBothRemove.Click += (s, e) =>
            {
                bool r1 = RemoveFromIgnoreFile(projectRoot, ".gitignore", relPath, itemName, suppressNotice: true, suppressRefresh: true);
                bool r2 = RemoveFromIgnoreFile(projectRoot, ".geminiignore", relPath, itemName, suppressNotice: true, suppressRefresh: false);
                if (r1 || r2)
                {
                    ShowNotice("Erfolgreich entfernt", $"'{itemName}' wurde aus .gitignore und .geminiignore entfernt.", InfoBarSeverity.Success);
                }
                else
                {
                    ShowNotice("Hinweis", $"'{itemName}' war weder in .gitignore noch in .geminiignore eingetragen.", InfoBarSeverity.Informational);
                }
            };
            subItem.Items.Add(itemBothRemove);
        }
        else if (!isGitExplicit && !isGeminiExplicit && !isGitIgnored && !isGeminiIgnored)
        {
            subItem.Items.Add(new MenuFlyoutSeparator());
            var itemBothAdd = new MenuFlyoutItem
            {
                Text = "Zu beiden (.gitignore & .geminiignore)",
                Icon = new FontIcon { Glyph = "\uE734" }
            };
            itemBothAdd.Click += (s, e) =>
            {
                bool a1 = AppendToIgnoreFile(projectRoot, ".gitignore", relPath, itemName, suppressNotice: true, suppressRefresh: true);
                bool a2 = AppendToIgnoreFile(projectRoot, ".geminiignore", relPath, itemName, suppressNotice: true, suppressRefresh: false);
                if (a1 || a2)
                {
                    ShowNotice("Erfolgreich hinzugefügt", $"'{itemName}' wurde zu .gitignore und .geminiignore hinzugefügt.", InfoBarSeverity.Success);
                }
            };
            subItem.Items.Add(itemBothAdd);
        }
        else if (isGitIgnored && isGeminiIgnored && !isGitNegated && !isGeminiNegated)
        {
            subItem.Items.Add(new MenuFlyoutSeparator());
            var itemBothWhitelist = new MenuFlyoutItem
            {
                Text = "In beiden freigeben (! Ausnahme)",
                Icon = new FontIcon { Glyph = "\uE73E" }
            };
            itemBothWhitelist.Click += (s, e) =>
            {
                bool w1 = WhitelistInIgnoreFile(projectRoot, ".gitignore", relPath, itemName, suppressNotice: true, suppressRefresh: true);
                bool w2 = WhitelistInIgnoreFile(projectRoot, ".geminiignore", relPath, itemName, suppressNotice: true, suppressRefresh: false);
                if (w1 || w2)
                {
                    ShowNotice("Erfolgreich freigegeben", $"'{itemName}' wurde in .gitignore und .geminiignore freigegeben (!).", InfoBarSeverity.Success);
                }
            };
            subItem.Items.Add(itemBothWhitelist);
        }

        // --- 4. .reflectionignore ---
        subItem.Items.Add(new MenuFlyoutSeparator());

        if (isReflectionExplicit)
        {
            var itemReflRemove = new MenuFlyoutItem
            {
                Text = "Aus .reflectionignore entfernen",
                Icon = new FontIcon { Glyph = "\uE74D" }
            };
            itemReflRemove.Click += (s, e) => RemoveFromIgnoreFile(projectRoot, ".reflectionignore", relPath, itemName);
            subItem.Items.Add(itemReflRemove);

            if (isReflectionIgnored)
            {
                var itemReflWhitelist = new MenuFlyoutItem
                {
                    Text = "In .reflectionignore freigeben (! Ausnahme)",
                    Icon = new FontIcon { Glyph = "\uE73E" }
                };
                itemReflWhitelist.Click += (s, e) => WhitelistInIgnoreFile(projectRoot, ".reflectionignore", relPath, itemName);
                subItem.Items.Add(itemReflWhitelist);
            }
        }
        else if (isReflectionNegated)
        {
            var itemReflNegRemove = new MenuFlyoutItem
            {
                Text = "Freigabe-Ausnahme (!) aus .reflectionignore entfernen",
                Icon = new FontIcon { Glyph = "\uE711" }
            };
            itemReflNegRemove.Click += (s, e) => RemoveFromIgnoreFile(projectRoot, ".reflectionignore", relPath, itemName);
            subItem.Items.Add(itemReflNegRemove);
        }
        else
        {
            var itemReflAdd = new MenuFlyoutItem
            {
                Text = "Zu .reflectionignore hinzufügen",
                Icon = new FontIcon { Glyph = "\uE721" }
            };
            itemReflAdd.Click += (s, e) => AppendToIgnoreFile(projectRoot, ".reflectionignore", relPath, itemName);
            subItem.Items.Add(itemReflAdd);

            if (isReflectionIgnored)
            {
                var itemReflWhitelist = new MenuFlyoutItem
                {
                    Text = "In .reflectionignore freigeben (! Ausnahme)",
                    Icon = new FontIcon { Glyph = "\uE73E" }
                };
                itemReflWhitelist.Click += (s, e) => WhitelistInIgnoreFile(projectRoot, ".reflectionignore", relPath, itemName);
                subItem.Items.Add(itemReflWhitelist);
            }
        }

        return subItem;
    }

    private bool AppendToIgnoreFile(string targetProjectDir, string ignoreFileName, string relPath, string itemName, bool suppressNotice = false, bool suppressRefresh = false)
    {
        try
        {
            string filePath = Path.Combine(targetProjectDir, ignoreFileName);
            List<string> existingLines = File.Exists(filePath)
                ? File.ReadAllLines(filePath).ToList()
                : new List<string>();

            string normalizedCheck = relPath.Replace('\\', '/').Trim('/');

            // Falls zuvor eine Freigabe-Ausnahme (!) existierte, diese entfernen
            int removedNegations = existingLines.RemoveAll(line =>
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || !trimmed.StartsWith('!')) return false;
                string check = trimmed[1..].Trim().Replace('\\', '/').Trim('/');
                return string.Equals(check, normalizedCheck, StringComparison.OrdinalIgnoreCase);
            });

            bool alreadyExists = existingLines.Any(line =>
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('!')) return false;
                string check = trimmed.Replace('\\', '/').Trim('/');
                return string.Equals(check, normalizedCheck, StringComparison.OrdinalIgnoreCase);
            });

            if (alreadyExists)
            {
                if (removedNegations > 0)
                {
                    string content = string.Join(Environment.NewLine, existingLines).TrimEnd() + Environment.NewLine;
                    File.WriteAllText(filePath, content, System.Text.Encoding.UTF8);
                    Helpers.IgnoreRuleMatcher.InvalidateCache(targetProjectDir);
                    if (!suppressRefresh && !string.IsNullOrEmpty(_lastFilesProjectPath))
                    {
                        UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                    }
                }

                if (!suppressNotice)
                {
                    ShowNotice("Bereits ignoriert", $"'{itemName}' ist bereits in {ignoreFileName} eingetragen.", InfoBarSeverity.Informational);
                }
                return false;
            }

            string entry = relPath.StartsWith("/") ? relPath : "/" + relPath;
            existingLines.Add(entry);
            string newContent = string.Join(Environment.NewLine, existingLines).TrimEnd() + Environment.NewLine;
            File.WriteAllText(filePath, newContent, System.Text.Encoding.UTF8);

            if (!suppressNotice)
            {
                ShowNotice("Erfolgreich hinzugefügt", $"'{entry}' wurde zu {ignoreFileName} hinzugefügt.", InfoBarSeverity.Success);
            }
            TelemetryLogger.Log($"[IgnoreTree] Appended '{entry}' to {filePath}");

            // Cache invalidieren und Dateibaum sofort aktualisieren, damit das neue Icon unmittelbar erscheint
            Helpers.IgnoreRuleMatcher.InvalidateCache(targetProjectDir);
            if (!suppressRefresh && !string.IsNullOrEmpty(_lastFilesProjectPath))
            {
                UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
            }
            return true;
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[IgnoreTree Error] {ex.Message}");
            if (!suppressNotice)
            {
                ShowNotice("Fehler", $"Konnte {ignoreFileName} nicht aktualisieren: {ex.Message}", InfoBarSeverity.Error);
            }
            return false;
        }
    }

    private bool RemoveFromIgnoreFile(string targetProjectDir, string ignoreFileName, string relPath, string itemName, bool suppressNotice = false, bool suppressRefresh = false)
    {
        try
        {
            string filePath = Path.Combine(targetProjectDir, ignoreFileName);
            if (!File.Exists(filePath)) return false;

            string normalizedCheck = relPath.Replace('\\', '/').Trim('/');
            var lines = File.ReadAllLines(filePath).ToList();

            int removedCount = lines.RemoveAll(line =>
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) return false;
                string check = (trimmed.StartsWith('!') ? trimmed[1..].Trim() : trimmed).Replace('\\', '/').Trim('/');
                return string.Equals(check, normalizedCheck, StringComparison.OrdinalIgnoreCase);
            });

            if (removedCount > 0)
            {
                string newContent = lines.Count > 0
                    ? string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine
                    : string.Empty;
                File.WriteAllText(filePath, newContent, System.Text.Encoding.UTF8);

                Helpers.IgnoreRuleMatcher.InvalidateCache(targetProjectDir);

                bool stillIgnored = false;
                if (!string.IsNullOrEmpty(targetProjectDir))
                {
                    string fullPath = Path.Combine(targetProjectDir, relPath.Replace('/', Path.DirectorySeparatorChar));
                    bool isFolder = relPath.EndsWith('/');
                    var status = Helpers.IgnoreRuleMatcher.CheckIgnoreStatus(targetProjectDir, fullPath, isFolder);
                    if (ignoreFileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)) stillIgnored = status.IsGitIgnored;
                    else if (ignoreFileName.Equals(".geminiignore", StringComparison.OrdinalIgnoreCase)) stillIgnored = status.IsGeminiIgnored;
                    else if (ignoreFileName.Equals(".reflectionignore", StringComparison.OrdinalIgnoreCase)) stillIgnored = status.IsReflectionIgnored;
                }

                if (!suppressNotice)
                {
                    if (stillIgnored)
                    {
                        ShowNotice("Eintrag entfernt", $"'{itemName}' wurde aus {ignoreFileName} entfernt. Hinweis: Die Datei wird weiterhin durch ein übergeordnetes Muster (z. B. *.pdf) erfasst. Nutze bei Bedarf 'In {ignoreFileName} freigeben (!)', um sie vollständig auszunehmen.", InfoBarSeverity.Warning);
                    }
                    else
                    {
                        ShowNotice("Erfolgreich entfernt", $"'{itemName}' wurde aus {ignoreFileName} entfernt und wird nicht mehr ignoriert.", InfoBarSeverity.Success);
                    }
                }
                TelemetryLogger.Log($"[IgnoreTree] Removed {removedCount} entry/entries for '{relPath}' from {filePath}");

                if (!suppressRefresh && !string.IsNullOrEmpty(_lastFilesProjectPath))
                {
                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                }
                return true;
            }
            else
            {
                if (!suppressNotice)
                {
                    ShowNotice("Nicht gefunden", $"'{itemName}' war nicht explizit in {ignoreFileName} eingetragen.", InfoBarSeverity.Informational);
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[IgnoreTree Remove Error] {ex.Message}");
            if (!suppressNotice)
            {
                ShowNotice("Fehler", $"Konnte Eintrag nicht aus {ignoreFileName} entfernen: {ex.Message}", InfoBarSeverity.Error);
            }
            return false;
        }
    }

    private bool WhitelistInIgnoreFile(string targetProjectDir, string ignoreFileName, string relPath, string itemName, bool suppressNotice = false, bool suppressRefresh = false)
    {
        try
        {
            string filePath = Path.Combine(targetProjectDir, ignoreFileName);
            List<string> existingLines = File.Exists(filePath)
                ? File.ReadAllLines(filePath).ToList()
                : new List<string>();

            string normalizedCheck = relPath.Replace('\\', '/').Trim('/');

            // Zuerst bestehende positive Einträge für denselben Pfad entfernen
            int removedPositives = existingLines.RemoveAll(line =>
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('!')) return false;
                string check = trimmed.Replace('\\', '/').Trim('/');
                return string.Equals(check, normalizedCheck, StringComparison.OrdinalIgnoreCase);
            });

            // Prüfen, ob bereits negiert
            bool alreadyNegated = existingLines.Any(line =>
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || !trimmed.StartsWith('!')) return false;
                string check = trimmed[1..].Trim().Replace('\\', '/').Trim('/');
                return string.Equals(check, normalizedCheck, StringComparison.OrdinalIgnoreCase);
            });

            string entry = "!" + (relPath.StartsWith("/") ? relPath : "/" + relPath);

            if (!alreadyNegated)
            {
                existingLines.Add(entry);
                string newContent = string.Join(Environment.NewLine, existingLines).TrimEnd() + Environment.NewLine;
                File.WriteAllText(filePath, newContent, System.Text.Encoding.UTF8);
            }
            else if (removedPositives > 0)
            {
                // Positive Einträge wurden bereinigt
                string newContent = string.Join(Environment.NewLine, existingLines).TrimEnd() + Environment.NewLine;
                File.WriteAllText(filePath, newContent, System.Text.Encoding.UTF8);
            }

            if (!suppressNotice)
            {
                ShowNotice("Erfolgreich freigegeben", $"'{itemName}' wurde in {ignoreFileName} freigegeben ({entry}) und wird nicht mehr ignoriert.", InfoBarSeverity.Success);
            }
            TelemetryLogger.Log($"[IgnoreTree] Whitelisted '{entry}' in {filePath}");

            Helpers.IgnoreRuleMatcher.InvalidateCache(targetProjectDir);
            if (!suppressRefresh && !string.IsNullOrEmpty(_lastFilesProjectPath))
            {
                UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
            }
            return true;
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[IgnoreTree Whitelist Error] {ex.Message}");
            if (!suppressNotice)
            {
                ShowNotice("Fehler", $"Konnte Freigabe in {ignoreFileName} nicht eintragen: {ex.Message}", InfoBarSeverity.Error);
            }
            return false;
        }
    }

    private bool _filesDragDropInitialized;
    private void EnsureFilesDragDropInitialized()
    {
        if (_filesDragDropInitialized || FilesPanelScrollViewer == null) return;
        _filesDragDropInitialized = true;

        FilesPanelScrollViewer.AllowDrop = true;
        FilesPanelScrollViewer.DragOver += (s, e) =>
        {
            if (string.IsNullOrEmpty(_lastFilesProjectPath) || !Directory.Exists(_lastFilesProjectPath) || _lastFilesIsGlobal)
            {
                e.AcceptedOperation = DataPackageOperation.None;
                return;
            }

            if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Text))
            {
                e.AcceptedOperation = (e.AllowedOperations & DataPackageOperation.Copy) != 0
                    ? DataPackageOperation.Copy
                    : (e.AllowedOperations & DataPackageOperation.Move) != 0
                        ? DataPackageOperation.Move
                        : DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "In Projekt-Wurzel ablegen";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
            }
        };

        FilesPanelScrollViewer.Drop += async (s, e) =>
        {
            if (!string.IsNullOrEmpty(_lastFilesProjectPath) && Directory.Exists(_lastFilesProjectPath) && !_lastFilesIsGlobal)
            {
                e.Handled = true;
                await HandleItemDroppedIntoFolderAsync(e.DataView, _lastFilesProjectPath);
            }
        };

        if (FilesPanel != null)
        {
            FilesPanel.AllowDrop = true;
            FilesPanel.DragOver += (s, e) =>
            {
                if (string.IsNullOrEmpty(_lastFilesProjectPath) || !Directory.Exists(_lastFilesProjectPath) || _lastFilesIsGlobal)
                {
                    e.AcceptedOperation = DataPackageOperation.None;
                    return;
                }

                if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Text))
                {
                    e.AcceptedOperation = (e.AllowedOperations & DataPackageOperation.Copy) != 0
                        ? DataPackageOperation.Copy
                        : (e.AllowedOperations & DataPackageOperation.Move) != 0
                            ? DataPackageOperation.Move
                            : DataPackageOperation.Copy;
                    e.DragUIOverride.Caption = "In Projekt-Wurzel ablegen";
                    e.DragUIOverride.IsCaptionVisible = true;
                    e.DragUIOverride.IsGlyphVisible = true;
                }
            };

            FilesPanel.Drop += async (s, e) =>
            {
                if (!string.IsNullOrEmpty(_lastFilesProjectPath) && Directory.Exists(_lastFilesProjectPath) && !_lastFilesIsGlobal)
                {
                    e.Handled = true;
                    await HandleItemDroppedIntoFolderAsync(e.DataView, _lastFilesProjectPath);
                }
            };
        }
    }

    private static string GetUniqueDestinationPath(string targetPath, bool isDir)
    {
        if (isDir ? !Directory.Exists(targetPath) : !File.Exists(targetPath)) return targetPath;

        string dir = Path.GetDirectoryName(targetPath) ?? "";
        string name = Path.GetFileNameWithoutExtension(targetPath);
        string ext = Path.GetExtension(targetPath);
        int count = 1;

        while (true)
        {
            string candidate = isDir
                ? Path.Combine(dir, $"{name} ({count})")
                : Path.Combine(dir, $"{name} ({count}){ext}");
            if (isDir ? !Directory.Exists(candidate) : !File.Exists(candidate))
            {
                return candidate;
            }
            count++;
        }
    }

    private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursively(subDir, destSubDir);
        }
    }

    private async Task HandleItemDroppedIntoFolderAsync(DataPackageView dataView, string targetFolder)
    {
        try
        {
            var sourcePaths = new List<string>();

            if (dataView.Contains(StandardDataFormats.StorageItems))
            {
                try
                {
                    var items = await dataView.GetStorageItemsAsync();
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            if (!string.IsNullOrEmpty(item.Path) && (File.Exists(item.Path) || Directory.Exists(item.Path)))
                            {
                                sourcePaths.Add(item.Path);
                            }
                        }
                    }
                }
                catch (Exception exStorage)
                {
                    TelemetryLogger.Log($"[DropTree GetStorageItems Error] {exStorage.Message}");
                }
            }

            if (sourcePaths.Count == 0 && dataView.Contains(StandardDataFormats.Text))
            {
                try
                {
                    string rawText = await dataView.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(rawText))
                    {
                        var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            string candidate = line.Trim().Trim('"');
                            if (File.Exists(candidate) || Directory.Exists(candidate))
                            {
                                sourcePaths.Add(candidate);
                            }
                        }
                    }
                }
                catch (Exception exText)
                {
                    TelemetryLogger.Log($"[DropTree GetText Error] {exText.Message}");
                }
            }

            if (sourcePaths.Count == 0) return;

            string projectRoot = _lastFilesProjectPath ?? "";
            string cleanProjectRoot = !string.IsNullOrEmpty(projectRoot)
                ? projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar
                : "";

            int successCount = 0;
            string lastProcessedName = "";
            bool lastWasInternal = false;

            foreach (var sourcePath in sourcePaths)
            {
                bool isDir = Directory.Exists(sourcePath);
                bool isFile = File.Exists(sourcePath);
                if (!isDir && !isFile) continue;

                string itemName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                string targetPath = Path.Combine(targetFolder, itemName);

                if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isInternal = !string.IsNullOrEmpty(cleanProjectRoot) &&
                    (sourcePath.StartsWith(cleanProjectRoot, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(sourcePath, cleanProjectRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));

                if (isDir)
                {
                    string cleanSource = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string cleanTarget = targetFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                    if (cleanTarget.StartsWith(cleanSource, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(cleanTarget, cleanSource, StringComparison.OrdinalIgnoreCase))
                    {
                        ShowNotice("Hinweis", $"Ordner '{itemName}' kann nicht in sich selbst verschoben werden.", InfoBarSeverity.Warning);
                        continue;
                    }

                    if (isInternal)
                    {
                        if (Directory.Exists(targetPath))
                        {
                            ShowNotice("Hinweis", $"Ein Ordner '{itemName}' existiert bereits am Zielort.", InfoBarSeverity.Warning);
                            continue;
                        }

                        Directory.Move(sourcePath, targetPath);

                        string oldPrefix = cleanSource;
                        string newPrefix = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                        var affectedExpanded = _expandedFolders.Where(f => f.Equals(sourcePath, StringComparison.OrdinalIgnoreCase) || f.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                        foreach (var exp in affectedExpanded)
                        {
                            _expandedFolders.Remove(exp);
                            if (exp.Equals(sourcePath, StringComparison.OrdinalIgnoreCase))
                                _expandedFolders.Add(targetPath);
                            else
                                _expandedFolders.Add(newPrefix + exp.Substring(oldPrefix.Length));
                        }
                        UpdatePersistentFolderSortPaths(sourcePath, targetPath);

                        bool tabChanged = false;
                        for (int i = 0; i < _openTabFilePaths.Count; i++)
                        {
                            if (_openTabFilePaths[i].StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                _openTabFilePaths[i] = newPrefix + _openTabFilePaths[i].Substring(oldPrefix.Length);
                                tabChanged = true;
                            }
                        }
                        if (_currentEditingFilePath != null && _currentEditingFilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            _currentEditingFilePath = newPrefix + _currentEditingFilePath.Substring(oldPrefix.Length);
                            if (TxtEditorPath != null) TxtEditorPath.Text = _currentEditingFilePath;
                            tabChanged = true;
                        }
                        if (tabChanged)
                        {
                            RenderWorkspaceTabs();
                        }
                    }
                    else
                    {
                        string finalTarget = GetUniqueDestinationPath(targetPath, isDir: true);
                        CopyDirectoryRecursively(sourcePath, finalTarget);
                        itemName = Path.GetFileName(finalTarget);
                    }
                }
                else
                {
                    if (isInternal)
                    {
                        if (File.Exists(targetPath))
                        {
                            ShowNotice("Hinweis", $"Eine Datei '{itemName}' existiert bereits am Zielort.", InfoBarSeverity.Warning);
                            continue;
                        }

                        File.Move(sourcePath, targetPath);

                        if (_openTabFilePaths.Contains(sourcePath, StringComparer.OrdinalIgnoreCase))
                        {
                            _openTabFilePaths.RemoveAll(p => string.Equals(p, sourcePath, StringComparison.OrdinalIgnoreCase));
                            _openTabFilePaths.Add(targetPath);
                            if (string.Equals(_currentEditingFilePath, sourcePath, StringComparison.OrdinalIgnoreCase))
                            {
                                _currentEditingFilePath = targetPath;
                                if (TxtEditorTitle != null) TxtEditorTitle.Text = itemName;
                                if (TxtEditorPath != null) TxtEditorPath.Text = targetPath;
                            }
                            RenderWorkspaceTabs();
                        }
                    }
                    else
                    {
                        string finalTarget = GetUniqueDestinationPath(targetPath, isDir: false);
                        File.Copy(sourcePath, finalTarget, overwrite: false);
                        itemName = Path.GetFileName(finalTarget);
                    }
                }

                successCount++;
                lastProcessedName = itemName;
                lastWasInternal = isInternal;
            }

            if (successCount > 0)
            {
                _expandedFolders.Add(targetFolder);

                if (_lastFilesProjectPath != null)
                {
                    UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
                }

                string displayTarget = _lastFilesProjectPath != null ? Path.GetRelativePath(_lastFilesProjectPath, targetFolder).Replace('\\', '/') : targetFolder;
                if (displayTarget == ".") displayTarget = "/ (Projekt-Wurzel)";

                if (successCount == 1)
                {
                    string actionWord = lastWasInternal ? "verschoben" : "kopiert";
                    ShowNotice($"Erfolgreich {actionWord}", $"'{lastProcessedName}' wurde nach '{displayTarget}' {actionWord}.", InfoBarSeverity.Success);
                }
                else
                {
                    ShowNotice("Erfolgreich übertragen", $"{successCount} Elemente wurden nach '{displayTarget}' übertragen.", InfoBarSeverity.Success);
                }
                TelemetryLogger.Log($"[DropTree] Handled {successCount} items into '{targetFolder}'");
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[DropTree Error] {ex.Message}");
            ShowNotice("Fehler bei Dateioperation", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void StageFileAttachment(string fullPath)
    {
        try
        {
            if (string.IsNullOrEmpty(fullPath) || (!File.Exists(fullPath) && !Directory.Exists(fullPath))) return;

            string normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var existing = _stagedAttachments.FirstOrDefault(a =>
                a.FilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _stagedAttachments.Remove(existing);
                if (_stagedAttachments.Count == 0)
                {
                    AttachmentStagingControl.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                string name = Path.GetFileName(normalizedPath);
                if (string.IsNullOrEmpty(name)) name = normalizedPath;

                _stagedAttachments.Add(new StagedAttachment
                {
                    FileName = name,
                    FilePath = fullPath
                });

                if (AttachmentStagingControl != null)
                {
                    AttachmentStagingControl.Visibility = Visibility.Visible;
                }
            }

            UpdateTokenPeakMeter(false);
            if (_lastFilesProjectPath != null)
            {
                UpdateFilesTab(_lastFilesIsGlobal, _lastFilesProjectPath, _lastFilesProjectName ?? "");
            }
            TxtChatInput?.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[StageFileAttachment ERROR] {ex.Message}");
        }
    }

    private void CopyFilePathToClipboard(string fullPath, bool isRelative)
    {
        try
        {
            string pathToCopy = fullPath;
            if (isRelative && !string.IsNullOrEmpty(_lastFilesProjectPath) && fullPath.StartsWith(_lastFilesProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                pathToCopy = Path.GetRelativePath(_lastFilesProjectPath, fullPath).Replace('\\', '/');
            }
            CopyUrlToClipboard(pathToCopy);
            ShowNotice("Pfad kopiert", $"'{pathToCopy}' in Zwischenablage kopiert.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CopyFilePath Error] {ex.Message}");
        }
    }



    private static void CopyUrlToClipboard(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(url);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            TelemetryLogger.Log($"[CopyUrlToClipboard] Copied URL to clipboard: {url}");
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CopyUrlToClipboard Error] {ex.Message}");
        }
    }

    private static async Task OpenUrlInDefaultBrowserAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        CopyUrlToClipboard(url);
        TelemetryLogger.Log($"[OpenUrlInDefaultBrowserAsync] URL copied to clipboard (browser launch disabled): {url}");
        await Task.CompletedTask;
    }
}

