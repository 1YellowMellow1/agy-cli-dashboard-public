using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Threading.Tasks;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private bool _isCoBrowserOpen = false;
    private bool _isSplitterDragging = false;
    private double _splitterStartX = 0;
    private double _splitterStartWidth = 0;
    private Window? _coBrowserPopOutWindow = null;

    private void InitializeCoBrowser()
    {
        if (UcHeader != null)
        {
            UcHeader.ToggleCoBrowserClick += Header_ToggleCoBrowserClick;
            UcHeader.ToggleCoBrowserRightTapped += Header_ToggleCoBrowserRightTapped;
        }

        if (UcCoBrowser != null)
        {
            UcCoBrowser.CloseRequested += CoBrowser_CloseRequested;
            UcCoBrowser.PopOutRequested += CoBrowser_PopOutRequested;
            UcCoBrowser.OpenRequested += CoBrowser_OpenRequested;
            UcCoBrowser.IsBrowserOpenQuery += CoBrowser_IsBrowserOpenQuery;
            UcCoBrowser.ReflectionPromptRequested += CoBrowser_ReflectionPromptRequested;
            UcCoBrowser.AssociatedChatClicked += CoBrowser_AssociatedChatClicked;
            UcCoBrowser.ConversationActive += CoBrowser_ConversationActive;

            // Hintergrund-Vorwärmung: WebView2 & HTTP-Server starten, damit Tools sofort bereit sind
            _ = UcCoBrowser.EnsureInitializedAsync();
        }
    }

    private void Header_ToggleCoBrowserClick(object sender, RoutedEventArgs e) => ToggleCoBrowser();
    private void CoBrowser_CloseRequested(object? sender, EventArgs e) => CloseCoBrowser();
    private void CoBrowser_PopOutRequested(object? sender, EventArgs e) => PopOutCoBrowser();
    private void CoBrowser_OpenRequested(object? sender, Views.CoBrowserOpenEventArgs e) => _ = OpenCoBrowserAsync(e.Url, e.ConversationId);
    private bool CoBrowser_IsBrowserOpenQuery() => _isCoBrowserOpen || _coBrowserPopOutWindow != null;
    private void CoBrowser_ReflectionPromptRequested(object? sender, EventArgs e) => InsertCoBrowserReflectionPrompt();

    public void DisposeCoBrowserEvents()
    {
        if (UcHeader != null)
        {
            UcHeader.ToggleCoBrowserClick -= Header_ToggleCoBrowserClick;
            UcHeader.ToggleCoBrowserRightTapped -= Header_ToggleCoBrowserRightTapped;
        }

        if (UcCoBrowser != null)
        {
            UcCoBrowser.CloseRequested -= CoBrowser_CloseRequested;
            UcCoBrowser.PopOutRequested -= CoBrowser_PopOutRequested;
            UcCoBrowser.OpenRequested -= CoBrowser_OpenRequested;
            UcCoBrowser.IsBrowserOpenQuery -= CoBrowser_IsBrowserOpenQuery;
            UcCoBrowser.ReflectionPromptRequested -= CoBrowser_ReflectionPromptRequested;
            UcCoBrowser.AssociatedChatClicked -= CoBrowser_AssociatedChatClicked;
            UcCoBrowser.ConversationActive -= CoBrowser_ConversationActive;
        }
    }

    public async void ToggleCoBrowser(string? initialUrl = null)
    {
        if (_isCoBrowserOpen)
        {
            CloseCoBrowser();
        }
        else
        {
            await OpenCoBrowserAsync(initialUrl);
        }
    }

    public async Task OpenCoBrowserAsync(string? initialUrl = null, string? conversationId = null)
    {
        _isCoBrowserOpen = true;

        // 60% Browser / 40% Terminal
        ColCoBrowser.Width = new GridLength(3, GridUnitType.Star);
        ColMainWorkspace.Width = new GridLength(2, GridUnitType.Star);

        SplitterCoBrowser.Visibility = Visibility.Visible;
        UcCoBrowser.Visibility = Visibility.Visible;

        // Button im Header hervorheben
        if (UcHeader?.BtnCoBrowserToggle != null)
        {
            UcHeader.BtnCoBrowserToggle.Background = (SolidColorBrush)Application.Current.Resources["AgyAccent"];
            if (UcHeader.TxtCoBrowserHeader != null)
            {
                UcHeader.TxtCoBrowserHeader.Foreground = (SolidColorBrush)Application.Current.Resources["AgyTextPrimary"];
            }
        }

        // Chat-Verknüpfung bestimmen:
        // 1. Wenn eine explizite Conversation-ID übergeben wurde (z.B. vom anfordernden Agenten/Tool):
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            CoBrowser_SetAssociatedChatById(conversationId);
        }
        // 2. Ansonsten: Nur wenn NOCH KEINE Bindung existiert (z.B. manueller Button-Klick im UI):
        else if (string.IsNullOrEmpty(UcCoBrowser.AssociatedConversationId))
        {
            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
            if (activeSession != null)
            {
                UcCoBrowser.UpdateAssociatedChat(activeSession.Title, activeSession.ConversationId);
            }
        }

        await UcCoBrowser.EnsureInitializedAsync(initialUrl);
    }

    public void CloseCoBrowser()
    {
        _isCoBrowserOpen = false;

        ColCoBrowser.Width = new GridLength(0);
        ColMainWorkspace.Width = new GridLength(1, GridUnitType.Star);

        SplitterCoBrowser.Visibility = Visibility.Collapsed;
        UcCoBrowser.Visibility = Visibility.Collapsed;

        // Button im Header zurücksetzen
        if (UcHeader?.BtnCoBrowserToggle != null)
        {
            UcHeader.BtnCoBrowserToggle.Background = (SolidColorBrush)Application.Current.Resources["AgyAccentSubtle"];
            if (UcHeader.TxtCoBrowserHeader != null)
            {
                UcHeader.TxtCoBrowserHeader.Foreground = (SolidColorBrush)Application.Current.Resources["AgyTextSecondary"];
            }
        }
    }

    private void Header_ToggleCoBrowserRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        InsertCoBrowserReflectionPrompt();
    }

    private DateTime _lastReflectionPromptTime = DateTime.MinValue;

    public void InsertCoBrowserReflectionPrompt()
    {
        if (DispatcherQueue == null) return;
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => InsertCoBrowserReflectionPrompt());
            return;
        }

        if ((DateTime.UtcNow - _lastReflectionPromptTime).TotalMilliseconds < 350)
        {
            return;
        }
        _lastReflectionPromptTime = DateTime.UtcNow;

        TelemetryLogger.Log("[CoBrowser] Rechtsklick ausgelöst: Reflexions-Prompt wird in TxtChatInput eingesetzt.");

        string activeProjPath = GetActiveTerminalProjectFocus().ProjectPath ?? AppDomain.CurrentDomain.BaseDirectory;
        string prompt = $"Führe anhand des Co-Browser Flight-Recorders (cobrowser_diagnostics mit reset: true) ein 4-Stufen-Audit aller gesammelten Sitzungsdaten durch (Projekt '{activeProjPath}'). Analysiere Latenzen, DOM-Überdeckungen und Fehler, ziehe bei unklaren Hängern die im Log hinterlegten Chat-Transcripts heran und setze das Protokoll für den nächsten Zyklus zurück. Welche C#-Core-Fixes leiten sich ab?";

        if (TxtChatInput != null)
        {
            TxtChatInput.Text = prompt;
            TxtChatInput.Focus(FocusState.Programmatic);
            TxtChatInput.SelectionStart = TxtChatInput.Text.Length;
        }
    }

    private void SplitterCoBrowser_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement border)
        {
            border.CapturePointer(e.Pointer);
            _isSplitterDragging = true;
            _splitterStartX = e.GetCurrentPoint(this.Content).Position.X;
            _splitterStartWidth = ColCoBrowser.ActualWidth;
            e.Handled = true;
        }
    }

    private void SplitterCoBrowser_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isSplitterDragging && this.Content != null)
        {
            double currentX = e.GetCurrentPoint(this.Content).Position.X;
            double delta = currentX - _splitterStartX;
            double totalWidth = this.Content.ActualSize.X - ColSidebar.ActualWidth;
            double newWidth = Math.Max(280, Math.Min(totalWidth - 320, _splitterStartWidth - delta));

            ColCoBrowser.Width = new GridLength(newWidth, GridUnitType.Pixel);
            ColMainWorkspace.Width = new GridLength(1, GridUnitType.Star);
            e.Handled = true;
        }
    }

    private void SplitterCoBrowser_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isSplitterDragging && sender is UIElement border)
        {
            border.ReleasePointerCapture(e.Pointer);
            _isSplitterDragging = false;
            e.Handled = true;
        }
    }

    private void PopOutCoBrowser()
    {
        try
        {
            if (_coBrowserPopOutWindow != null)
            {
                _coBrowserPopOutWindow.Activate();
                return;
            }

            // Aktuelle URL vor dem Schließen der eingebetteten Ansicht sichern
            string? currentUrl = UcCoBrowser?.CurrentUrl;

            // Pop-out Window erstellen
            var popOutWin = new Window();
            popOutWin.Title = "Antigravity KI-Co-Browser (Pop-out)";
            
            var popOutView = new Views.CoBrowserView();
            popOutView.IsPopOutMode = true;
            if (UcCoBrowser != null)
            {
                popOutView.CurrentZoomFactor = UcCoBrowser.CurrentZoomFactor;
                popOutView.IsAutoFitEnabled = UcCoBrowser.IsAutoFitEnabled;
            }
            popOutWin.Content = popOutView;

            popOutWin.Closed += (s, e) =>
            {
                try
                {
                    string? lastUrl = popOutView.CurrentUrl;
                    if (UcCoBrowser != null)
                    {
                        UcCoBrowser.CurrentZoomFactor = popOutView.CurrentZoomFactor;
                        UcCoBrowser.IsAutoFitEnabled = popOutView.IsAutoFitEnabled;
                    }
                    if (!string.IsNullOrWhiteSpace(lastUrl) && lastUrl != "about:blank" && UcCoBrowser?.IsInitialized == true)
                    {
                        UcCoBrowser.Navigate(lastUrl);
                    }
                    popOutView.Dispose();
                }
                catch { }
                _coBrowserPopOutWindow = null;

                // Nach Schließen des Pop-Outs Discovery-Datei wieder auf die Haupt-View ausrichten
                if (UcCoBrowser?.IsInitialized == true)
                {
                    UcCoBrowser.SaveEndpointDiscovery();
                }
            };

            popOutView.CloseRequested += (s, e) => popOutWin.Close();
            popOutView.OpenRequested += (s, e) =>
            {
                popOutWin.Activate();
                if (!string.IsNullOrWhiteSpace(e.ConversationId))
                {
                    CoBrowser_SetAssociatedChatById(e.ConversationId);
                }
                if (!string.IsNullOrWhiteSpace(e.Url))
                {
                    _ = popOutView.EnsureInitializedAsync(e.Url);
                }
            };
            popOutView.IsBrowserOpenQuery += () => _coBrowserPopOutWindow != null;
            popOutView.ReflectionPromptRequested += (s, e) => InsertCoBrowserReflectionPrompt();
            popOutView.AssociatedChatClicked += (s, convId) => CoBrowser_AssociatedChatClicked(s, convId);
            popOutView.ConversationActive += (s, convId) => CoBrowser_ConversationActive(s, convId);

            string? currentAssocId = UcCoBrowser?.AssociatedConversationId;
            string? currentAssocTitle = UcCoBrowser?.AssociatedChatTitle;
            if (!string.IsNullOrEmpty(currentAssocId))
            {
                popOutView.UpdateAssociatedChat(currentAssocTitle, currentAssocId);
            }
            else
            {
                var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
                if (activeSession != null)
                {
                    popOutView.UpdateAssociatedChat(activeSession.Title, activeSession.ConversationId);
                }
            }

            _coBrowserPopOutWindow = popOutWin;
            popOutWin.Activate();

            // Eingebettetes Panel einklappen, damit der Workspace volle Breite erhält
            if (_isCoBrowserOpen)
            {
                CloseCoBrowser();
            }

            _ = popOutView.EnsureInitializedAsync(currentUrl);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PopOutCoBrowser Error] {ex.Message}");
        }
    }

    private void CoBrowser_AssociatedChatClicked(object? sender, string convId)
    {
        try { this.Activate(); } catch { }

        if (string.IsNullOrEmpty(convId))
        {
            if (!string.IsNullOrEmpty(_activeTerminalId))
            {
                SwitchToTerminalTab(_activeTerminalId);
            }
            return;
        }

        var matchingSession = _terminalSessions.FirstOrDefault(t =>
            string.Equals(t.ConversationId, convId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Id, convId, StringComparison.OrdinalIgnoreCase));

        if (matchingSession != null)
        {
            SwitchToTerminalTab(matchingSession.Id);
        }
        else
        {
            AgyChatSession? chat = null;
            lock (_allMasterChats)
            {
                chat = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase));
            }
            chat ??= _chats.FirstOrDefault(c => string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase));

            if (chat != null)
            {
                _ = OpenChatWithAgent(chat, convId, "agy");
            }
            else if (!string.IsNullOrEmpty(_activeTerminalId))
            {
                SwitchToTerminalTab(_activeTerminalId);
            }
        }
    }

    private void CoBrowser_SetAssociatedChatById(string convId)
    {
        if (string.IsNullOrWhiteSpace(convId)) return;

        string chatTitle = string.Empty;
        var matchingSession = _terminalSessions.FirstOrDefault(t =>
            string.Equals(t.ConversationId, convId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Id, convId, StringComparison.OrdinalIgnoreCase));

        if (matchingSession != null && !string.IsNullOrWhiteSpace(matchingSession.Title))
        {
            chatTitle = matchingSession.Title;
        }
        else
        {
            lock (_allMasterChats)
            {
                var master = _allMasterChats.FirstOrDefault(c =>
                    string.Equals(c.Id, convId, StringComparison.OrdinalIgnoreCase));
                if (master != null && !string.IsNullOrWhiteSpace(master.Title))
                {
                    chatTitle = master.Title;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(chatTitle))
        {
            chatTitle = convId.Length > 8 ? $"Chat ({convId[..8]}...)" : $"Chat ({convId})";
        }

        UcCoBrowser?.UpdateAssociatedChat(chatTitle, convId);
        if (_coBrowserPopOutWindow?.Content is Views.CoBrowserView popOutView)
        {
            popOutView.UpdateAssociatedChat(chatTitle, convId);
        }
    }

    private void CoBrowser_ConversationActive(object? sender, string convId)
    {
        if (DispatcherQueue == null) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                CoBrowser_SetAssociatedChatById(convId);
            }
            catch { }
        });
    }

    public void UpdateAssociatedCoBrowserChat()
    {
        var popOutView = _coBrowserPopOutWindow?.Content as Views.CoBrowserView;
        if ((UcCoBrowser == null || !_isCoBrowserOpen) && popOutView == null) return;

        string? currentAssociatedId = UcCoBrowser?.AssociatedConversationId ?? popOutView?.AssociatedConversationId;

        // Wenn noch überhaupt keine Verknüpfung existiert (z.B. nach frischem App-Start)
        if (string.IsNullOrEmpty(currentAssociatedId))
        {
            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
            if (activeSession != null)
            {
                UcCoBrowser?.UpdateAssociatedChat(activeSession.Title, activeSession.ConversationId);
                popOutView?.UpdateAssociatedChat(activeSession.Title, activeSession.ConversationId);
            }
            return;
        }

        // Suche in aktiven Workspace-Sessions
        var matchingSession = _terminalSessions.FirstOrDefault(t =>
            string.Equals(t.ConversationId, currentAssociatedId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.Id, currentAssociatedId, StringComparison.OrdinalIgnoreCase));

        if (matchingSession != null)
        {
            // Tab existiert im Workspace: Titel synchronisieren, falls er umbenannt wurde
            if (!string.IsNullOrWhiteSpace(matchingSession.Title))
            {
                UcCoBrowser?.UpdateAssociatedChat(matchingSession.Title, matchingSession.ConversationId ?? currentAssociatedId);
                popOutView?.UpdateAssociatedChat(matchingSession.Title, matchingSession.ConversationId ?? currentAssociatedId);
            }
            return;
        }

        // Falls nicht in offenen Tabs: In Master-Chats prüfen
        lock (_allMasterChats)
        {
            var master = _allMasterChats.FirstOrDefault(c =>
                string.Equals(c.Id, currentAssociatedId, StringComparison.OrdinalIgnoreCase));
            if (master != null && !string.IsNullOrWhiteSpace(master.Title))
            {
                // Chat existiert in Historie: Titel synchron halten, Zuordnung bleibt stabil!
                UcCoBrowser?.UpdateAssociatedChat(master.Title, currentAssociatedId);
                popOutView?.UpdateAssociatedChat(master.Title, currentAssociatedId);
                return;
            }
        }

        // Hinweis: Ein einfacher Tab-Wechsel im Terminal darf die Bindung an den Chat NIEMALS
        // auf den aktuell fokussierten Tab umbiegen!
        // Nur wenn alle Workspace-Sessions geschlossen wurden:
        if (_terminalSessions.Count == 0)
        {
            UcCoBrowser?.UpdateAssociatedChat("Kein Chat verbunden", null);
            popOutView?.UpdateAssociatedChat("Kein Chat verbunden", null);
        }
    }
}
