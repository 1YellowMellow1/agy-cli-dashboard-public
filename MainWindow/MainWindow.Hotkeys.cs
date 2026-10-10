using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    private const int HOTKEY_ID_COPY_TERMINAL = 0x0001;
    private const int VK_CONTROL_KEY = 0x11;
    private const int VK_SHIFT_KEY = 0x10;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_C = 0x43;
    private const uint VK_RETURN = 0x0D;
    private const int WH_KEYBOARD = 2;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint WM_NCRBUTTONUP = 0x00A5;
    private const int GWLP_WNDPROC = -4;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private bool _isCopyHotkeyRegistered = false;
    private long _lastCopyTerminalTimestamp = 0;

    private IntPtr _hKeyboardHook = IntPtr.Zero;
    private HookProc? _keyboardHookDelegate;

    private static readonly Regex AnsiCsiRegex = new(@"\x1B\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);
    private static readonly Regex AnsiOscRegex = new(@"\x1B\][^\x07]*\x07", RegexOptions.Compiled);
    private static readonly Regex AnsiEscRegex = new(@"\x1B[()][A-Z0-9]", RegexOptions.Compiled);

    private IntPtr _oldWndProc = IntPtr.Zero;
    private WndProcDelegate? _wndProcDelegate;

    /// <summary>
    /// Registriert einen thread-lokalen Keyboard-Hook, um Tastatur-Events (insb. Enter)
    /// auch dann zuverlässig zu erkennen, wenn das native EasyTerminalControl (HWND) den Fokus hat.
    /// Dadurch kann ein aktives Sprachdiktat per Enter beendet, abgeschickt und der Cursor ins
    /// Chat-Eingabefeld zurückgesetzt werden.
    /// </summary>
    private void RegisterTerminalKeyboardHook()
    {
        if (_hKeyboardHook != IntPtr.Zero) return;
        try
        {
            _keyboardHookDelegate = new HookProc(TerminalKeyboardHookProc);
            uint threadId = GetCurrentThreadId();
            _hKeyboardHook = SetWindowsHookEx(WH_KEYBOARD, _keyboardHookDelegate, IntPtr.Zero, threadId);
            TelemetryLogger.Log($"[RegisterTerminalKeyboardHook] Hook installed = {_hKeyboardHook != IntPtr.Zero}, threadId = {threadId}");
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[RegisterTerminalKeyboardHook ERROR] {ex.Message}");
        }
    }

    private IntPtr TerminalKeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = wParam.ToInt32();
            bool isKeyUp = ((lParam.ToInt64() >> 31) & 1) != 0;

            if (vkCode == (int)VK_RETURN && !isKeyUp)
            {
                if (_isVoiceRecordingActive || _isVoicePendingInTerminal)
                {
                    try
                    {
                        var focused = FocusManager.GetFocusedElement(this.Content?.XamlRoot);
                        if (object.ReferenceEquals(focused, TxtChatInput))
                        {
                            // Wenn der Fokus im Chat-Eingabefeld liegt, Event nicht schlucken,
                            // damit TxtChatInput_KeyDown / TxtChatInput_KeyUp / SendPromptToTerminal normal feuern:
                            return CallNextHookEx(_hKeyboardHook, nCode, wParam, lParam);
                        }
                    }
                    catch { }

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        HandleVoiceSubmissionOnEnter();
                    });
                    return (IntPtr)1;
                }
            }

            // Fallback für Strg + Shift + C:
            // Fängt Tastendruck auch dann zuverlässig ab, wenn Win32 RegisterHotKey blockiert war
            // (z.B. durch Mehrfachstart, Vorinstanz-Races oder externe Software wie Radeon/DevTools).
            if (vkCode == (int)VK_C && !isKeyUp)
            {
                bool isCtrl = (GetKeyState(VK_CONTROL_KEY) & 0x8000) != 0;
                bool isShift = (GetKeyState(VK_SHIFT_KEY) & 0x8000) != 0;

                if (isCtrl && isShift)
                {
                    bool isTextEditorFocused = false;
                    try
                    {
                        var focused = FocusManager.GetFocusedElement(this.Content?.XamlRoot);
                        if (focused is TextBox tb && tb.SelectionLength > 0)
                        {
                            isTextEditorFocused = true;
                        }
                    }
                    catch { }

                    if (!isTextEditorFocused)
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            CopyTerminalOutputToClipboard();
                        });
                        return (IntPtr)1;
                    }
                }
                else if (isCtrl && !isShift)
                {
                    // Intelligentes Strg + C:
                    // Wenn Text im Terminal markiert ist: kopieren und Keystroke schlucken (kein SIGINT).
                    // Wenn KEIN Text markiert ist: Keystroke nicht schlucken, damit normaler Befehlsabbruch (SIGINT) feuert!
                    bool isTextEditorFocused = false;
                    try
                    {
                        var focused = FocusManager.GetFocusedElement(this.Content?.XamlRoot);
                        if (focused is TextBox)
                        {
                            isTextEditorFocused = true;
                        }
                    }
                    catch { }

                    if (!isTextEditorFocused && ActiveTerminal != null)
                    {
                        bool copied = false;
                        try
                        {
                            copied = TryCopyTerminalSelection(ActiveTerminal);
                        }
                        catch (Exception ex)
                        {
                            TelemetryLogger.Log($"[TerminalKeyboardHookProc Ctrl+C Error] {ex.Message}");
                        }

                        if (copied)
                        {
                            ShowNotice("Auswahl kopiert", "Markierter Text in die Zwischenablage kopiert.", InfoBarSeverity.Success);
                            return (IntPtr)1; // Konsumiere Event: Text wurde kopiert, laufenden Prozess nicht abbrechen!
                        }
                        // Wenn keine Auswahl vorhanden ist: Nicht konsumieren -> CallNextHookEx leitet Strg+C normal für SIGINT durch.
                    }
                }
            }
        }
        return CallNextHookEx(_hKeyboardHook, nCode, wParam, lParam);
    }

    private void BtnCopyTerminal_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnCopyTerminal_Click)} triggered.");
        CopyTerminalOutputToClipboard();
    }

    /// <summary>
    /// Registriert Ctrl+Shift+C als globalen Fenster-Hotkey via Win32 RegisterHotKey.
    /// Dies umgeht das HWND-Airspace-Problem des Terminal-Controls, bei dem WinUI-Events
    /// nicht feuern, weil die native Terminal-HWND alle Tastatur-Eingaben direkt abfängt.
    /// Deregistriert zuerst eventuell vorhandene Altregistrierungen (z.B. von einer
    /// vorherigen Instanz, die nicht sauber beendet wurde) und versucht bei Fehlschlag
    /// bis zu 3 Retries mit kurzer Verzögerung.
    /// </summary>
    private void RegisterCopyTerminalHotkey()
    {
        if (_hwnd == IntPtr.Zero) return;
        try
        {
            if (_oldWndProc == IntPtr.Zero)
            {
                _wndProcDelegate = new WndProcDelegate(HotkeyWndProc);
                _oldWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC,
                    Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
            }

            // Cleanup: Alte Registrierung entfernen (von vorheriger Instanz oder erneutem Aufruf)
            try { UnregisterHotKey(_hwnd, HOTKEY_ID_COPY_TERMINAL); } catch { }

            bool registered = RegisterHotKey(_hwnd, HOTKEY_ID_COPY_TERMINAL, MOD_CONTROL | MOD_SHIFT, VK_C);
            _isCopyHotkeyRegistered = registered;
            TelemetryLogger.Log($"[RegisterCopyTerminalHotkey] RegisterHotKey Ctrl+Shift+C = {registered}");

            if (!registered)
            {
                // Retry: Alte Instanz hält den Hotkey möglicherweise noch kurz (asynchron, um UI-Thread nicht zu blockieren)
                _ = Task.Run(async () =>
                {
                    const int maxRetries = 5;
                    const int retryDelayMs = 250;
                    for (int i = 1; i <= maxRetries; i++)
                    {
                        await Task.Delay(retryDelayMs);
                        var tcs = new TaskCompletionSource<bool>();
                        bool enqueued = DispatcherQueue != null && DispatcherQueue.TryEnqueue(() =>
                        {
                            if (_hwnd == IntPtr.Zero) { tcs.TrySetResult(false); return; }
                            try { UnregisterHotKey(_hwnd, HOTKEY_ID_COPY_TERMINAL); } catch { }
                            bool ok = RegisterHotKey(_hwnd, HOTKEY_ID_COPY_TERMINAL, MOD_CONTROL | MOD_SHIFT, VK_C);
                            _isCopyHotkeyRegistered = ok;
                            TelemetryLogger.Log($"[RegisterCopyTerminalHotkey] Retry {i}/{maxRetries}: RegisterHotKey Ctrl+Shift+C = {ok}");
                            tcs.TrySetResult(ok);
                        });
                        if (!enqueued) break;
                        bool reg = await tcs.Task;
                        if (reg) break;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[RegisterCopyTerminalHotkey ERROR] {ex.Message}");
        }
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated && !_isCopyHotkeyRegistered)
        {
            RegisterCopyTerminalHotkey();
        }
    }

    private IntPtr HotkeyWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_COPY_TERMINAL)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                CopyTerminalOutputToClipboard();
            });
            return IntPtr.Zero;
        }

        if (msg == WM_NCRBUTTONUP || msg == WM_CONTEXTMENU)
        {
            try
            {
                int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
                int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

                if (screenX != -1 || screenY != -1)
                {
                    // Wenn Rechtsklick über Co-Browser Toggle Button im Header war:
                    if (IsScreenPointOverElement(UcHeader?.BtnCoBrowserToggle, screenX, screenY))
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            InsertCoBrowserReflectionPrompt();
                        });
                        // Verhindert das Aufpoppen des Win32-Systemmenüs (Wiederherstellen, Verschieben, etc.)
                        return IntPtr.Zero;
                    }

                    // Systemmenü auch für andere Header-Buttons strikt unterdrücken
                    if (IsScreenPointOverElement(UcHeader?.BtnCheckUpdate, screenX, screenY) ||
                        IsScreenPointOverElement(UcHeader?.BtnGeminiModelStatus, screenX, screenY))
                    {
                        return IntPtr.Zero;
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[HotkeyWndProc ContextMenu Intercept ERROR] {ex.Message}");
            }
        }

        return _oldWndProc != IntPtr.Zero
            ? CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam)
            : IntPtr.Zero;
    }

    private bool IsScreenPointOverElement(FrameworkElement? element, int screenX, int screenY)
    {
        if (element == null || !element.IsLoaded || element.Visibility != Visibility.Visible || _hwnd == IntPtr.Zero)
            return false;

        var pt = new POINT { X = screenX, Y = screenY };
        if (!ScreenToClient(_hwnd, ref pt))
            return false;

        try
        {
            double scale = element.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0) scale = 1.0;

            double dipX = pt.X / scale;
            double dipY = pt.Y / scale;

            var transform = element.TransformToVisual(null);
            var bounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return bounds.Contains(new Windows.Foundation.Point(dipX, dipY));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Kopiert markierten Text aus dem Terminal in die Zwischenablage.
    /// Versucht zuerst GetSelectedText() auf dem inneren TerminalControl.
    /// Fällt nur auf den gesamten ConsoleOutputLog zurück wenn keine Auswahl besteht.
    /// </summary>
    private void CopyTerminalOutputToClipboard()
    {
        long now = Environment.TickCount64;
        if (now - _lastCopyTerminalTimestamp < 350)
        {
            return;
        }
        _lastCopyTerminalTimestamp = now;

        try
        {
            var terminal = ActiveTerminal;
            if (terminal == null)
            {
                ShowNotice("Kein Terminal", "Kein aktives Terminal zum Kopieren.", InfoBarSeverity.Warning);
                return;
            }

            // 1. Versuch: Markierten Text über GetSelectedText() holen
            string? selectedText = null;
            try
            {
                var innerTerminal = terminal.Terminal;
                if (innerTerminal != null)
                {
                    var getSelectedTextMethod = innerTerminal.GetType().GetMethod("GetSelectedText",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (getSelectedTextMethod != null)
                    {
                        selectedText = getSelectedTextMethod.Invoke(innerTerminal, null) as string;
                        TelemetryLogger.Log($"[CopyTerminal] GetSelectedText() returned {selectedText?.Length ?? -1} chars");
                    }
                }
            }
            catch (Exception ex)
            {
                TelemetryLogger.Log($"[CopyTerminal] GetSelectedText() failed: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(selectedText))
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(selectedText);
                Clipboard.SetContent(dataPackage);
                ShowNotice("Erfolgreich kopiert", $"Markierter Text kopiert ({selectedText.Length} Zeichen)", InfoBarSeverity.Success);
                return;
            }

            // 2. Fallback: Gesamten Terminal-Output kopieren
            if (terminal.ConPTYTerm != null)
            {
                var prop = terminal.ConPTYTerm.GetType().GetProperty("ConsoleOutputLog");
                if (prop != null)
                {
                    var sb = prop.GetValue(terminal.ConPTYTerm) as StringBuilder;
                    if (sb != null)
                    {
                        string text;
                        lock (sb)
                        {
                            text = sb.ToString();
                        }
                        text = AnsiCsiRegex.Replace(text, string.Empty);
                        text = AnsiOscRegex.Replace(text, string.Empty);
                        text = AnsiEscRegex.Replace(text, string.Empty);
                        text = text.Trim();

                        if (!string.IsNullOrEmpty(text))
                        {
                            var dataPackage = new DataPackage();
                            dataPackage.SetText(text);
                            Clipboard.SetContent(dataPackage);
                            ShowNotice("Erfolgreich kopiert", "Kein Text markiert -> gesamter Terminal-Inhalt kopiert", InfoBarSeverity.Success);
                        }
                        else
                        {
                            ShowNotice("Leer", "Kein Terminal-Inhalt zum Kopieren vorhanden.", InfoBarSeverity.Warning);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[CopyTerminalOutputToClipboard ERROR] {ex.Message}");
            ShowNotice("Fehler", "Fehler beim Kopieren: " + ex.Message, InfoBarSeverity.Error);
        }
    }
}
