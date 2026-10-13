using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace AgyCliDashboard;

/// <summary>
/// Schmaler Ziehgriff mit "Größe ändern"-Mauszeiger (SizeWestEast) für die leichtgewichtigen Splitter.
/// WinUI 3 bringt kein GridSplitter-Control mit, <c>Border</c> ist versiegelt und
/// <c>UIElement.ProtectedCursor</c> ist nur in Unterklassen setzbar - daher dieses minimale UserControl.
/// Inhalt (z. B. eine 1px-Trennlinie) kann wie bei einem Border als Kind angegeben werden.
/// </summary>
public sealed class ResizeHandle : UserControl
{
    public ResizeHandle()
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        // Kein Fokus-/Tab-Verhalten für einen reinen Maus-Griff
        IsTabStop = false;
    }
}
