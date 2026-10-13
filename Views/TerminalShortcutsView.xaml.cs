using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgyCliDashboard.Views;

public sealed partial class TerminalShortcutsView : UserControl
{
    public event RoutedEventHandler? CloseClick;
    public event RoutedEventHandler? AskShellShortcutsClick;

    public TerminalShortcutsView()
    {
        this.InitializeComponent();
        this.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CloseClick?.Invoke(this, new RoutedEventArgs());
            }
        };
    }

    private void BtnCloseShortcuts_Click(object sender, RoutedEventArgs e)
    {
        CloseClick?.Invoke(sender, e);
    }

    private void BtnAskShellShortcuts_Click(object sender, RoutedEventArgs e)
    {
        AskShellShortcutsClick?.Invoke(sender, e);
    }

    private void TxtShortcutSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string query = TxtShortcutSearch.Text.Trim().ToLowerInvariant();
        bool isSearching = !string.IsNullOrWhiteSpace(query);

        FilterVisibility(CardCategoryControl, query, isSearching);
        FilterVisibility(CardCategoryNavigation, query, isSearching);
        FilterVisibility(CardCategorySearch, query, isSearching);
    }

    private void FilterVisibility(Border cardBorder, string query, bool isSearching)
    {
        if (cardBorder == null) return;
        if (!isSearching)
        {
            cardBorder.Visibility = Visibility.Visible;
            if (cardBorder.Child is StackPanel resetStack)
            {
                foreach (var element in resetStack.Children)
                {
                    if (element is Border itemBorder)
                    {
                        itemBorder.Visibility = Visibility.Visible;
                    }
                }
            }
            return;
        }

        if (cardBorder.Child is StackPanel mainStack)
        {
            bool anyMatchInCard = false;
            foreach (var element in mainStack.Children)
            {
                if (element is Border itemBorder)
                {
                    if (itemBorder.Child is Grid grid)
                    {
                        string textContent = "";
                        foreach (var col in grid.Children)
                        {
                            if (col is Border keyBorder && keyBorder.Child is TextBlock keyTb)
                            {
                                textContent += " " + keyTb.Text;
                            }
                            else if (col is TextBlock descTb)
                            {
                                textContent += " " + descTb.Text;
                            }
                        }

                        bool match = textContent.ToLowerInvariant().Contains(query);
                        itemBorder.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                        if (match) anyMatchInCard = true;
                    }
                }
            }

            cardBorder.Visibility = anyMatchInCard ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
