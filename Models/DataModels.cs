using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
namespace AgyCliDashboard;



public class TerminalTabSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string? ConversationId { get; set; }
    public string Title { get; set; } = "Neuer Chat";
    public EasyWindowsTerminalControl.EasyTerminalControl Control { get; set; } = null!;
    /// <summary>Welcher Agent diese Session betreibt: "agy" oder "clawcode"</summary>
    public string AgentType { get; set; } = "agy";
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public bool IsUsed { get; set; } = false;
    public bool HasPromptBeenDispatched { get; set; } = false;
    public bool IsAutoReflexionEnabled { get; set; } = true;
    public bool IsDocumentViewActive { get; set; } = false;
    public bool IsWorking { get; set; } = false;
    public bool HasUnreadCompletion { get; set; } = false;
    public DateTime LastWorkingStateChangedUtc { get; set; } = DateTime.MinValue;
    public DateTime LastPromptDispatchedUtc { get; set; } = DateTime.MinValue;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class StagedAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public class UserPromptItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; set; }
    public string DisplayIndex => $"#{Index}";
    public string CleanText { get; set; } = string.Empty;
    public string RawText { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string FormattedTime => CreatedAt.ToLocalTime().ToString("HH:mm:ss");
    public string FormattedDateTime => CreatedAt.ToLocalTime().ToString("dd.MM. HH:mm");
    public bool HasAttachment { get; set; }
    public string AttachmentBadgeText { get; set; } = string.Empty;
    public Visibility AttachmentVisibility => HasAttachment ? Visibility.Visible : Visibility.Collapsed;

    private string _assistantSummary = string.Empty;
    public string AssistantSummary
    {
        get => _assistantSummary;
        set
        {
            if (_assistantSummary != value)
            {
                _assistantSummary = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AssistantSummary)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAssistantSummary)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AssistantSummaryVisibility)));
            }
        }
    }

    public bool HasAssistantSummary => !string.IsNullOrWhiteSpace(AssistantSummary);
    public Visibility AssistantSummaryVisibility => HasAssistantSummary ? Visibility.Visible : Visibility.Collapsed;
}

public class AgyChatSession : INotifyPropertyChanged
{
    private bool _isPinned = false;
    private bool _isSelected = false;
    private bool _isSelectionModeActive = false;
    private bool _isHovered = false;
    private bool _isAutoReflexionEnabled = true;
    private string _title = string.Empty;
    private DateTime _lastModified;
    private int _stepCount = 0;

    public string Id { get; set; } = string.Empty;
    public bool IsAutoReflexionEnabled
    {
        get => _isAutoReflexionEnabled;
        set
        {
            if (_isAutoReflexionEnabled != value)
            {
                _isAutoReflexionEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutoReflexionEnabled)));
            }
        }
    }
    public string Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            }
        }
    }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified
    {
        get => _lastModified;
        set
        {
            if (_lastModified != value)
            {
                _lastModified = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastModified)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayDate)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormattedDate)));
            }
        }
    }
    public string Workspace { get; set; } = string.Empty;
    public int StepCount
    {
        get => _stepCount;
        set
        {
            if (_stepCount != value)
            {
                _stepCount = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StepCount)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormattedDate)));
            }
        }
    }
    public DateTime DisplayDate => LastModified != DateTime.MinValue ? LastModified : CreatedAt;
    public string FormattedDate => StepCount > 0
        ? $"{DisplayDate:HH:mm} | {DisplayDate:dd.MM.} | {StepCount} Schritte"
        : $"{DisplayDate:HH:mm} | {DisplayDate:dd.MM.}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public bool IsSelectionModeActive
    {
        get => _isSelectionModeActive;
        set
        {
            if (_isSelectionModeActive != value)
            {
                _isSelectionModeActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionModeActive)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionCheckBoxVisibility)));
            }
        }
    }

    public Visibility SelectionCheckBoxVisibility => _isSelectionModeActive ? Visibility.Visible : Visibility.Collapsed;

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PinStrokeColor)));
            }
        }
    }

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (_isHovered != value)
            {
                _isHovered = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHovered)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HoverIndicatorVisibility)));
            }
        }
    }

    private static readonly Windows.UI.Color PinSky400Color = Microsoft.UI.ColorHelper.FromArgb(255, 56, 189, 248);
    private static readonly Windows.UI.Color Slate400Color = Microsoft.UI.ColorHelper.FromArgb(255, 148, 163, 184);

    public Brush PinStrokeColor => _isPinned
        ? new SolidColorBrush(PinSky400Color)
        : new SolidColorBrush(Slate400Color);
    public Visibility HoverIndicatorVisibility => _isHovered ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ProjectItemModel : INotifyPropertyChanged
{
    private bool _isVisible = true;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
            }
        }
    }
    private bool _isAutoReflexionEnabled = true;
    public bool IsAutoReflexionEnabled
    {
        get => _isAutoReflexionEnabled;
        set
        {
            if (_isAutoReflexionEnabled != value)
            {
                _isAutoReflexionEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAutoReflexionEnabled)));
            }
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class ProjectSelectorItemModel
{
    private static readonly SolidColorBrush BlueAccentBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 59, 130, 246)); // #3B82F6 Unser Blau (AgyAccent)
    private static readonly SolidColorBrush TextWhiteBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 248, 250, 252)); // #F8FAFC

    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsBoss { get; set; } = false;
    public bool IsGlobal { get; set; } = false;

    public Brush TextForeground => IsBoss ? BlueAccentBrush : TextWhiteBrush;
    public Windows.UI.Text.FontWeight TextFontWeight => IsBoss ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal;

    public override string ToString() => Name;
}

public class CustomInjectionItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsGlobal { get; set; } = true;
    public string TargetProject { get; set; } = string.Empty;
}

public class RuleControlData
{
    public bool german_language { get; set; } = true;
    public bool confirmation_before_action { get; set; } = true;
    public bool status_summary { get; set; } = true;
    public bool git_add_before_change { get; set; } = true;
    public bool write_protocol { get; set; } = true;
    public bool save_knowledge { get; set; } = true;
    public bool chat_coordination { get; set; } = true;
    public Dictionary<string, string> descriptions { get; set; } = new();
    public List<CustomInjectionItem> custom_global_injections { get; set; } = new();
}

public class SavedChatSessionState
{
    public string? ConversationId { get; set; }
    public string Title { get; set; } = "Chat";
    public string AgentType { get; set; } = "agy";
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public bool IsActive { get; set; } = false;
    public bool IsAutoReflexionEnabled { get; set; } = true;
}

public class UiStatesData
{
    public string LastSelectedProject { get; set; } = "Alle Projekte (Global)";
    public string LastActiveTab { get; set; } = "chats";
    public bool IsSidebarVisible { get; set; } = true;
    public double SidebarWidth { get; set; } = 350;
    public int TerminalFontSize { get; set; } = 16;
    public string LongPressTarget { get; set; } = string.Empty;
    public bool IsDeepThinkSticky { get; set; } = false;
    public bool IsConsoleHostEnforced { get; set; } = false;
    public bool RestoreOpenSessionsOnStartup { get; set; } = true;
    public List<SavedChatSessionState> OpenSessions { get; set; } = new();
    public string? LastActiveConversationId { get; set; }
    public List<string> RecentFirstFolders { get; set; } = new();
}

public class MaintenanceTimestampsData
{
    public Dictionary<string, string> Global { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> Projects { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ProjectSettingsData
{
    public static string GetDefaultBaseFolder()
    {
        // Community Edition: Standard-Benutzerverzeichnisse verwenden
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var repos = Path.Combine(userProfile, "source", "repos");
        if (Directory.Exists(repos)) return repos;
        var projects = Path.Combine(userProfile, "Projects");
        if (Directory.Exists(projects)) return projects;
        return projects;
    }

    public string BaseFolder { get; set; } = GetDefaultBaseFolder();
    public List<string> HiddenProjects { get; set; } = new();
    public List<string> DisabledAutoReflexionProjects { get; set; } = new();
    public string LastSelectedProject { get; set; } = string.Empty;
    public int TerminalFontSize { get; set; } = 16;
    public int ClawCodeHistoryLimit { get; set; } = 20;
    public const string DefaultClawCodeModel = "deepseek/deepseek-v4-flash";

    public bool IsAutoPushEnabled { get; set; } = false;
    public int AutoPushIntervalMinutes { get; set; } = 30;

    public string Language { get; set; } = "auto";
}

public class OpenRouterModelItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int ContextLength { get; set; }
    public bool IsFavorite { get; set; }
    public double PromptPrice { get; set; }
    public double CompletionPrice { get; set; }
    public long Created { get; set; }
    public double IntelligenceIndex { get; set; }
    public string Modality { get; set; } = string.Empty;
    public List<string> OutputModalities { get; set; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;
    public string Provider => Id.Contains('/') ? Id.Split('/')[0] : "";
    public string ContextFormatted
    {
        get
        {
            if (ContextLength >= 1000000) return $"{ContextLength / 1000000.0:0.#}M ctx";
            if (ContextLength >= 1000) return $"{ContextLength / 1000}k ctx";
            return ContextLength > 0 ? $"{ContextLength} ctx" : "";
        }
    }
    public bool IsFree => PromptPrice <= 0 && CompletionPrice <= 0;

    public bool IsImageModel
    {
        get
        {
            if (OutputModalities != null && OutputModalities.Any(o => string.Equals(o, "image", StringComparison.OrdinalIgnoreCase) || string.Equals(o, "audio", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (!string.IsNullOrEmpty(Modality) && (Modality.Contains("->text+image", StringComparison.OrdinalIgnoreCase) || Modality.Contains("->image", StringComparison.OrdinalIgnoreCase) || Modality.Contains("->text+audio", StringComparison.OrdinalIgnoreCase) || Modality.Contains("->audio", StringComparison.OrdinalIgnoreCase)))
                return true;

            string idLower = (Id ?? string.Empty).ToLowerInvariant();
            string nameLower = (Name ?? string.Empty).ToLowerInvariant();

            if (idLower.Contains("banana") || idLower.Contains("imagen") || idLower.Contains("-image") || idLower.Contains("diffusion") || idLower.Contains("lyria"))
                return true;

            if (nameLower.Contains("banana") || nameLower.Contains("imagen") || nameLower.Contains("image preview") || nameLower.Contains("image)") || nameLower.Contains("lyria"))
                return true;

            return false;
        }
    }
}

public static class TelemetryLogger
{
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "dashboard_telemetry.log");
    private static readonly string OldLogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "dashboard_telemetry.old.log");
    private const long MaxLogSizeBytes = 10 * 1024 * 1024; // 10 MB Limit
    private static readonly System.Collections.Concurrent.BlockingCollection<string> _queue = new();
    private static long _currentFileSize = -1;

    static TelemetryLogger()
    {
        Task.Run(() => 
        {
            try 
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(LogPath))
                {
                    _currentFileSize = new FileInfo(LogPath).Length;
                }
                else
                {
                    _currentFileSize = 0;
                }
            } 
            catch { }

            foreach (var message in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (_currentFileSize >= MaxLogSizeBytes)
                    {
                        try
                        {
                            if (File.Exists(OldLogPath)) File.Delete(OldLogPath);
                            if (File.Exists(LogPath)) File.Move(LogPath, OldLogPath);
                        }
                        catch { }
                        _currentFileSize = 0;
                    }

                    File.AppendAllText(LogPath, message);
                    _currentFileSize += System.Text.Encoding.UTF8.GetByteCount(message);
                }
                catch { }
            }
        });
    }

    public static void Log(string message)
    {
        try
        {
            _queue.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }
}

public class SystemRequirementItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Optional";
    public bool IsInstalled { get; set; }
    public string InstalledVersion { get; set; } = "Nicht installiert";
    public string LatestVersion { get; set; } = "Unbekannt";
    public bool IsUpToDate { get; set; } = true;
    public string StatusText { get; set; } = string.Empty;
    public string StatusColorHex { get; set; } = "#10B981";
    public string LeftLabel { get; set; } = "INSTALLIERT";
    public string RightLabel { get; set; } = "NEUESTE VERSION";
    public string ActionUrl { get; set; } = string.Empty;
    public string ActionButtonText { get; set; } = string.Empty;
    public string InstallPrompt { get; set; } = string.Empty;
    public string UninstallPrompt { get; set; } = string.Empty;
    public string IconSource { get; set; } = "ms-appx:///Assets/Icons/shield-check.svg";
}

public class CustomPromptItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Prompt";
    public string Color { get; set; } = "#8B5CF6";
    public string Prompt { get; set; } = string.Empty;
}

public class ProjectCustomPromptsData
{
    public int SelectedIndex { get; set; } = 0;
    public List<CustomPromptItem> Items { get; set; } = new();
}



