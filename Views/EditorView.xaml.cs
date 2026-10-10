using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace AgyCliDashboard.Views;

public sealed partial class EditorView : UserControl
{
    public event RoutedEventHandler? CopyEditorTextClick;
    public event RoutedEventHandler? SaveEditorFileClick;
    public event RoutedEventHandler? CloseEditorFileClick;
    public event RoutedEventHandler? PrintEditorTextClick;
    public event RoutedEventHandler? SavePdfEditorTextClick;
    public event RoutedEventHandler? StripMarkdownEditorTextClick;
    public event RoutedEventHandler? OpenExternalAppClick;
    public event KeyEventHandler? FileEditorKeyDown;

    private bool _isA4View = true;
    private DispatcherTimer? _autoScrollTimer;
    private DispatcherTimer? _copyFeedbackTimer;
    private Point _currentViewerPointer;
    private int _lastSelectionAnchor = -1;
    private bool _isPointerDragging = false;
    private bool _isGutterDragging = false;
    private long _lastClickTime = 0;
    private Point _lastClickPoint;
    private int _clickCount = 0;

    public EditorView()
    {
        InitializeComponent();
        InitializeSelectionAndAutoScroll();
    }

    public RichEditBox TxtFileEditor => RichFileEditor;

    public string Text
    {
        get => GetText();
        set => SetText(value);
    }

    public string SelectedText
    {
        get => GetSelectedText();
        set => SetSelectedText(value);
    }

    public string GetText()
    {
        try
        {
            RichFileEditor.Document.GetText(TextGetOptions.UseCrlf, out string text);
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine).TrimEnd('\r', '\n');
        }
        catch
        {
            return string.Empty;
        }
    }

    public void SetText(string text)
    {
        try
        {
            RichFileEditor.Document.SetText(TextSetOptions.None, text ?? string.Empty);
            ApplyDefaultTextColor();
            if (!string.IsNullOrEmpty(text))
            {
                RichFileEditor.Document.GetText(TextGetOptions.None, out string all);
                var range = RichFileEditor.Document.GetRange(0, all?.Length ?? 0);
                range.CharacterFormat.ForegroundColor = _isDarkSheet
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 226, 232, 240)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 15, 23, 42);
            }
        }
        catch { }
    }

    public string GetSelectedText()
    {
        try
        {
            RichFileEditor.Document.Selection.GetText(TextGetOptions.UseCrlf, out string sel);
            if (string.IsNullOrEmpty(sel)) return string.Empty;
            return sel.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void SetSelectedText(string text)
    {
        try
        {
            RichFileEditor.Document.Selection.SetText(TextSetOptions.None, text ?? string.Empty);
        }
        catch { }
    }

    public string GetRtf()
    {
        try
        {
            RichFileEditor.Document.GetText(TextGetOptions.FormatRtf, out string rtf);
            return rtf ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void SetRtf(string rtf)
    {
        try
        {
            RichFileEditor.Document.SetText(TextSetOptions.FormatRtf, rtf ?? string.Empty);
        }
        catch { }
    }

    // Format-Methoden
    public void ToggleBold()
    {
        try { RichFileEditor.Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle; } catch { }
    }

    public void ToggleItalic()
    {
        try { RichFileEditor.Document.Selection.CharacterFormat.Italic = FormatEffect.Toggle; } catch { }
    }

    public void ToggleUnderline()
    {
        try
        {
            var cf = RichFileEditor.Document.Selection.CharacterFormat;
            cf.Underline = (cf.Underline == UnderlineType.None) ? UnderlineType.Single : UnderlineType.None;
        }
        catch { }
    }

    public void ToggleStrikethrough()
    {
        try { RichFileEditor.Document.Selection.CharacterFormat.Strikethrough = FormatEffect.Toggle; } catch { }
    }

    public void ToggleBulletList()
    {
        try
        {
            var pf = RichFileEditor.Document.Selection.ParagraphFormat;
            pf.ListType = (pf.ListType == MarkerType.Bullet) ? MarkerType.None : MarkerType.Bullet;
        }
        catch { }
    }

    public void ToggleNumberedList()
    {
        try
        {
            var pf = RichFileEditor.Document.Selection.ParagraphFormat;
            pf.ListType = (pf.ListType == MarkerType.Arabic) ? MarkerType.None : MarkerType.Arabic;
        }
        catch { }
    }

    public void AlignLeft()
    {
        try { RichFileEditor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Left; } catch { }
    }

    public void AlignCenter()
    {
        try { RichFileEditor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Center; } catch { }
    }

    public void AlignRight()
    {
        try { RichFileEditor.Document.Selection.ParagraphFormat.Alignment = ParagraphAlignment.Right; } catch { }
    }

    // Format-Buttons Clicks
    private void BtnBold_Click(object sender, RoutedEventArgs e) => ToggleBold();
    private void BtnItalic_Click(object sender, RoutedEventArgs e) => ToggleItalic();
    private void BtnUnderline_Click(object sender, RoutedEventArgs e) => ToggleUnderline();
    private void BtnStrikethrough_Click(object sender, RoutedEventArgs e) => ToggleStrikethrough();
    private void BtnBulletList_Click(object sender, RoutedEventArgs e) => ToggleBulletList();
    private void BtnNumberedList_Click(object sender, RoutedEventArgs e) => ToggleNumberedList();
    private void BtnAlignLeft_Click(object sender, RoutedEventArgs e) => AlignLeft();
    private void BtnAlignCenter_Click(object sender, RoutedEventArgs e) => AlignCenter();
    private void BtnAlignRight_Click(object sender, RoutedEventArgs e) => AlignRight();

    private void CmbFontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbFontFamily?.SelectedItem is ComboBoxItem item && item.Content is string fontName && RichFileEditor != null)
        {
            try { RichFileEditor.Document.Selection.CharacterFormat.Name = fontName; } catch { }
        }
    }

    private void CmbFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbFontSize?.SelectedItem is ComboBoxItem item && float.TryParse(item.Content?.ToString(), out float pt) && RichFileEditor != null)
        {
            try { RichFileEditor.Document.Selection.CharacterFormat.Size = pt; } catch { }
        }
    }

    private bool _isDarkSheet = false;

    public bool IsDocumentMode => _isA4View;

    public void SetEditorMode(bool isDocumentMode)
    {
        _isA4View = isDocumentMode;
        if (BtnStripMarkdownEditor != null)
        {
            BtnStripMarkdownEditor.Visibility = isDocumentMode ? Visibility.Visible : Visibility.Collapsed;
        }
        if (MenuStripMarkdown != null)
        {
            MenuStripMarkdown.Visibility = isDocumentMode ? Visibility.Visible : Visibility.Collapsed;
        }
        if (PnlWordPadToolbar != null)
        {
            PnlWordPadToolbar.Visibility = isDocumentMode ? Visibility.Visible : Visibility.Collapsed;
        }
        if (DocPageSheet != null)
        {
            DocPageSheet.MaxWidth = double.PositiveInfinity;
            DocPageSheet.HorizontalAlignment = HorizontalAlignment.Stretch;
            DocPageSheet.Padding = isDocumentMode ? new Thickness(28, 24, 28, 24) : new Thickness(16, 12, 16, 12);
            DocPageSheet.Margin = new Thickness(0);
        }
        if (RichFileEditor != null)
        {
            RichFileEditor.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(isDocumentMode ? "Segoe UI" : "Consolas");
            RichFileEditor.FontSize = isDocumentMode ? 14 : 15;
        }

        if (isDocumentMode)
        {
            _isDarkSheet = false; // Original WordPad: Weißes Papier
            ApplySheetTheme();
        }
        else
        {
            // Quellcode-Modus: Im gewohnten dunklen IDE-Design
            _isDarkSheet = true;
            ApplySheetTheme();
        }
    }

    private void BtnToggleSheetTheme_Click(object sender, RoutedEventArgs e)
    {
        _isDarkSheet = !_isDarkSheet;
        ApplySheetTheme();
    }

    public void ApplySheetTheme()
    {
        if (DocPageSheet == null || RichFileEditor == null) return;

        if (_isDarkSheet)
        {
            DocPageSheet.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgySurfaceBg"];
            DocPageSheet.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgyBorderDefault"];
            var lightText = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgyTextPrimary"];
            RichFileEditor.Foreground = lightText;
            RichFileEditor.Resources["TextControlForeground"] = lightText;
            RichFileEditor.Resources["TextControlForegroundPointerOver"] = lightText;
            RichFileEditor.Resources["TextControlForegroundFocused"] = lightText;
            if (IconSheetTheme != null) IconSheetTheme.Glyph = "\uE706"; // Sonne (Klick für Weiß)
        }
        else
        {
            DocPageSheet.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
            DocPageSheet.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 203, 213, 225));
            var darkText = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 15, 23, 42));
            RichFileEditor.Foreground = darkText;
            RichFileEditor.Resources["TextControlForeground"] = darkText;
            RichFileEditor.Resources["TextControlForegroundPointerOver"] = darkText;
            RichFileEditor.Resources["TextControlForegroundFocused"] = darkText;
            if (IconSheetTheme != null) IconSheetTheme.Glyph = "\uE708"; // Mond (Klick für Dunkel)
        }
        ApplyDefaultTextColor();

        try
        {
            RichFileEditor.Document.GetText(TextGetOptions.None, out string all);
            if (!string.IsNullOrEmpty(all))
            {
                var range = RichFileEditor.Document.GetRange(0, all.Length);
                range.CharacterFormat.ForegroundColor = _isDarkSheet
                    ? Microsoft.UI.ColorHelper.FromArgb(255, 226, 232, 240)
                    : Microsoft.UI.ColorHelper.FromArgb(255, 15, 23, 42);
            }
        }
        catch { }
    }

    private void ApplyDefaultTextColor()
    {
        try
        {
            if (RichFileEditor == null) return;
            var color = _isDarkSheet
                ? Microsoft.UI.ColorHelper.FromArgb(255, 226, 232, 240)
                : Microsoft.UI.ColorHelper.FromArgb(255, 15, 23, 42);

            var def = RichFileEditor.Document.GetDefaultCharacterFormat();
            def.ForegroundColor = color;
            RichFileEditor.Document.SetDefaultCharacterFormat(def);
        }
        catch { }
    }

    // Header Toolbar Buttons
    private void BtnOpenExternalWordPad_Click(object sender, RoutedEventArgs e) => OpenExternalAppClick?.Invoke(sender, e);
    private void BtnCopyEditorText_Click(object sender, RoutedEventArgs e)
    {
        CopySelectionOrAll(plainTextOnly: false);
        CopyEditorTextClick?.Invoke(sender, e);
    }
    private void BtnSaveEditorFile_Click(object sender, RoutedEventArgs e) => SaveEditorFileClick?.Invoke(sender, e);
    private void BtnCloseEditorFile_Click(object sender, RoutedEventArgs e) => CloseEditorFileClick?.Invoke(sender, e);
    private void BtnPrintEditorText_Click(object sender, RoutedEventArgs e) => PrintEditorTextClick?.Invoke(sender, e);
    private void BtnSavePdfEditorText_Click(object sender, RoutedEventArgs e) => SavePdfEditorTextClick?.Invoke(sender, e);
    private void BtnStripMarkdownEditor_Click(object sender, RoutedEventArgs e) => StripMarkdownEditorTextClick?.Invoke(sender, e);

    // Kontextmenü Clicks
    private void MenuStripMarkdown_Click(object sender, RoutedEventArgs e) => StripMarkdownEditorTextClick?.Invoke(sender, e);
    private void MenuPrint_Click(object sender, RoutedEventArgs e) => PrintEditorTextClick?.Invoke(sender, e);
    private void MenuSavePdf_Click(object sender, RoutedEventArgs e) => SavePdfEditorTextClick?.Invoke(sender, e);
    private void MenuOpenExternal_Click(object sender, RoutedEventArgs e) => OpenExternalAppClick?.Invoke(sender, e);
    private void MenuBold_Click(object sender, RoutedEventArgs e) => ToggleBold();
    private void MenuItalic_Click(object sender, RoutedEventArgs e) => ToggleItalic();
    private void MenuUnderline_Click(object sender, RoutedEventArgs e) => ToggleUnderline();
    private void MenuBulletList_Click(object sender, RoutedEventArgs e) => ToggleBulletList();

    private void MenuCut_Click(object sender, RoutedEventArgs e)
    {
        try { RichFileEditor?.Document.Selection.Cut(); } catch { }
    }

    private void MenuCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RichFileEditor != null)
            {
                RichFileEditor.Document.Selection.Copy();
                ShowCopyFeedback();
            }
        }
        catch { }
    }

    private void MenuCopyPlain_Click(object sender, RoutedEventArgs e)
    {
        CopyAsPlainText();
    }

    private void MenuSelectBlock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RichFileEditor != null)
            {
                ExpandToSmartBlock(RichFileEditor.Document.Selection);
            }
        }
        catch { }
    }

    private void MenuPaste_Click(object sender, RoutedEventArgs e)
    {
        try { RichFileEditor?.Document.Selection.Paste(0); } catch { }
    }

    private void MenuSelectAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RichFileEditor != null)
            {
                RichFileEditor.Document.GetText(TextGetOptions.None, out string all);
                RichFileEditor.Document.Selection.SetRange(0, all?.Length ?? 0);
            }
        }
        catch { }
    }

    private void RichFileEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var altState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
        bool isAltDown = (altState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

        var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        bool isCtrlDown = (ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftDown = (shiftState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

        // Alt + Up: Stufenweise Markierung erweitern (Wort -> Zeile -> Block -> Dokument)
        if (isAltDown && e.Key == VirtualKey.Up)
        {
            e.Handled = true;
            ExpandSelectionStep();
            return;
        }

        // Ctrl + Shift + C: Als reinen Text kopieren
        if (isCtrlDown && isShiftDown && e.Key == VirtualKey.C)
        {
            e.Handled = true;
            CopyAsPlainText();
            return;
        }

        FileEditorKeyDown?.Invoke(sender, e);
    }

    private void InitializeSelectionAndAutoScroll()
    {
        try { RichFileEditor.SelectionFlyout = null; } catch { }
        RichFileEditor.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RichFileEditor_PointerPressed), handledEventsToo: true);
        RichFileEditor.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(RichFileEditor_PointerMoved), handledEventsToo: true);
        RichFileEditor.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(RichFileEditor_PointerReleased), handledEventsToo: true);
        RichFileEditor.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(RichFileEditor_PointerCaptureLost), handledEventsToo: true);

        DocPageSheet.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(DocPageSheet_PointerPressed), handledEventsToo: true);
        DocPageSheet.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(DocPageSheet_PointerMoved), handledEventsToo: true);
        DocPageSheet.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(DocPageSheet_PointerReleased), handledEventsToo: true);
    }

    private void RichFileEditor_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var ptr = e.GetCurrentPoint(RichFileEditor);
        if (!ptr.Properties.IsLeftButtonPressed) return;

        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        bool isShiftDown = (shiftState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;

        // Shift + Click: Schnellauswahl vom vorherigen Anker bis zum Klickpunkt
        if (isShiftDown && _lastSelectionAnchor >= 0)
        {
            try
            {
                var targetRange = RichFileEditor.Document.GetRangeFromPoint(ptr.Position, PointOptions.ClientCoordinates | PointOptions.AllowOffClient);
                int targetPos = targetRange.StartPosition;
                int start = Math.Min(_lastSelectionAnchor, targetPos);
                int end = Math.Max(_lastSelectionAnchor, targetPos);
                RichFileEditor.Document.Selection.SetRange(start, end);
                _isPointerDragging = false;
                e.Handled = true;
                return;
            }
            catch { }
        }

        // Klick-Kaskade (1x Cursor, 2x Wort, 3x Zeile, 4x Block/Absatz)
        long now = Environment.TickCount64;
        var viewerPt = e.GetCurrentPoint(EditorScrollViewer).Position;
        double dist = Math.Sqrt(Math.Pow(viewerPt.X - _lastClickPoint.X, 2) + Math.Pow(viewerPt.Y - _lastClickPoint.Y, 2));

        if (now - _lastClickTime < 450 && dist < 14)
        {
            _clickCount++;
        }
        else
        {
            _clickCount = 1;
        }
        _lastClickTime = now;
        _lastClickPoint = viewerPt;

        if (_clickCount == 3)
        {
            // Dreifachklick: Ganze Zeile markieren
            try
            {
                var range = RichFileEditor.Document.GetRangeFromPoint(ptr.Position, PointOptions.ClientCoordinates | PointOptions.AllowOffClient);
                range.Expand(TextRangeUnit.Line);
                RichFileEditor.Document.Selection.SetRange(range.StartPosition, range.EndPosition);
                _isPointerDragging = false;
                e.Handled = true;
                return;
            }
            catch { }
        }
        else if (_clickCount >= 4)
        {
            // Vierfachklick: Zusammenhängenden Block/Absatz markieren
            try
            {
                var range = RichFileEditor.Document.GetRangeFromPoint(ptr.Position, PointOptions.ClientCoordinates | PointOptions.AllowOffClient);
                ExpandToSmartBlock(range);
                RichFileEditor.Document.Selection.SetRange(range.StartPosition, range.EndPosition);
                _isPointerDragging = false;
                e.Handled = true;
                return;
            }
            catch { }
        }

        // Einfacher Klick oder Start für Drag: Anker merken
        try
        {
            var range = RichFileEditor.Document.GetRangeFromPoint(ptr.Position, PointOptions.ClientCoordinates | PointOptions.AllowOffClient);
            _lastSelectionAnchor = range.StartPosition;
        }
        catch
        {
            _lastSelectionAnchor = RichFileEditor.Document.Selection.StartPosition;
        }

        try { RichFileEditor.CapturePointer(e.Pointer); } catch { }
        _isPointerDragging = true;
    }

    private void RichFileEditor_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerDragging || EditorScrollViewer == null) return;

        var ptr = e.GetCurrentPoint(RichFileEditor);
        if (!ptr.Properties.IsLeftButtonPressed)
        {
            StopAutoScroll();
            _isPointerDragging = false;
            try { RichFileEditor.ReleasePointerCapture(e.Pointer); } catch { }
            return;
        }

        _currentViewerPointer = e.GetCurrentPoint(EditorScrollViewer).Position;

        double edgeMargin = 55.0;
        double topEdge = edgeMargin;
        double bottomEdge = EditorScrollViewer.ActualHeight - edgeMargin;

        if (_currentViewerPointer.Y < topEdge || _currentViewerPointer.Y > bottomEdge)
        {
            StartAutoScroll();
        }
        else
        {
            StopAutoScroll();
        }
    }

    private void RichFileEditor_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isPointerDragging = false;
        _isGutterDragging = false;
        StopAutoScroll();
        try { RichFileEditor.ReleasePointerCapture(e.Pointer); } catch { }
    }

    private void RichFileEditor_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isPointerDragging = false;
        _isGutterDragging = false;
        StopAutoScroll();
    }

    private void DocPageSheet_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var sheetPt = e.GetCurrentPoint(DocPageSheet);
        if (!sheetPt.Properties.IsLeftButtonPressed) return;

        // Wenn Klick im linken Randbereich (Gutter <= Padding.Left)
        double gutterWidth = DocPageSheet.Padding.Left;
        if (sheetPt.Position.X <= gutterWidth)
        {
            try
            {
                var transform = DocPageSheet.TransformToVisual(RichFileEditor);
                var editorPt = transform.TransformPoint(sheetPt.Position);
                editorPt.X = 5;

                var range = RichFileEditor.Document.GetRangeFromPoint(editorPt, PointOptions.ClientCoordinates | PointOptions.AllowOffClient);

                long now = Environment.TickCount64;
                if (now - _lastClickTime < 450)
                {
                    ExpandToSmartBlock(range);
                }
                else
                {
                    range.Expand(TextRangeUnit.Line);
                }
                _lastClickTime = now;

                RichFileEditor.Document.Selection.SetRange(range.StartPosition, range.EndPosition);
                _lastSelectionAnchor = range.StartPosition;
                _isGutterDragging = true;
                _isPointerDragging = true;
                try { DocPageSheet.CapturePointer(e.Pointer); } catch { }
                e.Handled = true;
            }
            catch { }
        }
    }

    private void DocPageSheet_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isGutterDragging) return;
        var sheetPt = e.GetCurrentPoint(DocPageSheet);
        if (!sheetPt.Properties.IsLeftButtonPressed)
        {
            _isGutterDragging = false;
            _isPointerDragging = false;
            StopAutoScroll();
            try { DocPageSheet.ReleasePointerCapture(e.Pointer); } catch { }
            return;
        }

        try
        {
            var transform = DocPageSheet.TransformToVisual(RichFileEditor);
            var editorPt = transform.TransformPoint(sheetPt.Position);
            editorPt.X = 5;

            var endRange = RichFileEditor.Document.GetRangeFromPoint(editorPt, PointOptions.ClientCoordinates | PointOptions.AllowOffClient);
            endRange.Expand(TextRangeUnit.Line);

            int start = Math.Min(_lastSelectionAnchor, endRange.StartPosition);
            int end = Math.Max(_lastSelectionAnchor, endRange.EndPosition);
            RichFileEditor.Document.Selection.SetRange(start, end);
        }
        catch { }

        _currentViewerPointer = e.GetCurrentPoint(EditorScrollViewer).Position;
        double edgeMargin = 55.0;
        if (_currentViewerPointer.Y < edgeMargin || _currentViewerPointer.Y > EditorScrollViewer.ActualHeight - edgeMargin)
        {
            StartAutoScroll();
        }
        else
        {
            StopAutoScroll();
        }
    }

    private void DocPageSheet_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _isGutterDragging = false;
        _isPointerDragging = false;
        StopAutoScroll();
        try { DocPageSheet.ReleasePointerCapture(e.Pointer); } catch { }
    }

    private void StartAutoScroll()
    {
        if (_autoScrollTimer == null)
        {
            _autoScrollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(20)
            };
            _autoScrollTimer.Tick += AutoScrollTimer_Tick;
        }
        if (!_autoScrollTimer.IsEnabled)
        {
            _autoScrollTimer.Start();
        }
    }

    private void StopAutoScroll()
    {
        if (_autoScrollTimer != null && _autoScrollTimer.IsEnabled)
        {
            _autoScrollTimer.Stop();
        }
    }

    private void AutoScrollTimer_Tick(object? sender, object e)
    {
        if ((!_isPointerDragging && !_isGutterDragging) || EditorScrollViewer == null || RichFileEditor == null)
        {
            StopAutoScroll();
            return;
        }

        double edgeMargin = 55.0;
        double topEdge = edgeMargin;
        double bottomEdge = EditorScrollViewer.ActualHeight - edgeMargin;
        double deltaY = 0;

        if (_currentViewerPointer.Y > bottomEdge)
        {
            double dist = _currentViewerPointer.Y - bottomEdge;
            deltaY = Math.Clamp(dist * 0.45 + 6.0, 6.0, 45.0);
        }
        else if (_currentViewerPointer.Y < topEdge)
        {
            double dist = topEdge - _currentViewerPointer.Y;
            deltaY = -Math.Clamp(dist * 0.45 + 6.0, 6.0, 45.0);
        }
        else
        {
            StopAutoScroll();
            return;
        }

        double currentOffset = EditorScrollViewer.VerticalOffset;
        double newOffset = Math.Clamp(currentOffset + deltaY, 0, EditorScrollViewer.ScrollableHeight);

        if (Math.Abs(newOffset - currentOffset) > 0.1)
        {
            EditorScrollViewer.ChangeView(null, newOffset, null, disableAnimation: true);

            try
            {
                var transform = EditorScrollViewer.TransformToVisual(RichFileEditor);
                var editorPt = transform.TransformPoint(_currentViewerPointer);
                editorPt.X = Math.Clamp(editorPt.X, 4, Math.Max(10, RichFileEditor.ActualWidth - 4));

                var targetRange = RichFileEditor.Document.GetRangeFromPoint(editorPt, PointOptions.ClientCoordinates | PointOptions.AllowOffClient);
                int targetPos = targetRange.StartPosition;

                if (_isGutterDragging)
                {
                    targetRange.Expand(TextRangeUnit.Line);
                    targetPos = deltaY > 0 ? targetRange.EndPosition : targetRange.StartPosition;
                }

                int start = Math.Min(_lastSelectionAnchor, targetPos);
                int end = Math.Max(_lastSelectionAnchor, targetPos);
                RichFileEditor.Document.Selection.SetRange(start, end);
            }
            catch { }
        }
    }

    public void ExpandToSmartBlock(ITextRange range)
    {
        try
        {
            RichFileEditor.Document.GetText(TextGetOptions.None, out string fullText);
            if (string.IsNullOrEmpty(fullText))
            {
                range.Expand(TextRangeUnit.Paragraph);
                return;
            }

            int pos = Math.Clamp(range.StartPosition, 0, fullText.Length - 1);

            // Rückwärts nach echter Leerzeile suchen
            int blockStart = 0;
            for (int i = pos - 1; i >= 0; i--)
            {
                if (fullText[i] == '\n' || fullText[i] == '\r')
                {
                    int prev = i - 1;
                    if (prev >= 0 && fullText[prev] == '\r' && fullText[i] == '\n')
                    {
                        prev--; // CRLF-Paar überspringen
                    }
                    while (prev >= 0 && (fullText[prev] == ' ' || fullText[prev] == '\t'))
                    {
                        prev--;
                    }
                    if (prev >= 0 && (fullText[prev] == '\n' || fullText[prev] == '\r'))
                    {
                        blockStart = i + 1;
                        break;
                    }
                }
            }

            // Vorwärts nach echter Leerzeile suchen
            int blockEnd = fullText.Length;
            for (int i = pos; i < fullText.Length; i++)
            {
                if (fullText[i] == '\n' || fullText[i] == '\r')
                {
                    int next = i + 1;
                    if (fullText[i] == '\r' && next < fullText.Length && fullText[next] == '\n')
                    {
                        next++; // CRLF-Paar überspringen
                    }
                    while (next < fullText.Length && (fullText[next] == ' ' || fullText[next] == '\t'))
                    {
                        next++;
                    }
                    if (next < fullText.Length && (fullText[next] == '\n' || fullText[next] == '\r'))
                    {
                        blockEnd = i;
                        break;
                    }
                }
            }

            while (blockStart < blockEnd && (fullText[blockStart] == '\r' || fullText[blockStart] == '\n' || fullText[blockStart] == ' ' || fullText[blockStart] == '\t'))
            {
                blockStart++;
            }
            while (blockEnd > blockStart && (fullText[blockEnd - 1] == '\r' || fullText[blockEnd - 1] == '\n' || fullText[blockEnd - 1] == ' ' || fullText[blockEnd - 1] == '\t'))
            {
                blockEnd--;
            }

            if (blockEnd > blockStart)
            {
                range.SetRange(blockStart, blockEnd);
            }
            else
            {
                range.Expand(TextRangeUnit.Paragraph);
            }
        }
        catch
        {
            range.Expand(TextRangeUnit.Paragraph);
        }
    }

    private void ExpandSelectionStep()
    {
        try
        {
            var sel = RichFileEditor.Document.Selection;
            int len = sel.Length;
            if (len == 0)
            {
                sel.Expand(TextRangeUnit.Word);
            }
            else
            {
                int prevStart = sel.StartPosition;
                int prevEnd = sel.EndPosition;
                sel.Expand(TextRangeUnit.Line);
                if (sel.StartPosition == prevStart && sel.EndPosition == prevEnd)
                {
                    ExpandToSmartBlock(sel);
                    if (sel.StartPosition == prevStart && sel.EndPosition == prevEnd)
                    {
                        sel.Expand(TextRangeUnit.Story);
                    }
                }
            }
        }
        catch { }
    }

    public void CopySelectionOrAll(bool plainTextOnly = false)
    {
        try
        {
            string sel = GetSelectedText();
            bool hasSelection = !string.IsNullOrWhiteSpace(sel);

            if (plainTextOnly)
            {
                string raw = hasSelection ? sel : GetText();
                string clean = Helpers.TextFormatHelper.StripMarkdown(raw);
                var dp = new DataPackage();
                dp.SetText(clean);
                Clipboard.SetContent(dp);
                ShowCopyFeedback();
                return;
            }

            if (hasSelection)
            {
                RichFileEditor.Document.Selection.Copy();
                ShowCopyFeedback();
            }
            else
            {
                string all = GetText();
                if (!string.IsNullOrEmpty(all))
                {
                    var dp = new DataPackage();
                    dp.SetText(all);
                    Clipboard.SetContent(dp);
                    ShowCopyFeedback();
                }
            }
        }
        catch { }
    }

    public void CopyAsPlainText() => CopySelectionOrAll(plainTextOnly: true);

    private void ShowCopyFeedback()
    {
        try
        {
            if (IconCopyEditor != null)
            {
                IconCopyEditor.Glyph = "\uE73E"; // Checkmark
                IconCopyEditor.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen);

                _copyFeedbackTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                _copyFeedbackTimer.Stop();
                _copyFeedbackTimer.Tick -= CopyFeedbackTimer_Tick;
                _copyFeedbackTimer.Tick += CopyFeedbackTimer_Tick;
                _copyFeedbackTimer.Start();
            }
        }
        catch { }
    }

    private void CopyFeedbackTimer_Tick(object? sender, object e)
    {
        try
        {
            _copyFeedbackTimer?.Stop();
            if (IconCopyEditor != null)
            {
                IconCopyEditor.Glyph = "\uE8C8"; // Clipboard Icon
                IconCopyEditor.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AgyTextSecondary"];
            }
        }
        catch { }
    }
}
