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
    private void BtnAddGlobalRuleFile_Click(object sender, RoutedEventArgs e) => AddGlobalRuleFileClick?.Invoke(sender, e);

    private void BtnToggleGlobalInjectionsCard_Click(object sender, RoutedEventArgs e) => ToggleGlobalInjectionsCardClick?.Invoke(sender, e);
    private void BtnAddGlobalInjection_Click(object sender, RoutedEventArgs e) => AddGlobalInjectionClick?.Invoke(sender, e);

    private void BtnToggleProjectInjectionsCard_Click(object sender, RoutedEventArgs e) => ToggleProjectInjectionsCardClick?.Invoke(sender, e);
    private void BtnAddProjectInjection_Click(object sender, RoutedEventArgs e) => AddProjectInjectionClick?.Invoke(sender, e);
}
