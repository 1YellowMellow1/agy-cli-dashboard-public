using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.System;
using System.IO;
using System.Text.Json;
using System.Collections.ObjectModel;
using System.Linq;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.Data.Sqlite;
using AgyCliDashboard.Helpers;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    private const uint ASFW_ANY = unchecked((uint)-1);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    private IntPtr _hwnd = IntPtr.Zero;

    // Chat Toolbar States
    private string _currentModelId = string.Empty;
    public string CurrentModelId
    {
        get => _currentModelId;
        set
        {
            _currentModelId = ResolveModelId(value);
            if (TxtModelName != null)
            {
                TxtModelName.Text = "Modell";
            }
            UpdateTokenPeakMeter(false);
        }
    }

    private string ResolveModelId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        
        var matchById = _cachedOpenRouterModels?.FirstOrDefault(m => string.Equals(m.Id, input, StringComparison.OrdinalIgnoreCase));
        if (matchById != null) return matchById.Id;

        var matchByName = _cachedOpenRouterModels?.FirstOrDefault(m => string.Equals(m.Name, input, StringComparison.OrdinalIgnoreCase) || string.Equals(m.DisplayName, input, StringComparison.OrdinalIgnoreCase));
        if (matchByName != null) return matchByName.Id;

        return input;
    }

    private DateTime _enterKeyDownTime = DateTime.MinValue;
    private DispatcherTimer? _longPressTimer;
    private DispatcherTimer? _progressAnimationTimer;
    private DateTime _pressStartTime = DateTime.MinValue;
    private bool _isLongPressThresholdReached = false;
    private FileSystemWatcher? _settingsWatcher;
    private System.Threading.Timer? _settingsDebounceTimer;
    private FileSystemWatcher? _brainWatcher;
    private System.Threading.Timer? _brainWatcherTimer;
    private readonly Services.IDatabaseService _databaseService = new Services.DatabaseService();
    private readonly Services.FileService _fileService = new Services.FileService();
    private DispatcherTimer? _statusHealthTimer;
    private static readonly System.Net.Http.HttpClient _healthHttpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    private string _lastRenderedTabsSignature = string.Empty;
    internal ObservableCollection<AgyChatSession> _chats = new();
    internal System.Collections.Generic.List<AgyChatSession> _allMasterChats = new();
    private ObservableCollection<ProjectItemModel> _allProjects = new();
    private ProjectSettingsData _projectSettings = new();
    private string _settingsFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "project_settings.json");
    private string _lastProjectFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "last_project.txt");
    private UiStatesData _uiStates = new();
    private string _pinnedFilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "pinned_chats.json");
    private int _isLoadChatsRunning = 0;
    private int _pendingLoadChatsRequest = 0;






    private static void WriteAllTextAtomic(string filePath, string content)
    {
        string? tempFile = null;
        try
        {
            string dir = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            tempFile = Path.Combine(dir, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempFile, content, System.Text.Encoding.UTF8);
            File.Move(tempFile, filePath, true);
            tempFile = null;
            Services.FileTimestampCache.Invalidate(filePath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"WriteAllTextAtomic error for '{filePath}': {ex.Message}");
            try
            {
                File.WriteAllText(filePath, content, System.Text.Encoding.UTF8);
                Services.FileTimestampCache.Invalidate(filePath);
            }
            catch (Exception fallbackEx)
            {
                TelemetryLogger.Log($"WriteAllTextAtomic fallback error for '{filePath}': {fallbackEx.Message}");
            }
        }
        finally
        {
            if (tempFile != null && File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    private static async System.Threading.Tasks.Task WriteAllTextAtomicAsync(string filePath, string content)
    {
        string? tempFile = null;
        try
        {
            string dir = Path.GetDirectoryName(filePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            tempFile = Path.Combine(dir, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(tempFile, content, System.Text.Encoding.UTF8);
            File.Move(tempFile, filePath, true);
            tempFile = null;
            Services.FileTimestampCache.Invalidate(filePath);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"WriteAllTextAtomicAsync error for '{filePath}': {ex.Message}");
            try
            {
                await File.WriteAllTextAsync(filePath, content, System.Text.Encoding.UTF8);
                Services.FileTimestampCache.Invalidate(filePath);
            }
            catch (Exception fallbackEx)
            {
                TelemetryLogger.Log($"WriteAllTextAtomicAsync fallback error for '{filePath}': {fallbackEx.Message}");
            }
        }
        finally
        {
            if (tempFile != null && File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    // Multi-Terminal Sessions Management
    private readonly ObservableCollection<TerminalTabSession> _terminalSessions = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _activeUiChatIds = new();
    private string _activeTerminalId = string.Empty;
    private string? _initialTerminalId = null;

    // Accessors for componentized Sidebar Panels
    public ScrollViewer FilesPanelScrollViewer => UcFilesSidebar.FilesPanelScrollViewer;
    public StackPanel FilesPanel => UcFilesSidebar.FilesPanel;
    public ContentControl FilesHeaderContainer => UcFilesSidebar.FilesHeaderContainer;
    public Grid PnlFileSearch => UcFilesSidebar.PnlFileSearch;
    public TextBox TxtFileSearch => UcFilesSidebar.TxtFileSearch;
    public Button BtnClearFileSearch => UcFilesSidebar.BtnClearFileSearch;
    public ScrollViewer GitPanelScrollViewer => UcGitSidebar.GitPanelScrollViewer;
    public StackPanel GitPanel => UcGitSidebar.GitPanel;
    public ScrollViewer DocsPanelScrollViewer => UcDocsSidebar.DocsPanelScrollViewer;
    public StackPanel DocsPanel => UcDocsSidebar.DocsPanel;

    public Border PnlAddInjectionDialog => UcDocsSidebar.PnlAddInjectionDialog;
    public TextBlock TxtAddInjectionTitle => UcDocsSidebar.TxtAddInjectionTitle;
    public TextBox TxtNewInjectionName => UcDocsSidebar.TxtNewInjectionName;
    public TextBox TxtNewInjectionContent => UcDocsSidebar.TxtNewInjectionContent;
    public Button BtnToggleProjectResourcesCard => UcDocsSidebar.BtnToggleProjectResourcesCard;
    public TextBlock TxtProjectDocsHeader => UcDocsSidebar.TxtProjectDocsHeader;
    public TextBlock TxtChevronProjectResources => UcDocsSidebar.TxtChevronProjectResources;
    public StackPanel PnlProjectResourcesContent => UcDocsSidebar.PnlProjectResourcesContent;
    public Button BtnAddProjectDoc => UcDocsSidebar.BtnAddProjectDoc;
    public StackPanel PnlProjectDocsList => UcDocsSidebar.PnlProjectDocsList;
    public Button BtnAddProjectRuleFile => UcDocsSidebar.BtnAddProjectRuleFile;
    public StackPanel PnlProjectRulesFilesList => UcDocsSidebar.PnlProjectRulesFilesList;
    public Button BtnOpenProjectProtocol => UcDocsSidebar.BtnOpenProjectProtocol;
    public Button BtnToggleGlobalResourcesCard => UcDocsSidebar.BtnToggleGlobalResourcesCard;

    public TextBlock TxtChevronGlobalResources => UcDocsSidebar.TxtChevronGlobalResources;
    public StackPanel PnlGlobalResourcesContent => UcDocsSidebar.PnlGlobalResourcesContent;
    public StackPanel PnlGlobalDocsList => UcDocsSidebar.PnlGlobalDocsList;
    public Button BtnAddGlobalKnowledge => UcDocsSidebar.BtnAddGlobalKnowledge;
    public Button BtnImportGlobalKnowledge => UcDocsSidebar.BtnImportGlobalKnowledge;
    public Button BtnExportGlobalKnowledge => UcDocsSidebar.BtnExportGlobalKnowledge;
    public StackPanel PnlGlobalKnowledgeList => UcDocsSidebar.PnlGlobalKnowledgeList;
    public StackPanel PnlGlobalSkillsList => UcDocsSidebar.PnlGlobalSkillsList;
    public Button BtnAddGlobalRuleFile => UcDocsSidebar.BtnAddGlobalRuleFile;
    public StackPanel PnlGlobalRulesFilesList => UcDocsSidebar.PnlGlobalRulesFilesList;
    public StackPanel PnlGlobalSettingsList => UcDocsSidebar.PnlGlobalSettingsList;
    public Button BtnToggleGlobalInjectionsCard => UcDocsSidebar.BtnToggleGlobalInjectionsCard;
    public TextBlock TxtChevronGlobalInjections => UcDocsSidebar.TxtChevronGlobalInjections;
    public StackPanel PnlGlobalInjectionsCardContent => UcDocsSidebar.PnlGlobalInjectionsCardContent;
    public StackPanel PnlGlobalInjectionsList => UcDocsSidebar.PnlGlobalInjectionsList;
    public Button BtnAddGlobalInjection => UcDocsSidebar.BtnAddGlobalInjection;
    public Button BtnToggleProjectInjectionsCard => UcDocsSidebar.BtnToggleProjectInjectionsCard;
    public TextBlock TxtProjectInjectionHeader => UcDocsSidebar.TxtProjectInjectionHeader;
    public TextBlock TxtChevronProjectInjections => UcDocsSidebar.TxtChevronProjectInjections;
    public StackPanel PnlProjectInjectionsCardContent => UcDocsSidebar.PnlProjectInjectionsCardContent;
    public StackPanel PnlProjectInjectionsList => UcDocsSidebar.PnlProjectInjectionsList;
    public Button BtnAddProjectInjection => UcDocsSidebar.BtnAddProjectInjection;

    public Grid PnlModelPicker => UcModelPicker.PnlModelPicker;
    public TextBlock TxtModelCount => UcModelPicker.TxtModelCount;
    public TextBox TxtModelSearch => UcModelPicker.TxtModelSearch;
    public ComboBox CboModelProvider => UcModelPicker.CboModelProvider;
    public ComboBox CboModelSort => UcModelPicker.CboModelSort;
    public ToggleButton BtnTogglePriceScore => UcModelPicker.BtnTogglePriceScore;
    public ListView ListModels => UcModelPicker.ListModels;
    public ObservableCollection<ModelPickerListItemViewModel> VisibleModels => UcModelPicker.VisibleModels;

    public Border AppHeaderBorder => UcHeader.AppHeaderBorder;
    public FrameworkElement? AppTitleBar => (FrameworkElement?)UcHeader?.TitleBarDragArea ?? UcHeader?.AppTitleBar;
    public Border BdrConnectionStatus => UcHeader.BdrConnectionStatus;
    public Microsoft.UI.Xaml.Shapes.Path IconConnectionStatus => UcHeader.IconConnectionStatus;
    public TextBlock TxtConnectionStatus => UcHeader.TxtConnectionStatus;
    public Border BdrApiLoadStatus => UcHeader.BdrApiLoadStatus;
    public Microsoft.UI.Xaml.Shapes.Ellipse DotApiLoadStatus => UcHeader.DotApiLoadStatus;
    public ProgressRing SpinnerApiLoadStatus => UcHeader.SpinnerApiLoadStatus;
    public TextBlock TxtApiLoadStatus => UcHeader.TxtApiLoadStatus;
    public Button BtnCheckUpdate => UcHeader.BtnCheckUpdate;
    public Microsoft.UI.Xaml.Shapes.Ellipse DotCliStatus => UcHeader.DotCliStatus;
    public ProgressRing SpinnerCliStatus => UcHeader.SpinnerCliStatus;
    public TextBlock TxtCliStatus => UcHeader.TxtCliStatus;
    public Border BdrOpenRouterBalance => UcHeader.BdrOpenRouterBalance;
    public TextBlock TxtOpenRouterBalance => UcHeader.TxtOpenRouterBalance;
    public Border PnlHeaderNotification => UcHeader.PnlHeaderNotification;
    public Microsoft.UI.Xaml.Shapes.Ellipse DotHeaderNotice => UcHeader.DotHeaderNotice;
    public TextBlock TxtHeaderNoticeTitle => UcHeader.TxtHeaderNoticeTitle;
    public TextBlock TxtHeaderNoticeMessage => UcHeader.TxtHeaderNoticeMessage;
    public Button BtnGeminiModelStatus => UcHeader.BtnGeminiModelStatus;
    public TextBlock TxtGeminiModelStatus => UcHeader.TxtGeminiModelStatus;
    public StackPanel RightTitleBarArea => UcHeader.RightTitleBarArea;

    public Grid EditorContainer => UcEditor.EditorContainer;
    public TextBlock TxtEditorTitle => UcEditor.TxtEditorTitle;
    public TextBlock TxtEditorPath => UcEditor.TxtEditorPath;
    public Button BtnSaveEditorFile => UcEditor.BtnSaveEditorFile;
    public RichEditBox TxtFileEditor => UcEditor.TxtFileEditor;
    public RichEditBox RichFileEditor => UcEditor.RichFileEditor;
    public Button BtnOpenExternalWordPad => UcEditor.BtnOpenExternalWordPad;

    public EasyWindowsTerminalControl.EasyTerminalControl? ActiveTerminal
    {
        get
        {
            var session = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId) ?? _terminalSessions.FirstOrDefault();
            return session?.Control;
        }
    }

    public MainWindow()
    {
        this.InitializeComponent();

        // Attach ChatSidebar events
        if (UcChatSidebar != null)
        {
            UcChatSidebar.OpenAgyChatRequested += UcChatSidebar_OpenAgyChatRequested;
            UcChatSidebar.OpenClawChatRequested += UcChatSidebar_OpenClawChatRequested;
            UcChatSidebar.PinToggled += UcChatSidebar_PinToggled;
            UcChatSidebar.SingleChatDeleted += UcChatSidebar_SingleChatDeleted;
            UcChatSidebar.MultiChatsDeleted += UcChatSidebar_MultiChatsDeleted;
            UcChatSidebar.MultiSelectChanged += UcChatSidebar_MultiSelectChanged;
        }

        InitPromptsSidebar();
        InitNotesSidebar();

        // Attach PromptEditor events
        if (UcPromptEditor != null)
        {
            UcPromptEditor.AttachFilesClick += BtnAttachFiles_Click;
            UcPromptEditor.ModelSelectClick += BtnModelSelect_Click;
            UcPromptEditor.DeepThinkClick += BtnDeepThink_Click;
            UcPromptEditor.DeepThinkRightTapped += BtnDeepThink_RightTapped;
            UcPromptEditor.ProjectDocsClick += BtnProjectDocs_Click;
            UcPromptEditor.ProjectDocsRightTapped += BtnProjectDocs_RightTapped;
            UcPromptEditor.GlobalErrorCheckClick += BtnGlobalErrorCheck_Click;
            UcPromptEditor.GlobalErrorCheckRightTapped += BtnGlobalErrorCheck_RightTapped;
            UcPromptEditor.SubagentClick += BtnSubagent_Click;
            UcPromptEditor.SubagentRightTapped += BtnSubagent_RightTapped;
            UcPromptEditor.WebResearchClick += BtnWebResearch_Click;
            UcPromptEditor.WebResearchRightTapped += BtnWebResearch_RightTapped;

            UcPromptEditor.ReflectionClick += BtnReflection_Click;
            UcPromptEditor.ReflectionRightTapped += BtnReflection_RightTapped;
            UcPromptEditor.E2ETestClick += BtnE2ETest_Click;
            UcPromptEditor.E2ETestRightTapped += BtnE2ETest_RightTapped;
            UcPromptEditor.DeadCodeCheckClick += BtnDeadCodeCheck_Click;
            UcPromptEditor.DeadCodeCheckRightTapped += BtnDeadCodeCheck_RightTapped;
            UcPromptEditor.IrrwegCleanupClick += BtnIrrwegCleanup_Click;
            UcPromptEditor.IrrwegCleanupRightTapped += BtnIrrwegCleanup_RightTapped;
            UcPromptEditor.SimplifyClick += BtnSimplify_Click;
            UcPromptEditor.SimplifyRightTapped += BtnSimplify_RightTapped;
            UcPromptEditor.ZeroBugClick += BtnZeroBug_Click;
            UcPromptEditor.ZeroBugRightTapped += BtnZeroBug_RightTapped;
            UcPromptEditor.DauTestClick += BtnDauTest_Click;
            UcPromptEditor.DauTestRightTapped += BtnDauTest_RightTapped;
            UcPromptEditor.CustomPromptClick += BtnCustomPrompt_Click;
            UcPromptEditor.CustomPromptRightTapped += BtnCustomPrompt_RightTapped;
            UcPromptEditor.GitStatusBadgeClick += BtnGitStatusBadge_Click;
            UcPromptEditor.ProtocolBadgeClick += BtnProtocolBadge_Click;
            UcPromptEditor.KnowledgeBadgeClick += BtnKnowledgeBadge_Click;

            UcPromptEditor.UndoClick += BtnUndo_Click;
            UcPromptEditor.AutoReflexionClick += TglAutoReflexion_Click;
            UcPromptEditor.AutoReflexionRightTapped += TglAutoReflexion_RightTapped;
            UcPromptEditor.RemoveAttachmentClick += BtnRemoveAttachment_Click;
            UcPromptEditor.ChatInputDragOver += TxtChatInput_DragOver;
            UcPromptEditor.ChatInputDrop += TxtChatInput_Drop;
            UcPromptEditor.ChatInputPaste += TxtChatInput_Paste;
            UcPromptEditor.ChatInputKeyDown += TxtChatInput_KeyDown;
            UcPromptEditor.ChatInputKeyUp += TxtChatInput_KeyUp;
            UcPromptEditor.ChatInputTextChanged += TxtChatInput_TextChanged;
            UcPromptEditor.ChatInputLoaded += TxtChatInput_Loaded;
            UcPromptEditor.VoiceClick += BtnVoice_Click;
            UcPromptEditor.SendMessageClick += BtnSendMessage_Click;
            UcPromptEditor.SendMessageRightTapped += BtnSendMessage_RightTapped;
            UcPromptEditor.SendMessagePointerPressed += BtnSendMessage_PointerPressed;
            UcPromptEditor.SendMessagePointerReleased += BtnSendMessage_PointerReleased;
            UcPromptEditor.SendMessagePointerExited += BtnSendMessage_PointerExited;
            UcPromptEditor.CancelCommandClick += BtnCancelCommand_Click;
        }

        // Attach DocsSidebar events
        if (UcDocsSidebar != null)
        {
            UcDocsSidebar.CancelAddInjectionClick += BtnCancelAddInjection_Click;
            UcDocsSidebar.SaveNewInjectionClick += BtnSaveNewInjection_Click;
            UcDocsSidebar.ToggleProjectResourcesCardClick += BtnToggleProjectResourcesCard_Click;
            UcDocsSidebar.AddProjectDocClick += BtnAddProjectDoc_Click;
            UcDocsSidebar.AddProjectRuleFileClick += BtnAddProjectRuleFile_Click;
            UcDocsSidebar.OpenProjectProtocolClick += BtnOpenProjectProtocol_Click;
            UcDocsSidebar.ToggleGlobalResourcesCardClick += BtnToggleGlobalResourcesCard_Click;
            UcDocsSidebar.AddGlobalKnowledgeClick += BtnAddGlobalKnowledge_Click;
            UcDocsSidebar.ImportGlobalKnowledgeClick += BtnImportGlobalKnowledge_Click;
            UcDocsSidebar.ExportGlobalKnowledgeClick += BtnExportGlobalKnowledge_Click;
            UcDocsSidebar.AddGlobalRuleFileClick += BtnAddGlobalRuleFile_Click;
            UcDocsSidebar.ToggleGlobalInjectionsCardClick += BtnToggleGlobalInjectionsCard_Click;
            UcDocsSidebar.AddGlobalInjectionClick += BtnAddGlobalInjection_Click;
            UcDocsSidebar.ToggleProjectInjectionsCardClick += BtnToggleProjectInjectionsCard_Click;
            UcDocsSidebar.AddProjectInjectionClick += BtnAddProjectInjection_Click;
        }

        // Attach ModelPicker events
        if (UcModelPicker != null)
        {
            UcModelPicker.ModelPickerBackClick += BtnModelPickerBack_Click;
            UcModelPicker.RefreshModelsClick += BtnRefreshModels_Click;
            UcModelPicker.ModelSearchTextChanged += TxtModelSearch_TextChanged;
            UcModelPicker.ModelProviderSelectionChanged += CboModelProvider_SelectionChanged;
            UcModelPicker.ModelSortSelectionChanged += CboModelSort_SelectionChanged;
            UcModelPicker.TogglePriceScoreClick += BtnTogglePriceScore_Click;
            UcModelPicker.ModelItemClick += UcModelPicker_ModelItemClick;
            UcModelPicker.FavoriteToggleClick += UcModelPicker_FavoriteToggleClick;
        }

        if (UcTerminalShortcuts != null)
        {
            UcTerminalShortcuts.CloseClick += BtnCloseShortcuts_Click;
            UcTerminalShortcuts.AskShellShortcutsClick += BtnAskShellShortcuts_Click;
        }

        // Attach Header events
        if (UcHeader != null)
        {
            UcHeader.ToggleSidebarClick += BtnToggleSidebar_Click;
            UcHeader.CheckUpdateClick += BtnCheckUpdate_Click;
            UcHeader.CheckUpdateRightTapped += BtnCheckUpdate_RightTapped;
            UcHeader.CloseHeaderNoticeClick += BtnCloseHeaderNotice_Click;
            UcHeader.GeminiModelStatusClick += BtnGeminiModelStatus_Click;
            UcHeader.GeminiModelStatusRightTapped += BtnGeminiModelStatus_RightTapped;
        }

        // Attach Editor events
        if (UcEditor != null)
        {
            UcEditor.SaveEditorFileClick += BtnSaveEditorFile_Click;
            UcEditor.CloseEditorFileClick += BtnCloseEditorFile_Click;
            UcEditor.PrintEditorTextClick += BtnPrintEditorText_Click;
            UcEditor.SavePdfEditorTextClick += BtnSavePdfEditorText_Click;
            UcEditor.StripMarkdownEditorTextClick += BtnStripMarkdownEditor_Click;
            UcEditor.OpenExternalAppClick += BtnOpenExternalWordPad_Click;
            UcEditor.FileEditorKeyDown += TxtFileEditor_KeyDown;
        }

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (this.Content is FrameworkElement rootElement)
        {
            rootElement.Loaded += (s, e) =>
            {
                OptimizeImageIconRasterization(rootElement);
            };
        }
        LoadUiStates();
        LoadProjectSettings();
        Services.LocalizationService.Instance.Initialize(_projectSettings.Language);
        Services.LocalizationService.Instance.LanguageChanged += ApplyLocalization;
        ApplyLocalization();
        SyncRulesToGeminiMd();
        _ = System.Threading.Tasks.Task.Run(() => Helpers.AutoReflectionManager.EnsureGlobalHookConfigured());
        RestoreUiLayout();
        RestoreSavedSessions();
        RestoreActiveTab();
        InitModelWatcher();
        InitBrainWatcher();
        InitStatusBadgesWatcher();
        InitConsoleHostToggle();
        
        AttachmentStagingControl.ItemsSource = _stagedAttachments;
        ListProjectItems.ItemsSource = _allProjects;
        LoadPinnedChatIds();
        _ = LoadChatsAsync();
        Title = "ANTIGRAVITY CLI Dashboard";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        RegisterCopyTerminalHotkey();
        RegisterTerminalKeyboardHook();

        _globalKeyDownHandler = new KeyEventHandler(Global_KeyDown);
        this.Content.AddHandler(UIElement.KeyDownEvent, _globalKeyDownHandler, true);

        // BtnDeepThink Longpress: Per AddHandler mit handledEventsToo=true registrieren,
        // weil WinUI 3 Button das PointerPressed-Event intern verschluckt.
        _btnDeepThinkPointerPressedHandler = new PointerEventHandler(BtnDeepThink_PointerPressed);
        _btnDeepThinkPointerReleasedHandler = new PointerEventHandler(BtnDeepThink_PointerReleased);

        BtnDeepThink.AddHandler(UIElement.PointerPressedEvent, _btnDeepThinkPointerPressedHandler, true);
        BtnDeepThink.AddHandler(UIElement.PointerReleasedEvent, _btnDeepThinkPointerReleasedHandler, true);
        BtnDeepThink.AddHandler(UIElement.PointerCanceledEvent, _btnDeepThinkPointerReleasedHandler, true);

        UpdateModeButtonVisuals();
        LoadMaintenanceTimestamps();
        UpdateMaintenanceToolTips();

        try
        {
            string iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                if (hIcon != IntPtr.Zero)
                {
                    Microsoft.UI.IconId iconId = Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon);
                    AppWindow.SetIcon(iconId);
                }
                IntPtr hSmallIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                if (hSmallIcon != IntPtr.Zero && _hwnd != IntPtr.Zero) SendMessage(_hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmallIcon);
                if (hIcon != IntPtr.Zero && _hwnd != IntPtr.Zero) SendMessage(_hwnd, WM_SETICON, (IntPtr)ICON_BIG, hIcon);
            }
        }
        catch { }

        // Custom Dark TitleBar Configuration
        if (AppWindow.TitleBar != null)
        {
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
            AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
            AppWindow.TitleBar.ButtonHoverForegroundColor = Colors.White;
            AppWindow.TitleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 30, 31, 34);
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            SetTitleBar(AppTitleBar);
        }

        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        TxtFontSize.Text = _fontSize.ToString();
        RenderWorkspaceTabs();
        this.Activated += MainWindow_Activated;
        this.Closed += MainWindow_Closed;

        UpdateBuildStatus();
        InitializeBuildFreshnessWatcher();
        InitializeCliVersionWatcher();
        _ = CheckForCliUpdatesAsync(showNoticeOnUpToDate: false);
        InitializeStatusHealthTimer();
        InitializeCoBrowser();
        InitializeAutoPush();
        
    }




    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // Hotkey deregistrieren & Unmanaged WindowProc Hook wiederherstellen
        try { UnregisterHotKey(_hwnd, HOTKEY_ID_COPY_TERMINAL); } catch { }
        _isCopyHotkeyRegistered = false;
        if (_hwnd != IntPtr.Zero && _oldWndProc != IntPtr.Zero)
        {
            try { SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldWndProc); } catch { }
            _oldWndProc = IntPtr.Zero;
        }

        if (_hKeyboardHook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_hKeyboardHook); } catch { }
            _hKeyboardHook = IntPtr.Zero;
            _keyboardHookDelegate = null;
        }

        try
        {
            if (this.Content != null && _globalKeyDownHandler != null)
                this.Content.RemoveHandler(UIElement.KeyDownEvent, _globalKeyDownHandler);

            if (BtnDeepThink != null)
            {
                if (_btnDeepThinkPointerPressedHandler != null)
                    BtnDeepThink.RemoveHandler(UIElement.PointerPressedEvent, _btnDeepThinkPointerPressedHandler);
                if (_btnDeepThinkPointerReleasedHandler != null)
                {
                    BtnDeepThink.RemoveHandler(UIElement.PointerReleasedEvent, _btnDeepThinkPointerReleasedHandler);
                    BtnDeepThink.RemoveHandler(UIElement.PointerCanceledEvent, _btnDeepThinkPointerReleasedHandler);
                }
            }
        }
        catch { }

        // Unsubscribe UserControl events
        if (UcChatSidebar != null)
        {
            UcChatSidebar.OpenAgyChatRequested -= UcChatSidebar_OpenAgyChatRequested;
            UcChatSidebar.OpenClawChatRequested -= UcChatSidebar_OpenClawChatRequested;
            UcChatSidebar.PinToggled -= UcChatSidebar_PinToggled;
            UcChatSidebar.SingleChatDeleted -= UcChatSidebar_SingleChatDeleted;
            UcChatSidebar.MultiChatsDeleted -= UcChatSidebar_MultiChatsDeleted;
            UcChatSidebar.MultiSelectChanged -= UcChatSidebar_MultiSelectChanged;
        }

        DisposePromptsSidebar();
        try { _coBrowserPopOutWindow?.Close(); } catch { }
        try { DisposeCoBrowserEvents(); } catch { }
        try { UcCoBrowser?.Dispose(); } catch { }
        

        if (UcPromptEditor != null)
        {
            UcPromptEditor.AttachFilesClick -= BtnAttachFiles_Click;
            UcPromptEditor.ModelSelectClick -= BtnModelSelect_Click;
            UcPromptEditor.DeepThinkClick -= BtnDeepThink_Click;
            UcPromptEditor.DeepThinkRightTapped -= BtnDeepThink_RightTapped;
            UcPromptEditor.ProjectDocsClick -= BtnProjectDocs_Click;
            UcPromptEditor.ProjectDocsRightTapped -= BtnProjectDocs_RightTapped;
            UcPromptEditor.GlobalErrorCheckClick -= BtnGlobalErrorCheck_Click;
            UcPromptEditor.GlobalErrorCheckRightTapped -= BtnGlobalErrorCheck_RightTapped;
            UcPromptEditor.SubagentClick -= BtnSubagent_Click;
            UcPromptEditor.SubagentRightTapped -= BtnSubagent_RightTapped;
            UcPromptEditor.WebResearchClick -= BtnWebResearch_Click;
            UcPromptEditor.WebResearchRightTapped -= BtnWebResearch_RightTapped;

            UcPromptEditor.ReflectionClick -= BtnReflection_Click;
            UcPromptEditor.ReflectionRightTapped -= BtnReflection_RightTapped;
            UcPromptEditor.E2ETestClick -= BtnE2ETest_Click;
            UcPromptEditor.E2ETestRightTapped -= BtnE2ETest_RightTapped;
            UcPromptEditor.DeadCodeCheckClick -= BtnDeadCodeCheck_Click;
            UcPromptEditor.DeadCodeCheckRightTapped -= BtnDeadCodeCheck_RightTapped;
            UcPromptEditor.IrrwegCleanupClick -= BtnIrrwegCleanup_Click;
            UcPromptEditor.IrrwegCleanupRightTapped -= BtnIrrwegCleanup_RightTapped;
            UcPromptEditor.SimplifyClick -= BtnSimplify_Click;
            UcPromptEditor.SimplifyRightTapped -= BtnSimplify_RightTapped;
            UcPromptEditor.ZeroBugClick -= BtnZeroBug_Click;
            UcPromptEditor.ZeroBugRightTapped -= BtnZeroBug_RightTapped;
            UcPromptEditor.DauTestClick -= BtnDauTest_Click;
            UcPromptEditor.DauTestRightTapped -= BtnDauTest_RightTapped;
            UcPromptEditor.CustomPromptClick -= BtnCustomPrompt_Click;
            UcPromptEditor.CustomPromptRightTapped -= BtnCustomPrompt_RightTapped;
            UcPromptEditor.GitStatusBadgeClick -= BtnGitStatusBadge_Click;
            UcPromptEditor.ProtocolBadgeClick -= BtnProtocolBadge_Click;
            UcPromptEditor.KnowledgeBadgeClick -= BtnKnowledgeBadge_Click;

            UcPromptEditor.UndoClick -= BtnUndo_Click;
            UcPromptEditor.AutoReflexionClick -= TglAutoReflexion_Click;
            UcPromptEditor.AutoReflexionRightTapped -= TglAutoReflexion_RightTapped;
            UcPromptEditor.RemoveAttachmentClick -= BtnRemoveAttachment_Click;
            UcPromptEditor.ChatInputDragOver -= TxtChatInput_DragOver;
            UcPromptEditor.ChatInputDrop -= TxtChatInput_Drop;
            UcPromptEditor.ChatInputPaste -= TxtChatInput_Paste;
            UcPromptEditor.ChatInputKeyDown -= TxtChatInput_KeyDown;
            UcPromptEditor.ChatInputKeyUp -= TxtChatInput_KeyUp;
            UcPromptEditor.ChatInputTextChanged -= TxtChatInput_TextChanged;
            UcPromptEditor.ChatInputLoaded -= TxtChatInput_Loaded;
            UcPromptEditor.VoiceClick -= BtnVoice_Click;
            UcPromptEditor.SendMessageClick -= BtnSendMessage_Click;
            UcPromptEditor.SendMessageRightTapped -= BtnSendMessage_RightTapped;
            UcPromptEditor.SendMessagePointerPressed -= BtnSendMessage_PointerPressed;
            UcPromptEditor.SendMessagePointerReleased -= BtnSendMessage_PointerReleased;
            UcPromptEditor.SendMessagePointerExited -= BtnSendMessage_PointerExited;
            UcPromptEditor.CancelCommandClick -= BtnCancelCommand_Click;
        }

        if (UcDocsSidebar != null)
        {
            UcDocsSidebar.CancelAddInjectionClick -= BtnCancelAddInjection_Click;
            UcDocsSidebar.SaveNewInjectionClick -= BtnSaveNewInjection_Click;
            UcDocsSidebar.ToggleProjectResourcesCardClick -= BtnToggleProjectResourcesCard_Click;
            UcDocsSidebar.AddProjectDocClick -= BtnAddProjectDoc_Click;
            UcDocsSidebar.AddProjectRuleFileClick -= BtnAddProjectRuleFile_Click;
            UcDocsSidebar.OpenProjectProtocolClick -= BtnOpenProjectProtocol_Click;
            UcDocsSidebar.ToggleGlobalResourcesCardClick -= BtnToggleGlobalResourcesCard_Click;
            UcDocsSidebar.AddGlobalKnowledgeClick -= BtnAddGlobalKnowledge_Click;
            UcDocsSidebar.ImportGlobalKnowledgeClick -= BtnImportGlobalKnowledge_Click;
            UcDocsSidebar.ExportGlobalKnowledgeClick -= BtnExportGlobalKnowledge_Click;
            UcDocsSidebar.AddGlobalRuleFileClick -= BtnAddGlobalRuleFile_Click;
            UcDocsSidebar.ToggleGlobalInjectionsCardClick -= BtnToggleGlobalInjectionsCard_Click;
            UcDocsSidebar.AddGlobalInjectionClick -= BtnAddGlobalInjection_Click;
            UcDocsSidebar.ToggleProjectInjectionsCardClick -= BtnToggleProjectInjectionsCard_Click;
            UcDocsSidebar.AddProjectInjectionClick -= BtnAddProjectInjection_Click;
        }

        if (UcModelPicker != null)
        {
            UcModelPicker.ModelPickerBackClick -= BtnModelPickerBack_Click;
            UcModelPicker.RefreshModelsClick -= BtnRefreshModels_Click;
            UcModelPicker.ModelSearchTextChanged -= TxtModelSearch_TextChanged;
            UcModelPicker.ModelProviderSelectionChanged -= CboModelProvider_SelectionChanged;
            UcModelPicker.ModelSortSelectionChanged -= CboModelSort_SelectionChanged;
            UcModelPicker.TogglePriceScoreClick -= BtnTogglePriceScore_Click;
            UcModelPicker.ModelItemClick -= UcModelPicker_ModelItemClick;
            UcModelPicker.FavoriteToggleClick -= UcModelPicker_FavoriteToggleClick;
        }

        if (UcTerminalShortcuts != null)
        {
            UcTerminalShortcuts.CloseClick -= BtnCloseShortcuts_Click;
            UcTerminalShortcuts.AskShellShortcutsClick -= BtnAskShellShortcuts_Click;
        }

        if (UcHeader != null)
        {
            UcHeader.ToggleSidebarClick -= BtnToggleSidebar_Click;
            
            
            UcHeader.CheckUpdateClick -= BtnCheckUpdate_Click;
            UcHeader.CheckUpdateRightTapped -= BtnCheckUpdate_RightTapped;
            UcHeader.CloseHeaderNoticeClick -= BtnCloseHeaderNotice_Click;
            UcHeader.GeminiModelStatusClick -= BtnGeminiModelStatus_Click;
            UcHeader.GeminiModelStatusRightTapped -= BtnGeminiModelStatus_RightTapped;
        }

        if (UcEditor != null)
        {
            UcEditor.SaveEditorFileClick -= BtnSaveEditorFile_Click;
            UcEditor.CloseEditorFileClick -= BtnCloseEditorFile_Click;
            UcEditor.PrintEditorTextClick -= BtnPrintEditorText_Click;
            UcEditor.SavePdfEditorTextClick -= BtnSavePdfEditorText_Click;
            UcEditor.StripMarkdownEditorTextClick -= BtnStripMarkdownEditor_Click;
            UcEditor.OpenExternalAppClick -= BtnOpenExternalWordPad_Click;
            UcEditor.FileEditorKeyDown -= TxtFileEditor_KeyDown;
        }

        if (CmbProjectSelector != null)
        {
            CmbProjectSelector.SelectionChanged -= CmbProjectSelector_SelectionChanged;
        }

        // Timer stoppen & disposen
        try { _statusHealthTimer?.Stop(); _statusHealthTimer = null; } catch { }
        try { StopAutoPushTimer(); } catch { }
        try { _progressAnimationTimer?.Stop(); _progressAnimationTimer = null; } catch { }
        try { _longPressTimer?.Stop(); _longPressTimer = null; } catch { }
        try { _deepThinkBtnLongPressTimer?.Stop(); _deepThinkBtnLongPressTimer = null; } catch { }
        try { _noticeAutoCloseTimer?.Stop(); _noticeAutoCloseTimer = null; } catch { }
        lock (_syncLockBrainTimer) { try { _brainWatcherTimer?.Dispose(); _brainWatcherTimer = null; } catch { } }
        lock (_syncLockBadgeTimer)
        {
            try { _badgeDebounceTimer?.Dispose(); _badgeDebounceTimer = null; } catch { }
            try { _flashResetTimer?.Dispose(); _flashResetTimer = null; } catch { }
        }
        try { _settingsDebounceTimer?.Dispose(); _settingsDebounceTimer = null; } catch { }
        try { _peakDecayTimer?.Stop(); _peakDecayTimer = null; } catch { }
        try
        {
            foreach (var kvp in _transcriptDebounceTimers)
            {
                try { kvp.Value.Dispose(); } catch { }
            }
            _transcriptDebounceTimers.Clear();
        }
        catch { }

        // FileSystemWatcher entkoppeln, stoppen & disposen
        DisposeFileSystemWatchers();

        // Offene Sessions und UI-Status sichern, bevor Sessions freigegeben werden
        SaveUiStates();

        foreach (var session in _terminalSessions.ToList())
        {
            DisposeTerminalSession(session);
        }
        _terminalSessions.Clear();

        _hwnd = IntPtr.Zero;
    }

    private void UcModelPicker_ModelItemClick(object? sender, ModelPickerRowViewModel rowVm)
    {
        SelectModel(rowVm.Id);
    }

    private void UcModelPicker_FavoriteToggleClick(object? sender, string modelId)
    {
        ToggleFavoriteModel(modelId);
    }

    private void DisposeFileSystemWatchers()
    {
        if (_settingsWatcher != null)
        {
            try
            {
                _settingsWatcher.EnableRaisingEvents = false;
                _settingsWatcher.Changed -= OnSettingsFileChanged;
                _settingsWatcher.Created -= OnSettingsFileChanged;
                _settingsWatcher.Renamed -= OnSettingsFileRenamed;
                _settingsWatcher.Dispose();
            }
            catch { }
            _settingsWatcher = null;
        }
        lock (_settingsDebounceLock)
        {
            try { _settingsDebounceTimer?.Dispose(); _settingsDebounceTimer = null; } catch { }
        }

        if (_brainWatcher != null)
        {
            try
            {
                _brainWatcher.EnableRaisingEvents = false;
                _brainWatcher.Changed -= OnBrainTranscriptChanged;
                _brainWatcher.Created -= OnBrainTranscriptChanged;
                _brainWatcher.Deleted -= OnBrainTranscriptChanged;
                _brainWatcher.Renamed -= OnBrainTranscriptRenamed;
                _brainWatcher.Dispose();
            }
            catch { }
            _brainWatcher = null;
        }
        lock (_syncLockBrainTimer)
        {
            try { _brainWatcherTimer?.Dispose(); _brainWatcherTimer = null; } catch { }
            foreach (var kvp in _transcriptDebounceTimers)
            {
                try { kvp.Value.Dispose(); } catch { }
            }
            _transcriptDebounceTimers.Clear();
        }

        if (_conversationsWatcher != null)
        {
            try
            {
                _conversationsWatcher.EnableRaisingEvents = false;
                _conversationsWatcher.Changed -= OnConversationDbChanged;
                _conversationsWatcher.Created -= OnConversationDbChanged;
                _conversationsWatcher.Dispose();
            }
            catch { }
            _conversationsWatcher = null;
        }
        foreach (var kvp in _conversationDebounceTimers)
        {
            try { kvp.Value.Dispose(); } catch { }
        }
        _conversationDebounceTimers.Clear();

        if (_jetboxWatcher != null)
        {
            try
            {
                _jetboxWatcher.EnableRaisingEvents = false;
                _jetboxWatcher.Changed -= OnJetboxSummariesChanged;
                _jetboxWatcher.Created -= OnJetboxSummariesChanged;
                _jetboxWatcher.Dispose();
            }
            catch { }
            _jetboxWatcher = null;
        }
        try { _jetboxDebounceTimer?.Dispose(); _jetboxDebounceTimer = null; } catch { }

        if (_globalKnowledgeWatcher != null)
        {
            try
            {
                _globalKnowledgeWatcher.EnableRaisingEvents = false;
                _globalKnowledgeWatcher.Changed -= OnFileSystemEventTriggered;
                _globalKnowledgeWatcher.Created -= OnFileSystemEventTriggered;
                _globalKnowledgeWatcher.Dispose();
            }
            catch { }
            _globalKnowledgeWatcher = null;
        }

        if (_projectFileWatcher != null)
        {
            try
            {
                _projectFileWatcher.EnableRaisingEvents = false;
                _projectFileWatcher.Changed -= OnFileSystemEventTriggered;
                _projectFileWatcher.Created -= OnFileSystemEventTriggered;
                _projectFileWatcher.Deleted -= OnFileSystemEventTriggered;
                _projectFileWatcher.Renamed -= OnFileSystemEventTriggered;
                _projectFileWatcher.Dispose();
            }
            catch { }
            _projectFileWatcher = null;
        }

        lock (_syncLockFilesTabTimer)
        {
            _filesTabDebounceTimer?.Dispose();
            _filesTabDebounceTimer = null;
        }
        lock (_syncLockBadgeTimer)
        {
            try { _badgeDebounceTimer?.Dispose(); _badgeDebounceTimer = null; } catch { }
            try { _flashResetTimer?.Dispose(); _flashResetTimer = null; } catch { }
        }

        if (_cliFileWatcher != null)
        {
            try
            {
                _cliFileWatcher.EnableRaisingEvents = false;
                _cliFileWatcher.Changed -= OnAgyFileChanged;
                _cliFileWatcher.Created -= OnAgyFileChanged;
                _cliFileWatcher.Renamed -= OnAgyFileRenamed;
                _cliFileWatcher.Dispose();
            }
            catch { }
            _cliFileWatcher = null;
        }
        lock (_syncLockCliWatcherTimer)
        {
            try { _cliWatcherDebounceTimer?.Dispose(); _cliWatcherDebounceTimer = null; } catch { }
        }

        DisposeBuildFreshnessWatcher();

        

        Services.LocalizationService.Instance.LanguageChanged -= ApplyLocalization;
    }

    private void BtnTerminalShortcuts_Click(object sender, RoutedEventArgs e)
    {
        if (UcTerminalShortcuts != null)
        {
            UcTerminalShortcuts.Visibility = UcTerminalShortcuts.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (UcTerminalShortcuts.Visibility == Visibility.Visible)
            {
                if (UcModelPicker != null) UcModelPicker.Visibility = Visibility.Collapsed;
                if (PnlSystemCheckPanel != null) PnlSystemCheckPanel.Visibility = Visibility.Collapsed;
                if (PnlCustomPromptEditor != null) PnlCustomPromptEditor.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void BtnCloseShortcuts_Click(object sender, RoutedEventArgs e)
    {
        if (UcTerminalShortcuts != null) UcTerminalShortcuts.Visibility = Visibility.Collapsed;
    }

    private void BtnAskShellShortcuts_Click(object sender, RoutedEventArgs e)
    {
        if (UcTerminalShortcuts != null) UcTerminalShortcuts.Visibility = Visibility.Collapsed;

        if (TxtChatInput != null)
        {
            TxtChatInput.Text = "[Welche nützlichen Terminal Tastenkombinationen (Shortcuts) gibt es für meine Shell und wie kann ich sie anpassen?]";
            TxtChatInput.Focus(FocusState.Programmatic);
            TxtChatInput.SelectionStart = TxtChatInput.Text.Length;
        }
    }

    private void InitConsoleHostToggle()
    {
        try
        {
            if (TglConsoleHost != null)
            {
                // Reine Lese-Initialisierung: UI-Zustand ohne ungefragte Registry-Manipulation beim App-Start
                bool isEnforced = _uiStates.IsConsoleHostEnforced || ConsoleHostHelper.IsConsoleHostEnforced();
                TglConsoleHost.IsChecked = isEnforced;
                UpdateConsoleHostTooltip(isEnforced);
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[ConsoleHostToggle] Init error: {ex.Message}");
        }
    }

    private void TglConsoleHost_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool isEnforced = TglConsoleHost?.IsChecked == true;
            ConsoleHostHelper.SetConsoleHostEnforced(isEnforced);
            _uiStates.IsConsoleHostEnforced = isEnforced;
            SaveUiStates();
            UpdateConsoleHostTooltip(isEnforced);
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[ConsoleHostToggle] Click error: {ex.Message}");
        }
    }

    private void UpdateConsoleHostTooltip(bool isEnforced)
    {
        if (TglConsoleHost != null)
        {
            ToolTipService.SetToolTip(TglConsoleHost, isEnforced
                ? "Stiller Konsolenhost aktiv (Verhindert Terminal-Aufblitzen bei AGY-Tools)"
                : "Standard-Terminal-App aktiv (Windows Terminal)");
        }
    }





    public void ApplyLocalization()
    {
        if (TabChatsBtn != null) ToolTipService.SetToolTip(TabChatsBtn, Loc.Tr("Nav.ChatsTooltip"));
        if (TabPromptsBtn != null) ToolTipService.SetToolTip(TabPromptsBtn, Loc.Tr("Nav.PromptsTooltip"));
        if (TabFilesBtn != null) ToolTipService.SetToolTip(TabFilesBtn, Loc.Tr("Nav.FilesTooltip"));
        if (TabDocsBtn != null) ToolTipService.SetToolTip(TabDocsBtn, Loc.Tr("Nav.DocsTooltip"));
        if (TabGitBtn != null) ToolTipService.SetToolTip(TabGitBtn, Loc.Tr("Nav.GitTooltip"));

        if (TxtProjectsHeader != null) TxtProjectsHeader.Text = Loc.Tr("Toolbar.ProjectsHeader", "PROJEKTE");
        if (BtnNewChat != null) ToolTipService.SetToolTip(BtnNewChat, Loc.Tr("Toolbar.NewChatTooltip", "Neuen Chat starten (/clear · Setzt das Gespräch zurück und leert den Prompt-Verlauf)"));
        if (BtnRefreshSidebar != null) ToolTipService.SetToolTip(BtnRefreshSidebar, Loc.Tr("Toolbar.RefreshWorkspaceTooltip", "Workspace aktualisieren (Liest Chats, Projektdateien, Git-Status und Regeln neu ein)"));
        if (BtnChatsMore != null) ToolTipService.SetToolTip(BtnChatsMore, Loc.Tr("Toolbar.MoreChatActionsTooltip", "Weitere Chat-Aktionen (Pins lösen, mehrere Chats auswählen und bereinigen)"));
        if (MenuItemUnpinAll != null)
        {
            MenuItemUnpinAll.Text = Loc.Tr("Toolbar.UnpinAll", "Alle Pins lösen");
            ToolTipService.SetToolTip(MenuItemUnpinAll, Loc.Tr("Toolbar.UnpinAllTooltip", "Löst alle Fixierungen auf, sodass alle Chats wieder chronologisch sortiert werden"));
        }
        if (MenuItemDeleteSelectedChats != null)
        {
            MenuItemDeleteSelectedChats.Text = Loc.Tr("Toolbar.SelectAndDeleteChats", "Chats auswählen & löschen");
            ToolTipService.SetToolTip(MenuItemDeleteSelectedChats, Loc.Tr("Toolbar.SelectAndDeleteChatsTooltip", "Aktiviert die Kontrollkästchen zur Auswahl mehrerer Chats zum gemeinsamen Löschen"));
        }
        if (BtnProjectSettings != null) ToolTipService.SetToolTip(BtnProjectSettings, Loc.Tr("Toolbar.ProjectSettingsTooltip", "Projekte ein- / ausblenden"));
        if (BtnProjectHeaderLink != null) ToolTipService.SetToolTip(BtnProjectHeaderLink, Loc.Tr("Toolbar.OpenRepoTooltip", "Repository im Browser öffnen (oder Remote verknüpfen)"));
        if (BtnEditRemote != null) ToolTipService.SetToolTip(BtnEditRemote, Loc.Tr("Toolbar.EditRemoteTooltip", "Repository-Link hinzufügen / bearbeiten"));
        if (BtnAutoPush != null) ToolTipService.SetToolTip(BtnAutoPush, Loc.Tr("Toolbar.AutoPushTooltip", "Auto-Backup: Erstellt alle 30 Min. automatisch einen Git-Commit und pusht zu Remote"));

        UcFilesSidebar?.ApplyLocalization();
        UcChatSidebar?.ApplyLocalization();
        UcDocsSidebar?.ApplyLocalization();
        UcPromptsSidebar?.ApplyLocalization();
        UcGitSidebar?.ApplyLocalization();
        UcCoBrowser?.ApplyLocalization();
        UcModelPicker?.ApplyLocalization();
        UcPromptEditor?.ApplyLocalization();

        UpdateLanguageUiState();
    }

    private KeyEventHandler? _globalKeyDownHandler;

    private PointerEventHandler? _btnDeepThinkPointerPressedHandler;
    private PointerEventHandler? _btnDeepThinkPointerReleasedHandler;

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
    };
}

