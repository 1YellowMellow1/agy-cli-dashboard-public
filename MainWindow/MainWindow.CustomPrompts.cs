using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Windows.System;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private string _customPromptsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "project_custom_prompts.json");
    private Dictionary<string, ProjectCustomPromptsData> _projectCustomPrompts = new(StringComparer.OrdinalIgnoreCase);
    private int _editingCustomPromptIndex = 0;
    private string _selectedEditingColorHex = "#8B5CF6";
    private ProjectCustomPromptsData? _currentEditingProjectData = null;
    private string _currentCustomPromptProject = string.Empty;

    public static readonly (string Hex, string Name)[] PromptColorPalette = new[]
    {
        ("#8B5CF6", "Violett"),
        ("#3B82F6", "Blau"),
        ("#10B981", "Smaragd"),
        ("#F59E0B", "Bernstein"),
        ("#F43F5E", "Koralle"),
        ("#06B6D4", "Cyan"),
        ("#EC4899", "Pink"),
        ("#94A3B8", "Slate")
    };

    public static SolidColorBrush HexToBrush(string hex)
    {
        return new SolidColorBrush(HexToColor(hex, 255));
    }

    public static Windows.UI.Color HexToColor(string hex, byte alpha = 255)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Windows.UI.Color.FromArgb(alpha, 148, 163, 184);
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6)
        {
            if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
            {
                return Windows.UI.Color.FromArgb(alpha, r, g, b);
            }
        }
        else if (hex.Length == 8)
        {
            if (byte.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte a) &&
                byte.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte r) &&
                byte.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte g) &&
                byte.TryParse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber, null, out byte b))
            {
                return Windows.UI.Color.FromArgb(alpha == 255 ? a : alpha, r, g, b);
            }
        }
        return Windows.UI.Color.FromArgb(alpha, 148, 163, 184);
    }

    private Dictionary<string, ProjectCustomPromptsData> LoadProjectCustomPrompts()
    {
        try
        {
            var loaded = Services.FileTimestampCache.GetOrLoadSync(_customPromptsFilePath, filePath =>
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(json);
                    var dict = new Dictionary<string, ProjectCustomPromptsData>(StringComparer.OrdinalIgnoreCase);

                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            string promptText = prop.Value.GetString() ?? "";
                            if (!string.IsNullOrWhiteSpace(promptText))
                            {
                                dict[prop.Name] = new ProjectCustomPromptsData
                                {
                                    SelectedIndex = 0,
                                    Items = new List<CustomPromptItem>
                                    {
                                        new() { Name = "Prompt 1", Color = "#8B5CF6", Prompt = promptText }
                                    }
                                };
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            var data = JsonSerializer.Deserialize<ProjectCustomPromptsData>(prop.Value.GetRawText(), _jsonOptions);
                            if (data != null)
                            {
                                dict[prop.Name] = data;
                            }
                        }
                    }
                    return dict;
                }
                return null;
            });
            if (loaded != null)
            {
                _projectCustomPrompts = new Dictionary<string, ProjectCustomPromptsData>(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CustomPrompt] Error loading project custom prompts: {ex.Message}");
        }
        return _projectCustomPrompts;
    }

    private void SaveProjectCustomPrompts()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_customPromptsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(_projectCustomPrompts, _jsonOptions);
            WriteAllTextAtomic(_customPromptsFilePath, json);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CustomPrompt] Error saving project custom prompts: {ex.Message}");
        }
    }

    private string GetEffectiveProjectName()
    {
        var (termProjName, _) = GetActiveTerminalProjectFocus();
        if (!string.IsNullOrWhiteSpace(termProjName))
        {
            if (termProjName.Equals("Globaler Fokus", StringComparison.OrdinalIgnoreCase) || termProjName.Contains("Global") || termProjName.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                return "Global";
            }
            return termProjName;
        }

        string selected = GetSelectedProjectName();
        if (!string.IsNullOrWhiteSpace(selected) && !selected.Contains("Global") && !selected.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return selected;
        }

        return "Global";
    }

    private ProjectCustomPromptsData GetCustomPromptsDataForCurrentProject()
    {
        LoadProjectCustomPrompts();
        string proj = GetEffectiveProjectName();

        if (_projectCustomPrompts.TryGetValue(proj, out var data) && data != null && data.Items.Count > 0)
        {
            return data;
        }

        // Fallback auf Global oder Alle Projekte (Global)
        if (_projectCustomPrompts.TryGetValue("Global", out var globalData) && globalData != null && globalData.Items.Count > 0)
        {
            return globalData;
        }
        if (_projectCustomPrompts.TryGetValue("Alle Projekte (Global)", out var allGlobalData) && allGlobalData != null && allGlobalData.Items.Count > 0)
        {
            return allGlobalData;
        }

        return new ProjectCustomPromptsData();
    }

    private CustomPromptItem? GetFirstCustomPrompt()
    {
        var data = GetCustomPromptsDataForCurrentProject();
        if (data.Items.Count == 0) return null;
        return data.Items[0];
    }

    private string GetCustomPromptForCurrentProject()
    {
        return GetFirstCustomPrompt()?.Prompt ?? string.Empty;
    }

    private void BtnCustomPrompt_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnCustomPrompt_Click)} triggered.");
        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftPressed = shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (isShiftPressed)
        {
            _isCustomPromptModeEnabled = !_isCustomPromptModeEnabled;
            UpdateModeButtonVisuals();
        }
        else
        {
            var firstPrompt = GetFirstCustomPrompt();
            if (firstPrompt == null || string.IsNullOrWhiteSpace(firstPrompt.Prompt))
            {
                string currentProject = GetEffectiveProjectName();
                ShowNotice("Kein Custom Prompt", $"Für '{currentProject}' ist noch kein Prompt definiert. Im geöffneten Menü kannst du sofort einen anlegen.", InfoBarSeverity.Informational);
                ShowCustomPromptEditorPanel();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(TxtChatInput.Text))
                {
                    SendDirectCustomPrompt();
                }
                else
                {
                    _isCustomPromptModeEnabled = !_isCustomPromptModeEnabled;
                    UpdateModeButtonVisuals();
                }
            }
        }
    }

    private void BtnCustomPrompt_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightClick Trace] {nameof(BtnCustomPrompt_RightTapped)} triggered.");
        ShowCustomPromptEditorPanel();
        e.Handled = true;
    }

    private void SendDirectCustomPrompt()
    {
        string text = GetCustomPromptForCurrentProject();
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!text.StartsWith("[") || !text.EndsWith("]")) text = $"[{text.Trim('[', ']')}]";

        var activeSession = GetActiveTerminalSession();
        if (activeSession != null)
        {
            activeSession.IsUsed = true;
            activeSession.HasPromptBeenDispatched = true;
            if (activeSession.Id == _initialTerminalId)
            {
                _initialTerminalId = null;
            }

            if (activeSession.Title == "Neuer Chat" || string.IsNullOrWhiteSpace(activeSession.Title))
            {
                string promptTitle = Services.DatabaseService.CleanUserInputToTitle(text);
                if (!string.IsNullOrWhiteSpace(promptTitle))
                {
                    activeSession.Title = promptTitle;
                    RenderWorkspaceTabs();
                }
            }
        }

        try
        {
            if (ActiveTerminal?.ConPTYTerm != null)
            {
                ActiveTerminal.ConPTYTerm.WriteToTerm(PrepareConPtyPayload(text));
                _lastPromptSendTime = DateTime.Now;
                TriggerPromptResumeSync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error sending custom prompt command: {ex.Message}");
        }
    }

    public void SyncCustomPromptEditorPanel(bool force = false)
    {
        if (PnlCustomPromptEditor == null || PnlCustomPromptEditor.Visibility != Visibility.Visible)
        {
            return;
        }

        string currentProject = GetEffectiveProjectName();
        if (!force && string.Equals(_currentCustomPromptProject, currentProject, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentCustomPromptProject = currentProject;
        LoadProjectCustomPrompts();

        if (TxtCustomPromptProjectBadge != null)
        {
            TxtCustomPromptProjectBadge.Text = currentProject;
        }
        if (TxtCustomPromptPanelTitle != null)
        {
            TxtCustomPromptPanelTitle.Text = "CUSTOM PROMPTS";
        }

        ProjectCustomPromptsData? data = null;
        if (_projectCustomPrompts.TryGetValue(currentProject, out var directData) && directData != null && directData.Items.Count > 0)
        {
            data = directData;
        }
        else if (currentProject.Contains("Global", StringComparison.OrdinalIgnoreCase) || currentProject.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (_projectCustomPrompts.TryGetValue("Global", out var gData) && gData != null && gData.Items.Count > 0) data = gData;
            else if (_projectCustomPrompts.TryGetValue("Alle Projekte (Global)", out var agData) && agData != null && agData.Items.Count > 0) data = agData;
        }

        if (data == null || data.Items.Count == 0)
        {
            _currentEditingProjectData = new ProjectCustomPromptsData
            {
                SelectedIndex = 0,
                Items = new List<CustomPromptItem>()
            };
        }
        else
        {
            // Clone for safe in-memory editing
            _currentEditingProjectData = new ProjectCustomPromptsData
            {
                SelectedIndex = 0,
                Items = data.Items.Select(it => new CustomPromptItem { Id = it.Id, Name = it.Name, Color = it.Color, Prompt = it.Prompt }).ToList()
            };
        }

        // Switch to List View by default on project sync/open
        if (PnlCustomPromptListView != null) PnlCustomPromptListView.Visibility = Visibility.Visible;
        if (PnlCustomPromptFormView != null) PnlCustomPromptFormView.Visibility = Visibility.Collapsed;

        RenderCustomPromptCardsList();
    }

    private void ShowCustomPromptEditorPanel()
    {
        // Hide conflicting full-sidebar panels
        if (UcModelPicker != null) UcModelPicker.Visibility = Visibility.Collapsed;
        if (UcTerminalShortcuts != null) UcTerminalShortcuts.Visibility = Visibility.Collapsed;
        if (PnlSystemCheckPanel != null) PnlSystemCheckPanel.Visibility = Visibility.Collapsed;

        if (PnlCustomPromptEditor != null)
        {
            PnlCustomPromptEditor.Visibility = Visibility.Visible;
        }

        SyncCustomPromptEditorPanel(force: true);
    }

    private void RenderCustomPromptCardsList()
    {
        if (PnlCustomPromptCardsList == null || _currentEditingProjectData == null) return;
        PnlCustomPromptCardsList.Children.Clear();

        string currentProj = GetEffectiveProjectName();

        if (_currentEditingProjectData.Items.Count == 0)
        {
            var emptyBorder = new Border
            {
                Background = (SolidColorBrush)Application.Current.Resources["AgyAccentSubtle"],
                BorderBrush = (SolidColorBrush)Application.Current.Resources["AgyBorderDefault"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 16, 14, 16),
                Margin = new Thickness(0, 4, 0, 4)
            };
            var emptyStack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            emptyStack.Children.Add(new TextBlock
            {
                Text = "Noch keine Custom Buttons vorhanden",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["AgyTextSecondary"],
                HorizontalAlignment = HorizontalAlignment.Center
            });
            emptyStack.Children.Add(new TextBlock
            {
                Text = $"Klicke oben auf '+ Neuer Button', um für '{currentProj}' einen Prompt anzulegen.",
                FontSize = 11.5,
                Foreground = (SolidColorBrush)Application.Current.Resources["AgyTextMuted"],
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            });
            emptyBorder.Child = emptyStack;
            PnlCustomPromptCardsList.Children.Add(emptyBorder);
            return;
        }

        for (int i = 0; i < _currentEditingProjectData.Items.Count; i++)
        {
            var item = _currentEditingProjectData.Items[i];
            int itemIndex = i;

            var cardBorder = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 28, 32, 40)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 47, 61)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 8, 8)
            };

            var cardGrid = new Grid();
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 1. Color Indicator Circle (Left)
            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = HexToBrush(item.Color),
                Margin = new Thickness(2, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);

            // 2. Info Column (Title + Prompt preview)
            var infoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            headerRow.Children.Add(new TextBlock
            {
                Text = item.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (SolidColorBrush)Application.Current.Resources["AgyTextPrimary"],
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (itemIndex == 0)
            {
                var mainBadge = new Border
                {
                    Background = new SolidColorBrush(HexToColor(item.Color, 40)),
                    BorderBrush = HexToBrush(item.Color),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(5, 1, 5, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                mainBadge.Child = new TextBlock
                {
                    Text = "Toolbar-Stern",
                    FontSize = 10,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = HexToBrush(item.Color)
                };
                headerRow.Children.Add(mainBadge);
            }
            infoStack.Children.Add(headerRow);

            string preview = string.IsNullOrWhiteSpace(item.Prompt) ? "[Kein Text hinterlegt]" : item.Prompt.Replace("\r", " ").Replace("\n", " ");
            infoStack.Children.Add(new TextBlock
            {
                Text = preview,
                FontSize = 11.5,
                Foreground = (SolidColorBrush)Application.Current.Resources["AgyTextMuted"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
            Grid.SetColumn(infoStack, 1);

            // 3. Actions Column (Edit & Delete)
            var actionsStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

            var editBtn = new Button
            {
                AllowFocusOnInteraction = false,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(5)
            };
            ToolTipService.SetToolTip(editBtn, "Button bearbeiten");
            editBtn.Content = new ImageIcon
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/edit-3.svg")),
                Width = 14,
                Height = 14
            };
            editBtn.Click += (s, e) =>
            {
                OpenPromptEditorForm(itemIndex);
            };

            var deleteBtn = new Button
            {
                AllowFocusOnInteraction = false,
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(5)
            };
            ToolTipService.SetToolTip(deleteBtn, "Button löschen");
            deleteBtn.Content = new ImageIcon
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(new Uri("ms-appx:///Assets/Icons/trash-2.svg")),
                Width = 14,
                Height = 14
            };
            deleteBtn.Click += (s, e) =>
            {
                DeleteSpecificCustomPrompt(itemIndex);
            };

            actionsStack.Children.Add(editBtn);
            actionsStack.Children.Add(deleteBtn);
            Grid.SetColumn(actionsStack, 2);

            cardGrid.Children.Add(dot);
            cardGrid.Children.Add(infoStack);
            cardGrid.Children.Add(actionsStack);
            cardBorder.Child = cardGrid;

            // Make the entire card clickable (except actions) to execute prompt
            cardBorder.PointerEntered += (s, e) =>
            {
                cardBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 42, 54));
                cardBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 66, 84));
            };
            cardBorder.PointerExited += (s, e) =>
            {
                cardBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 28, 32, 40));
                cardBorder.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 42, 47, 61));
            };
            cardBorder.PointerPressed += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject dep && FindParent<Button>(dep) != null) return;
                ExecuteSpecificCustomPrompt(item.Prompt);
            };

            PnlCustomPromptCardsList.Children.Add(cardBorder);
        }
    }

    private static T? FindParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void ExecuteSpecificCustomPrompt(string promptText)
    {
        if (string.IsNullOrWhiteSpace(promptText)) return;

        string formatted = promptText.Trim();
        if (!formatted.StartsWith("[") || !formatted.EndsWith("]")) formatted = $"[{formatted.Trim('[', ']')}]";

        if (string.IsNullOrWhiteSpace(TxtChatInput.Text))
        {
            var activeSession = GetActiveTerminalSession();
            if (activeSession != null)
            {
                activeSession.IsUsed = true;
                activeSession.HasPromptBeenDispatched = true;
                if (activeSession.Id == _initialTerminalId)
                {
                    _initialTerminalId = null;
                }

                if (activeSession.Title == "Neuer Chat" || string.IsNullOrWhiteSpace(activeSession.Title))
                {
                    string promptTitle = Services.DatabaseService.CleanUserInputToTitle(formatted);
                    if (!string.IsNullOrWhiteSpace(promptTitle))
                    {
                        activeSession.Title = promptTitle;
                        RenderWorkspaceTabs();
                    }
                }
            }

            try
            {
                if (ActiveTerminal?.ConPTYTerm != null)
                {
                    ActiveTerminal.ConPTYTerm.WriteToTerm(PrepareConPtyPayload(formatted));
                    _lastPromptSendTime = DateTime.Now;
                    TriggerPromptResumeSync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending custom prompt command: {ex.Message}");
            }
        }
        else
        {
            string currentText = TxtChatInput.Text;
            if (!currentText.EndsWith(" "))
            {
                TxtChatInput.Text = currentText + " " + formatted;
            }
            else
            {
                TxtChatInput.Text = currentText + formatted;
            }
            TxtChatInput.Focus(FocusState.Programmatic);
            TxtChatInput.SelectionStart = TxtChatInput.Text.Length;
        }
    }

    private void BtnAddCustomPrompt_Click(object sender, RoutedEventArgs e)
    {
        OpenPromptEditorForm(-1);
    }

    private void OpenPromptEditorForm(int index)
    {
        _editingCustomPromptIndex = index;
        string currentProj = GetEffectiveProjectName();

        if (PnlCustomPromptListView != null) PnlCustomPromptListView.Visibility = Visibility.Collapsed;
        if (PnlCustomPromptFormView != null) PnlCustomPromptFormView.Visibility = Visibility.Visible;

        if (index < 0 || _currentEditingProjectData == null || index >= _currentEditingProjectData.Items.Count)
        {
            if (TxtCustomPromptPanelTitle != null) TxtCustomPromptPanelTitle.Text = "NEUER PROMPT";
            if (TxtCustomPromptName != null) TxtCustomPromptName.Text = $"Prompt {(_currentEditingProjectData?.Items.Count ?? 0) + 1}";
            _selectedEditingColorHex = PromptColorPalette[(_currentEditingProjectData?.Items.Count ?? 0) % PromptColorPalette.Length].Hex;
            if (TxtCustomPromptInput != null) TxtCustomPromptInput.Text = "";
        }
        else
        {
            if (TxtCustomPromptPanelTitle != null) TxtCustomPromptPanelTitle.Text = "PROMPT BEARBEITEN";
            var item = _currentEditingProjectData.Items[index];
            if (TxtCustomPromptName != null) TxtCustomPromptName.Text = item.Name;
            _selectedEditingColorHex = item.Color;
            if (TxtCustomPromptInput != null) TxtCustomPromptInput.Text = item.Prompt;
        }

        RenderColorPalette(_selectedEditingColorHex);

        TxtCustomPromptName?.Focus(FocusState.Programmatic);
        TxtCustomPromptName?.SelectAll();
    }

    private void RenderColorPalette(string selectedHex)
    {
        if (PnlColorPalette == null) return;
        PnlColorPalette.Children.Clear();

        foreach (var (hex, name) in PromptColorPalette)
        {
            bool isColorSelected = string.Equals(hex, selectedHex, StringComparison.OrdinalIgnoreCase);

            var colorBtn = new Button
            {
                AllowFocusOnInteraction = false,
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(13),
                Background = HexToBrush(hex),
                BorderBrush = isColorSelected ? new SolidColorBrush(Microsoft.UI.Colors.White) : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(isColorSelected ? 2.5 : 1)
            };
            ToolTipService.SetToolTip(colorBtn, name);

            if (isColorSelected)
            {
                colorBtn.Content = new TextBlock
                {
                    Text = "✓",
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }

            string targetHex = hex;
            colorBtn.Click += (s, e) =>
            {
                _selectedEditingColorHex = targetHex;
                RenderColorPalette(targetHex);
            };

            PnlColorPalette.Children.Add(colorBtn);
        }
    }

    private void BtnBackToPromptList_Click(object sender, RoutedEventArgs e)
    {
        if (PnlCustomPromptFormView != null) PnlCustomPromptFormView.Visibility = Visibility.Collapsed;
        if (PnlCustomPromptListView != null) PnlCustomPromptListView.Visibility = Visibility.Visible;
        if (TxtCustomPromptPanelTitle != null) TxtCustomPromptPanelTitle.Text = "CUSTOM PROMPTS";
        RenderCustomPromptCardsList();
    }

    private void BtnSaveCustomPromptForm_Click(object sender, RoutedEventArgs e)
    {
        ExecuteSaveCustomPromptForm();
    }

    private void ExecuteSaveCustomPromptForm()
    {
        string currentProject = GetEffectiveProjectName();
        string name = TxtCustomPromptName?.Text.Trim() ?? "";
        string prompt = TxtCustomPromptInput?.Text.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(name)) name = "Custom Prompt";

        if (_currentEditingProjectData == null)
        {
            _currentEditingProjectData = new ProjectCustomPromptsData();
        }

        if (_editingCustomPromptIndex < 0 || _editingCustomPromptIndex >= _currentEditingProjectData.Items.Count)
        {
            _currentEditingProjectData.Items.Add(new CustomPromptItem
            {
                Name = name,
                Color = _selectedEditingColorHex,
                Prompt = prompt
            });
        }
        else
        {
            var item = _currentEditingProjectData.Items[_editingCustomPromptIndex];
            item.Name = name;
            item.Color = _selectedEditingColorHex;
            item.Prompt = prompt;
        }

        _projectCustomPrompts[currentProject] = _currentEditingProjectData;
        SaveProjectCustomPrompts();

        UpdateCustomPromptButtonToolTip();
        UpdateModeButtonVisuals();

        BtnBackToPromptList_Click(this, new RoutedEventArgs());
        ShowNotice("Custom Prompt gespeichert", $"Prompt '{name}' für '{currentProject}' erfolgreich gesichert.", InfoBarSeverity.Success);
    }

    private void DeleteSpecificCustomPrompt(int index)
    {
        string currentProject = GetEffectiveProjectName();
        if (_currentEditingProjectData != null && index >= 0 && index < _currentEditingProjectData.Items.Count)
        {
            string deletedName = _currentEditingProjectData.Items[index].Name;
            _currentEditingProjectData.Items.RemoveAt(index);
            _projectCustomPrompts[currentProject] = _currentEditingProjectData;
            SaveProjectCustomPrompts();

            UpdateCustomPromptButtonToolTip();
            UpdateModeButtonVisuals();
            RenderCustomPromptCardsList();

            ShowNotice("Prompt gelöscht", $"Prompt '{deletedName}' wurde entfernt.", InfoBarSeverity.Informational);
        }
    }

    private void HideCustomPromptEditorPanel()
    {
        if (PnlCustomPromptEditor != null)
        {
            PnlCustomPromptEditor.Visibility = Visibility.Collapsed;
        }
        _currentCustomPromptProject = string.Empty;
    }

    private void BtnCloseCustomPromptPanel_Click(object sender, RoutedEventArgs e)
    {
        HideCustomPromptEditorPanel();
    }

    private void TxtCustomPromptInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            if (ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            {
                e.Handled = true;
                ExecuteSaveCustomPromptForm();
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            BtnBackToPromptList_Click(this, new RoutedEventArgs());
        }
    }

    public void UpdateCustomPromptButtonToolTip()
    {
        if (BtnCustomPrompt == null) return;
        string currentProject = GetEffectiveProjectName();
        var firstPrompt = GetFirstCustomPrompt();

        if (PnlCustomPromptEditor != null && PnlCustomPromptEditor.Visibility == Visibility.Visible)
        {
            SyncCustomPromptEditorPanel(force: false);
        }

        if (firstPrompt == null || string.IsNullOrWhiteSpace(firstPrompt.Prompt))
        {
            ToolTipService.SetToolTip(BtnCustomPrompt, $"Custom Prompt ({currentProject}): [Noch nicht definiert] (Klick: Ausführen / Einfügen | Rechtsklick: Buttons verwalten)");
        }
        else
        {
            string preview = firstPrompt.Prompt.Length > 80 ? firstPrompt.Prompt.Substring(0, 77) + "..." : firstPrompt.Prompt;
            ToolTipService.SetToolTip(BtnCustomPrompt, $"Custom Prompt ({currentProject}) [{firstPrompt.Name}]: \"{preview}\" (Klick: 1. Prompt senden / einfügen | Rechtsklick: Buttons verwalten)");
        }
    }
}
