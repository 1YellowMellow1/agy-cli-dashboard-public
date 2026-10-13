using Microsoft.UI.Xaml.Controls;

namespace AgyCliDashboard.Views;

public sealed partial class GitSidebarView : UserControl
{
    public GitSidebarView()
    {
        this.InitializeComponent();
    }

    public void ApplyLocalization()
    {
        if (TxtGitHeader != null)
        {
            TxtGitHeader.Text = Helpers.Loc.Tr("Sidebars.Git.Header", "GIT REPOSITORY & HISTORIE");
        }
        if (TxtGitEmptySelection != null)
        {
            TxtGitEmptySelection.Text = Helpers.Loc.Tr("Sidebars.Git.EmptySelection", "Bitte wähle oben ein konkretes Projekt aus, um dessen Git-Status anzuzeigen.");
        }
    }
}
