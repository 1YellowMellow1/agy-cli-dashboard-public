using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AgyCliDashboard.Views;

public sealed partial class DocsSidebarView : UserControl
{
    public event RoutedEventHandler? CancelAddInjectionClick;
    public event RoutedEventHandler? SaveNewInjectionClick;

    public event RoutedEventHandler? ToggleProjectResourcesCardClick;
    public event RoutedEventHandler? AddProjectDocClick;
    public event RoutedEventHandler? AddProjectRuleFileClick;
    public event RoutedEventHandler? OpenProjectProtocolClick;

    public event RoutedEventHandler? ToggleGlobalResourcesCardClick;
    public event RoutedEventHandler? AddGlobalKnowledgeClick;
    public event RoutedEventHandler? ImportGlobalKnowledgeClick;
    public event RoutedEventHandler? ExportGlobalKnowledgeClick;
    public event RoutedEventHandler? AddGlobalRuleFileClick;

    public event RoutedEventHandler? ToggleGlobalInjectionsCardClick;
    public event RoutedEventHandler? AddGlobalInjectionClick;

    public event RoutedEventHandler? ToggleProjectInjectionsCardClick;
    public event RoutedEventHandler? AddProjectInjectionClick;

    public DocsSidebarView()
    {
        this.InitializeComponent();
    }

    private void BtnCancelAddInjection_Click(object sender, RoutedEventArgs e) => CancelAddInjectionClick?.Invoke(sender, e);
    private void BtnSaveNewInjection_Click(object sender, RoutedEventArgs e) => SaveNewInjectionClick?.Invoke(sender, e);

    private void BtnToggleProjectResourcesCard_Click(object sender, RoutedEventArgs e) => ToggleProjectResourcesCardClick?.Invoke(sender, e);
    private void BtnAddProjectDoc_Click(object sender, RoutedEventArgs e) => AddProjectDocClick?.Invoke(sender, e);
    private void BtnAddProjectRuleFile_Click(object sender, RoutedEventArgs e) => AddProjectRuleFileClick?.Invoke(sender, e);
    private void BtnOpenProjectProtocol_Click(object sender, RoutedEventArgs e) => OpenProjectProtocolClick?.Invoke(sender, e);

    private void BtnToggleGlobalResourcesCard_Click(object sender, RoutedEventArgs e) => ToggleGlobalResourcesCardClick?.Invoke(sender, e);
    private void BtnAddGlobalKnowledge_Click(object sender, RoutedEventArgs e) => AddGlobalKnowledgeClick?.Invoke(sender, e);
    private void BtnImportGlobalKnowledge_Click(object sender, RoutedEventArgs e) => ImportGlobalKnowledgeClick?.Invoke(sender, e);
    private void BtnExportGlobalKnowledge_Click(object sender, RoutedEventArgs e) => ExportGlobalKnowledgeClick?.Invoke(sender, e);
    private void BtnAddGlobalRuleFile_Click(object sender, RoutedEventArgs e) => AddGlobalRuleFileClick?.Invoke(sender, e);

    private void BtnToggleGlobalInjectionsCard_Click(object sender, RoutedEventArgs e) => ToggleGlobalInjectionsCardClick?.Invoke(sender, e);
    private void BtnAddGlobalInjection_Click(object sender, RoutedEventArgs e) => AddGlobalInjectionClick?.Invoke(sender, e);

    private void BtnToggleProjectInjectionsCard_Click(object sender, RoutedEventArgs e) => ToggleProjectInjectionsCardClick?.Invoke(sender, e);
    private void BtnAddProjectInjection_Click(object sender, RoutedEventArgs e) => AddProjectInjectionClick?.Invoke(sender, e);

    public void ApplyLocalization()
    {
        if (TxtDocsHeader != null)
        {
            TxtDocsHeader.Text = Helpers.Loc.Tr("Sidebars.Docs.Header", "REGEL-STEUERUNG & WISSEN");
        }
        if (TxtAddInjectionTitle != null)
        {
            TxtAddInjectionTitle.Text = Helpers.Loc.Tr("Sidebars.Docs.AddRuleTitle", "NEUE REGEL HINZUFÜGEN");
        }
        if (TxtNewInjectionNameLabel != null)
        {
            TxtNewInjectionNameLabel.Text = Helpers.Loc.Tr("Sidebars.Docs.RuleNameLabel", "Name (z. B. TypeScript Strict)");
        }
        if (TxtNewInjectionName != null)
        {
            TxtNewInjectionName.PlaceholderText = Helpers.Loc.Tr("Sidebars.Docs.RuleNamePlaceholder", "Kurzname...");
        }
        if (TxtNewInjectionContentLabel != null)
        {
            TxtNewInjectionContentLabel.Text = Helpers.Loc.Tr("Sidebars.Docs.RuleContentLabel", "Regeltext");
        }
        if (TxtNewInjectionContent != null)
        {
            TxtNewInjectionContent.PlaceholderText = Helpers.Loc.Tr("Sidebars.Docs.RuleContentPlaceholder", "Anweisung für die KI...");
        }
        if (BtnCancelAddInjection != null)
        {
            BtnCancelAddInjection.Content = Helpers.Loc.Tr("Sidebars.Docs.Cancel", "Abbrechen");
            ToolTipService.SetToolTip(BtnCancelAddInjection, Helpers.Loc.Tr("Sidebars.Docs.CancelTooltip", "Regel-Erstellung abbrechen (Esc)"));
        }
        if (BtnSaveNewInjection != null)
        {
            BtnSaveNewInjection.Content = Helpers.Loc.Tr("Sidebars.Docs.Save", "Speichern");
            ToolTipService.SetToolTip(BtnSaveNewInjection, Helpers.Loc.Tr("Sidebars.Docs.SaveTooltip", "Regel speichern (Strg+Enter)"));
        }
        if (BtnToggleProjectResourcesCard != null)
        {
            ToolTipService.SetToolTip(BtnToggleProjectResourcesCard, Helpers.Loc.Tr("Sidebars.Docs.ToggleProjectResourcesTooltip", "Projekt-Ressourcen ein- oder ausklappen"));
        }
        if (TxtProjectDocsHeader != null)
        {
            TxtProjectDocsHeader.Text = Helpers.Loc.Tr("Sidebars.Docs.ProjectResourcesHeader", "PROJEKT RESSOURCEN");
        }
        if (TxtProjectDocsSubHeader != null)
        {
            TxtProjectDocsSubHeader.Text = Helpers.Loc.Tr("Sidebars.Docs.ProjectDocsHeader", "WISSEN & DOKUMENTE");
        }
        if (BtnAddProjectDoc != null)
        {
            ToolTipService.SetToolTip(BtnAddProjectDoc, Helpers.Loc.Tr("Sidebars.Docs.AddProjectDocTooltip", "Neue Wissensdatei (.md) in docs/ erstellen"));
        }
        if (BtnImportGlobalKnowledge != null)
        {
            ToolTipService.SetToolTip(BtnImportGlobalKnowledge, Helpers.Loc.Tr("Sidebars.Docs.ImportGlobalKnowledgeTooltip", "Wissen (.md) importieren"));
        }
        if (BtnExportGlobalKnowledge != null)
        {
            ToolTipService.SetToolTip(BtnExportGlobalKnowledge, Helpers.Loc.Tr("Sidebars.Docs.ExportGlobalKnowledgeTooltip", "Wissen exportieren (Backup / Sharing)"));
        }
    }
}
