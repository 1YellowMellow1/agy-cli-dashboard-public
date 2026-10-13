using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;

namespace AgyCliDashboard;

/// <summary>
/// Verstellbare Sidebar-Breite (Splitter am linken Sidebar-Rand). Breite wird in ui_states.json persistiert.
/// Analog zum Co-Browser-Splitter: Pointer-Capture statt GridSplitter (in WinUI 3 nicht verfügbar).
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double SidebarDefaultWidth = 350;
    private const double SidebarMinWidth = 300;
    private const double SidebarMaxWidth = 720;
    /// <summary>Mindestbreite, die für Terminal/Workspace neben der Sidebar übrig bleiben muss.</summary>
    private const double WorkspaceMinWidth = 480;

    private bool _isSidebarSplitterDragging = false;
    private double _sidebarSplitterStartX = 0;
    private double _sidebarSplitterStartWidth = 0;

    /// <summary>Aktuell gewünschte Sidebar-Breite in px (persistiert, auf gültigen Bereich begrenzt).</summary>
    private double SidebarWidthPx
    {
        get
        {
            double w = _uiStates.SidebarWidth;
            if (double.IsNaN(w) || double.IsInfinity(w) || w <= 0) w = SidebarDefaultWidth;
            return Math.Clamp(w, SidebarMinWidth, SidebarMaxWidth);
        }
    }

    /// <summary>
    /// Setzt Spaltenbreite und Splitter-Sichtbarkeit passend zu <see cref="_isSidebarVisible"/>.
    /// Einzige Stelle, die <c>ColSidebar.Width</c> setzt (Toggle, Layout-Restore, System-Check).
    /// </summary>
    private void ApplySidebarWidth()
    {
        if (ColSidebar == null) return;
        ColSidebar.Width = _isSidebarVisible ? new GridLength(SidebarWidthPx) : new GridLength(0);
        if (SplitterSidebar != null)
        {
            SplitterSidebar.Visibility = _isSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void SplitterSidebar_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement border && this.Content != null && ColSidebar != null)
        {
            var pt = e.GetCurrentPoint(this.Content);
            if (!pt.Properties.IsLeftButtonPressed) return;

            if (border.CapturePointer(e.Pointer))
            {
                _isSidebarSplitterDragging = true;
                _sidebarSplitterStartX = pt.Position.X;
                _sidebarSplitterStartWidth = ColSidebar.ActualWidth;
            }
            e.Handled = true;
        }
    }

    private void SplitterSidebar_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSidebarSplitterDragging || this.Content == null || ColSidebar == null) return;

        double currentX = e.GetCurrentPoint(this.Content).Position.X;
        double delta = currentX - _sidebarSplitterStartX;
        // Maus nach links = Sidebar wird breiter. Obergrenze schützt die Mindestbreite des Workspace.
        double maxWidth = Math.Max(SidebarMinWidth, Math.Min(SidebarMaxWidth, this.Content.ActualSize.X - WorkspaceMinWidth));
        double newWidth = Math.Clamp(_sidebarSplitterStartWidth - delta, SidebarMinWidth, maxWidth);

        _uiStates.SidebarWidth = Math.Round(newWidth);
        ColSidebar.Width = new GridLength(newWidth, GridUnitType.Pixel);
        e.Handled = true;
    }

    private void SplitterSidebar_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isSidebarSplitterDragging && sender is UIElement border)
        {
            border.ReleasePointerCapture(e.Pointer);
        }
        EndSidebarSplitterDrag();
        e.Handled = true;
    }

    private void SplitterSidebar_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Auch bei verlorenem Capture (Fokuswechsel, Alt-Tab) sauber beenden und speichern.
        EndSidebarSplitterDrag();
    }

    private void EndSidebarSplitterDrag()
    {
        if (!_isSidebarSplitterDragging) return;
        _isSidebarSplitterDragging = false;
        if (BdrSplitterSidebarIndicator != null)
        {
            BdrSplitterSidebarIndicator.Background = NavTransparentBrush;
        }
        SaveUiStates();
    }

    private void SplitterSidebar_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (BdrSplitterSidebarIndicator != null)
        {
            BdrSplitterSidebarIndicator.Background = Application.Current.Resources.TryGetValue("AgyAccent", out var accent) && accent is Brush accentBrush
                ? accentBrush
                : NavTransparentBrush;
        }
    }

    private void SplitterSidebar_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isSidebarSplitterDragging && BdrSplitterSidebarIndicator != null)
        {
            BdrSplitterSidebarIndicator.Background = NavTransparentBrush;
        }
    }

    private void SplitterSidebar_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        _uiStates.SidebarWidth = SidebarDefaultWidth;
        ApplySidebarWidth();
        SaveUiStates();
        e.Handled = true;
    }
}
