using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using EasyWindowsTerminalControl;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private void SaveCurrentEditingFile()
    {
        if (string.IsNullOrEmpty(_currentEditingFilePath) || UcEditor == null)
        {
            ShowNotice("Keine Datei aktiv", "Es ist derzeit keine Datei im Editor geöffnet.", InfoBarSeverity.Warning);
            return;
        }

        try
        {
            if (_currentEditingFilePath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
            {
                WriteAllTextAtomic(_currentEditingFilePath, UcEditor.GetRtf());
            }
            else
            {
                WriteAllTextAtomic(_currentEditingFilePath, UcEditor.GetText());
            }
            ShowNotice("Datei gespeichert", $"Gespeichert: {Path.GetFileName(_currentEditingFilePath)}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"SaveCurrentEditingFile ERROR: {ex.Message}");
            ShowNotice("Fehler beim Speichern", $"Die Datei konnte nicht gespeichert werden: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void BtnSaveEditorFile_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnSaveEditorFile_Click)} triggered.");
        SaveCurrentEditingFile();
    }

    private void BtnCloseEditorFile_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnCloseEditorFile_Click)} triggered.");
        var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
        if (activeSession?.IsDocumentViewActive == true || _isResponseSplitOpen)
        {
            ToggleTerminalDocumentMode(forceState: false);
            return;
        }
        if (!string.IsNullOrEmpty(_currentEditingFilePath))
        {
            CloseFileTab(_currentEditingFilePath);
        }
        else
        {
            SwitchToTerminalTab();
        }
    }

    private void BtnOpenExternalWordPad_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnOpenExternalWordPad_Click)} triggered.");
        OpenCurrentDocumentInExternalApp();
    }

    public void OpenCurrentDocumentInExternalApp()
    {
        try
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string tempDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "temp");
            Directory.CreateDirectory(tempDir);
            CleanupOldTempFiles(tempDir);

            string rawTitle = !string.IsNullOrWhiteSpace(_currentEditingFilePath)
                ? Path.GetFileNameWithoutExtension(_currentEditingFilePath)
                : "Dokument";
            string safeTitle = string.Concat(rawTitle.Split(Path.GetInvalidFileNameChars()));
            string tempRtf = Path.Combine(tempDir, $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..4]}.rtf");
            string rtf = UcEditor?.GetRtf() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rtf) || !rtf.StartsWith("{\\rtf"))
            {
                string plain = UcEditor?.GetText() ?? string.Empty;
                rtf = Helpers.MarkdownToRtfConverter.ConvertToRtf(plain);
            }

            File.WriteAllText(tempRtf, rtf, Encoding.UTF8);

            var psi = new ProcessStartInfo(tempRtf)
            {
                UseShellExecute = true
            };
            Process.Start(psi);

            ShowNotice("In Office geöffnet", "Das Dokument wurde im Standardprogramm (z. B. Word oder WordPad) geöffnet.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[OpenCurrentDocumentInExternalApp ERROR] {ex.Message}");
            ShowNotice("Fehler beim Öffnen", $"Konnte das Dokument nicht in der externen App öffnen: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void BtnPrintEditorText_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnPrintEditorText_Click)} triggered.");
        FrameworkElement? anchor = (sender is MenuFlyoutItem)
            ? (FrameworkElement?)UcEditor?.BtnPrintEditorText ?? (FrameworkElement?)TxtFileEditor ?? (FrameworkElement)this.Content
            : (sender as FrameworkElement ?? (FrameworkElement?)UcEditor?.BtnPrintEditorText ?? (FrameworkElement?)TxtFileEditor ?? (FrameworkElement)this.Content);
        ShowPrintFlyout(anchor);
    }

    private void BtnSavePdfEditorText_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnSavePdfEditorText_Click)} triggered.");
        _ = SaveCurrentEditorContentAsPdfAsync();
    }

    private void BtnStripMarkdownEditor_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnStripMarkdownEditor_Click)} triggered.");
        CleanMarkdownInEditor();
    }

    public void CleanMarkdownInEditor()
    {
        if (UcEditor == null) return;

        try
        {
            string sel = UcEditor.GetSelectedText();
            if (!string.IsNullOrEmpty(sel))
            {
                string cleaned = Helpers.TextFormatHelper.StripMarkdown(sel);
                UcEditor.SetSelectedText(cleaned);
                ShowNotice("Markierung bereinigt", "Markdown-Formatierungen aus der Textauswahl wurden entfernt.", InfoBarSeverity.Success);
            }
            else if (UcEditor.IsDocumentMode)
            {
                string all = UcEditor.GetText();
                if (!string.IsNullOrEmpty(all))
                {
                    UcEditor.SetText(Helpers.TextFormatHelper.StripMarkdown(all));
                    ShowNotice("Dokument bereinigt", "Das gesamte Dokument wurde in reinen Text ohne Markdowns umgewandelt.", InfoBarSeverity.Success);
                }
            }
            else
            {
                ShowNotice("Schutz vor Quellcode-Veränderung", "Im Quellcode-Modus kann Markdown-Bereinigung nur auf selektierten Text angewendet werden, um Code-Elemente (z. B. Generics und Unterstriche) zu schützen.", InfoBarSeverity.Informational);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CleanMarkdownInEditor ERROR] {ex.Message}");
            ShowNotice("Fehler beim Bereinigen", $"Markdown konnte nicht bereinigt werden: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void TxtFileEditor_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if ((ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down)
        {
            if (e.Key == VirtualKey.S)
            {
                e.Handled = true;
                SaveCurrentEditingFile();
            }
            else if (e.Key == VirtualKey.P)
            {
                e.Handled = true;
                ShowPrintFlyout((FrameworkElement?)UcEditor?.BtnPrintEditorText ?? (FrameworkElement?)TxtFileEditor ?? (FrameworkElement)this.Content);
            }
            else if (e.Key == VirtualKey.O)
            {
                e.Handled = true;
                OpenCurrentDocumentInExternalApp();
            }
        }
    }

    private string GetCleanEditorText(out bool wasSelectionUsed)
    {
        string raw = UcEditor?.GetSelectedText() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            wasSelectionUsed = true;
        }
        else
        {
            wasSelectionUsed = false;
            raw = UcEditor?.GetText() ?? string.Empty;
        }

        return Helpers.TextFormatHelper.StripMarkdown(raw);
    }

    public async Task SaveCurrentEditorContentAsPdfAsync(string? preCleanedText = null)
    {
        try
        {
            string cleanText = preCleanedText ?? GetCleanEditorText(out _);
            if (string.IsNullOrWhiteSpace(cleanText))
            {
                ShowNotice("Kein Text vorhanden", "Es ist kein Text zum Exportieren als PDF vorhanden.", InfoBarSeverity.Warning);
                return;
            }

            string docTitle = !string.IsNullOrWhiteSpace(_currentEditingFilePath)
                ? Path.GetFileNameWithoutExtension(_currentEditingFilePath)
                : "Antwort";

            var picker = new Windows.Storage.Pickers.FileSavePicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("PDF Dokument (*.pdf)", new List<string> { ".pdf" });
            picker.SuggestedFileName = $"{docTitle}_{DateTime.Now:yyyyMMdd_HHmm}.pdf";

            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                byte[] pdfBytes = Services.PrintAndPdfService.GeneratePdf(cleanText, docTitle);
                await File.WriteAllBytesAsync(file.Path, pdfBytes);
                ShowNotice("PDF gespeichert", $"Die Datei wurde erfolgreich gespeichert: {file.Name}", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[SaveCurrentEditorContentAsPdfAsync ERROR] {ex.Message}");
            ShowNotice("Fehler beim PDF-Export", $"Konnte die PDF-Datei nicht erstellen: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    public void ShowPrintFlyout(FrameworkElement? anchor)
    {
        try
        {
            string cleanText = GetCleanEditorText(out bool wasSelection);
            if (string.IsNullOrWhiteSpace(cleanText))
            {
                ShowNotice("Kein Text vorhanden", "Es ist kein Text zum Drucken vorhanden.", InfoBarSeverity.Warning);
                return;
            }

            var printers = Services.PrintAndPdfService.GetInstalledPrinters();
            string defaultPrinter = Services.PrintAndPdfService.GetDefaultPrinter();

            var flyout = new Flyout();
            var sp = new StackPanel { Spacing = 10, Width = 310, Padding = new Thickness(4) };

            // Header
            var headerSp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            headerSp.Children.Add(new FontIcon { Glyph = "\uE749", FontSize = 15, Foreground = Application.Current.Resources["AgyTextStrong"] as Brush });
            headerSp.Children.Add(new TextBlock
            {
                Text = "Dokument drucken",
                FontSize = 13.5,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = Application.Current.Resources["AgyTextPrimary"] as Brush
            });
            sp.Children.Add(headerSp);

            // Scope & Status
            int charCount = cleanText.Length;
            int lineCount = cleanText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).Length;
            string scopeInfo = wasSelection ? "Markierte Auswahl" : "Gesamtes Dokument";
            var txtScope = new TextBlock
            {
                Text = $"{scopeInfo} ({charCount:N0} Zeichen, {lineCount} Zeilen, WordPad-Dokumenten-Layout)",
                FontSize = 11,
                Foreground = Application.Current.Resources["AgyTextMuted"] as Brush,
                TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(txtScope);

            // Printer Selector
            var pnlPrinter = new StackPanel { Spacing = 4 };
            pnlPrinter.Children.Add(new TextBlock
            {
                Text = "Drucker:",
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Application.Current.Resources["AgyTextSecondary"] as Brush
            });
            var cmbPrinters = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = printers,
                FontSize = 12
            };
            int selIdx = printers.FindIndex(p => string.Equals(p, defaultPrinter, StringComparison.OrdinalIgnoreCase));
            cmbPrinters.SelectedIndex = selIdx >= 0 ? selIdx : 0;
            pnlPrinter.Children.Add(cmbPrinters);
            sp.Children.Add(pnlPrinter);

            // Copies Selector
            var pnlCopies = new StackPanel { Spacing = 4 };
            pnlCopies.Children.Add(new TextBlock
            {
                Text = "Exemplare:",
                FontSize = 11.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Application.Current.Resources["AgyTextSecondary"] as Brush
            });
            var cmbCopies = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new List<int> { 1, 2, 3, 4, 5, 10 },
                FontSize = 12
            };
            cmbCopies.SelectedIndex = 0;
            pnlCopies.Children.Add(cmbCopies);
            sp.Children.Add(pnlCopies);

            // Print Button
            var btnPrint = new Button
            {
                Content = "Jetzt drucken",
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Background = Application.Current.Resources["AgyAccent"] as Brush,
                Foreground = Application.Current.Resources["AgyTextPrimary"] as Brush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(0, 6, 0, 6)
            };

            // Save PDF Alternative Button
            var btnPdf = new Button
            {
                Content = "Als PDF speichern...",
                FontSize = 12,
                Background = Application.Current.Resources["AgySurfaceBg"] as Brush,
                Foreground = Application.Current.Resources["AgyTextStrong"] as Brush,
                BorderBrush = Application.Current.Resources["AgyBorderDefault"] as Brush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(0, 5, 0, 5)
            };

            btnPrint.Click += async (s, e) =>
            {
                flyout.Hide();
                string selectedPrinter = cmbPrinters.SelectedItem as string ?? defaultPrinter;
                int copies = cmbCopies.SelectedItem is int c ? c : 1;

                if (string.Equals(selectedPrinter, "Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase))
                {
                    await SaveCurrentEditorContentAsPdfAsync(cleanText);
                }
                else
                {
                    string docTitle = !string.IsNullOrWhiteSpace(_currentEditingFilePath)
                        ? Path.GetFileName(_currentEditingFilePath)
                        : "Antwort";

                    bool ok = await Services.PrintAndPdfService.PrintTextAsync(cleanText, selectedPrinter, copies, docTitle);
                    this.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (ok)
                        {
                            ShowNotice("Druckauftrag gesendet", $"Druckauftrag erfolgreich an '{selectedPrinter}' übermittelt ({copies}x).", InfoBarSeverity.Success);
                        }
                        else
                        {
                            ShowNotice("Druckfehler", $"Der Druckauftrag an '{selectedPrinter}' konnte nicht übermittelt werden.", InfoBarSeverity.Error);
                        }
                    });
                }
            };

            btnPdf.Click += async (s, e) =>
            {
                flyout.Hide();
                await SaveCurrentEditorContentAsPdfAsync(cleanText);
            };

            sp.Children.Add(btnPrint);
            sp.Children.Add(btnPdf);

            flyout.Content = sp;

            FrameworkElement target = (anchor != null && !(anchor is MenuFlyoutItem) && anchor.IsLoaded)
                ? anchor
                : (FrameworkElement?)UcEditor?.BtnPrintEditorText ?? TxtFileEditor ?? (FrameworkElement)this.Content;

            if (target.XamlRoot != null)
            {
                flyout.XamlRoot = target.XamlRoot;
            }
            else if (this.Content is FrameworkElement root && root.XamlRoot != null)
            {
                flyout.XamlRoot = root.XamlRoot;
            }

            flyout.ShowAt(target);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[ShowPrintFlyout ERROR] {ex.Message}");
            ShowNotice("Fehler beim Drucken", $"Der Druckdialog konnte nicht geöffnet werden: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void BtnOpenLastResponse_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnOpenLastResponse_Click)} triggered.");
        OpenLastResponseInEditor();
    }

    private void BtnOpenLastResponse_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[RightTapped Trace] {nameof(BtnOpenLastResponse_RightTapped)} triggered.");
        e.Handled = true;
        OpenFullChatInEditor();
    }

    private void BtnOpenFullChat_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnOpenFullChat_Click)} triggered.");
        OpenFullChatInEditor();
    }

    public string? GetLastModelResponseText()
    {
        try
        {
            var activeSession = GetActiveTerminalSession();
            string? convId = activeSession?.ConversationId;

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string brainBase = Path.Combine(userProfile, ".gemini", "antigravity-cli", "brain");

            if (string.IsNullOrEmpty(convId) && Directory.Exists(brainBase))
            {
                var latestDir = new DirectoryInfo(brainBase)
                    .GetDirectories()
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (latestDir != null)
                {
                    convId = latestDir.Name;
                }
            }

            if (string.IsNullOrEmpty(convId)) return null;

            string logDir = Path.Combine(brainBase, convId, ".system_generated", "logs");
            string fullLogFile = Path.Combine(logDir, "transcript_full.jsonl");
            string compactLogFile = Path.Combine(logDir, "transcript.jsonl");

            string? targetFile = File.Exists(fullLogFile) ? fullLogFile : (File.Exists(compactLogFile) ? compactLogFile : null);
            if (targetFile == null) return null;

            using var fs = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);

            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                try
                {
                    using var doc = JsonDocument.Parse(lines[i]);
                    var root = doc.RootElement;
                    string source = root.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                    string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

                    // Nur MODEL Antworten berücksichtigen (keine Tool-Outputs / GENERIC)
                    if (string.Equals(source, "MODEL", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(type, "GENERIC", StringComparison.OrdinalIgnoreCase))
                        {
                            continue; // Interne Tool-Konsolenausgaben überspringen!
                        }

                        // Prüfen ob nur tool_calls ohne Text-Antwort vorliegen
                        if (root.TryGetProperty("tool_calls", out var tc) && tc.ValueKind == JsonValueKind.Array && tc.GetArrayLength() > 0)
                        {
                            if (!root.TryGetProperty("content", out var cc) || string.IsNullOrWhiteSpace(cc.GetString()))
                            {
                                continue;
                            }
                        }

                        if (root.TryGetProperty("content", out var c))
                        {
                            string text = c.GetString() ?? "";
                            string trimmed = text.Trim();
                            if (!string.IsNullOrWhiteSpace(trimmed))
                            {
                                // Ausschluss von typischen Tool-Header-Ausgaben
                                if (trimmed.StartsWith("Created At:", StringComparison.OrdinalIgnoreCase) ||
                                    trimmed.StartsWith("Completed At:", StringComparison.OrdinalIgnoreCase) ||
                                    trimmed.StartsWith("The command exited", StringComparison.OrdinalIgnoreCase) ||
                                    trimmed.StartsWith("File Path:", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                return trimmed;
                            }
                        }
                    }
                }
                catch
                {
                    // JSON Parsing-Toleranz bei Zeilen-Fragmenten
                }
            }
        }
        catch { }
        return null;
    }

    private bool _isResponseSplitOpen = false;
    private bool _isEditorSplitterDragging = false;
    private double _editorSplitterStartX;
    private double _editorSplitterStartWidth;

    private void SplitterEditor_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement border)
        {
            border.CapturePointer(e.Pointer);
            _isEditorSplitterDragging = true;
            _editorSplitterStartX = e.GetCurrentPoint(this.Content).Position.X;
            _editorSplitterStartWidth = ColEditor?.ActualWidth ?? 300;
            e.Handled = true;
        }
    }

    private void SplitterEditor_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isEditorSplitterDragging && this.Content != null && ColEditor != null && ColTerminal != null)
        {
            double currentX = e.GetCurrentPoint(this.Content).Position.X;
            double delta = currentX - _editorSplitterStartX;
            double workspaceWidth = ColMainWorkspace?.ActualWidth ?? 800;
            double newWidth = Math.Max(240, Math.Min(workspaceWidth - 240, _editorSplitterStartWidth - delta));

            ColEditor.Width = new GridLength(newWidth, GridUnitType.Pixel);
            ColTerminal.Width = new GridLength(1, GridUnitType.Star);
            e.Handled = true;
        }
    }

    private void SplitterEditor_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isEditorSplitterDragging && sender is UIElement border)
        {
            border.ReleasePointerCapture(e.Pointer);
            _isEditorSplitterDragging = false;
            e.Handled = true;
        }
    }

    public (string rawChat, string? lastResponseSnippet) GetFullChatHistoryMarkdown(TerminalTabSession? session)
    {
        try
        {
            string? convId = session?.ConversationId;
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string brainBase = Path.Combine(userProfile, ".gemini", "antigravity-cli", "brain");

            if (string.IsNullOrEmpty(convId) && Directory.Exists(brainBase))
            {
                var latestDir = new DirectoryInfo(brainBase)
                    .GetDirectories()
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (latestDir != null) convId = latestDir.Name;
            }

            if (string.IsNullOrEmpty(convId))
            {
                string? lastOnly = GetLastModelResponseText();
                return (lastOnly ?? "Kein Gesprächsverlauf vorhanden.", lastOnly);
            }

            string logDir = Path.Combine(brainBase, convId, ".system_generated", "logs");
            string fullLogFile = Path.Combine(logDir, "transcript_full.jsonl");
            string compactLogFile = Path.Combine(logDir, "transcript.jsonl");
            string? targetFile = File.Exists(fullLogFile) ? fullLogFile : (File.Exists(compactLogFile) ? compactLogFile : null);

            if (targetFile == null)
            {
                string? lastOnly = GetLastModelResponseText();
                return (lastOnly ?? "Kein Gesprächsverlauf vorhanden.", lastOnly);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# Chatverlauf: {session?.Title ?? convId}");
            sb.AppendLine();

            string? lastResponseSnippet = null;

            using (var fs = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        string source = root.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                        string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

                        if (root.TryGetProperty("content", out var c))
                        {
                            string text = c.GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(text)) continue;

                            if (string.Equals(source, "USER_INPUT", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(source, "USER_EXPLICIT", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(type, "USER_INPUT", StringComparison.OrdinalIgnoreCase))
                            {
                                var match = Regex.Match(text, @"<USER_REQUEST>\s*(.*?)\s*</USER_REQUEST>", RegexOptions.Singleline);
                                string cleanUser = match.Success ? match.Groups[1].Value.Trim() : text.Trim();
                                sb.AppendLine("----------------------------------------");
                                sb.AppendLine("👤 Benutzer:");
                                sb.AppendLine();
                                sb.AppendLine(cleanUser);
                                sb.AppendLine();
                            }
                            else if (string.Equals(source, "MODEL", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.Equals(type, "GENERIC", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue; // Interne Tool-Konsolenausgaben überspringen
                                }

                                string trimmed = text.Trim();
                                if (trimmed.StartsWith("Created At:", StringComparison.OrdinalIgnoreCase) ||
                                    trimmed.StartsWith("Completed At:", StringComparison.OrdinalIgnoreCase) ||
                                    trimmed.StartsWith("The command exited", StringComparison.OrdinalIgnoreCase) ||
                                    trimmed.StartsWith("File Path:", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                sb.AppendLine("----------------------------------------");
                                sb.AppendLine("🤖 KI:");
                                sb.AppendLine();
                                sb.AppendLine(trimmed);
                                sb.AppendLine();

                                if (trimmed.Length > 0)
                                {
                                    int snippetLen = Math.Min(trimmed.Length, 80);
                                    lastResponseSnippet = trimmed.Substring(0, snippetLen).Trim();
                                }
                            }
                        }
                    }
                    catch
                    {
                        // JSON Parsing-Toleranz bei Zeilen-Fragmenten
                    }
                }
            }

            string full = sb.ToString();
            return (!string.IsNullOrWhiteSpace(full) ? full : "Kein Gesprächsverlauf vorhanden.", lastResponseSnippet);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[GetFullChatHistoryMarkdown ERROR] {ex.Message}");
            return ("Fehler beim Laden des Chatverlaufs.", null);
        }
    }

    private string? GetCurrentTerminalSelectionOrVisibleText(EasyTerminalControl? termControl)
    {
        if (termControl == null) return null;
        try
        {
            var innerTerminal = termControl.Terminal;
            if (innerTerminal != null)
            {
                var type = innerTerminal.GetType();
                var getSelTextMethod = type.GetMethod("GetSelectedText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (getSelTextMethod != null)
                {
                    var textObj = getSelTextMethod.Invoke(innerTerminal, null);
                    if (textObj is string sel && !string.IsNullOrWhiteSpace(sel))
                    {
                        return sel.Trim();
                    }
                }

                var selProp = type.GetProperty("SelectedText") ?? type.GetProperty("Selection");
                if (selProp != null && selProp.GetValue(innerTerminal) is string s && !string.IsNullOrWhiteSpace(s))
                {
                    return s.Trim();
                }
            }

            string? log = Helpers.TerminalBufferManager.GetOutputLogText(termControl.ConPTYTerm);
            if (!string.IsNullOrWhiteSpace(log))
            {
                var lines = log.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .Where(l => !l.Contains("for shortcuts") && !l.Contains("Gemini") && !l.Trim().StartsWith(">"))
                               .Reverse()
                               .Take(3)
                               .ToList();
                if (lines.Count > 0)
                {
                    string joined = string.Join(" ", lines).Trim();
                    if (joined.Length >= 8)
                    {
                        return joined.Substring(0, Math.Min(joined.Length, 60));
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public void ToggleTerminalDocumentMode(bool? forceState = null)
    {
        try
        {
            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId) ?? _terminalSessions.FirstOrDefault();
            if (activeSession == null) return;

            bool targetDocMode = forceState ?? !activeSession.IsDocumentViewActive;

            if (targetDocMode)
            {
                // Scroll-Anker aus aktuellem Terminal erfassen BEVOR das Control minimiert wird
                string? terminalSnippet = GetCurrentTerminalSelectionOrVisibleText(activeSession.Control);

                var (rawChat, lastSnippet) = GetFullChatHistoryMarkdown(activeSession);

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string tempDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "temp");
                Directory.CreateDirectory(tempDir);
                string tempFile = Path.Combine(tempDir, "chat_verlauf.rtf");

                string rtf = Helpers.MarkdownToRtfConverter.ConvertToRtf(rawChat, isDarkTheme: true);
                WriteAllTextAtomic(tempFile, rtf);

                if (UcEditor != null)
                {
                    UcEditor.SetEditorMode(isDocumentMode: true);
                    UcEditor.SetToolbarsVisibility(false); // Standardmäßig vollständig eingeklappt für Clean UIX!
                    UcEditor.SetRtf(rtf);
                    if (TxtEditorTitle != null) TxtEditorTitle.Text = $"Chatverlauf: {activeSession.Title}";
                    if (TxtEditorPath != null) TxtEditorPath.Text = "Vollständiger Verlauf · Strg+C für RTF/Office · Klick auf 'Werkzeuge' für alle Formatierungsbuttons";
                }

                activeSession.IsDocumentViewActive = true;
                _isResponseSplitOpen = false;
                _currentEditingFilePath = tempFile;

                // Vollbild-Verwandlung: HWND-Terminal ausblenden, Editor auf volle 100% Breite
                foreach (var ts in _terminalSessions)
                {
                    ts.Control.Visibility = Visibility.Collapsed;
                    ts.Control.Width = 0;
                    ts.Control.Height = 0;
                }

                if (ColTerminal != null) ColTerminal.Width = new GridLength(0);
                if (ColEditorSplitter != null) ColEditorSplitter.Width = new GridLength(0);
                if (ColEditor != null) ColEditor.Width = new GridLength(1, GridUnitType.Star);
                if (SplitterEditor != null) SplitterEditor.Visibility = Visibility.Collapsed;
                if (TerminalContainer != null) TerminalContainer.Visibility = Visibility.Collapsed;
                if (UcEditor != null) UcEditor.Visibility = Visibility.Visible;

                UpdateWorkspaceModeButton(isDocumentMode: true);
                RenderWorkspaceTabs();

                // An den aktuellen Terminalpunkt scrollen
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    try
                    {
                        bool scrolled = false;
                        if (!string.IsNullOrWhiteSpace(terminalSnippet))
                        {
                            scrolled = UcEditor?.ScrollToText(terminalSnippet) ?? false;
                        }
                        if (!scrolled && !string.IsNullOrWhiteSpace(lastSnippet))
                        {
                            scrolled = UcEditor?.ScrollToText(lastSnippet) ?? false;
                        }
                        if (!scrolled)
                        {
                            UcEditor?.ScrollToLastResponse();
                        }
                    }
                    catch { }
                });

                ShowNotice("Chat-Dokument aktiv", "Gesamter Verlauf geladen & am aktuellen Punkt positioniert (Klick auf 'Werkzeuge' für Leiste).", InfoBarSeverity.Success);
            }
            else
            {
                activeSession.IsDocumentViewActive = false;
                _isResponseSplitOpen = false;
                if (!string.IsNullOrEmpty(_currentEditingFilePath) && 
                    (_currentEditingFilePath.EndsWith("letzte_antwort.rtf", StringComparison.OrdinalIgnoreCase) ||
                     _currentEditingFilePath.EndsWith("chat_verlauf.rtf", StringComparison.OrdinalIgnoreCase)))
                {
                    _currentEditingFilePath = null;
                }

                // Vollbild-Verwandlung: Editor ausblenden, Terminal auf volle 100% Breite & HWND reaktivieren
                if (ColEditor != null) ColEditor.Width = new GridLength(0);
                if (ColEditorSplitter != null) ColEditorSplitter.Width = new GridLength(0);
                if (ColTerminal != null) ColTerminal.Width = new GridLength(1, GridUnitType.Star);
                if (SplitterEditor != null) SplitterEditor.Visibility = Visibility.Collapsed;
                if (UcEditor != null) UcEditor.Visibility = Visibility.Collapsed;
                if (TerminalContainer != null) TerminalContainer.Visibility = Visibility.Visible;

                foreach (var ts in _terminalSessions)
                {
                    if (ts.Id == activeSession.Id)
                    {
                        ts.Control.Visibility = Visibility.Visible;
                        ts.Control.Width = double.NaN;
                        ts.Control.Height = double.NaN;
                        ts.Control.UpdateLayout();
                        ApplyFontSizeAndFamily(ts.Control, _fontSize);
                    }
                    else
                    {
                        ts.Control.Visibility = Visibility.Collapsed;
                        ts.Control.Width = 0;
                        ts.Control.Height = 0;
                    }
                }

                UpdateWorkspaceModeButton(isDocumentMode: false);
                RenderWorkspaceTabs();

                try { TxtChatInput?.Focus(FocusState.Programmatic); } catch { }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[ToggleTerminalDocumentMode ERROR] {ex.Message}");
            ShowNotice("Fehler beim Umschalten", $"Konnte den Dokumentenmodus nicht umschalten: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    public void UpdateWorkspaceModeButton(bool isDocumentMode)
    {
        if (BtnToggleWorkspaceMode != null)
        {
            if (isDocumentMode)
            {
                BtnToggleWorkspaceMode.Background = (Brush)Application.Current.Resources["AgyDocAccentSubtle"];
                BtnToggleWorkspaceMode.BorderBrush = (Brush)Application.Current.Resources["AgyDocAccent"];
                if (IcoWorkspaceMode != null)
                {
                    IcoWorkspaceMode.Foreground = (Brush)Application.Current.Resources["AgyDocAccentLight"];
                    IcoWorkspaceMode.Glyph = "\uE756"; // Terminal Prompt
                }
                ToolTipService.SetToolTip(BtnToggleWorkspaceMode, "Zurück zur interaktiven Konsole wechseln (Strg+D · Rechtsklick auf Tab)");
            }
            else
            {
                BtnToggleWorkspaceMode.Background = (Brush)Application.Current.Resources["AgySurfaceSubtle"];
                BtnToggleWorkspaceMode.BorderBrush = (Brush)Application.Current.Resources["AgyBorderFaint"];
                if (IcoWorkspaceMode != null)
                {
                    IcoWorkspaceMode.Foreground = (Brush)Application.Current.Resources["AgyTextSecondary"];
                    IcoWorkspaceMode.Glyph = "\uE8A5"; // Document Icon
                }
                ToolTipService.SetToolTip(BtnToggleWorkspaceMode, "In formatierte RTF-Dokumentenansicht umschalten (Strg+D · Rechtsklick auf Tab)");
            }
        }

        if (BtnToggleEditorToolbars != null)
        {
            BtnToggleEditorToolbars.Visibility = isDocumentMode ? Visibility.Visible : Visibility.Collapsed;
            if (isDocumentMode)
            {
                UpdateEditorToolbarsButton(UcEditor?.AreToolbarsVisible ?? false);
            }
        }
    }

    private void BtnToggleEditorToolbars_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnToggleEditorToolbars_Click)} triggered.");
        ToggleEditorToolbars();
    }

    public void ToggleEditorToolbars(bool? forceState = null)
    {
        if (UcEditor == null) return;
        bool newState = forceState ?? !UcEditor.AreToolbarsVisible;
        UcEditor.SetToolbarsVisibility(newState);
        UpdateEditorToolbarsButton(newState);
    }

    public void UpdateEditorToolbarsButton(bool areToolbarsVisible)
    {
        if (BtnToggleEditorToolbars == null) return;
        if (areToolbarsVisible)
        {
            BtnToggleEditorToolbars.Background = (Brush)Application.Current.Resources["AgyDocAccentSubtle"];
            BtnToggleEditorToolbars.BorderBrush = (Brush)Application.Current.Resources["AgyDocAccent"];
            if (IcoEditorToolbars != null) IcoEditorToolbars.Foreground = (Brush)Application.Current.Resources["AgyDocAccentLight"];
            ToolTipService.SetToolTip(BtnToggleEditorToolbars, "Werkzeuge & Formatierungsleiste vollständig ausblenden");
        }
        else
        {
            BtnToggleEditorToolbars.Background = (Brush)Application.Current.Resources["AgySurfaceSubtle"];
            BtnToggleEditorToolbars.BorderBrush = (Brush)Application.Current.Resources["AgyBorderFaint"];
            if (IcoEditorToolbars != null) IcoEditorToolbars.Foreground = (Brush)Application.Current.Resources["AgyTextSecondary"];
            ToolTipService.SetToolTip(BtnToggleEditorToolbars, "Werkzeuge & Formatierungsleiste vollständig einblenden");
        }
    }

    private void BtnToggleWorkspaceMode_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnToggleWorkspaceMode_Click)} triggered.");
        ToggleTerminalDocumentMode();
    }

    public void ToggleLastResponseSplitView()
    {
        ToggleTerminalDocumentMode();
    }

    public void CloseResponseSplit()
    {
        ToggleTerminalDocumentMode(forceState: false);
    }

    public void OpenLastResponseInEditor()
    {
        ToggleTerminalDocumentMode(forceState: true);
    }

    private void BtnCopyLastResponse_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnCopyLastResponse_Click)} triggered.");
        CopyLastResponseToClipboard();
    }

    public void CopyLastResponseToClipboard()
    {
        try
        {
            string? lastResponse = GetLastModelResponseText();
            if (string.IsNullOrWhiteSpace(lastResponse))
            {
                ShowNotice("Keine Antwort gefunden", "Es wurde noch keine Text-Antwort der KI in diesem Verlauf gefunden.", InfoBarSeverity.Informational);
                return;
            }

            string clean = Helpers.TextFormatHelper.StripMarkdown(lastResponse);
            var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dp.SetText(clean);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);

            int charCount = clean.Length;
            int lineCount = clean.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).Length;
            ShowNotice("Antwort kopiert", $"Die letzte KI-Antwort liegt in der Zwischenablage ({charCount:N0} Zeichen, {lineCount} Zeilen).", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CopyLastResponseToClipboard ERROR] {ex.Message}");
            ShowNotice("Fehler beim Kopieren", $"Konnte die Antwort nicht kopieren: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    public void OpenFullChatInEditor()
    {
        try
        {
            var activeSession = GetActiveTerminalSession();
            string? convId = activeSession?.ConversationId;

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string brainBase = Path.Combine(userProfile, ".gemini", "antigravity-cli", "brain");

            if (string.IsNullOrEmpty(convId) && Directory.Exists(brainBase))
            {
                var latestDir = new DirectoryInfo(brainBase)
                    .GetDirectories()
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (latestDir != null)
                {
                    convId = latestDir.Name;
                }
            }

            if (string.IsNullOrEmpty(convId))
            {
                ShowNotice("Keine Sitzung aktiv", "Es konnte keine aktive Konversation ermittelt werden.", InfoBarSeverity.Warning);
                return;
            }

            string logDir = Path.Combine(brainBase, convId, ".system_generated", "logs");
            string fullLogFile = Path.Combine(logDir, "transcript_full.jsonl");
            string compactLogFile = Path.Combine(logDir, "transcript.jsonl");

            string? targetFile = File.Exists(fullLogFile) ? fullLogFile : (File.Exists(compactLogFile) ? compactLogFile : null);

            if (targetFile == null)
            {
                ShowNotice("Kein Chat-Verlauf vorhanden", $"Für den Chat '{convId}' liegt noch kein Protokoll vor.", InfoBarSeverity.Warning);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# Chatverlauf: {activeSession?.Title ?? convId}");
            sb.AppendLine();

            using (var fs = new FileStream(targetFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, Encoding.UTF8))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        string source = root.TryGetProperty("source", out var s) ? s.GetString() ?? "" : "";
                        string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";

                        if (root.TryGetProperty("content", out var c))
                        {
                            string text = c.GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(text)) continue;

                            if (string.Equals(source, "USER_INPUT", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(source, "USER_EXPLICIT", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(type, "USER_INPUT", StringComparison.OrdinalIgnoreCase))
                            {
                                var match = Regex.Match(text, @"<USER_REQUEST>\s*(.*?)\s*</USER_REQUEST>", RegexOptions.Singleline);
                                string cleanUser = match.Success ? match.Groups[1].Value.Trim() : text.Trim();
                                sb.AppendLine("----------------------------------------");
                                sb.AppendLine("👤 Benutzer:");
                                sb.AppendLine();
                                sb.AppendLine(cleanUser);
                                sb.AppendLine();
                            }
                            else if (string.Equals(source, "MODEL", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.Equals(type, "GENERIC", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue; // Interne Tool-Konsolenausgaben überspringen!
                                }

                                string trimmed = text.Trim();
                                if (trimmed.StartsWith("Created At:", StringComparison.OrdinalIgnoreCase) ||
                                    trimmed.StartsWith("Completed At:", StringComparison.OrdinalIgnoreCase) ||
                                    trimmed.StartsWith("The command exited", StringComparison.OrdinalIgnoreCase) ||
                                    trimmed.StartsWith("File Path:", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                sb.AppendLine("----------------------------------------");
                                sb.AppendLine("🤖 KI:");
                                sb.AppendLine();
                                sb.AppendLine(trimmed);
                                sb.AppendLine();
                            }
                        }
                    }
                    catch
                    {
                        // JSON Parsing-Toleranz bei Zeilen-Fragmenten
                    }
                }
            }

            string rawChat = sb.ToString();
            string tempDir = Path.Combine(userProfile, ".gemini", "antigravity-cli", "temp");
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, "chat_verlauf.rtf");

            string rtf = Helpers.MarkdownToRtfConverter.ConvertToRtf(rawChat);
            WriteAllTextAtomic(tempFile, rtf);

            OpenFileInEditor(tempFile, rtf);
            ShowNotice("Chatverlauf geöffnet", "Der gesamte Chatverlauf wurde als formatiertes Dokument im WordPad-Tab geöffnet.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[OpenFullChatInEditor ERROR] {ex.Message}");
            ShowNotice("Fehler beim Öffnen", $"Konnte den Chatverlauf nicht öffnen: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private static void CleanupOldTempFiles(string tempDir)
    {
        try
        {
            if (!Directory.Exists(tempDir)) return;
            var files = Directory.GetFiles(tempDir, "*.rtf");
            var cutoff = DateTime.UtcNow.AddHours(-24);
            foreach (var f in files)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(f) < cutoff)
                    {
                        File.Delete(f);
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
