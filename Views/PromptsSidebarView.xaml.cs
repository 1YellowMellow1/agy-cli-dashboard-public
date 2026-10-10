using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using AgyCliDashboard.Helpers;

namespace AgyCliDashboard.Views;

public sealed partial class PromptsSidebarView : UserControl
{
    public event EventHandler<string>? PromptSelectedForInput;
    public event EventHandler<UserPromptItem>? PromptOpenInEditorRequested;
    public event EventHandler? RefreshRequested;

    public ObservableCollection<UserPromptItem> DisplayedPrompts { get; } = new();
    private readonly List<UserPromptItem> _allPrompts = new();

    public PromptsSidebarView()
    {
        this.InitializeComponent();
    }

    public void SetPrompts(string chatTitle, IEnumerable<UserPromptItem> prompts)
    {
        string title = string.IsNullOrWhiteSpace(chatTitle) ? "Aktiver Chat" : chatTitle;
        if (TxtChatTitle != null && TxtChatTitle.Text != title)
        {
            TxtChatTitle.Text = title;
        }

        var incomingList = prompts as IList<UserPromptItem> ?? prompts.ToList();

        // 1. Identitätsprüfung: Haben sich die Prompts überhaupt geändert?
        // Verhindert unnötige UI-Resets & ungewolltes Scrollen, während die KI im Hintergrund antwortet
        if (_allPrompts.Count == incomingList.Count)
        {
            bool isIdentical = true;
            for (int i = 0; i < _allPrompts.Count; i++)
            {
                if (_allPrompts[i].Index != incomingList[i].Index ||
                    _allPrompts[i].CleanText != incomingList[i].CleanText)
                {
                    isIdentical = false;
                    break;
                }

                // In-Place Update für Fazit (falls nachträglich durch KI-Antwort ergänzt)
                if (_allPrompts[i].AssistantSummary != incomingList[i].AssistantSummary)
                {
                    _allPrompts[i].AssistantSummary = incomingList[i].AssistantSummary;
                }
            }

            if (isIdentical)
            {
                // Prompts sind strukturell identisch -> keinerlei UI- oder Scroll-Aktivität
                return;
            }
        }

        // 2. Append-Only Prüfung: Wurden lediglich am Ende neue Prompts hinzugefügt?
        // Bestehende Elemente und Scroll-Position bleiben vollständig intakt
        if (incomingList.Count > _allPrompts.Count && _allPrompts.Count > 0)
        {
            bool isAppendOnly = true;
            for (int i = 0; i < _allPrompts.Count; i++)
            {
                if (_allPrompts[i].Index != incomingList[i].Index ||
                    _allPrompts[i].CleanText != incomingList[i].CleanText)
                {
                    isAppendOnly = false;
                    break;
                }

                // In-Place Update für Fazit auch bei bestehenden Prompts prüfen
                if (_allPrompts[i].AssistantSummary != incomingList[i].AssistantSummary)
                {
                    _allPrompts[i].AssistantSummary = incomingList[i].AssistantSummary;
                }
            }

            if (isAppendOnly)
            {
                string query = TxtSearch?.Text?.Trim() ?? string.Empty;
                for (int i = _allPrompts.Count; i < incomingList.Count; i++)
                {
                    var item = incomingList[i];
                    _allPrompts.Add(item);
                    if (MatchesFilter(item, query))
                    {
                        DisplayedPrompts.Add(item);
                    }
                }
                UpdateHeaderAndEmptyState();
                return;
            }
        }

        // 3. Vollständige Neubelegung (z. B. Wechsel zu einem anderen Chat)
        _allPrompts.Clear();
        _allPrompts.AddRange(incomingList);
        ApplyFilter();
        ScrollToBottom();
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private static bool MatchesFilter(UserPromptItem item, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return (item.CleanText?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
               (item.DisplayIndex?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
               (!string.IsNullOrEmpty(item.AssistantSummary) && item.AssistantSummary.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateHeaderAndEmptyState()
    {
        if (TxtPromptCount != null)
        {
            TxtPromptCount.Text = $"{_allPrompts.Count} Prompt{(_allPrompts.Count == 1 ? "" : "s")}";
        }
        if (PnlEmptyState != null)
        {
            PnlEmptyState.Visibility = DisplayedPrompts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ListPrompts != null)
        {
            ListPrompts.Visibility = DisplayedPrompts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ApplyFilter()
    {
        string query = TxtSearch?.Text?.Trim() ?? string.Empty;
        DisplayedPrompts.Clear();

        var filtered = string.IsNullOrEmpty(query)
            ? _allPrompts
            : _allPrompts.Where(p => MatchesFilter(p, query));

        foreach (var p in filtered)
        {
            DisplayedPrompts.Add(p);
        }

        UpdateHeaderAndEmptyState();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BtnLoadPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is UserPromptItem item)
        {
            PromptSelectedForInput?.Invoke(this, item.CleanText);
        }
    }

    private void BtnCopyPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is UserPromptItem item)
        {
            try
            {
                var dp = new DataPackage();
                dp.SetText(item.CleanText);
                Clipboard.SetContent(dp);
                ToolTipService.SetToolTip(btn, "Kopiert!");
                var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                EventHandler<object> tickHandler = null!;
                tickHandler = (s, ev) =>
                {
                    resetTimer.Stop();
                    resetTimer.Tick -= tickHandler;
                    try { ToolTipService.SetToolTip(btn, "Prompt in Zwischenablage kopieren"); } catch { }
                };
                resetTimer.Tick += tickHandler;
                resetTimer.Start();
            }
            catch { }
        }
    }

    private void BtnCopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is UserPromptItem item && !string.IsNullOrWhiteSpace(item.AssistantSummary))
        {
            try
            {
                var dp = new DataPackage();
                dp.SetText(item.AssistantSummary);
                Clipboard.SetContent(dp);
                ToolTipService.SetToolTip(btn, "Fazit kopiert!");
                var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                EventHandler<object> tickHandler = null!;
                tickHandler = (s, ev) =>
                {
                    resetTimer.Stop();
                    resetTimer.Tick -= tickHandler;
                    try { ToolTipService.SetToolTip(btn, "Fazit in Zwischenablage kopieren"); } catch { }
                };
                resetTimer.Tick += tickHandler;
                resetTimer.Start();
            }
            catch { }
        }
    }

    private void BtnOpenEditor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is UserPromptItem item)
        {
            PromptOpenInEditorRequested?.Invoke(this, item);
        }
    }

    private void PromptCard_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Klicks auf Aktions-Buttons nicht abfangen
        if (e.OriginalSource is DependencyObject dep)
        {
            var parentBtn = FindParent<Button>(dep);
            if (parentBtn != null) return;
        }

        if (sender is FrameworkElement fe && fe.DataContext is UserPromptItem item)
        {
            PromptSelectedForInput?.Invoke(this, item.CleanText);
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

    private DispatcherTimer? _scrollTimer;

    public void ScrollToBottom()
    {
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ScrollToBottom());
            return;
        }

        if (DisplayedPrompts.Count == 0 || this.Visibility != Visibility.Visible) return;

        PerformScrollToBottom();

        // Zweiter Durchlauf nach kurzem Render-Intervall für variable Item-Höhen & WinUI-Virtualisierung (debounced)
        _scrollTimer?.Stop();
        _scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _scrollTimer.Tick += (s, e) =>
        {
            _scrollTimer?.Stop();
            _scrollTimer = null;
            PerformScrollToBottom();
        };
        _scrollTimer.Start();
    }

    private void PerformScrollToBottom()
    {
        try
        {
            if (this.Visibility != Visibility.Visible || DisplayedPrompts.Count == 0 || ListPrompts == null) return;
            var lastItem = DisplayedPrompts[^1];
            ListPrompts.ScrollIntoView(lastItem);

            var sv = FindScrollViewer(ListPrompts);
            if (sv != null && sv.ScrollableHeight > 0)
            {
                sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[PromptsSidebarView PerformScrollToBottom Error] {ex.Message}");
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? parent)
    {
        if (parent == null) return null;
        if (parent is ScrollViewer sv) return sv;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }
}
