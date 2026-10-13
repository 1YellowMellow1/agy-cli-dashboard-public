using Microsoft.UI.Xaml.Controls;

namespace AgyCliDashboard.Views;

public sealed partial class FilesSidebarView : UserControl
{
    public FilesSidebarView()
    {
        this.InitializeComponent();
    }

    public void ApplyLocalization()
    {
        if (TxtFilesHeader != null)
        {
            TxtFilesHeader.Text = Helpers.Loc.Tr("Sidebars.Files.Header", "PROJEKT-DATEIEN");
        }
        if (TxtFileSearch != null)
        {
            TxtFileSearch.PlaceholderText = Helpers.Loc.Tr("Sidebars.Files.SearchPlaceholder", "Dateien durchsuchen... (Esc)");
        }
        if (BtnClearFileSearch != null)
        {
            ToolTipService.SetToolTip(BtnClearFileSearch, Helpers.Loc.Tr("Sidebars.Files.ClearSearchTooltip", "Suche leeren (Esc)"));
        }
        if (TxtFilesEmptySelection != null)
        {
            TxtFilesEmptySelection.Text = Helpers.Loc.Tr("Sidebars.Files.EmptySelection", "Bitte wähle oben ein konkretes Projekt aus, um dessen Dateien anzuzeigen.");
        }
    }
}
