using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace AgyCliDashboard.Views;

public sealed partial class HeaderView : UserControl
{
    public event RoutedEventHandler? ToggleSidebarClick;
    public event RoutedEventHandler? ToggleCoBrowserClick;
    public event RightTappedEventHandler? ToggleCoBrowserRightTapped;
    public event RoutedEventHandler? CheckUpdateClick;
    public event RightTappedEventHandler? CheckUpdateRightTapped;
    public event RoutedEventHandler? CloseHeaderNoticeClick;
    public event RoutedEventHandler? GeminiModelStatusClick;
    public event RightTappedEventHandler? GeminiModelStatusRightTapped;

    public HeaderView()
    {
        InitializeComponent();
    }

    private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        ToggleSidebarClick?.Invoke(sender, e);
    }

    private void BtnCoBrowserToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleCoBrowserClick?.Invoke(sender, e);
    }

    private void BtnCoBrowserToggle_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ToggleCoBrowserRightTapped?.Invoke(sender, e);
    }

    private void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateClick?.Invoke(sender, e);
    }

    private void BtnCheckUpdate_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        CheckUpdateRightTapped?.Invoke(sender, e);
    }

    private void BtnCloseHeaderNotice_Click(object sender, RoutedEventArgs e)
    {
        CloseHeaderNoticeClick?.Invoke(sender, e);
    }

    private void BtnGeminiModelStatus_Click(object sender, RoutedEventArgs e)
    {
        GeminiModelStatusClick?.Invoke(sender, e);
    }

    private void BtnGeminiModelStatus_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        GeminiModelStatusRightTapped?.Invoke(sender, e);
    }
}
