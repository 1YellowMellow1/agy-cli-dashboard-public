using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;

namespace AgyCliDashboard;

public sealed partial class MainWindow : Window
{
    public const int MaxTerminalTabs = 20;

    private readonly Dictionary<string, List<Action>> _terminalEventUnsubscribers = new();

    private void InitHiddenSyncTerminal()
    {
        try
        {
            if (_hiddenSyncTerminal != null)
            {
                bool isAlive = true;
                try
                {
                    if (_hiddenSyncTerminal.ConPTYTerm?.Process?.HasExited == true)
                    {
                        isAlive = false;
                    }
                }
                catch { }

                if (isAlive) return;

                TelemetryLogger.Log("[AutoSync] Dedicated hidden background sync terminal process was dead/exited. Cleaning up and re-initializing...");
                try
                {
                    if (HiddenSyncTerminalContainer != null && HiddenSyncTerminalContainer.Children.Contains(_hiddenSyncTerminal))
                    {
                        HiddenSyncTerminalContainer.Children.Remove(_hiddenSyncTerminal);
                    }
                }
                catch { }
                DisposeTerminalControl(_hiddenSyncTerminal);
                _hiddenSyncTerminal = null;
            }

            TelemetryLogger.Log("[AutoSync] Initializing dedicated hidden background sync terminal with fixed sync ID...");
            string codingDir = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) && Directory.Exists(_projectSettings.BaseFolder)
                ? _projectSettings.BaseFolder
                : ProjectSettingsData.GetDefaultBaseFolder();
            string syncConvId = "sync-knowledge-dashboard-sync";
            string startupCmd = Directory.Exists(codingDir)
                ? $"cmd.exe /k chcp 65001 >nul & cd /d \"{codingDir}\" & cls & agy --conversation {syncConvId}"
                : $"cmd.exe /k chcp 65001 >nul & cls & agy --conversation {syncConvId}";

            _hiddenSyncTerminal = new EasyWindowsTerminalControl.EasyTerminalControl
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0),
                StartupCommandLine = startupCmd
            };

            if (HiddenSyncTerminalContainer != null)
            {
                HiddenSyncTerminalContainer.Children.Add(_hiddenSyncTerminal);
            }
            TelemetryLogger.Log("[AutoSync] Hidden ConPTY sync terminal initialized successfully.");
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[AutoSync ERROR] InitHiddenSyncTerminal failed: {ex.Message}");
        }
    }

    private bool _isRestoringSessions = false;
    private bool _isSessionsRestored = false;

    private void RestoreSavedSessions()
    {
        if (!_uiStates.RestoreOpenSessionsOnStartup || _uiStates.OpenSessions == null || _uiStates.OpenSessions.Count == 0)
        {
            _isSessionsRestored = true;
            var initialTerm = CreateNewTerminalSession("Neuer Chat");
            _activeTerminalId = initialTerm.Id;
            _initialTerminalId = initialTerm.Id;
            SwitchToTerminalTab(initialTerm.Id);
            return;
        }

        string userBrainDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "brain");
        string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder();

        string? activeSessionIdToFocus = null;
        int restoredCount = 0;

        _isRestoringSessions = true;
        try
        {
            foreach (var savedState in _uiStates.OpenSessions)
            {
                if (string.IsNullOrWhiteSpace(savedState.ConversationId)) continue;

                if (_terminalSessions.Any(t => string.Equals(t.ConversationId, savedState.ConversationId, StringComparison.OrdinalIgnoreCase)))
                {
                    TelemetryLogger.Log($"[RestoreSession] Conversation already open, skipping duplicate: {savedState.ConversationId}");
                    continue;
                }

                string chatFolder = Path.Combine(userBrainDir, savedState.ConversationId);
                if (!Directory.Exists(chatFolder))
                {
                    TelemetryLogger.Log($"[RestoreSession] Conversation folder not found, skipping: {savedState.ConversationId}");
                    continue;
                }

                if (_terminalSessions.Count >= MaxTerminalTabs)
                {
                    TelemetryLogger.Log($"[RestoreSession] Max terminal tabs reached ({MaxTerminalTabs}), stopping restore.");
                    break;
                }

                try
                {
                    string projName = savedState.ProjectName;
                    string projPath = savedState.ProjectPath;
                    if (string.IsNullOrWhiteSpace(projPath) || !Directory.Exists(projPath))
                    {
                        projName = "Globaler Fokus";
                        projPath = baseFolder;
                    }

                    string agentType = string.Equals(savedState.AgentType, "clawcode", StringComparison.OrdinalIgnoreCase) ? "clawcode" : "agy";
                    string title = savedState.Title?.Trim() ?? "Chat";
                    if (string.Equals(title, "Neuer Chat", StringComparison.OrdinalIgnoreCase))
                    {
                        title = "Chat";
                    }

                    if (title.StartsWith("\U0001F680")) title = title.Substring("\U0001F680".Length).TrimStart();
                    if (title.StartsWith("\U0001F980")) title = title.Substring("\U0001F980".Length).TrimStart();

                    string? customCmd = null;
                    if (agentType == "clawcode")
                    {
                        if (TryBuildClawCodeStartupCommand(savedState.ConversationId, projPath, ref title, out string? clawStartupCmd, out _))
                        {
                            customCmd = clawStartupCmd;
                        }
                        else
                        {
                            agentType = "agy";
                            title = $"\U0001F680 {title}";
                        }
                    }
                    else
                    {
                        title = $"\U0001F680 {title}";
                    }

                    var session = CreateNewTerminalSession(title, savedState.ConversationId, customCmd, projectName: projName, projectPath: projPath);
                    session.ConversationId = savedState.ConversationId;
                    session.AgentType = agentType;
                    session.IsUsed = true;
                    session.HasPromptBeenDispatched = true;
                    session.IsAutoReflexionEnabled = savedState.IsAutoReflexionEnabled;

                    restoredCount++;

                    if (savedState.IsActive || string.Equals(savedState.ConversationId, _uiStates.LastActiveConversationId, StringComparison.OrdinalIgnoreCase))
                    {
                        activeSessionIdToFocus = session.Id;
                    }
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[RestoreSession] Error restoring chat '{savedState.ConversationId}': {ex.Message}");
                }
            }
        }
        finally
        {
            _isRestoringSessions = false;
        }

        _isSessionsRestored = true;

        if (restoredCount == 0 || _terminalSessions.Count == 0)
        {
            var initialTerm = CreateNewTerminalSession("Neuer Chat");
            _activeTerminalId = initialTerm.Id;
            _initialTerminalId = initialTerm.Id;
            SwitchToTerminalTab(initialTerm.Id);
        }
        else
        {
            _initialTerminalId = null;
            string targetId = activeSessionIdToFocus ?? _terminalSessions.Last().Id;
            SwitchToTerminalTab(targetId);
        }
    }

    private TerminalTabSession CreateNewTerminalSession(string title, string? conversationId = null, string? customStartupCommand = null, string? projectName = null, string? projectPath = null)
    {
        return CreateTerminalSessionInternal(title, conversationId, customStartupCommand, projectName, projectPath);
    }

    private TerminalTabSession CreateTerminalSessionInternal(string title, string? conversationId, string? customStartupCommand, string? projectName, string? projectPath)
    {
        TelemetryLogger.Log($"[Terminal Trace] Creating terminal session: '{title}', ConvId: '{conversationId}', Custom: {customStartupCommand != null}");
        string startupCmd;
        string sessionId = Guid.NewGuid().ToString("N");

        string sessionProjName = projectName ?? string.Empty;
        string sessionProjPath = projectPath ?? string.Empty;
        string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder();
        string baseFolderName = Path.GetFileName(baseFolder.TrimEnd('\\', '/'));

        // 1. Falls kein Projektname übergeben wurde, aber eine conversationId vorliegt: Workspace aus Chat ermitteln
        if (string.IsNullOrEmpty(sessionProjName) && !string.IsNullOrEmpty(conversationId))
        {
            AgyChatSession? existingChat = null;
            lock (_allMasterChats)
            {
                existingChat = _allMasterChats.FirstOrDefault(c => string.Equals(c.Id, conversationId, StringComparison.OrdinalIgnoreCase));
            }
            existingChat ??= _chats.FirstOrDefault(c => string.Equals(c.Id, conversationId, StringComparison.OrdinalIgnoreCase));

            if (existingChat != null && !string.IsNullOrWhiteSpace(existingChat.Workspace))
            {
                string ws = existingChat.Workspace.Trim();
                if (ws.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                {
                    try { ws = Uri.UnescapeDataString(new Uri(ws).LocalPath); } catch { }
                }

                if (Directory.Exists(ws) || ws.Contains('\\') || ws.Contains('/'))
                {
                    string cleanWs = ws.Replace('/', '\\').TrimEnd('\\');
                    string folderName = Path.GetFileName(cleanWs);

                    // Nur als konkretes Projekt werten, wenn es nicht der Root-Basisordner selbst ist
                    if (!string.Equals(cleanWs, baseFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(folderName, baseFolderName, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(folderName))
                    {
                        sessionProjName = folderName;
                        sessionProjPath = cleanWs;
                    }
                }
                else if (!ws.Equals("all", StringComparison.OrdinalIgnoreCase) && !ws.Contains("Global", StringComparison.OrdinalIgnoreCase))
                {
                    string candidate = Path.Combine(baseFolder, ws);
                    if (Directory.Exists(candidate))
                    {
                        sessionProjName = ws;
                        sessionProjPath = candidate;
                    }
                }
            }
        }

        // 2. Falls immer noch kein Projektname ermittelt wurde: UI-Auswahl / State heranziehen
        if (string.IsNullOrEmpty(sessionProjName))
        {
            string selectedProject = projectName ?? GetSelectedProjectName();
            if ((string.IsNullOrWhiteSpace(selectedProject) || selectedProject.Contains("Global") || selectedProject.Equals("all", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(_uiStates.LastSelectedProject) && !_uiStates.LastSelectedProject.Contains("Global"))
            {
                selectedProject = _uiStates.LastSelectedProject;
            }

            bool isGlobal = string.IsNullOrWhiteSpace(selectedProject) || selectedProject.Contains("Global") || selectedProject.Equals("all", StringComparison.OrdinalIgnoreCase);
            sessionProjName = isGlobal ? "Globaler Fokus" : selectedProject;
        }

        // 3. sessionProjPath sicherstellen und validieren
        bool isGlobalProj = string.IsNullOrWhiteSpace(sessionProjName) || sessionProjName.Contains("Global", StringComparison.OrdinalIgnoreCase) || sessionProjName.Equals("all", StringComparison.OrdinalIgnoreCase) || sessionProjName.Equals("Globaler Fokus", StringComparison.OrdinalIgnoreCase);
        if (isGlobalProj)
        {
            sessionProjName = "Globaler Fokus";
            sessionProjPath = baseFolder;
        }
        else if (string.IsNullOrEmpty(sessionProjPath) || !Directory.Exists(sessionProjPath))
        {
            sessionProjPath = Path.Combine(baseFolder, sessionProjName);
        }

        // Automatische Ausfallsicherheit: Lokale .agents/hooks.json und .reflectionignore für Projekt sicherstellen
        if (!isGlobalProj && !string.IsNullOrEmpty(sessionProjPath) && Directory.Exists(sessionProjPath))
        {
            string targetPath = sessionProjPath;
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                Helpers.AutoReflectionManager.EnsureProjectHookConfigured(targetPath);
                Helpers.AutoReflectionManager.EnsureProjectIgnoreFileExists(targetPath);
            });
        }

        // 4. ConPTY Startup-Befehl generieren
        if (!string.IsNullOrEmpty(customStartupCommand))
        {
            startupCmd = customStartupCommand;
        }
        else
        {
            string agyCmd = string.IsNullOrEmpty(conversationId) ? "agy" : $"agy --conversation {conversationId}";
            if (!isGlobalProj && !string.IsNullOrEmpty(sessionProjPath) && Directory.Exists(sessionProjPath))
            {
                startupCmd = $"cmd.exe /k chcp 65001 >nul & cd /d \"{sessionProjPath}\" & cls & {agyCmd}";
            }
            else
            {
                if (Directory.Exists(baseFolder))
                    startupCmd = $"cmd.exe /k chcp 65001 >nul & cd /d \"{baseFolder}\" & cls & {agyCmd}";
                else
                    startupCmd = $"cmd.exe /k chcp 65001 >nul & cls & {agyCmd}";
            }
        }

        var termControl = new EasyWindowsTerminalControl.EasyTerminalControl
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0),
            StartupCommandLine = startupCmd,
            LogConPTYOutput = true,
            FontSizeWhenSettingTheme = _fontSize,
            FontFamilyWhenSettingTheme = new FontFamily("Consolas"),
            Visibility = Visibility.Visible,
            Width = double.NaN,
            Height = double.NaN
        };

        TerminalTabSession? sessionRef = null;

        termControl.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter && (_isVoiceRecordingActive || _isVoicePendingInTerminal))
            {
                e.Handled = true;
                HandleVoiceSubmissionOnEnter();
                return;
            }

            var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            bool isCtrlDown = ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            if (isCtrlDown && (e.Key == VirtualKey.C || e.Key == VirtualKey.Insert))
            {
                if (this.TryCopyTerminalSelection(termControl))
                {
                    e.Handled = true;
                }
            }
            else if (e.Key != VirtualKey.Control && e.Key != VirtualKey.Shift && e.Key != VirtualKey.Menu && e.Key != VirtualKey.CapitalLock)
            {
                if (sessionRef != null)
                {
                    sessionRef.IsUsed = true;
                    if (sessionRef.Id == _initialTerminalId)
                    {
                        _initialTerminalId = null;
                    }
                }
            }
        };

        termControl.RightTapped += (s, e) =>
        {
            if (this.TryCopyTerminalSelection(termControl))
            {
                e.Handled = true;
            }
        };

        bool isUrlEventsAttached = false;
        termControl.Loaded += (s, e) =>
        {
            if (isUrlEventsAttached) return;
            isUrlEventsAttached = true;
            try
            {
                var innerTerminal = termControl.Terminal;

                if (innerTerminal != null)
                {
                    try
                    {
                        var targetObj = (object)innerTerminal;
                        var events = targetObj.GetType().GetEvents(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        
                        TelemetryLogger.Log($"[Terminal Events] Found {events.Length} events on innerTerminal: {string.Join(", ", events.Select(ev => ev.Name))}");

                        foreach (var openUrlEvt in events.Where(ev => ev.Name.IndexOf("Url", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                                     ev.Name.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                                     ev.Name.IndexOf("Hyperlink", StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            TelemetryLogger.Log($"[Terminal Event Match] Attaching to event: {openUrlEvt.Name} on {targetObj.GetType().Name}");
                            if (openUrlEvt.EventHandlerType != null)
                            {
                                var invoke = openUrlEvt.EventHandlerType.GetMethod("Invoke");
                                if (invoke != null)
                                {
                                    var parameters = invoke.GetParameters();
                                    if (parameters.Length >= 2)
                                    {
                                        var paramExprs = parameters.Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType, p.Name)).ToArray();
                                        Action<object, object> onUrlClicked = (sObj, aObj) =>
                                        {
                                            try
                                            {
                                                if (aObj != null)
                                                {
                                                    var props = aObj.GetType().GetProperties();
                                                    var targetProp = props.FirstOrDefault(p => p.Name.Equals("Uri", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Url", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Link", StringComparison.OrdinalIgnoreCase));
                                                    string? urlStr = targetProp?.GetValue(aObj)?.ToString();
                                                    if (!string.IsNullOrWhiteSpace(urlStr))
                                                    {
                                                        TelemetryLogger.Log($"[Terminal OpenURL] Copying link to clipboard (browser launch disabled): {urlStr}");
                                                        DispatcherQueue?.TryEnqueue(() => CopyUrlToClipboard(urlStr));
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                TelemetryLogger.Log($"[Terminal OpenURL Click Exception] {ex.Message}");
                                            }
                                        };

                                        var callExpr = System.Linq.Expressions.Expression.Call(
                                             System.Linq.Expressions.Expression.Constant(onUrlClicked.Target),
                                             onUrlClicked.Method,
                                             System.Linq.Expressions.Expression.Convert(paramExprs[0], typeof(object)),
                                             System.Linq.Expressions.Expression.Convert(paramExprs[1], typeof(object))
                                        );

                                        var lambda = System.Linq.Expressions.Expression.Lambda(openUrlEvt.EventHandlerType, callExpr, paramExprs);
                                        var compiledLambda = lambda.Compile();
                                        openUrlEvt.AddEventHandler(targetObj, compiledLambda);
                                        
                                        if (!_terminalEventUnsubscribers.ContainsKey(sessionId))
                                        {
                                            _terminalEventUnsubscribers[sessionId] = new List<Action>();
                                        }
                                        _terminalEventUnsubscribers[sessionId].Add(() => openUrlEvt.RemoveEventHandler(targetObj, compiledLambda));
                                    }
                                }
                            }
                        }

                        // Inner Terminal Input-Überwachung: Direkte Tastatureingaben im PTY erfassen
                        var inputEvt = events.FirstOrDefault(ev => ev.Name.Equals("CharacterReceived", StringComparison.OrdinalIgnoreCase) || 
                                                                   ev.Name.Equals("KeyDown", StringComparison.OrdinalIgnoreCase));
                        if (inputEvt?.EventHandlerType != null)
                        {
                            var invoke = inputEvt.EventHandlerType.GetMethod("Invoke");
                            if (invoke != null)
                            {
                                var parameters = invoke.GetParameters();
                                if (parameters.Length >= 2)
                                {
                                    var paramExprs = parameters.Select(p => System.Linq.Expressions.Expression.Parameter(p.ParameterType, p.Name)).ToArray();
                                    Action<object, object> onTerminalActivity = (sObj, aObj) =>
                                    {
                                        if (sessionRef != null)
                                        {
                                            sessionRef.IsUsed = true;
                                            if (sessionRef.Id == _initialTerminalId)
                                            {
                                                _initialTerminalId = null;
                                            }
                                        }
                                    };

                                    var callExpr = System.Linq.Expressions.Expression.Call(
                                         System.Linq.Expressions.Expression.Constant(onTerminalActivity.Target),
                                         onTerminalActivity.Method,
                                         System.Linq.Expressions.Expression.Convert(paramExprs[0], typeof(object)),
                                         System.Linq.Expressions.Expression.Convert(paramExprs[1], typeof(object))
                                    );

                                    var lambda = System.Linq.Expressions.Expression.Lambda(inputEvt.EventHandlerType, callExpr, paramExprs);
                                    var compiledLambda = lambda.Compile();
                                    inputEvt.AddEventHandler(targetObj, compiledLambda);

                                    if (!_terminalEventUnsubscribers.ContainsKey(sessionId))
                                    {
                                        _terminalEventUnsubscribers[sessionId] = new List<Action>();
                                    }
                                    _terminalEventUnsubscribers[sessionId].Add(() => inputEvt.RemoveEventHandler(targetObj, compiledLambda));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TelemetryLogger.Log($"[Terminal OpenURL Setup Exception] {ex.Message}");
                    }
                }
            }
            catch { }
            ApplyFontSizeAndFamily(termControl, _fontSize);
        };
        ApplyFontSizeAndFamily(termControl, _fontSize);

        bool autoReflexion = GetProjectAutoReflexionDefault(sessionProjName);
        if (!string.IsNullOrEmpty(conversationId))
        {
            autoReflexion = LoadChatAutoReflexion(conversationId, sessionProjName);
        }

        var session = new TerminalTabSession
        {
            Id = sessionId,
            Title = title,
            Control = termControl,
            ConversationId = conversationId,
            AgentType = customStartupCommand != null && customStartupCommand.Contains("claw") ? "clawcode" : "agy",
            ProjectName = sessionProjName,
            ProjectPath = sessionProjPath,
            IsUsed = !string.IsNullOrEmpty(conversationId) || customStartupCommand != null,
            HasPromptBeenDispatched = !string.IsNullOrEmpty(conversationId) || customStartupCommand != null,
            IsAutoReflexionEnabled = autoReflexion,
            CreatedAt = DateTime.UtcNow
        };
        sessionRef = session;

        _activeUiChatIds[session.Id] = 1;
        if (!string.IsNullOrEmpty(session.ConversationId))
        {
            _activeUiChatIds[session.ConversationId] = 1;
        }

        _terminalSessions.Add(session);

        if (TerminalContainer != null)
        {
            TerminalContainer.Children.Add(termControl);
        }

        _ = RefreshCliVersionDisplayAsync(forceRemoteCheck: false);

        return session;
    }

    private bool TryCopyTerminalSelection(EasyWindowsTerminalControl.EasyTerminalControl termControl)
    {
        if (termControl == null) return false;
        try
        {
            var innerTerminal = termControl.Terminal;
            if (innerTerminal != null)
            {
                var type = innerTerminal.GetType();

                var getSelTextMethod = type.GetMethod("GetSelectedText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (getSelTextMethod != null)
                {
                    try
                    {
                        var textObj = getSelTextMethod.Invoke(innerTerminal, null);
                        string? text = textObj as string;
                        if (!string.IsNullOrEmpty(text))
                        {
                            var dataPackage = new DataPackage();
                            dataPackage.SetText(text);
                            Clipboard.SetContent(dataPackage);
                            TelemetryLogger.Log($"[TryCopyTerminalSelection] Copied selected text ({text.Length} chars) via GetSelectedText().");
                            return true;
                        }
                    }
                    catch (Exception exGet)
                    {
                        TelemetryLogger.Log($"[TryCopyTerminalSelection GetSelectedText Error] {exGet.Message}");
                    }
                }

                bool hasSelection = false;
                var hasSelProp = type.GetProperty("HasSelection");
                if (hasSelProp != null && hasSelProp.GetValue(innerTerminal) is bool b)
                {
                    hasSelection = b;
                }

                var selTextProp = type.GetProperty("SelectedText") ?? type.GetProperty("Selection");
                string? selText = null;
                if (selTextProp != null)
                {
                    selText = selTextProp.GetValue(innerTerminal) as string;
                    if (!string.IsNullOrWhiteSpace(selText))
                    {
                        hasSelection = true;
                    }
                }

                if (hasSelection)
                {
                    if (!string.IsNullOrEmpty(selText))
                    {
                        var dataPackage = new DataPackage();
                        dataPackage.SetText(selText);
                        Clipboard.SetContent(dataPackage);
                        TelemetryLogger.Log($"[TryCopyTerminalSelection] Copied selected text ({selText.Length} chars) via selTextProp.");
                        return true;
                    }

                    var copyMethods = type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                          .Where(m => m.Name.Contains("CopySelection") || m.Name.Equals("Copy", StringComparison.OrdinalIgnoreCase))
                                          .ToList();

                    foreach (var copyMethod in copyMethods)
                    {
                        try
                        {
                            var parameters = copyMethod.GetParameters();
                            if (parameters.Length == 0)
                            {
                                copyMethod.Invoke(innerTerminal, null);
                                TelemetryLogger.Log($"[TryCopyTerminalSelection] Invoked {copyMethod.Name}().");
                                return true;
                            }
                            else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                            {
                                copyMethod.Invoke(innerTerminal, new object[] { false });
                                TelemetryLogger.Log($"[TryCopyTerminalSelection] Invoked {copyMethod.Name}(false).");
                                return true;
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TelemetryLogger.Log($"[TryCopyTerminalSelection ERROR] {ex.Message}");
        }

        return false;
    }

    private void RenderWorkspaceTabs(bool force = false)
    {
        if (DispatcherQueue != null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => RenderWorkspaceTabs(force));
            return;
        }

        if (PnlWorkspaceTabs == null) return;

        var sb = new System.Text.StringBuilder();
        foreach (var ts in _terminalSessions)
        {
            bool isActive = string.IsNullOrEmpty(_currentEditingFilePath) && _activeTerminalId == ts.Id;
            bool isCloseable = _terminalSessions.Count > 1;
            sb.Append($"term:{ts.Id}:{ts.Title}:{isActive}:{isCloseable};");
        }
        foreach (var path in _openTabFilePaths)
        {
            bool isActive = string.Equals(_currentEditingFilePath, path, StringComparison.OrdinalIgnoreCase);
            sb.Append($"file:{path}:{isActive};");
        }
        string signature = sb.ToString();

        if (!force && signature == _lastRenderedTabsSignature && PnlWorkspaceTabs.Children.Count > 0)
        {
            return;
        }
        _lastRenderedTabsSignature = signature;

        PnlWorkspaceTabs.Children.Clear();

        Border? activeBorder = null;
        foreach (var ts in _terminalSessions)
        {
            bool isActive = string.IsNullOrEmpty(_currentEditingFilePath) && _activeTerminalId == ts.Id;
            bool isCloseable = _terminalSessions.Count > 1;
            var termBorder = CreateWorkspaceTabBorder(ts.Title, isActive, isCloseable, tag: "terminal:" + ts.Id, agentType: ts.AgentType);
            PnlWorkspaceTabs.Children.Add(termBorder);
            if (isActive) activeBorder = termBorder;
        }

        foreach (var path in _openTabFilePaths)
        {
            bool isActive = string.Equals(_currentEditingFilePath, path, StringComparison.OrdinalIgnoreCase);
            string title = Path.GetFileName(path);
            var fileBorder = CreateWorkspaceTabBorder(title, isActive, isCloseable: true, tag: path);
            PnlWorkspaceTabs.Children.Add(fileBorder);
            if (isActive) activeBorder = fileBorder;
        }

        if (activeBorder != null)
        {
            activeBorder.Loaded += (s, e) =>
            {
                try { activeBorder.StartBringIntoView(); } catch { }
                UpdateWorkspaceTabScrollButtons();
            };
            if (activeBorder.IsLoaded)
            {
                try { activeBorder.StartBringIntoView(); } catch { }
            }
        }

        UpdateOpenRouterBalanceVisibility();
        UpdateAssociatedCoBrowserChat();
        UpdateWorkspaceTabScrollButtons();
    }

    private Border CreateWorkspaceTabBorder(string title, bool isActive, bool isCloseable, string tag, string agentType = "agy")
    {
        var border = new Border
        {
            Background = (Brush)Application.Current.Resources[isActive ? "AgyTabActiveBg" : "AgySurfaceBg"],
            BorderBrush = (Brush)Application.Current.Resources[isActive ? "AgyAccent" : "AgyBorderFaint"],
            BorderThickness = new Thickness(1, isActive ? 2 : 1, 1, 0),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            MinWidth = 100,
            MaxWidth = 200,
            Height = 32,
            Padding = new Thickness(10, 4, 8, 4),
            Tag = tag
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (isCloseable)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        bool isTerminal = tag.StartsWith("terminal");
        FrameworkElement iconElement;
        if (isTerminal && string.Equals(agentType, "clawcode", StringComparison.OrdinalIgnoreCase))
        {
            iconElement = new TextBlock
            {
                Text = "🦞",
                FontSize = 14.4,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        else
        {
            string iconPath = isTerminal
                ? "ms-appx:///Assets/Icons/antigravity-logo.svg"
                : "ms-appx:///Assets/Icons/file-text.svg";

            iconElement = new ImageIcon
            {
                Source = CreateHighResSvg(iconPath),
                Width = 13,
                Height = 13,
                Margin = new Thickness(0, 0, 6, 0),
                Opacity = isActive ? 1.0 : 0.75
            };
        }
        Grid.SetColumn(iconElement, 0);

        string displayTitle = title ?? "Unbenannt";
        if (displayTitle.StartsWith("\U0001F9BE ") || displayTitle.StartsWith("🦞 ")) displayTitle = displayTitle.Substring(displayTitle.IndexOf(' ') + 1);
        if (displayTitle.StartsWith("\U0001F980 ") || displayTitle.StartsWith("🦀 ")) displayTitle = displayTitle.Substring(displayTitle.IndexOf(' ') + 1);
        if (displayTitle.StartsWith("\U0001F680 ") || displayTitle.StartsWith("🚀 ")) displayTitle = displayTitle.Substring(displayTitle.IndexOf(' ') + 1);

        var txt = new TextBlock
        {
            Text = displayTitle,
            FontSize = 12.5,
            FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = (Brush)Application.Current.Resources[isActive ? "AgyTextPrimary" : "AgyTextSecondary"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(txt, 1);

        grid.Children.Add(iconElement);
        grid.Children.Add(txt);

        if (isCloseable)
        {
            var closeBtn = new Button
            {
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(6, 0, 0, 0),
                Tag = tag
            };
            ToolTipService.SetToolTip(closeBtn, "Tab schließen");
            closeBtn.Content = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/x.svg"), Width = 9, Height = 9, Opacity = 0.65 };
            closeBtn.Click += (s, e) =>
            {
                if (tag.StartsWith("terminal:"))
                {
                    string termId = tag.Substring("terminal:".Length);
                    CloseTerminalTab(termId);
                }
                else
                {
                    CloseFileTab(tag);
                }
            };
            Grid.SetColumn(closeBtn, 2);
            grid.Children.Add(closeBtn);
        }

        border.Child = grid;

        border.PointerPressed += (s, e) =>
        {
            if (e.OriginalSource is DependencyObject dep && FindParent<Button>(dep) != null)
            {
                return;
            }

            var ptrPt = e.GetCurrentPoint(border);
            if (ptrPt.Properties.IsRightButtonPressed || ptrPt.Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.RightButtonPressed)
            {
                return;
            }

            if (ptrPt.Properties.IsMiddleButtonPressed || ptrPt.Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.MiddleButtonPressed)
            {
                if (tag.StartsWith("terminal:"))
                {
                    if (_terminalSessions.Count > 1)
                    {
                        string termId = tag.Substring("terminal:".Length);
                        CloseTerminalTab(termId);
                    }
                }
                else if (tag != "terminal")
                {
                    CloseFileTab(tag);
                }
                e.Handled = true;
                return;
            }

            if (tag.StartsWith("terminal:"))
            {
                string termId = tag.Substring("terminal:".Length);
                SwitchToTerminalTab(termId);
            }
            else if (tag == "terminal")
            {
                SwitchToTerminalTab();
            }
            else
            {
                if (string.Equals(_currentEditingFilePath, tag, StringComparison.OrdinalIgnoreCase))
                {
                    SwitchToTab(tag);
                }
                else
                {
                    OpenFileInEditor(tag);
                }
            }
        };

        // Context Menu (Rechtsklick mit Shift) auf dem Tab
        var tabFlyout = new MenuFlyout();

        var openLastResponseItem = new MenuFlyoutItem
        {
            Text = "Letzte KI-Antwort im Editor öffnen",
            Icon = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/file-text.svg") }
        };
        openLastResponseItem.Click += (s, e) =>
        {
            if (tag.StartsWith("terminal:"))
            {
                string termId = tag.Substring("terminal:".Length);
                SwitchToTerminalTab(termId);
            }
            else if (tag == "terminal")
            {
                SwitchToTerminalTab();
            }
            OpenLastResponseInEditor();
        };
        tabFlyout.Items.Add(openLastResponseItem);
        tabFlyout.Items.Add(new MenuFlyoutSeparator());

        if (isCloseable)
        {
            var closeItem = new MenuFlyoutItem
            {
                Text = "Tab schließen",
                Icon = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/x.svg") }
            };
            closeItem.Click += (s, e) =>
            {
                if (tag.StartsWith("terminal:"))
                {
                    string termId = tag.Substring("terminal:".Length);
                    CloseTerminalTab(termId);
                }
                else
                {
                    CloseFileTab(tag);
                }
            };
            tabFlyout.Items.Add(closeItem);
        }

        bool hasMultipleTabs = (_terminalSessions.Count + _openTabFilePaths.Count) > 1;

        var closeOthersItem = new MenuFlyoutItem
        {
            Text = "Andere Tabs schließen",
            IsEnabled = hasMultipleTabs
        };
        closeOthersItem.Click += (s, e) =>
        {
            if (tag.StartsWith("terminal:"))
            {
                string termId = tag.Substring("terminal:".Length);
                SwitchToTerminalTab(termId);
                BtnCloseOtherTabs_Click(s, e);
            }
            else
            {
                _openTabFilePaths.RemoveAll(p => !string.Equals(p, tag, StringComparison.OrdinalIgnoreCase));
                if (!string.Equals(_currentEditingFilePath, tag, StringComparison.OrdinalIgnoreCase))
                {
                    OpenFileInEditor(tag);
                }
                RenderWorkspaceTabs(force: true);
                ShowNotice("Tab-Verwaltung", $"Andere Tabs geschlossen. '{Path.GetFileName(tag)}' bleibt geöffnet.", InfoBarSeverity.Informational);
            }
        };
        tabFlyout.Items.Add(closeOthersItem);

        if (_openTabFilePaths.Count > 0)
        {
            var closeAllFilesItem = new MenuFlyoutItem
            {
                Text = "Alle Dateitabs schließen"
            };
            closeAllFilesItem.Click += (s, e) =>
            {
                _openTabFilePaths.Clear();
                SwitchToTerminalTab();
                RenderWorkspaceTabs(force: true);
            };
            tabFlyout.Items.Add(closeAllFilesItem);
        }

        if (!tag.StartsWith("terminal"))
        {
            tabFlyout.Items.Add(new MenuFlyoutSeparator());
            var copyPathItem = new MenuFlyoutItem
            {
                Text = "Dateipfad kopieren",
                Icon = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/copy.svg") }
            };
            copyPathItem.Click += (s, e) =>
            {
                var dp = new DataPackage();
                dp.SetText(tag);
                Clipboard.SetContent(dp);
                ShowNotice("Kopiert", $"Pfad kopiert: {tag}", InfoBarSeverity.Informational);
            };
            tabFlyout.Items.Add(copyPathItem);
        }

        border.RightTapped += (s, e) =>
        {
            e.Handled = true;
            bool isShift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (isShift)
            {
                tabFlyout.ShowAt(border);
                return;
            }

            string? targetTermId = tag.StartsWith("terminal:") ? tag.Substring("terminal:".Length) : null;
            bool isCurrentTab = (targetTermId != null && targetTermId == _activeTerminalId) || (tag == "terminal" && string.IsNullOrEmpty(_activeTerminalId));

            if (isCurrentTab)
            {
                TelemetryLogger.Log("[WorkspaceTab RightTapped] Toggle der letzten KI-Antwort im Split-View.");
                ToggleLastResponseSplitView();
            }
            else
            {
                if (targetTermId != null)
                {
                    SwitchToTerminalTab(targetTermId);
                }
                else if (tag == "terminal")
                {
                    SwitchToTerminalTab();
                }

                TelemetryLogger.Log("[WorkspaceTab RightTapped] Tab-Wechsel & Öffnen der letzten KI-Antwort im Split-View.");
                if (!_isResponseSplitOpen)
                {
                    ToggleLastResponseSplitView();
                }
            }
        };

        string tabTooltip = isTerminal
            ? $"{title}\n(Rechtsklick: Antwort als Split-View öffnen/schließen)"
            : $"{title} ({tag})\n(Rechtsklick: Antwort als Split-View öffnen/schließen)";
        ToolTipService.SetToolTip(border, tabTooltip);

        return border;
    }

    private void SvWorkspaceTabs_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (SvWorkspaceTabs == null) return;
        var ptrPt = e.GetCurrentPoint(SvWorkspaceTabs);
        int delta = ptrPt.Properties.MouseWheelDelta;
        if (delta == 0) return;

        double targetOffset;
        if (ptrPt.Properties.IsHorizontalMouseWheel)
        {
            targetOffset = SvWorkspaceTabs.HorizontalOffset + delta;
        }
        else
        {
            // Vertikales Mausrad: nach unten drehen (negativer Delta) scrollt nach rechts (+)
            // nach oben drehen (positiver Delta) scrollt nach links (-)
            targetOffset = SvWorkspaceTabs.HorizontalOffset - (delta * 1.2);
        }

        targetOffset = Math.Clamp(targetOffset, 0, Math.Max(0, SvWorkspaceTabs.ScrollableWidth));
        SvWorkspaceTabs.ChangeView(targetOffset, null, null, disableAnimation: false);
        e.Handled = true;
        UpdateWorkspaceTabScrollButtons();
    }

    private void SvWorkspaceTabs_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        UpdateWorkspaceTabScrollButtons();
    }

    private void SvWorkspaceTabs_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateWorkspaceTabScrollButtons();
    }

    private void BtnScrollTabsLeft_Click(object sender, RoutedEventArgs e)
    {
        if (SvWorkspaceTabs == null) return;
        double targetOffset = Math.Max(0, SvWorkspaceTabs.HorizontalOffset - 160);
        SvWorkspaceTabs.ChangeView(targetOffset, null, null, disableAnimation: false);
        UpdateWorkspaceTabScrollButtons();
    }

    private void BtnScrollTabsRight_Click(object sender, RoutedEventArgs e)
    {
        if (SvWorkspaceTabs == null) return;
        double targetOffset = Math.Min(SvWorkspaceTabs.ScrollableWidth, SvWorkspaceTabs.HorizontalOffset + 160);
        SvWorkspaceTabs.ChangeView(targetOffset, null, null, disableAnimation: false);
        UpdateWorkspaceTabScrollButtons();
    }

    private void FlyoutWorkspaceTabsList_Opening(object? sender, object e)
    {
        PopulateWorkspaceTabsFlyout();
    }

    private void PopulateWorkspaceTabsFlyout()
    {
        if (FlyoutWorkspaceTabsList == null) return;
        FlyoutWorkspaceTabsList.Items.Clear();

        if (_terminalSessions.Count > 0)
        {
            foreach (var ts in _terminalSessions)
            {
                bool isActive = string.IsNullOrEmpty(_currentEditingFilePath) && _activeTerminalId == ts.Id;
                string iconPath = string.Equals(ts.AgentType, "clawcode", StringComparison.OrdinalIgnoreCase)
                    ? "ms-appx:///Assets/Icons/clawcode-logo.svg"
                    : "ms-appx:///Assets/Icons/antigravity-logo.svg";

                var item = new MenuFlyoutItem
                {
                    Text = (isActive ? "●  " : "    ") + (ts.Title ?? "Terminal"),
                    FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Icon = new ImageIcon { Source = CreateHighResSvg(iconPath), Width = 14, Height = 14 }
                };
                string termId = ts.Id;
                item.Click += (s, e) =>
                {
                    SwitchToTerminalTab(termId);
                };
                FlyoutWorkspaceTabsList.Items.Add(item);
            }
        }

        if (_terminalSessions.Count > 0 && _openTabFilePaths.Count > 0)
        {
            FlyoutWorkspaceTabsList.Items.Add(new MenuFlyoutSeparator());
        }

        if (_openTabFilePaths.Count > 0)
        {
            foreach (var filePath in _openTabFilePaths)
            {
                bool isActive = string.Equals(_currentEditingFilePath, filePath, StringComparison.OrdinalIgnoreCase);
                string rawName = Path.GetFileName(filePath);
                string fileName = string.IsNullOrEmpty(rawName) ? filePath : rawName;

                var item = new MenuFlyoutItem
                {
                    Text = (isActive ? "●  " : "    ") + fileName,
                    FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    Icon = new ImageIcon { Source = CreateHighResSvg("ms-appx:///Assets/Icons/file-text.svg"), Width = 14, Height = 14 }
                };
                string capturedPath = filePath;
                item.Click += (s, e) =>
                {
                    if (string.Equals(_currentEditingFilePath, capturedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        SwitchToTab(capturedPath);
                    }
                    else
                    {
                        OpenFileInEditor(capturedPath);
                    }
                };
                FlyoutWorkspaceTabsList.Items.Add(item);
            }
        }

        if (FlyoutWorkspaceTabsList.Items.Count == 0)
        {
            FlyoutWorkspaceTabsList.Items.Add(new MenuFlyoutItem { Text = "Keine Tabs geöffnet", IsEnabled = false });
        }
    }

    private void UpdateWorkspaceTabScrollButtons()
    {
        if (SvWorkspaceTabs == null) return;

        DispatcherQueue?.TryEnqueue(() =>
        {
            if (SvWorkspaceTabs == null) return;
            double scrollable = SvWorkspaceTabs.ScrollableWidth;
            double offset = SvWorkspaceTabs.HorizontalOffset;

            bool hasOverflow = scrollable > 4;

            if (BtnScrollTabsLeft != null)
            {
                BtnScrollTabsLeft.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
                BtnScrollTabsLeft.IsEnabled = offset > 4;
                BtnScrollTabsLeft.Opacity = (offset > 4) ? 1.0 : 0.35;
            }

            if (BtnScrollTabsRight != null)
            {
                BtnScrollTabsRight.Visibility = hasOverflow ? Visibility.Visible : Visibility.Collapsed;
                BtnScrollTabsRight.IsEnabled = offset < (scrollable - 4);
                BtnScrollTabsRight.Opacity = (offset < (scrollable - 4)) ? 1.0 : 0.35;
            }

            int totalTabs = _terminalSessions.Count + _openTabFilePaths.Count;
            if (BtnOpenTabsList != null)
            {
                BtnOpenTabsList.Visibility = (totalTabs > 1 || hasOverflow) ? Visibility.Visible : Visibility.Collapsed;
            }
        });
    }

    private void SwitchToTerminalTab(string? terminalId = null)
    {
        ResetModeToggles();
        if (!string.IsNullOrEmpty(_currentEditingFilePath) && UcEditor != null)
        {
            try
            {
                if (_currentEditingFilePath.EndsWith(".rtf", StringComparison.OrdinalIgnoreCase))
                {
                    WriteAllTextAtomic(_currentEditingFilePath, UcEditor.GetRtf());
                }
                else
                {
                    WriteAllTextAtomic(_currentEditingFilePath, UcEditor.GetText());
                }
            }
            catch { }
        }
        _currentEditingFilePath = null;
        if (!string.IsNullOrEmpty(terminalId) && _terminalSessions.Any(t => t.Id == terminalId))
        {
            _activeTerminalId = terminalId;
        }
        else if (_terminalSessions.Count > 0)
        {
            if (string.IsNullOrEmpty(_activeTerminalId) || !_terminalSessions.Any(t => t.Id == _activeTerminalId))
            {
                _activeTerminalId = _terminalSessions[0].Id;
            }
        }
        else
        {
            _activeTerminalId = string.Empty;
        }

        foreach (var ts in _terminalSessions)
        {
            if (ts.Id == _activeTerminalId)
            {
                ts.Control.Visibility = Visibility.Visible;
                ts.Control.Width = double.NaN;
                ts.Control.Height = double.NaN;
                ts.Control.UpdateLayout();
                ApplyFontSizeAndFamily(ts.Control, _fontSize);
            }
            else
            {
                ts.Control.Visibility = Visibility.Collapsed;
                ts.Control.Width = 0;
                ts.Control.Height = 0;
            }
        }

        if (_isResponseSplitOpen)
        {
            CloseResponseSplit();
        }
        else
        {
            if (ColTerminal != null) ColTerminal.Width = new GridLength(1, GridUnitType.Star);
            if (ColEditor != null) ColEditor.Width = new GridLength(0);
            if (SplitterEditor != null) SplitterEditor.Visibility = Visibility.Collapsed;
        }

        if (TerminalContainer != null) TerminalContainer.Visibility = Visibility.Visible;
        if (EditorContainer != null) EditorContainer.Visibility = Visibility.Collapsed;
        if (UcEditor != null) UcEditor.Visibility = Visibility.Collapsed;
        RenderWorkspaceTabs();

        if (!string.IsNullOrEmpty(_activeTerminalId))
        {
            var activeSession = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
            if (activeSession != null)
            {
                var (focusProjName, _) = GetActiveTerminalProjectFocus();
                string targetProject = string.IsNullOrWhiteSpace(focusProjName) || focusProjName.Contains("Global") ? "Alle Projekte (Global)" : focusProjName;
                if (!string.Equals(_uiStates.LastSelectedProject, targetProject, StringComparison.OrdinalIgnoreCase))
                {
                    _uiStates.LastSelectedProject = targetProject;
                    _projectSettings.LastSelectedProject = targetProject;
                    UpdateProjectComboBox();
                    UpdateActiveTabContent();
                    UpdateProjectFileWatcher();
                }

                Helpers.TerminalBufferManager.EnforceBufferLimit(activeSession.Control);
                try
                {
                    TxtChatInput?.Focus(FocusState.Programmatic);
                }
                catch { }

                if (!string.IsNullOrEmpty(activeSession.ConversationId))
                {
                    string savedModel = LoadChatModel(activeSession.ConversationId);
                    if (activeSession.AgentType == "clawcode")
                    {
                        if (string.IsNullOrEmpty(savedModel) || !savedModel.Contains('/') || savedModel.Contains(' '))
                        {
                            savedModel = LoadClawCodeModelFromSettings();
                        }
                        if (string.IsNullOrEmpty(savedModel) || !savedModel.Contains('/') || savedModel.Contains(' '))
                        {
                            savedModel = ProjectSettingsData.DefaultClawCodeModel;
                        }

                        WriteModelToClawSettings(savedModel);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(savedModel))
                        {
                            string brainPath = ResolveActiveTranscriptPath(activeSession.ConversationId);
                            if (_transcriptTokensCache.TryGetValue(brainPath, out var cached) && !string.IsNullOrWhiteSpace(cached.DetectedModel))
                            {
                                savedModel = cached.DetectedModel;
                            }
                        }
                        if (string.IsNullOrEmpty(savedModel))
                        {
                            savedModel = LoadAgyModelFromSettings();
                        }
                    }

                    if (!string.IsNullOrEmpty(savedModel))
                    {
                        if (TxtModelName != null) CurrentModelId = savedModel;
                        AgyChatSession? chat = null;
                        lock (_allMasterChats)
                        {
                            chat = _allMasterChats.FirstOrDefault(c => c.Id == activeSession.ConversationId);
                        }
                        chat ??= _chats.FirstOrDefault(c => c.Id == activeSession.ConversationId);
                        if (chat != null) chat.SelectedModel = savedModel;
                        SaveChatModel(activeSession.ConversationId, savedModel);
                    }
                }

                _isUpdatingTglAutoReflexion = true;
                try
                {
                    if (UcPromptEditor?.TglAutoReflexion != null)
                    {
                        UcPromptEditor.TglAutoReflexion.IsChecked = activeSession.IsAutoReflexionEnabled;
                    }
                }
                finally
                {
                    _isUpdatingTglAutoReflexion = false;
                }
            }
        }

        UpdateProjectFocusBadgeUI();
        UpdateTokenPeakMeter(false);

        // Sidebar-Overlays verbergen, die zum vorherigen Kontext gehörten
        HideModelPickerPanel();
        if (UcTerminalShortcuts != null) UcTerminalShortcuts.Visibility = Visibility.Collapsed;
        if (PnlSystemCheckPanel != null) PnlSystemCheckPanel.Visibility = Visibility.Collapsed;

        // Forciertes Live-Update für geöffnete Sidebar-Panels bei Tabwechsel
        if (PnlCustomPromptEditor != null && PnlCustomPromptEditor.Visibility == Visibility.Visible)
        {
            SyncCustomPromptEditorPanel(force: true);
        }

        if (string.Equals(_uiStates.LastActiveTab, "prompts", StringComparison.OrdinalIgnoreCase))
        {
            UpdatePromptsTab(force: true);
        }

        SaveUiStates();
    }

    private void SwitchToTab(string filePath)
    {
        _currentEditingFilePath = filePath;
        foreach (var ts in _terminalSessions)
        {
            ts.Control.Visibility = Visibility.Collapsed;
            ts.Control.Width = 0;
            ts.Control.Height = 0;
        }

        if (_isResponseSplitOpen)
        {
            CloseResponseSplit();
        }

        if (ColTerminal != null) ColTerminal.Width = new GridLength(0);
        if (ColEditor != null) ColEditor.Width = new GridLength(1, GridUnitType.Star);
        if (SplitterEditor != null) SplitterEditor.Visibility = Visibility.Collapsed;

        if (TerminalContainer != null) TerminalContainer.Visibility = Visibility.Collapsed;
        if (EditorContainer != null) EditorContainer.Visibility = Visibility.Visible;
        if (UcEditor != null) UcEditor.Visibility = Visibility.Visible;
        RenderWorkspaceTabs();
    }

    private void CloseTerminalTab(string terminalId)
    {
        var session = _terminalSessions.FirstOrDefault(t => t.Id == terminalId);
        if (session != null)
        {
            if (_terminalSessions.Count == 1)
            {
                var newSess = CreateNewTerminalSession("Neuer Chat");
                _activeTerminalId = newSess.Id;
                _initialTerminalId = newSess.Id;

                DisposeTerminalSession(session);
                TerminalContainer?.Children.Remove(session.Control);
                _terminalSessions.Remove(session);

                SwitchToTerminalTab(newSess.Id);
                RefreshActiveTerminal(forceRestart: false, showNotice: false);
                return;
            }

            DisposeTerminalSession(session);
            TerminalContainer?.Children.Remove(session.Control);
            _terminalSessions.Remove(session);

            if (_activeTerminalId == terminalId)
            {
                _activeTerminalId = string.Empty;
            }
        }

        EnsureAtLeastOneTerminalSession();
        if (!string.IsNullOrEmpty(_activeTerminalId))
        {
            RefreshActiveTerminal(forceRestart: false, showNotice: false);
        }
        SaveUiStates();
    }

    private void EnsureAtLeastOneTerminalSession()
    {
        if (_terminalSessions == null || _terminalSessions.Count == 0)
        {
            TelemetryLogger.Log("[Terminal] No terminal sessions remaining after deletion. Opening a new terminal session automatically.");
            var newSess = CreateNewTerminalSession("Neuer Chat");
            _activeTerminalId = newSess.Id;
            _initialTerminalId = newSess.Id;
            SwitchToTerminalTab(newSess.Id);
        }
        else if (string.IsNullOrEmpty(_activeTerminalId) || !_terminalSessions.Any(s => s.Id == _activeTerminalId))
        {
            string currentProject = GetSelectedProjectName();
            bool isCurrentGlobal = string.IsNullOrWhiteSpace(currentProject) || currentProject.Contains("Global", StringComparison.OrdinalIgnoreCase) || currentProject.Equals("all", StringComparison.OrdinalIgnoreCase);

            var sameProjectSession = _terminalSessions.LastOrDefault(s =>
            {
                bool isSessionGlobal = string.IsNullOrWhiteSpace(s.ProjectName) || s.ProjectName.Contains("Global", StringComparison.OrdinalIgnoreCase) || s.ProjectName.Equals("all", StringComparison.OrdinalIgnoreCase);
                if (isCurrentGlobal && isSessionGlobal) return true;
                return !isCurrentGlobal && !isSessionGlobal && string.Equals(s.ProjectName, currentProject, StringComparison.OrdinalIgnoreCase);
            });
            
            var targetSession = sameProjectSession ?? _terminalSessions.LastOrDefault();
            if (targetSession != null)
            {
                _activeTerminalId = targetSession.Id;
            }
            else
            {
                TelemetryLogger.Log("[Terminal] No remaining sessions. Creating a new one automatically.");
                var newSess = CreateNewTerminalSession("Neuer Chat");
                _activeTerminalId = newSess.Id;
                _initialTerminalId = newSess.Id;
            }
            SwitchToTerminalTab(_activeTerminalId);
        }
        else
        {
            RenderWorkspaceTabs(force: true);
        }
    }

    private void CloseInitialPlaceholderTab(string? exceptSessionId = null)
    {
        if (string.IsNullOrEmpty(_initialTerminalId)) return;
        if (_initialTerminalId == exceptSessionId) return;

        var placeholder = _terminalSessions.FirstOrDefault(t => t.Id == _initialTerminalId);
        if (placeholder != null)
        {
            // 1. Wenn bereits eine ConversationId existiert, ist es NIEMALS ein ungenutzter Platzhalter!
            if (!string.IsNullOrEmpty(placeholder.ConversationId))
            {
                _initialTerminalId = null;
                return;
            }

            // 2. Wenn der Tab explizit als genutzt oder abgesendet markiert ist:
            if (placeholder.IsUsed || placeholder.HasPromptBeenDispatched)
            {
                _initialTerminalId = null;
                return;
            }

            // 3. Wenn der Tab bereits einen individuellen Titel erhalten hat (nicht mehr "Neuer Chat" oder leer):
            if (!string.IsNullOrWhiteSpace(placeholder.Title) && !placeholder.Title.Equals("Neuer Chat", StringComparison.OrdinalIgnoreCase))
            {
                _initialTerminalId = null;
                return;
            }

            // 4. ConPTY-Puffer prüfen: Wenn die CLI bereits substantiellen Output geliefert hat oder aktiv arbeitet:
            if (placeholder.Control?.ConPTYTerm != null)
            {
                string? buffer = Helpers.TerminalBufferManager.GetOutputLogText(placeholder.Control.ConPTYTerm);
                if (!string.IsNullOrEmpty(buffer))
                {
                    // Ein reiner ungenutzter Start-Prompt hat typischerweise < 1500 Zeichen.
                    // Wenn Benutzereingaben, Werkzeuge oder aktiver Output vorliegen, schützen!
                    if (buffer.Contains("USER_INPUT", StringComparison.OrdinalIgnoreCase) ||
                        buffer.Contains("Running", StringComparison.OrdinalIgnoreCase) ||
                        buffer.Contains("tool", StringComparison.OrdinalIgnoreCase) ||
                        buffer.Length > 4000)
                    {
                        TelemetryLogger.Log($"[Terminal Cleanup] Retaining placeholder tab '{placeholder.Title}' (ID: {placeholder.Id}) due to active terminal output ({buffer.Length} chars).");
                        placeholder.IsUsed = true;
                        _initialTerminalId = null;
                        return;
                    }
                }
            }

            TelemetryLogger.Log($"[Terminal Cleanup] Closing unused initial placeholder tab '{placeholder.Title}' (ID: {placeholder.Id})");
            DisposeTerminalSession(placeholder);
            TerminalContainer?.Children.Remove(placeholder.Control);
            _terminalSessions.Remove(placeholder);

            if (_activeTerminalId == placeholder.Id)
            {
                _activeTerminalId = string.Empty;
            }
            RenderWorkspaceTabs(force: true);
        }

        _initialTerminalId = null;
    }

    private void DisposeTerminalSession(TerminalTabSession session)
    {
        try
        {
            _activeUiChatIds.TryRemove(session.Id, out _);
            if (!string.IsNullOrEmpty(session.ConversationId))
            {
                _activeUiChatIds.TryRemove(session.ConversationId, out _);
            }
            
            if (_terminalEventUnsubscribers.TryGetValue(session.Id, out var unsubscribers))
            {
                foreach (var unsub in unsubscribers)
                {
                    try { unsub(); } catch { }
                }
                _terminalEventUnsubscribers.Remove(session.Id);
            }

            DisposeTerminalControl(session.Control);
        }
        catch { }
    }

    private void DisposeTerminalControl(EasyWindowsTerminalControl.EasyTerminalControl? control)
    {
        if (control == null) return;
        try
        {
            var conPty = control.ConPTYTerm;
            int pid = 0;
            IDisposable? disposablePty = conPty as IDisposable;
            if (conPty != null)
            {
                try
                {
                    var processProp = conPty.GetType().GetProperty("Process");
                    if (processProp != null)
                    {
                        var procObj = processProp.GetValue(conPty);
                        if (procObj is System.Diagnostics.Process p)
                        {
                            pid = p.Id;
                        }
                        else if (procObj != null)
                        {
                            try
                            {
                                dynamic dynProc = procObj;
                                try { pid = (int)dynProc.Id; } catch { }
                                if (pid == 0) try { pid = (int)dynProc.ProcessId; } catch { }
                                if (pid == 0) try { pid = (int)dynProc.Pid; } catch { }
                                if (pid == 0) try { pid = (int)dynProc.Process.Id; } catch { }
                            }
                            catch { }

                            if (pid == 0)
                            {
                                foreach (var prop in procObj.GetType().GetProperties())
                                {
                                    if (prop.Name == "Id" || prop.Name == "ProcessId" || prop.Name == "Pid")
                                    {
                                        try { pid = Convert.ToInt32(prop.GetValue(procObj)); break; } catch { }
                                    }
                                }
                            }
                        }
                    }

                    if (pid == 0)
                    {
                        var consoleField = conPty.GetType().GetField("TheConsole", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (consoleField != null)
                        {
                            var consoleObj = consoleField.GetValue(conPty);
                            if (consoleObj != null)
                            {
                                try
                                {
                                    dynamic dynConsole = consoleObj;
                                    try { pid = (int)dynConsole.ProcessId; } catch { }
                                    if (pid == 0) try { pid = (int)dynConsole.Id; } catch { }
                                    if (pid == 0) try { pid = (int)dynConsole.Pid; } catch { }
                                    if (pid == 0) try { pid = (int)dynConsole.Process.Id; } catch { }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    TelemetryLogger.Log($"[Terminal Cleanup] Error extracting PID: {ex.Message}");
                }

                TelemetryLogger.Log($"[Terminal Cleanup] Extracted PID: {pid}");

                Helpers.TerminalBufferManager.ClearBuffer(control);

                if (pid > 0)
                {
                    KillProcessTreeAndDispose(pid, disposablePty);
                }
                else
                {
                    try { disposablePty?.Dispose(); } catch { }
                }

                try { control.DisconnectConPTYTerm(); } catch { }
            }
        }
        catch { }
    }

    private void KillProcessTreeAndDispose(int pid, IDisposable? ptyDisposable)
    {
        try
        {
            if (pid > 0)
            {
                try
                {
                    using var proc = System.Diagnostics.Process.GetProcessById(pid);
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(2000);
                }
                catch (ArgumentException) { /* Process already exited */ }
                catch (InvalidOperationException) { /* Process already exited */ }
                catch (System.ComponentModel.Win32Exception) { /* Access denied or already exited */ }
            }
        }
        catch { }
        finally
        {
            try { ptyDisposable?.Dispose(); } catch { }
        }
    }

    private void BtnRefreshTerminal_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnRefreshTerminal_Click)} triggered.");
        if (!string.IsNullOrEmpty(_currentEditingFilePath))
        {
            OpenFileInEditor(_currentEditingFilePath);
            ShowNotice("Datei neu geladen", $"'{Path.GetFileName(_currentEditingFilePath)}' wurde von der Festplatte aktualisiert.", InfoBarSeverity.Informational);
        }
        else
        {
            RefreshActiveTerminal(forceRestart: true);
        }
    }

    private void BtnRefreshTerminal_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnRefreshTerminal_RightTapped)} triggered.");
        e.Handled = true;
        if (!string.IsNullOrEmpty(_currentEditingFilePath))
        {
            OpenFileInEditor(_currentEditingFilePath);
            ShowNotice("Datei neu geladen", $"'{Path.GetFileName(_currentEditingFilePath)}' wurde von der Festplatte aktualisiert.", InfoBarSeverity.Informational);
        }
        else
        {
            RefreshActiveTerminal(forceRestart: true);
        }
    }

    private void RefreshActiveTerminal(bool forceRestart = false, bool showNotice = true)
    {
        if (string.IsNullOrEmpty(_activeTerminalId) && _terminalSessions.Count > 0)
        {
            _activeTerminalId = _terminalSessions[0].Id;
        }

        var session = _terminalSessions.FirstOrDefault(t => t.Id == _activeTerminalId);
        if (session == null) return;

        if (forceRestart)
        {
            TelemetryLogger.Log($"[Terminal Refresh] Force restarting terminal session {session.Id} ('{session.Title}').");
            string title = session.Title;
            string? convId = session.ConversationId;
            string? agentType = session.AgentType;

            string? customCmd = null;
            if (agentType == "clawcode" || (session.Control?.StartupCommandLine != null && session.Control.StartupCommandLine.Contains("claw")))
            {
                customCmd = session.Control?.StartupCommandLine;
            }

            string? projName = session.ProjectName;
            string? projPath = session.ProjectPath;

            DisposeTerminalSession(session);
            if (TerminalContainer != null && session.Control != null)
            {
                TerminalContainer.Children.Remove(session.Control);
            }
            _terminalSessions.Remove(session);

            var newSess = CreateNewTerminalSession(title, convId, customCmd, projName, projPath);
            SwitchToTerminalTab(newSess.Id);
            if (showNotice) ShowNotice("Terminal-Session neu gestartet", $"Die CLI-Session '{title}' wurde neu initialisiert.", InfoBarSeverity.Success);
            return;
        }

        TelemetryLogger.Log($"[Terminal Refresh] Performing layout re-fit & font theme update for active terminal {session.Id}.");
        if (session.Control != null)
        {
            Helpers.TerminalBufferManager.EnforceBufferLimit(session.Control);
            session.Control.Visibility = Visibility.Visible;
            session.Control.Width = double.NaN;
            session.Control.Height = double.NaN;
            session.Control.UpdateLayout();
            ApplyFontSizeAndFamily(session.Control, _fontSize);
            try
            {
                session.Control.Focus(FocusState.Programmatic);
            }
            catch { }
        }
        if (showNotice) ShowNotice("Terminal Re-Fit", "Das Terminal-Layout wurde neu berechnet und gerendert.", InfoBarSeverity.Informational);
    }

    private void BtnCloseOtherTabs_Click(object sender, RoutedEventArgs e)
    {
        TelemetryLogger.Log($"[Click Trace] {nameof(BtnCloseOtherTabs_Click)} triggered.");

        if (!string.IsNullOrEmpty(_currentEditingFilePath))
        {
            string activeFile = _currentEditingFilePath;
            _openTabFilePaths.RemoveAll(p => !string.Equals(p, activeFile, StringComparison.OrdinalIgnoreCase));
            RenderWorkspaceTabs(force: true);
            ShowNotice("Tab-Verwaltung", $"Andere Tabs geschlossen. '{Path.GetFileName(activeFile)}' bleibt geöffnet.", InfoBarSeverity.Informational);
        }
        else
        {
            _openTabFilePaths.Clear();

            var otherSessions = _terminalSessions.Where(s => s.Id != _activeTerminalId).ToList();
            foreach (var session in otherSessions)
            {
                DisposeTerminalSession(session);
                TerminalContainer?.Children.Remove(session.Control);
                _terminalSessions.Remove(session);
            }

            if (!string.IsNullOrEmpty(_activeTerminalId))
            {
                SwitchToTerminalTab(_activeTerminalId);
            }
            else
            {
                EnsureAtLeastOneTerminalSession();
            }
            RenderWorkspaceTabs(force: true);
            ShowNotice("Tab-Verwaltung", "Andere Tabs geschlossen.", InfoBarSeverity.Informational);
        }
    }

    private string? ResolveClawCodeExecutable()
    {
        string baseFolder = !string.IsNullOrWhiteSpace(_projectSettings?.BaseFolder) ? _projectSettings.BaseFolder : ProjectSettingsData.GetDefaultBaseFolder();
        string dashboardProjectDir = Path.Combine(baseFolder, "agy-cli-dashboard");

        var candidatePaths = new List<string>
        {
            // 1. AppContext.BaseDirectory (App execution folder)
            Path.Combine(AppContext.BaseDirectory, "ClawCode", "rust", "target", "release", "claw.exe"),
            Path.Combine(AppContext.BaseDirectory, "ClawCode", "rust", "target", "debug", "claw.exe"),
            Path.Combine(AppContext.BaseDirectory, "claw.exe"),

            // 2. Development project root
            Path.Combine(dashboardProjectDir, "ClawCode", "rust", "target", "release", "claw.exe"),
            Path.Combine(dashboardProjectDir, "ClawCode", "rust", "target", "debug", "claw.exe"),

            // 3. Current working directory
            Path.Combine(Directory.GetCurrentDirectory(), "ClawCode", "rust", "target", "release", "claw.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "ClawCode", "rust", "target", "debug", "claw.exe")
        };

        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
        candidatePaths.AddRange(pathDirs.Select(dir => Path.Combine(dir, "claw.exe")));

        return candidatePaths
            .Where(File.Exists)
            .OrderByDescending(p => File.GetLastWriteTimeUtc(p))
            .FirstOrDefault();
    }

    private bool TryBuildClawCodeStartupCommand(
        string conversationId,
        string workingDirectory,
        ref string title,
        out string? startupCommand,
        out string? errorMessage)
    {
        startupCommand = null;
        errorMessage = null;

        string? clawExePath = ResolveClawCodeExecutable();
        if (string.IsNullOrEmpty(clawExePath))
        {
            errorMessage = "claw.exe nicht gefunden. Bitte ClawCode erst kompilieren (cargo build).";
            return false;
        }

        string openRouterKey = GetOpenRouterApiKey();
        if (string.IsNullOrEmpty(openRouterKey))
        {
            errorMessage = "Bitte setze die Umgebungsvariable OPENROUTER_API_KEY oder trage den Key in den AGY-Settings ein.";
            return false;
        }

        _ = FetchOpenRouterCreditsAsync();

        string modelToUse = LoadChatModel(conversationId);
        if (string.IsNullOrEmpty(modelToUse) || !modelToUse.Contains('/') || modelToUse.Contains(' '))
        {
            modelToUse = ProjectSettingsData.DefaultClawCodeModel;
        }

        int historyLimit = _projectSettings?.ClawCodeHistoryLimit > 0 ? _projectSettings.ClawCodeHistoryLimit : 20;

        string cleanTitle = title;
        if (cleanTitle.StartsWith("\U0001F980")) cleanTitle = cleanTitle.Substring("\U0001F980".Length).TrimStart();
        else if (cleanTitle.StartsWith("\U0001F680")) cleanTitle = cleanTitle.Substring("\U0001F680".Length).TrimStart();

        string envSetup = $"set \"ANTHROPIC_API_KEY=\" & set \"ANTHROPIC_AUTH_TOKEN={openRouterKey}\" & set \"ANTHROPIC_BASE_URL=https://openrouter.ai/api\" & set \"OPENAI_API_KEY={openRouterKey}\" & set \"OPENAI_BASE_URL=https://openrouter.ai/api/v1\" & set \"ANTHROPIC_MODEL={modelToUse}\" & set \"AGY_CHAT_TITLE={cleanTitle}\" & set \"CLAW_HISTORY_LIMIT={historyLimit}\"";
        string clawCmd = $"\"{clawExePath}\" --agy-conversation {conversationId} --model {modelToUse} --permission-mode read-only --history-limit {historyLimit}";
        startupCommand = $"cmd.exe /k cd /d \"{workingDirectory}\" & {envSetup} & cls & {clawCmd}";
        title = $"\U0001F980 {cleanTitle}";
        return true;
    }
}

