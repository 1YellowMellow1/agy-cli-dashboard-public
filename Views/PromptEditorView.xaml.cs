using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AgyCliDashboard.Views;

public sealed partial class PromptEditorView : UserControl
{
    // Public Events for Parent (MainWindow) Hookup
    public event RoutedEventHandler? AttachFilesClick;
    public event RoutedEventHandler? ModelSelectClick;

    public event RoutedEventHandler? DeepThinkClick;
    public event RightTappedEventHandler? DeepThinkRightTapped;

    public event RoutedEventHandler? ProjectDocsClick;
    public event RightTappedEventHandler? ProjectDocsRightTapped;

    public event RoutedEventHandler? GlobalErrorCheckClick;
    public event RightTappedEventHandler? GlobalErrorCheckRightTapped;

    public event RoutedEventHandler? SubagentClick;
    public event RightTappedEventHandler? SubagentRightTapped;

    public event RoutedEventHandler? WebResearchClick;
    public event RightTappedEventHandler? WebResearchRightTapped;


    public event RoutedEventHandler? ReflectionClick;
    public event RightTappedEventHandler? ReflectionRightTapped;

    public event RoutedEventHandler? E2ETestClick;
    public event RightTappedEventHandler? E2ETestRightTapped;

    public event RoutedEventHandler? DeadCodeCheckClick;
    public event RightTappedEventHandler? DeadCodeCheckRightTapped;

    public event RoutedEventHandler? IrrwegCleanupClick;
    public event RightTappedEventHandler? IrrwegCleanupRightTapped;

    public event RoutedEventHandler? SimplifyClick;
    public event RightTappedEventHandler? SimplifyRightTapped;

    public event RoutedEventHandler? ZeroBugClick;
    public event RightTappedEventHandler? ZeroBugRightTapped;

    public event RoutedEventHandler? DauTestClick;
    public event RightTappedEventHandler? DauTestRightTapped;

    public event RoutedEventHandler? CustomPromptClick;
    public event RightTappedEventHandler? CustomPromptRightTapped;

    public event RoutedEventHandler? GitStatusBadgeClick;
    public event RoutedEventHandler? ProtocolBadgeClick;
    public event RoutedEventHandler? KnowledgeBadgeClick;

    public event RoutedEventHandler? UndoClick;
    public event RoutedEventHandler? AutoReflexionClick;
    public event RightTappedEventHandler? AutoReflexionRightTapped;

    public event RoutedEventHandler? RemoveAttachmentClick;
    public event DragEventHandler? ChatInputDragOver;
    public event DragEventHandler? ChatInputDrop;
    public event EventHandler<TextControlPasteEventArgs>? ChatInputPaste;
    public event KeyEventHandler? ChatInputKeyDown;
    public event KeyEventHandler? ChatInputKeyUp;
    public event TextChangedEventHandler? ChatInputTextChanged;
    public event RoutedEventHandler? ChatInputLoaded;

    public event RoutedEventHandler? VoiceClick;
    public event RoutedEventHandler? SendMessageClick;
    public event PointerEventHandler? SendMessagePointerPressed;
    public event PointerEventHandler? SendMessagePointerReleased;
    public event PointerEventHandler? SendMessagePointerExited;

    public event RoutedEventHandler? CancelCommandClick;

    public PromptEditorView()
    {
        this.InitializeComponent();
        ApplyLocalization();
    }

    public void ApplyLocalization()
    {
        if (TxtChatInput != null)
        {
            TxtChatInput.PlaceholderText = AgyCliDashboard.Helpers.Loc.Tr("Chat.Placeholder");
        }
        if (BtnSendMessage != null)
        {
            ToolTipService.SetToolTip(BtnSendMessage, AgyCliDashboard.Helpers.Loc.Tr("Chat.SendTooltip"));
        }
        if (BtnCancelCommand != null)
        {
            ToolTipService.SetToolTip(BtnCancelCommand, AgyCliDashboard.Helpers.Loc.Tr("Chat.CancelTooltip"));
        }
        if (BtnVoice != null)
        {
            ToolTipService.SetToolTip(BtnVoice, AgyCliDashboard.Helpers.Loc.Tr("Chat.VoiceTooltip"));
        }
        if (BtnDeepThink != null)
        {
            ToolTipService.SetToolTip(BtnDeepThink, AgyCliDashboard.Helpers.Loc.Tr("Chat.DeepThinkTooltip"));
        }
        if (BtnProjectDocs != null)
        {
            ToolTipService.SetToolTip(BtnProjectDocs, AgyCliDashboard.Helpers.Loc.Tr("Chat.DocsTooltip"));
        }
        if (BtnSubagent != null)
        {
            ToolTipService.SetToolTip(BtnSubagent, AgyCliDashboard.Helpers.Loc.Tr("Chat.SubagentTooltip"));
        }
        if (BtnReflection != null)
        {
            ToolTipService.SetToolTip(BtnReflection, AgyCliDashboard.Helpers.Loc.Tr("Chat.ReflectionTooltip"));
        }
        if (BtnWebResearch != null)
        {
            ToolTipService.SetToolTip(BtnWebResearch, AgyCliDashboard.Helpers.Loc.Tr("Chat.WebResearchTooltip"));
        }
        if (BtnE2ETest != null)
        {
            ToolTipService.SetToolTip(BtnE2ETest, AgyCliDashboard.Helpers.Loc.Tr("Chat.E2ETestTooltip"));
        }
        if (BtnDeadCodeCheck != null)
        {
            ToolTipService.SetToolTip(BtnDeadCodeCheck, AgyCliDashboard.Helpers.Loc.Tr("Chat.DeadCodeTooltip"));
        }
    }

    private void BtnAttachFiles_Click(object sender, RoutedEventArgs e) => AttachFilesClick?.Invoke(sender, e);
    private void BtnModelSelect_Click(object sender, RoutedEventArgs e) => ModelSelectClick?.Invoke(sender, e);

    private void BtnDeepThink_Click(object sender, RoutedEventArgs e) => DeepThinkClick?.Invoke(sender, e);
    private void BtnDeepThink_RightTapped(object sender, RightTappedRoutedEventArgs e) => DeepThinkRightTapped?.Invoke(sender, e);

    private void BtnProjectDocs_Click(object sender, RoutedEventArgs e) => ProjectDocsClick?.Invoke(sender, e);
    private void BtnProjectDocs_RightTapped(object sender, RightTappedRoutedEventArgs e) => ProjectDocsRightTapped?.Invoke(sender, e);

    private void BtnGlobalErrorCheck_Click(object sender, RoutedEventArgs e) => GlobalErrorCheckClick?.Invoke(sender, e);
    private void BtnGlobalErrorCheck_RightTapped(object sender, RightTappedRoutedEventArgs e) => GlobalErrorCheckRightTapped?.Invoke(sender, e);

    private void BtnSubagent_Click(object sender, RoutedEventArgs e) => SubagentClick?.Invoke(sender, e);
    private void BtnSubagent_RightTapped(object sender, RightTappedRoutedEventArgs e) => SubagentRightTapped?.Invoke(sender, e);

    private void BtnWebResearch_Click(object sender, RoutedEventArgs e) => WebResearchClick?.Invoke(sender, e);
    private void BtnWebResearch_RightTapped(object sender, RightTappedRoutedEventArgs e) => WebResearchRightTapped?.Invoke(sender, e);


    private void BtnReflection_Click(object sender, RoutedEventArgs e) => ReflectionClick?.Invoke(sender, e);
    private void BtnReflection_RightTapped(object sender, RightTappedRoutedEventArgs e) => ReflectionRightTapped?.Invoke(sender, e);

    private void BtnE2ETest_Click(object sender, RoutedEventArgs e) => E2ETestClick?.Invoke(sender, e);
    private void BtnE2ETest_RightTapped(object sender, RightTappedRoutedEventArgs e) => E2ETestRightTapped?.Invoke(sender, e);

    private void BtnDeadCodeCheck_Click(object sender, RoutedEventArgs e) => DeadCodeCheckClick?.Invoke(sender, e);
    private void BtnDeadCodeCheck_RightTapped(object sender, RightTappedRoutedEventArgs e) => DeadCodeCheckRightTapped?.Invoke(sender, e);

    private void BtnIrrwegCleanup_Click(object sender, RoutedEventArgs e) => IrrwegCleanupClick?.Invoke(sender, e);
    private void BtnIrrwegCleanup_RightTapped(object sender, RightTappedRoutedEventArgs e) => IrrwegCleanupRightTapped?.Invoke(sender, e);

    private void BtnSimplify_Click(object sender, RoutedEventArgs e) => SimplifyClick?.Invoke(sender, e);
    private void BtnSimplify_RightTapped(object sender, RightTappedRoutedEventArgs e) => SimplifyRightTapped?.Invoke(sender, e);

    private void BtnZeroBug_Click(object sender, RoutedEventArgs e) => ZeroBugClick?.Invoke(sender, e);
    private void BtnZeroBug_RightTapped(object sender, RightTappedRoutedEventArgs e) => ZeroBugRightTapped?.Invoke(sender, e);

    private void BtnDauTest_Click(object sender, RoutedEventArgs e) => DauTestClick?.Invoke(sender, e);
    private void BtnDauTest_RightTapped(object sender, RightTappedRoutedEventArgs e) => DauTestRightTapped?.Invoke(sender, e);

    private void BtnCustomPrompt_Click(object sender, RoutedEventArgs e) => CustomPromptClick?.Invoke(sender, e);
    private void BtnCustomPrompt_RightTapped(object sender, RightTappedRoutedEventArgs e) => CustomPromptRightTapped?.Invoke(sender, e);

    private void BtnGitStatusBadge_Click(object sender, RoutedEventArgs e) => GitStatusBadgeClick?.Invoke(sender, e);
    private void BtnProtocolBadge_Click(object sender, RoutedEventArgs e) => ProtocolBadgeClick?.Invoke(sender, e);
    private void BtnKnowledgeBadge_Click(object sender, RoutedEventArgs e) => KnowledgeBadgeClick?.Invoke(sender, e);

    private void BtnUndo_Click(object sender, RoutedEventArgs e) => UndoClick?.Invoke(sender, e);
    private void TglAutoReflexion_Click(object sender, RoutedEventArgs e) => AutoReflexionClick?.Invoke(sender, e);
    private void TglAutoReflexion_RightTapped(object sender, RightTappedRoutedEventArgs e) => AutoReflexionRightTapped?.Invoke(sender, e);

    private void BtnRemoveAttachment_Click(object sender, RoutedEventArgs e) => RemoveAttachmentClick?.Invoke(sender, e);
    private void TxtChatInput_DragOver(object sender, DragEventArgs e) => ChatInputDragOver?.Invoke(sender, e);
    private void TxtChatInput_Drop(object sender, DragEventArgs e) => ChatInputDrop?.Invoke(sender, e);
    private void BdrPromptCapsule_DragOver(object sender, DragEventArgs e) => ChatInputDragOver?.Invoke(sender, e);
    private void BdrPromptCapsule_Drop(object sender, DragEventArgs e) => ChatInputDrop?.Invoke(sender, e);
    private void TxtChatInput_Paste(object sender, TextControlPasteEventArgs e) => ChatInputPaste?.Invoke(sender, e);
    private void TxtChatInput_KeyDown(object sender, KeyRoutedEventArgs e) => ChatInputKeyDown?.Invoke(sender, e);
    private void TxtChatInput_KeyUp(object sender, KeyRoutedEventArgs e) => ChatInputKeyUp?.Invoke(sender, e);
    private void TxtChatInput_TextChanged(object sender, TextChangedEventArgs e) => ChatInputTextChanged?.Invoke(sender, e);
    private void TxtChatInput_Loaded(object sender, RoutedEventArgs e) => ChatInputLoaded?.Invoke(sender, e);

    private void BtnVoice_Click(object sender, RoutedEventArgs e) => VoiceClick?.Invoke(sender, e);
    private void BtnSendMessage_Click(object sender, RoutedEventArgs e) => SendMessageClick?.Invoke(sender, e);
    private void BtnSendMessage_PointerPressed(object sender, PointerRoutedEventArgs e) => SendMessagePointerPressed?.Invoke(sender, e);
    private void BtnSendMessage_PointerReleased(object sender, PointerRoutedEventArgs e) => SendMessagePointerReleased?.Invoke(sender, e);
    private void BtnSendMessage_PointerExited(object sender, PointerRoutedEventArgs e) => SendMessagePointerExited?.Invoke(sender, e);

    private void BtnCancelCommand_Click(object sender, RoutedEventArgs e) => CancelCommandClick?.Invoke(sender, e);
}
