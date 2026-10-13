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

    public void ApplyLocalization()
    {
        if (TxtModelPickerTitle != null)
        {
            TxtModelPickerTitle.Text = Helpers.Loc.Tr("ModelPicker.Title", "OpenRouter Modell");
        }
        if (TxtModelSearch != null)
        {
            TxtModelSearch.PlaceholderText = Helpers.Loc.Tr("ModelPicker.SearchPlaceholder", "Modell suchen...");
        }
        if (BtnModelPickerBack != null)
        {
            ToolTipService.SetToolTip(BtnModelPickerBack, Helpers.Loc.Tr("ModelPicker.BackTooltip", "Zurück zur Sidebar"));
        }
        if (BtnRefreshModels != null)
        {
            ToolTipService.SetToolTip(BtnRefreshModels, Helpers.Loc.Tr("ModelPicker.RefreshTooltip", "Modelle von OpenRouter neu laden"));
        }
        if (CbiSortName != null) CbiSortName.Content = Helpers.Loc.Tr("ModelPicker.SortName", "Name (A → Z)");
        if (CbiSortPriceAsc != null) CbiSortPriceAsc.Content = Helpers.Loc.Tr("ModelPicker.SortPriceAsc", "Günstigste ($ ↑)");
        if (CbiSortPriceDesc != null) CbiSortPriceDesc.Content = Helpers.Loc.Tr("ModelPicker.SortPriceDesc", "Teuerste ($ ↓)");
        if (CbiSortCtxDesc != null) CbiSortCtxDesc.Content = Helpers.Loc.Tr("ModelPicker.SortCtxDesc", "Max. Kontext (Groß → Klein)");
        if (BtnTogglePriceScore != null)
        {
            ToolTipService.SetToolTip(BtnTogglePriceScore, Helpers.Loc.Tr("ModelPicker.ScoreTooltip", "Kostenanzeige umschalten: Relativer Score (0-100) vs. Exakter Preis ($ pro 1 Mio. Tokens)"));
        }
    }
}
