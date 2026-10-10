using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AgyCliDashboard.Views;

public sealed partial class ModelPickerView : UserControl
{
    public ObservableCollection<ModelPickerListItemViewModel> VisibleModels { get; } = new();

    public event RoutedEventHandler? ModelPickerBackClick;
    public event RoutedEventHandler? RefreshModelsClick;
    public event TextChangedEventHandler? ModelSearchTextChanged;
    public event SelectionChangedEventHandler? ModelProviderSelectionChanged;
    public event SelectionChangedEventHandler? ModelSortSelectionChanged;
    public event RoutedEventHandler? TogglePriceScoreClick;
    public event EventHandler<ModelPickerRowViewModel>? ModelItemClick;
    public event EventHandler<string>? FavoriteToggleClick;

    public ModelPickerView()
    {
        InitializeComponent();
    }

    private void BtnModelPickerBack_Click(object sender, RoutedEventArgs e)
    {
        ModelPickerBackClick?.Invoke(sender, e);
    }

    private void BtnRefreshModels_Click(object sender, RoutedEventArgs e)
    {
        RefreshModelsClick?.Invoke(sender, e);
    }

    private void TxtModelSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ModelSearchTextChanged?.Invoke(sender, e);
    }

    private void CboModelProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ModelProviderSelectionChanged?.Invoke(sender, e);
    }

    private void CboModelSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ModelSortSelectionChanged?.Invoke(sender, e);
    }

    private void BtnTogglePriceScore_Click(object sender, RoutedEventArgs e)
    {
        TogglePriceScoreClick?.Invoke(sender, e);
    }

    private void ListModels_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ModelPickerRowViewModel rowVm)
        {
            ModelItemClick?.Invoke(this, rowVm);
        }
    }

    private void BtnStar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelId)
        {
            FavoriteToggleClick?.Invoke(this, modelId);
        }
    }

    private void BtnStar_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }
}
