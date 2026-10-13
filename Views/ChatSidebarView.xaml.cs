using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace AgyCliDashboard.Views;

public sealed partial class ChatSidebarView : UserControl
{
    private string? _pendingDeleteConvId = null;
    private bool _isMultiSelectMode = false;

    public ObservableCollection<AgyChatSession> Chats
    {
        get { return (ObservableCollection<AgyChatSession>)GetValue(ChatsProperty); }
        set { SetValue(ChatsProperty, value); }
    }

    public static readonly DependencyProperty ChatsProperty =
        DependencyProperty.Register("Chats", typeof(ObservableCollection<AgyChatSession>), typeof(ChatSidebarView),
            new PropertyMetadata(null, OnChatsChanged));

    private static void OnChatsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatSidebarView view)
        {
            if (e.OldValue is ObservableCollection<AgyChatSession> oldList)
            {
                oldList.CollectionChanged -= view.Chats_CollectionChanged;
            }
            if (e.NewValue is ObservableCollection<AgyChatSession> newList)
            {
                newList.CollectionChanged += view.Chats_CollectionChanged;
            }
            view.UpdateEmptyState();
        }
    }

    private void Chats_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyState();
    }

    public void UpdateEmptyState()
    {
        if (PnlEmptyState == null) return;
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                PnlEmptyState.Visibility = (Chats == null || Chats.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
            });
        }
        else
        {
            PnlEmptyState.Visibility = (Chats == null || Chats.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // Events
    public event EventHandler<string>? OpenAgyChatRequested;
#pragma warning disable CS0067
    public event EventHandler<string>? OpenClawChatRequested;
#pragma warning restore CS0067
    public event EventHandler<string>? PinToggled;
    public event EventHandler<string>? SingleChatDeleted;
    public event EventHandler<List<string>>? MultiChatsDeleted;
    public event EventHandler<bool>? MultiSelectChanged;

    public ChatSidebarView()
    {
        this.InitializeComponent();
        UpdateEmptyState();
    }

    public void SetMultiSelectMode(bool enabled)
    {
        _isMultiSelectMode = enabled;
        PnlMultiSelectBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
        {
            PnlMultiDeleteConfirm.Visibility = Visibility.Collapsed;
            ListChats.Visibility = Visibility.Visible;
        }

        if (Chats != null)
        {
            foreach (var chat in Chats)
            {
                chat.IsSelectionModeActive = enabled;
                if (!enabled)
                {
                    chat.IsSelected = false;
                }
            }
        }
        UpdateDeleteSelectedButtonText();
        MultiSelectChanged?.Invoke(this, enabled);
    }

    private void UpdateDeleteSelectedButtonText()
    {
        if (Chats == null) return;
        int selectedCount = Chats.Count(c => c.IsSelected);
        TxtDeleteSelectedCount.Text = $"Löschen ({selectedCount})";
        BtnDeleteSelectedChats.IsEnabled = selectedCount > 0;
        BtnDeleteSelectedChats.Opacity = selectedCount > 0 ? 1.0 : 0.5;
    }

    // --- UI EVENT HANDLERS ---

    private void BtnSelectAllChats_Click(object sender, RoutedEventArgs e)
    {
        if (Chats == null) return;
        foreach (var chat in Chats) chat.IsSelected = true;
        UpdateDeleteSelectedButtonText();
    }

    private void BtnDeselectAllChats_Click(object sender, RoutedEventArgs e)
    {
        if (Chats == null) return;
        foreach (var chat in Chats) chat.IsSelected = false;
        UpdateDeleteSelectedButtonText();
    }

    private void BtnDeleteSelectedChats_Click(object sender, RoutedEventArgs e)
    {
        if (Chats == null) return;
        var selectedChats = Chats.Where(c => c.IsSelected).ToList();
        if (selectedChats.Count == 0) return;

        TxtMultiDeleteConfirmDetail.Text = $"Möchtest du {selectedChats.Count} ausgewählte Chats wirklich dauerhaft löschen?";
        ListChats.Visibility = Visibility.Collapsed;
        PnlMultiDeleteConfirm.Visibility = Visibility.Visible;
    }

    private void BtnCancelMultiSelect_Click(object sender, RoutedEventArgs e)
    {
        SetMultiSelectMode(false);
    }

    private void BtnCancelMultiDeleteConfirm_Click(object sender, RoutedEventArgs e)
    {
        PnlMultiDeleteConfirm.Visibility = Visibility.Collapsed;
        ListChats.Visibility = Visibility.Visible;
    }

    private void BtnConfirmMultiDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Chats == null) return;
        var selectedChats = Chats.Where(c => c.IsSelected).Select(c => c.Id).ToList();

        PnlMultiDeleteConfirm.Visibility = Visibility.Collapsed;
        ListChats.Visibility = Visibility.Visible;

        if (selectedChats.Count > 0)
        {
            MultiChatsDeleted?.Invoke(this, selectedChats);
            SetMultiSelectMode(false);
        }
    }

    private void ChatCheckBox_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void ChatCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        UpdateDeleteSelectedButtonText();
    }

    private void ChatItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement elem)
        {
            var actionPanel = elem.FindName("ActionPanel") as UIElement;
            if (actionPanel != null) actionPanel.Opacity = 1;
            if (elem.DataContext is AgyChatSession session) session.IsHovered = true;
        }
    }

    private void ChatItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement elem)
        {
            var actionPanel = elem.FindName("ActionPanel") as UIElement;
            if (actionPanel != null) actionPanel.Opacity = 0;
            if (elem.DataContext is AgyChatSession session)
            {
                session.IsHovered = false;
            }
        }
    }

    private void ChatItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindParent<Button>(dep) != null) return;

        if (sender is FrameworkElement elem && elem.DataContext is AgyChatSession session)
        {
            if (_isMultiSelectMode)
            {
                session.IsSelected = !session.IsSelected;
                UpdateDeleteSelectedButtonText();
                return;
            }
            OpenAgyChatRequested?.Invoke(this, session.Id);
        }
    }

    private void BtnTogglePin_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void BtnTogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is string convId)
        {
            PinToggled?.Invoke(this, convId);
        }
    }

    private void BtnDeleteSingleChat_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void BtnDeleteSingleChat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is string convId && Chats != null)
        {
            var chatItem = Chats.FirstOrDefault(c => c.Id == convId);
            if (chatItem != null)
            {
                _pendingDeleteConvId = convId;
                TxtSingleDeleteConfirmTitle.Text = $"'{chatItem.Title}'";
                ListChats.Visibility = Visibility.Collapsed;
                PnlSingleDeleteConfirm.Visibility = Visibility.Visible;
            }
        }
    }

    private void BtnCancelSingleDelete_Click(object sender, RoutedEventArgs e)
    {
        _pendingDeleteConvId = null;
        PnlSingleDeleteConfirm.Visibility = Visibility.Collapsed;
        ListChats.Visibility = Visibility.Visible;
    }

    private void BtnConfirmSingleDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_pendingDeleteConvId))
        {
            string targetId = _pendingDeleteConvId;
            _pendingDeleteConvId = null;

            PnlSingleDeleteConfirm.Visibility = Visibility.Collapsed;
            ListChats.Visibility = Visibility.Visible;

            SingleChatDeleted?.Invoke(this, targetId);
        }
    }

    public void ApplyLocalization()
    {
        if (TxtChatsHeader != null)
        {
            TxtChatsHeader.Text = Helpers.Loc.Tr("Sidebars.Chats.Header", "CHATS & SESSIONS");
        }
        if (TxtSelectAllLabel != null)
        {
            TxtSelectAllLabel.Text = Helpers.Loc.Tr("Sidebars.Chats.SelectAll", "Alle");
        }
        if (BtnSelectAllChats != null)
        {
            ToolTipService.SetToolTip(BtnSelectAllChats, Helpers.Loc.Tr("Sidebars.Chats.SelectAllTooltip", "Alle angezeigten Chats markieren"));
        }
        if (TxtSelectNoneLabel != null)
        {
            TxtSelectNoneLabel.Text = Helpers.Loc.Tr("Sidebars.Chats.SelectNone", "Keine");
        }
        if (BtnDeselectAllChats != null)
        {
            ToolTipService.SetToolTip(BtnDeselectAllChats, Helpers.Loc.Tr("Sidebars.Chats.SelectNoneTooltip", "Markierung aufheben"));
        }
        if (BtnDeleteSelectedChats != null)
        {
            ToolTipService.SetToolTip(BtnDeleteSelectedChats, Helpers.Loc.Tr("Sidebars.Chats.DeleteBatchTooltip", "Ausgewählte Chats löschen"));
        }
        if (BtnCancelMultiSelect != null)
        {
            ToolTipService.SetToolTip(BtnCancelMultiSelect, Helpers.Loc.Tr("Sidebars.Chats.CancelSelectTooltip", "Auswahlmodus beenden (Esc)"));
        }
        if (TxtSingleDeleteConfirmTitleHeader != null)
        {
            TxtSingleDeleteConfirmTitleHeader.Text = Helpers.Loc.Tr("Sidebars.Chats.SingleDeleteTitle", "Chat unwiderruflich löschen?");
        }
        if (BtnCancelSingleDelete != null)
        {
            BtnCancelSingleDelete.Content = Helpers.Loc.Tr("Sidebars.Chats.SingleDeleteCancel", "Abbrechen");
            ToolTipService.SetToolTip(BtnCancelSingleDelete, Helpers.Loc.Tr("Sidebars.Chats.SingleDeleteCancelTooltip", "Abbrechen (Esc)"));
        }
        if (BtnConfirmSingleDelete != null)
        {
            BtnConfirmSingleDelete.Content = Helpers.Loc.Tr("Sidebars.Chats.SingleDeleteConfirm", "Löschen");
            ToolTipService.SetToolTip(BtnConfirmSingleDelete, Helpers.Loc.Tr("Sidebars.Chats.SingleDeleteConfirmTooltip", "Endgültig löschen"));
        }
        if (TxtMultiDeleteConfirmTitle != null)
        {
            TxtMultiDeleteConfirmTitle.Text = Helpers.Loc.Tr("Sidebars.Chats.MultiDeleteTitle", "Ausgewählte Chats löschen?");
        }
        if (TxtMultiDeleteConfirmDetail != null)
        {
            TxtMultiDeleteConfirmDetail.Text = Helpers.Loc.Tr("Sidebars.Chats.MultiDeleteConfirmDetail", "Möchtest du die ausgewählten Chats wirklich dauerhaft löschen?");
        }
        if (BtnCancelMultiDeleteConfirm != null)
        {
            BtnCancelMultiDeleteConfirm.Content = Helpers.Loc.Tr("Sidebars.Chats.SingleDeleteCancel", "Abbrechen");
        }
        if (BtnConfirmMultiDelete != null)
        {
            BtnConfirmMultiDelete.Content = Helpers.Loc.Tr("Sidebars.Chats.SingleDeleteConfirm", "Löschen");
        }
        if (TxtEmptyChatsTitle != null)
        {
            TxtEmptyChatsTitle.Text = Helpers.Loc.Tr("Sidebars.Chats.EmptyChats", "Noch keine Chats vorhanden");
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
}

