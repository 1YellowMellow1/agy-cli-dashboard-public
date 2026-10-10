using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AgyCliDashboard;

public abstract class ModelPickerListItemViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class ModelPickerHeaderViewModel : ModelPickerListItemViewModel
{
    public string Title { get; set; } = string.Empty;
    public bool ShowTopDivider { get; set; }
    public Visibility DividerVisibility => ShowTopDivider ? Visibility.Visible : Visibility.Collapsed;
    public SolidColorBrush HeaderBrush { get; set; } = new(Color.FromArgb(255, 148, 163, 184));
}

public class ModelPickerRowViewModel : ModelPickerListItemViewModel
{
    private bool _isFavorite;
    private bool _isActive;
    private string _priceDisplay = string.Empty;

    public OpenRouterModelItem Model { get; }

    public ModelPickerRowViewModel(OpenRouterModelItem model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model;
        _isFavorite = model.IsFavorite;
    }

    public string Id => Model?.Id ?? string.Empty;
    public string DisplayName => Model?.DisplayName ?? string.Empty;
    public string ContextFormatted => Model?.ContextFormatted ?? string.Empty;

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite != value)
            {
                _isFavorite = value;
                Model.IsFavorite = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FavoriteGlyph));
                OnPropertyChanged(nameof(FavoriteBrush));
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveRowBgBrush));
                OnPropertyChanged(nameof(ActiveRowBorderBrush));
                OnPropertyChanged(nameof(ActiveBadgeVisibility));
            }
        }
    }

    public string PriceDisplay
    {
        get => _priceDisplay;
        set
        {
            if (_priceDisplay != value)
            {
                _priceDisplay = value;
                OnPropertyChanged();
            }
        }
    }

    private static readonly SolidColorBrush FavAmberBrush = new(Color.FromArgb(255, 234, 179, 8));
    private static readonly SolidColorBrush FavSlate400Brush = new(Color.FromArgb(255, 148, 163, 184));
    private static readonly SolidColorBrush ActiveBgBrush = new(Color.FromArgb(255, 30, 41, 59));
    private static readonly SolidColorBrush InactiveBgBrush = new(Color.FromArgb(255, 15, 23, 42));
    private static readonly SolidColorBrush ActiveBorderBrush = new(Color.FromArgb(255, 59, 130, 246));
    private static readonly SolidColorBrush InactiveBorderBrush = new(Color.FromArgb(255, 30, 41, 59));

    public string FavoriteGlyph => _isFavorite ? "\uE735" : "\uE734";
    public SolidColorBrush FavoriteBrush => _isFavorite ? FavAmberBrush : FavSlate400Brush;
    public SolidColorBrush ActiveRowBgBrush => _isActive ? ActiveBgBrush : InactiveBgBrush;
    public SolidColorBrush ActiveRowBorderBrush => _isActive ? ActiveBorderBrush : InactiveBorderBrush;
    public Visibility ActiveBadgeVisibility => _isActive ? Visibility.Visible : Visibility.Collapsed;
}
