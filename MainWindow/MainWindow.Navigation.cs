using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private int _baseTerminalFontSize = 13;
    private int _fontSize = 13;
    private bool _isSidebarVisible = true;
    private static readonly SolidColorBrush NavHoverBgBrush = new(Windows.UI.Color.FromArgb(20, 255, 255, 255));
    private static readonly SolidColorBrush NavTransparentBrush = new(Microsoft.UI.Colors.Transparent);
    private static readonly SolidColorBrush NavActiveFgBrush = new(Microsoft.UI.Colors.White);

    private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnToggleSidebar_Click)} triggered.");
        _isSidebarVisible = !_isSidebarVisible;
        ApplySidebarWidth();
    }

    private void ApplyFontSizeAndFamily(EasyWindowsTerminalControl.EasyTerminalControl termControl, int size)
    {
        if (termControl == null) return;
        termControl.FontSizeWhenSettingTheme = size;
        termControl.FontFamilyWhenSettingTheme = new FontFamily("Consolas");

        try
        {
            var innerTerminal = termControl.Terminal;
            if (innerTerminal != null)
            {
                var methods = innerTerminal.GetType().GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                var setThemeMethod = methods.FirstOrDefault(m => m.Name == "SetTheme" && m.GetParameters().Length == 4);

                if (setThemeMethod != null)
                {
                    var paramsInfo = setThemeMethod.GetParameters();
                    var themeParamType = paramsInfo[0].ParameterType;
                    object themeInstance = Activator.CreateInstance(themeParamType)!;

                    // COLORREF format: 0x00BBGGRR -> R=14, G=16, B=19 (#0E1013, Dark Slate Terminal Background)
                    uint bgRef = (uint)(14 | (16 << 8) | (19 << 16));
                    uint fgRef = (uint)(226 | (232 << 8) | (240 << 16)); // Light slate text #E2E8F0

                    var bgField = themeParamType.GetField("DefaultBackground") ?? themeParamType.GetField("Background");
                    var bgProp = themeParamType.GetProperty("DefaultBackground") ?? themeParamType.GetProperty("Background");
                    if (bgField != null) bgField.SetValue(themeInstance, bgRef);
                    else if (bgProp != null) bgProp.SetValue(themeInstance, bgRef);

                    var fgField = themeParamType.GetField("DefaultForeground") ?? themeParamType.GetField("Foreground");
                    var fgProp = themeParamType.GetProperty("DefaultForeground") ?? themeParamType.GetProperty("Foreground");
                    if (fgField != null) fgField.SetValue(themeInstance, fgRef);
                    else if (fgProp != null) fgProp.SetValue(themeInstance, fgRef);

                    // Selection Background: Slate-700 #334155
                    uint selRef = (uint)(51 | (65 << 8) | (85 << 16));
                    var selField = themeParamType.GetField("DefaultSelectionBackground") ?? themeParamType.GetField("SelectionBackground");
                    var selProp = themeParamType.GetProperty("DefaultSelectionBackground") ?? themeParamType.GetProperty("SelectionBackground");
                    if (selField != null) selField.SetValue(themeInstance, selRef);
                    else if (selProp != null) selProp.SetValue(themeInstance, selRef);

                    // 16-Farben ANSI-Palette harmonisieren:
                    // Index 0 (Black) wird auf bgRef (#0E1013) gemappt, um pechschwarze Blöcke (z. B. bei Build-Outputs wie npm/vite) zu verhindern.
                    // Index 8 (Bright Black / Dimmed) wird auf Slate-400 (#94A3B8) gemappt, um unsichtbare "Schwarz-auf-Schwarz"-Schriften zu eliminieren.
                    static uint ToColorRef(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));
                    uint[] colorTable = new uint[16]
                    {
                        bgRef,                      // 0: Black -> identisch mit Dashboard Background #0E1013
                        ToColorRef(224, 108, 117),  // 1: Red #E06C75
                        ToColorRef(152, 195, 121),  // 2: Green #98C379
                        ToColorRef(229, 192, 123),  // 3: Yellow #E5C07B
                        ToColorRef(97, 175, 239),   // 4: Blue #61AFEF
                        ToColorRef(198, 120, 221),  // 5: Magenta #C678DD
                        ToColorRef(86, 182, 194),   // 6: Cyan #56B6C2
                        ToColorRef(220, 223, 228),  // 7: White #DCDFE4
                        ToColorRef(148, 163, 184),  // 8: Bright Black -> Slate-400 #94A3B8 (für Dimmed / Gray)
                        ToColorRef(224, 108, 117),  // 9: Bright Red #E06C75
                        ToColorRef(152, 195, 121),  // 10: Bright Green #98C379
                        ToColorRef(229, 192, 123),  // 11: Bright Yellow #E5C07B
                        ToColorRef(97, 175, 239),   // 12: Bright Blue #61AFEF
                        ToColorRef(198, 120, 221),  // 13: Bright Magenta #C678DD
                        ToColorRef(86, 182, 194),   // 14: Bright Cyan #56B6C2
                        ToColorRef(255, 255, 255)   // 15: Bright White #FFFFFF
                    };

                    var colorTableField = themeParamType.GetField("ColorTable") ?? themeParamType.GetField("_ColorTable");
                    var colorTableProp = themeParamType.GetProperty("ColorTable");
                    if (colorTableField != null) colorTableField.SetValue(themeInstance, colorTable);
                    else if (colorTableProp != null) colorTableProp.SetValue(themeInstance, colorTable);

                    var bgColor = System.Drawing.Color.FromArgb(255, 14, 16, 19);

                    setThemeMethod.Invoke(innerTerminal, new object[] {
                        themeInstance,
                        "Consolas",
                        (short)size,
                        bgColor
                    });

                    var themeProp = termControl.GetType().GetProperty("Theme");
                    if (themeProp != null)
                    {
                        themeProp.SetValue(termControl, themeInstance);
                    }
                }
            }
        }
        catch { }
    }

    private void BtnFontDecrease_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnFontDecrease_Click)} triggered.");
        if (_baseTerminalFontSize > 8)
        {
            _baseTerminalFontSize -= 1;
            _fontSize = _baseTerminalFontSize;
            TxtFontSize.Text = _fontSize.ToString();
            _projectSettings.TerminalFontSize = _baseTerminalFontSize;
            SaveProjectSettings(refreshUI: false);
            foreach (var ts in _terminalSessions)
            {
                if (ts.Control != null)
                {
                    ApplyFontSizeAndFamily(ts.Control, _fontSize);
                }
            }
        }
    }

    private void BtnFontIncrease_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnFontIncrease_Click)} triggered.");
        if (_baseTerminalFontSize < 60)
        {
            _baseTerminalFontSize += 1;
            _fontSize = _baseTerminalFontSize;
            TxtFontSize.Text = _fontSize.ToString();
            _projectSettings.TerminalFontSize = _baseTerminalFontSize;
            SaveProjectSettings(refreshUI: false);
            foreach (var ts in _terminalSessions)
            {
                if (ts.Control != null)
                {
                    ApplyFontSizeAndFamily(ts.Control, _fontSize);
                }
            }
        }
    }



    private void TabNav_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tabTag)
        {
            if (!string.Equals(tabTag, _uiStates.LastActiveTab, StringComparison.OrdinalIgnoreCase))
            {
                btn.Background = NavHoverBgBrush;
            }
        }
    }

    private void TabNav_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tabTag)
        {
            if (!string.Equals(tabTag, _uiStates.LastActiveTab, StringComparison.OrdinalIgnoreCase))
            {
                btn.Background = NavTransparentBrush;
            }
        }
    }

    private void UpdateSidebarTabVisuals(string activeTabTag)
    {
        var tabs = new[]
        {
            (Btn: TabChatsBtn, Indicator: TabChatsIndicator, Tag: "chats"),
            (Btn: TabPromptsBtn, Indicator: TabPromptsIndicator, Tag: "prompts"),
            (Btn: TabFilesBtn, Indicator: TabFilesIndicator, Tag: "files"),
            (Btn: TabDocsBtn, Indicator: TabDocsIndicator, Tag: "docs"),
            (Btn: TabGitBtn, Indicator: TabGitIndicator, Tag: "git"),
            (Btn: TabNotesBtn, Indicator: TabNotesIndicator, Tag: "notes")
        };

        var activeBg = (Brush)Application.Current.Resources["AgyAccentSubtle"];
        var inactiveBg = NavTransparentBrush;

        foreach (var t in tabs)
        {
            if (t.Btn == null) continue;
            bool isActive = string.Equals(t.Tag, activeTabTag, StringComparison.OrdinalIgnoreCase) ||
                            (string.Equals(t.Tag, "notes", StringComparison.OrdinalIgnoreCase) && string.Equals(activeTabTag, "todos", StringComparison.OrdinalIgnoreCase));

            t.Btn.Background = isActive ? activeBg : inactiveBg;

            if (t.Indicator != null)
            {
                t.Indicator.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void TabNav_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(TabNav_Click)} triggered.");
        if (sender is Button btn && btn.Tag is string tabTag)
        {
            if (tabTag == "todos") tabTag = "notes";
            _uiStates.LastActiveTab = tabTag;
            SaveUiStates();

            UpdateSidebarTabVisuals(tabTag);

            // Toggle Panel Visibility
            UcChatSidebar.Visibility = tabTag == "chats" ? Visibility.Visible : Visibility.Collapsed;
            UcPromptsSidebar.Visibility = tabTag == "prompts" ? Visibility.Visible : Visibility.Collapsed;
            UcFilesSidebar.Visibility = tabTag == "files" ? Visibility.Visible : Visibility.Collapsed;
            UcDocsSidebar.Visibility = tabTag == "docs" ? Visibility.Visible : Visibility.Collapsed;
            UcGitSidebar.Visibility = tabTag == "git" ? Visibility.Visible : Visibility.Collapsed;
            UcNotesSidebar.Visibility = tabTag == "notes" ? Visibility.Visible : Visibility.Collapsed;

            if (tabTag == "prompts")
            {
                UcPromptsSidebar?.ScrollToBottom();
            }

            // Auto-close fullscreen overlays on tab change
            HideCustomPromptEditorPanel();
            if (UcModelPicker != null) UcModelPicker.Visibility = Visibility.Collapsed;
            if (UcTerminalShortcuts != null) UcTerminalShortcuts.Visibility = Visibility.Collapsed;
            if (PnlSystemCheckPanel != null) PnlSystemCheckPanel.Visibility = Visibility.Collapsed;

            // Toggle Header Chat Buttons Visibility
            if (BtnChatsMore != null) BtnChatsMore.Visibility = tabTag == "chats" ? Visibility.Visible : Visibility.Collapsed;

            UpdateActiveTabContent();
        }
    }
}
