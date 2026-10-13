using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private readonly Services.SystemCheckService _systemCheckService = new Services.SystemCheckService();

    public void BtnSystemCheck_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnSystemCheck_Click)} triggered.");
        RecordMaintenanceTimestamp("system_check");

        if (ColSidebar != null)
        {
            _isSidebarVisible = true;
            ApplySidebarWidth();
        }

        if (PnlSystemCheckPanel != null)
        {
            PnlSystemCheckPanel.Visibility = Visibility.Visible;
        }

        _ = RenderSystemRequirementsAsync(forceRefresh: true);
    }

    private void BtnCloseSystemCheckPanel_Click(object sender, RoutedEventArgs e)
    {
        if (PnlSystemCheckPanel != null)
        {
            PnlSystemCheckPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnRefreshSystemCheck_Click(object sender, RoutedEventArgs e)
    {
        RecordMaintenanceTimestamp("system_check");
        _ = RenderSystemRequirementsAsync(forceRefresh: true);
    }

    private long _systemCheckGeneration = 0;

    private async Task RenderSystemRequirementsAsync(bool forceRefresh = false)
    {
        long currentGeneration = System.Threading.Interlocked.Increment(ref _systemCheckGeneration);

        if (PnlSystemCheckList == null) return;

        // Show loading indicator
        PnlSystemCheckList.Children.Clear();
        var loadingSp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(4) };
        var zapIcon = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/zap.svg"), Width = 12, Height = 12 };
        var loadingText = new TextBlock
        {
            Text = "Prüfe System-Voraussetzungen & Versionen...",
            FontSize = 10.5,
            Foreground = ParseHexColor("#8B949E"),
            VerticalAlignment = VerticalAlignment.Center
        };
        loadingSp.Children.Add(zapIcon);
        loadingSp.Children.Add(loadingText);
        PnlSystemCheckList.Children.Add(loadingSp);

        // Start all checks in parallel
        var tasks = _systemCheckService.CreateCheckTasks();
        int totalCount = tasks.Count;
        int doneCount = 0;
        var checkBoxesWithPrompts = new List<(CheckBox cb, SystemRequirementItem item)>();
        var outdatedItems = new List<SystemRequirementItem>();

        // Pre-create 2-row x 2-column header layout (50% width each)
        var actionHeaderPanel = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        actionHeaderPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actionHeaderPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        actionHeaderPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionHeaderPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Row 0: Selection Controls (Alles auswählen / Auswahl aufheben)
        var btnSelectAll = new Button 
        { 
            Content = "Alles auswählen", 
            FontSize = 12, 
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 6),
            Padding = new Thickness(8, 6, 8, 6), 
            Background = ParseHexColor("#21262D"), 
            Foreground = ParseHexColor("#C9D1D9"), 
            BorderThickness = new Thickness(1), 
            BorderBrush = ParseHexColor("#30363D"), 
            CornerRadius = new CornerRadius(6) 
        };
        var btnDeselectAll = new Button 
        { 
            Content = "Auswahl aufheben", 
            FontSize = 12, 
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 6),
            Padding = new Thickness(8, 6, 8, 6), 
            Background = ParseHexColor("#21262D"), 
            Foreground = ParseHexColor("#C9D1D9"), 
            BorderThickness = new Thickness(1), 
            BorderBrush = ParseHexColor("#30363D"), 
            CornerRadius = new CornerRadius(6) 
        };
        Grid.SetRow(btnSelectAll, 0);
        Grid.SetColumn(btnSelectAll, 0);
        Grid.SetRow(btnDeselectAll, 0);
        Grid.SetColumn(btnDeselectAll, 1);

        // Row 1: Action Buttons (Alle aktualisieren / Gewählte ausführen)
        var iconUpdateAll = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/zap-white.svg"), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        var txtUpdateAll = new TextBlock 
        { 
            Text = "Alle aktuell", 
            FontSize = 12, 
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, 
            Foreground = ParseHexColor("#8B949E"), 
            VerticalAlignment = VerticalAlignment.Center 
        };
        var btnUpdateAllSp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        btnUpdateAllSp.Children.Add(iconUpdateAll);
        btnUpdateAllSp.Children.Add(txtUpdateAll);
        var btnUpdateAll = new Button 
        { 
            Content = btnUpdateAllSp, 
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(8, 6, 8, 6), 
            Background = ParseHexColor("#21262D"), 
            BorderThickness = new Thickness(1), 
            BorderBrush = ParseHexColor("#30363D"), 
            CornerRadius = new CornerRadius(6), 
            IsEnabled = false 
        };

        var iconMasterAction = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/rocket-white.svg"), Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        var txtMasterAction = new TextBlock 
        { 
            Text = "Gewählte ausführen", 
            FontSize = 12, 
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, 
            Foreground = ParseHexColor("#FFFFFF"), 
            VerticalAlignment = VerticalAlignment.Center 
        };
        var btnMasterActionSp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        btnMasterActionSp.Children.Add(iconMasterAction);
        btnMasterActionSp.Children.Add(txtMasterAction);
        var btnMasterAction = new Button 
        { 
            Content = btnMasterActionSp, 
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(8, 6, 8, 6), 
            Background = ParseHexColor("#238636"), 
            BorderThickness = new Thickness(0), 
            CornerRadius = new CornerRadius(6) 
        };
        Grid.SetRow(btnUpdateAll, 1);
        Grid.SetColumn(btnUpdateAll, 0);
        Grid.SetRow(btnMasterAction, 1);
        Grid.SetColumn(btnMasterAction, 1);

        btnSelectAll.Click += (s, ev) => { foreach (var cbItem in checkBoxesWithPrompts) cbItem.cb.IsChecked = true; };
        btnDeselectAll.Click += (s, ev) => { foreach (var cbItem in checkBoxesWithPrompts) cbItem.cb.IsChecked = false; };

        btnUpdateAll.Click += (s, ev) => {
            if (outdatedItems.Count > 0 && UcPromptEditor?.TxtChatInput != null)
            {
                var updateLines = outdatedItems.Select(x => $"{x.Name} (Aktuell: {x.InstalledVersion} ➔ Neueste: {x.LatestVersion}) | Befehl: {x.InstallPrompt}").ToList();
                string masterPrompt = "[Bitte führe folgende Aktualisierungen auf meinem System durch:\n\n- " + string.Join("\n- ", updateLines) + "\n\nGib mir nach Abschluss eine strukturierte Vorher-Nachher-Liste (keine Tabellen) mit allen aktualisierten Programmen und deren neuen Versionen aus.]";
                UcPromptEditor.TxtChatInput.Text = masterPrompt;
                UcPromptEditor.TxtChatInput.Focus(FocusState.Programmatic);
                UcPromptEditor.TxtChatInput.SelectionStart = masterPrompt.Length;
            }
        };

        btnMasterAction.Click += (s, ev) => {
            var selectedItems = checkBoxesWithPrompts.Where(x => x.cb.IsChecked == true).Select(x => x.item).ToList();
            if (selectedItems.Count > 0 && UcPromptEditor?.TxtChatInput != null)
            {
                var actionLines = new List<string>();
                foreach (var sel in selectedItems)
                {
                    if (sel.IsInstalled && !sel.IsUpToDate && !string.IsNullOrEmpty(sel.InstallPrompt))
                        actionLines.Add($"[Update] {sel.Name} (Aktuell: {sel.InstalledVersion} ➔ Neueste: {sel.LatestVersion}) | Befehl: {sel.InstallPrompt}");
                    else if (!sel.IsInstalled && !string.IsNullOrEmpty(sel.InstallPrompt))
                        actionLines.Add($"[Install] {sel.Name} | Befehl: {sel.InstallPrompt}");
                    else if (sel.IsInstalled && !string.IsNullOrEmpty(sel.UninstallPrompt))
                        actionLines.Add($"[Deinstallieren] {sel.Name} | Befehl: {sel.UninstallPrompt}");
                    else if (!string.IsNullOrEmpty(sel.InstallPrompt))
                        actionLines.Add($"{sel.Name} | Befehl: {sel.InstallPrompt}");
                }

                string masterPrompt = "[Bitte führe folgende Aktionen für die ausgewählten Werkzeuge aus:\n\n- " + string.Join("\n- ", actionLines) + "\n\nGib mir nach Abschluss eine Bestätigungsübersicht aus.]";
                UcPromptEditor.TxtChatInput.Text = masterPrompt;
                UcPromptEditor.TxtChatInput.Focus(FocusState.Programmatic);
                UcPromptEditor.TxtChatInput.SelectionStart = masterPrompt.Length;
            }
        };

        actionHeaderPanel.Children.Add(btnSelectAll);
        actionHeaderPanel.Children.Add(btnDeselectAll);
        actionHeaderPanel.Children.Add(btnUpdateAll);
        actionHeaderPanel.Children.Add(btnMasterAction);

        var progressText = new TextBlock
        {
            Text = "✓ 0 von " + totalCount + " Checks abgeschlossen...",
            FontSize = 11.5,
            Foreground = ParseHexColor("#58A6FF"),
            Margin = new Thickness(4, 0, 0, 8)
        };

        // Category panels: sorted insertion order (Basics=0, tools=1, Optional=99)
        var categoryPanels = new Dictionary<string, StackPanel>(StringComparer.OrdinalIgnoreCase);
        var categorySortKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool initialized = false;

        // Process results incrementally – append only, never clear
        var pending = new List<Task<SystemRequirementItem>>(tasks);
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);

            SystemRequirementItem? item = null;
            try { item = await completed; }
            catch { /* do not skip, we must increment and update UI */ }

            doneCount++;
            int currentDone = doneCount; // local copy for closure – avoids stale reads when multiple tasks complete simultaneously

            DispatcherQueue.TryEnqueue(() =>
            {
                if (System.Threading.Interlocked.Read(ref _systemCheckGeneration) != currentGeneration) return;

                // First result: swap loading indicator for stable header
                if (!initialized)
                {
                    PnlSystemCheckList.Children.Clear();
                    PnlSystemCheckList.Children.Add(actionHeaderPanel);
                    PnlSystemCheckList.Children.Add(progressText);
                    initialized = true;
                }

                // Update progress text in-place (no re-creation)
                if (currentDone < totalCount)
                {
                    progressText.Text = $"✓ {currentDone} von {totalCount} Checks abgeschlossen...";
                    progressText.Visibility = Visibility.Visible;
                }
                else
                {
                    progressText.Visibility = Visibility.Collapsed;
                }

                if (item == null) return;

                // Track outdated item and update button state
                if (item.IsInstalled && !item.IsUpToDate)
                {
                    if (!outdatedItems.Any(x => x.Id == item.Id))
                    {
                        outdatedItems.Add(item);
                        txtUpdateAll.Text = $"Alle aktualisieren ({outdatedItems.Count})";
                        txtUpdateAll.Foreground = ParseHexColor("#FFFFFF");
                        btnUpdateAll.Background = ParseHexColor("#9A3412");
                        btnUpdateAll.BorderBrush = ParseHexColor("#F59E0B");
                        btnUpdateAll.BorderThickness = new Thickness(1);
                        btnUpdateAll.IsEnabled = true;
                    }
                }

                // Find or create category panel
                string cat = item.Category ?? "Sonstige";
                if (!categoryPanels.TryGetValue(cat, out var catPanel))
                {
                    catPanel = new StackPanel();
                    var catHeader = new TextBlock
                    {
                        Text = cat.ToUpperInvariant(),
                        FontSize = 11,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        Foreground = ParseHexColor("#8B949E"),
                        Margin = new Thickness(4, 16, 0, 8),
                        CharacterSpacing = 50
                    };
                    catPanel.Children.Add(catHeader);
                    categoryPanels[cat] = catPanel;

                    int sortKey = cat.Equals("Basics", StringComparison.OrdinalIgnoreCase) ? 0
                        : cat.Equals("Optional", StringComparison.OrdinalIgnoreCase) ? 99 : 1;
                    categorySortKeys[cat] = sortKey;

                    // Insert category panel at correct sorted position
                    int insertIdx = PnlSystemCheckList.Children.Count; // default: append at end
                    for (int i = 2; i < PnlSystemCheckList.Children.Count; i++) // skip header + progress
                    {
                        if (PnlSystemCheckList.Children[i] is StackPanel existingPanel)
                        {
                            var existingCat = categoryPanels.FirstOrDefault(kv => kv.Value == existingPanel).Key;
                            if (existingCat != null && categorySortKeys.TryGetValue(existingCat, out int existingKey) && existingKey > sortKey)
                            {
                                insertIdx = i;
                                break;
                            }
                        }
                    }
                    PnlSystemCheckList.Children.Insert(insertIdx, catPanel);
                }

                // Append card to category panel (never destroyed)
                RenderSystemCheckCard(item, checkBoxesWithPrompts, catPanel);
            });
        }
    }

    private void RenderSystemCheckCard(SystemRequirementItem req, List<(CheckBox cb, SystemRequirementItem item)> checkBoxesWithPrompts, StackPanel targetPanel)
    {
        bool isOutdated = req.IsInstalled && !req.IsUpToDate;

        var card = new Border
        {
            Background = isOutdated ? ParseHexColor("#1A1612") : ParseHexColor("#161B22"),
            BorderBrush = isOutdated ? ParseHexColor("#F59E0B") : ParseHexColor("#30363D"),
            BorderThickness = new Thickness(isOutdated ? 1.5 : 1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var mainPanel = new StackPanel { Spacing = 8 };

        // 1. Header Row (Icon + Name on left, Status Pill Badge on right)
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var titleBlock = new TextBlock
        {
            Text = req.Name,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ParseHexColor("#F0F6FC"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        titlePanel.Children.Add(titleBlock);

        // Right: Status Pill Badge
        var statusColor = ParseHexColor(req.StatusColorHex);
        var statusPill = new Border
        {
            Background = GetSubtleStatusBg(req.StatusColorHex),
            BorderBrush = statusColor,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusText = new TextBlock
        {
            Text = req.StatusText,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = statusColor,
            VerticalAlignment = VerticalAlignment.Center
        };
        statusPill.Child = statusText;

        Grid.SetColumn(titlePanel, 0);
        Grid.SetColumn(statusPill, 1);
        headerGrid.Children.Add(titlePanel);
        headerGrid.Children.Add(statusPill);
        mainPanel.Children.Add(headerGrid);

        // 2. Description
        if (!string.IsNullOrEmpty(req.Description))
        {
            var descBlock = new TextBlock
            {
                Text = req.Description,
                FontSize = 12.5,
                Foreground = ParseHexColor("#8B949E"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, -2, 0, 2)
            };
            mainPanel.Children.Add(descBlock);
        }

        // 3. Structured 2-Column Version Box
        var versionGrid = new Grid
        {
            Background = ParseHexColor("#0D1117"),
            BorderBrush = isOutdated ? ParseHexColor("#382E1E") : ParseHexColor("#21262D"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6)
        };
        versionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        versionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: Installed Version / Status
        string leftLabelText = string.IsNullOrEmpty(req.LeftLabel) ? "INSTALLIERT" : req.LeftLabel;
        var installedStack = new StackPanel { Spacing = 2 };
        var installedLabel = new TextBlock
        {
            Text = leftLabelText,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = ParseHexColor("#6E7681"),
            CharacterSpacing = 20
        };
        var installedVal = new TextBlock
        {
            Text = req.InstalledVersion,
            FontSize = 13.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = req.IsInstalled ? (isOutdated ? ParseHexColor("#E3B341") : ParseHexColor("#C9D1D9")) : ParseHexColor("#F85149"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        installedStack.Children.Add(installedLabel);
        installedStack.Children.Add(installedVal);

        // Right: Latest Version / Metrics
        string rightLabelText = string.IsNullOrEmpty(req.RightLabel) ? "NEUESTE VERSION" : req.RightLabel;
        if (isOutdated) rightLabelText += " (UPDATE)";

        var latestStack = new StackPanel { Spacing = 2 };
        var latestLabel = new TextBlock
        {
            Text = rightLabelText,
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = isOutdated ? ParseHexColor("#F59E0B") : ParseHexColor("#6E7681"),
            CharacterSpacing = 20
        };
        var latestVal = new TextBlock
        {
            Text = req.LatestVersion,
            FontSize = 13.5,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = isOutdated ? ParseHexColor("#F59E0B") : ParseHexColor("#58A6FF"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        latestStack.Children.Add(latestLabel);
        latestStack.Children.Add(latestVal);

        Grid.SetColumn(installedStack, 0);
        Grid.SetColumn(latestStack, 1);
        versionGrid.Children.Add(installedStack);
        versionGrid.Children.Add(latestStack);
        mainPanel.Children.Add(versionGrid);

        // 4. Action Row (Checkbox on left, Action/Uninstall buttons on right)
        bool hasInstallAction = !string.IsNullOrEmpty(req.ActionButtonText);
        bool hasUninstallAction = req.IsInstalled && !string.IsNullOrEmpty(req.UninstallPrompt);

        if (hasInstallAction || hasUninstallAction || !string.IsNullOrEmpty(req.InstallPrompt))
        {
            var actionRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Checkbox for bulk actions
            var cb = new CheckBox 
            { 
                Content = "Auswählen", 
                FontSize = 12.5, 
                Foreground = ParseHexColor("#8B949E"),
                VerticalAlignment = VerticalAlignment.Center 
            };
            checkBoxesWithPrompts.Add((cb, req));
            Grid.SetColumn(cb, 0);
            actionRow.Children.Add(cb);

            // Right Button Stack (Update/Install + Uninstall)
            var buttonStack = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                Spacing = 8, 
                HorizontalAlignment = HorizontalAlignment.Right, 
                VerticalAlignment = VerticalAlignment.Center 
            };

            // Uninstall / Delete Button
            if (hasUninstallAction)
            {
                var uninstallBtn = new Button
                {
                    Content = "Löschen",
                    Height = 32,
                    FontSize = 12,
                    FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                    Background = ParseHexColor("#21262D"),
                    Foreground = ParseHexColor("#F85149"),
                    BorderBrush = ParseHexColor("#30363D"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                uninstallBtn.Click += (s, ev) =>
                {
                    if (UcPromptEditor?.TxtChatInput != null)
                    {
                        string uninstallText = $"[Bitte deinstalliere {req.Name} von meinem System:\n\n{req.UninstallPrompt}]";
                        UcPromptEditor.TxtChatInput.Text = uninstallText;
                        UcPromptEditor.TxtChatInput.Focus(FocusState.Programmatic);
                        UcPromptEditor.TxtChatInput.SelectionStart = uninstallText.Length;
                    }
                };
                buttonStack.Children.Add(uninstallBtn);
            }

            // Primary Action Button (Aktualisieren / Installieren / etc.)
            if (hasInstallAction)
            {
                string promptText = !string.IsNullOrWhiteSpace(req.InstallPrompt)
                    ? req.InstallPrompt
                    : $"Bitte installiere oder aktualisiere {req.Name} auf meinem System.";
                promptText = $"[{promptText}]";

                var actionBtn = new Button
                {
                    Content = req.ActionButtonText,
                    Height = 32,
                    FontSize = 12.5,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Background = isOutdated ? ParseHexColor("#9A3412") : ParseHexColor("#1F6FEB"),
                    Foreground = ParseHexColor("#FFFFFF"),
                    BorderBrush = isOutdated ? ParseHexColor("#F59E0B") : ParseHexColor("#388BFD"),
                    BorderThickness = new Thickness(isOutdated ? 1 : 0),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };

                actionBtn.Click += (s, ev) =>
                {
                    if (UcPromptEditor?.TxtChatInput != null)
                    {
                        UcPromptEditor.TxtChatInput.Text = promptText;
                        UcPromptEditor.TxtChatInput.Focus(FocusState.Programmatic);
                        UcPromptEditor.TxtChatInput.SelectionStart = promptText.Length;
                    }
                };

                buttonStack.Children.Add(actionBtn);
            }

            Grid.SetColumn(buttonStack, 1);
            actionRow.Children.Add(buttonStack);
            mainPanel.Children.Add(actionRow);
        }

        card.Child = mainPanel;
        targetPanel.Children.Add(card);
    }

    private SolidColorBrush GetSubtleStatusBg(string hexColor)
    {
        try
        {
            hexColor = hexColor.TrimStart('#');
            if (hexColor.Length == 6)
            {
                byte r = byte.Parse(hexColor.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hexColor.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hexColor.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return new SolidColorBrush(Windows.UI.Color.FromArgb(30, r, g, b));
            }
        }
        catch { }
        return new SolidColorBrush(Windows.UI.Color.FromArgb(30, 16, 185, 129));
    }

    private SolidColorBrush ParseHexColor(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
            }
        }
        catch { }
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
    }
}
