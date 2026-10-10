using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AgyCliDashboard.Helpers;

namespace AgyCliDashboard.Services;

public class SystemCheckService : ISystemCheckService
{
    private static readonly HttpClient _httpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return client;
    }

    private static readonly Regex VersionRegex = new(@"\d+(\.\d+)+", RegexOptions.Compiled);

    private static readonly string[] KnownToolPaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "bin"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin"),
        @"C:\Program Files\Git\cmd",
        @"C:\Program Files\Git\bin",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links")
    };

    private static readonly string[] ExecutableExtensions =
    {
        ".exe",
        ".cmd",
        ".bat",
        ".com",
        ".ps1"
    };

    private void EnsurePathLoaded()
    {
        try
        {
            var processPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process) ?? string.Empty;
            var machinePath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? string.Empty;
            var userPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;

            var allSegments = $"{processPath};{machinePath};{userPath}".Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var pathList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var seg in allSegments)
            {
                if (!string.IsNullOrWhiteSpace(seg) && Directory.Exists(seg))
                {
                    pathList.Add(seg);
                }
            }

            foreach (var known in KnownToolPaths)
            {
                if (Directory.Exists(known))
                {
                    pathList.Add(known);
                }
            }

            try
            {
                var pythonBase = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
                if (Directory.Exists(pythonBase))
                {
                    foreach (var pyDir in Directory.GetDirectories(pythonBase))
                    {
                        var scripts = Path.Combine(pyDir, "Scripts");
                        if (Directory.Exists(scripts)) pathList.Add(scripts);
                    }
                }
            }
            catch { }

            string mergedPath = string.Join(';', pathList);
            Environment.SetEnvironmentVariable("Path", mergedPath, EnvironmentVariableTarget.Process);
        }
        catch { }
    }

    public List<Task<SystemRequirementItem>> CreateCheckTasks()
    {
        EnsurePathLoaded();

        return new List<Task<SystemRequirementItem>>
        {
            CheckDotNetAsync(),
            CheckGitAsync(),
            CheckPowerShellAsync(),
            CheckAgyCliAsync(),
            CheckNodeJsAsync(),
            CheckPythonAsync(),
            CheckGitHubCliAsync(),
            CheckInternetConnectivityAsync(),
            CheckGoogleCloudSdkAsync(),
            Task.FromResult(CheckWindowsOs()),
            Task.FromResult(CheckEasyTerminalControl()),
            Task.FromResult(CheckWinAppSdk()),
            Task.FromResult(CheckWebView2()),
            CheckRipgrepAsync(),
            CheckFdFindAsync(),
            Task.FromResult(CheckOpenRouterApi()),
            Task.FromResult(CheckSqliteDb())
        };
    }

    private async Task<SystemRequirementItem> CheckDotNetAsync()
    {
        string output = await RunCommandQuietAsync("dotnet", "--version");
        bool isInstalled = !string.IsNullOrWhiteSpace(output);
        string ver = "Fehlt im PATH";
        if (isInstalled)
        {
            ver = output.Trim();
        }

        string latest = await FetchLatestDotNetVersionAsync() ?? "10.0.302";

        var item = new SystemRequirementItem
        {
            Category = "C# / .NET",
            Id = "dotnet",
            Name = ".NET SDK & Desktop Runtime",
            Description = "Nativ benötigte Laufzeitumgebung für das WinUI 3 Dashboard",
            IconSource = "ms-appx:///Assets/Icons/cpu.svg"
        };
        ApplyVersionStatus(item, isInstalled, ver, latest,
            "Bitte installiere das .NET SDK 10.0 und die Desktop Runtime auf meinem System.",
            $"Bitte aktualisiere das .NET SDK auf Version {latest}: winget upgrade Microsoft.DotNet.SDK.10",
            "Bitte deinstalliere das .NET SDK von meinem System: winget uninstall Microsoft.DotNet.SDK.10");
        return item;
    }

    private async Task<SystemRequirementItem> CheckGitAsync()
    {
        string output = await RunCommandQuietAsync("git", "--version");
        if (string.IsNullOrWhiteSpace(output))
        {
            string programFilesGit = @"C:\Program Files\Git\cmd\git.exe";
            string userAppdataGit = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe");
            if (File.Exists(programFilesGit)) output = await RunCommandQuietAsync(programFilesGit, "--version");
            else if (File.Exists(userAppdataGit)) output = await RunCommandQuietAsync(userAppdataGit, "--version");
        }

        bool isInstalled = !string.IsNullOrWhiteSpace(output);
        string ver = "Fehlt im PATH";
        if (isInstalled)
        {
            ver = output.Replace("git version", "").Trim();
            ver = Regex.Replace(ver, @"\.windows\.", ".");
            if (!ver.StartsWith('v') && !ver.StartsWith('V')) ver = "v" + ver;
        }

        string latest = await FetchGitHubLatestReleaseAsync("git-for-windows/git", "2.55.0");

        var item = new SystemRequirementItem
        {
            Category = "Basics",
            Id = "git",
            Name = "Git for Windows",
            Description = "Benötigt für Git-Status, Commits, Diffing & Ripgrep-Suche",
            IconSource = "ms-appx:///Assets/Icons/git-icon-black.svg"
        };
        ApplyVersionStatus(item, isInstalled, ver, latest,
            "Bitte installiere Git for Windows auf meinem System.",
            $"Bitte aktualisiere Git for Windows auf Version {latest}: winget upgrade Git.Git",
            "Bitte deinstalliere Git for Windows von meinem System: winget uninstall Git.Git");
        return item;
    }

    private async Task<SystemRequirementItem> CheckPowerShellAsync()
    {
        string pwshOutput = await RunCommandQuietAsync("pwsh", "--version");
        bool hasPwsh7 = !string.IsNullOrWhiteSpace(pwshOutput);
        string psVersion = hasPwsh7 ? pwshOutput.Replace("PowerShell", "").Trim() : "5.1 (Windows PS)";
        if (hasPwsh7 && !psVersion.StartsWith('v') && !psVersion.StartsWith('V')) psVersion = "v" + psVersion;

        string latest = await FetchGitHubLatestReleaseAsync("PowerShell/PowerShell", "7.5.0");

        string policyOutput = await RunCommandQuietAsync("powershell", "-NoProfile -Command \"Get-ExecutionPolicy\"");
        string policy = policyOutput?.Trim() ?? "Restricted";
        bool policyOk = !policy.Equals("Restricted", StringComparison.OrdinalIgnoreCase) &&
                         !policy.Equals("Undefined", StringComparison.OrdinalIgnoreCase);

        var item = new SystemRequirementItem
        {
            Id = "powershell",
            Name = "PowerShell & ExecutionPolicy",
            Description = "Benötigt für Skripte (updater.ps1, e2e_test.ps1 & Release-Builds)",
            IconSource = "ms-appx:///Assets/Icons/terminal.svg"
        };

        if (!hasPwsh7)
        {
            ApplyVersionStatus(item, false, "Fehlt im PATH", latest,
                "Bitte installiere PowerShell 7 (pwsh) auf meinem Windows-System: winget install Microsoft.PowerShell",
                $"Bitte aktualisiere PowerShell 7 auf Version {latest}: winget upgrade Microsoft.PowerShell",
                "Bitte deinstalliere PowerShell 7 von meinem System: winget uninstall Microsoft.PowerShell");
        }
        else if (!policyOk)
        {
            item.IsInstalled = true;
            item.InstalledVersion = psVersion;
            item.LatestVersion = latest;
            item.IsUpToDate = false;
            item.StatusText = "Policy blockiert";
            item.StatusColorHex = "#F59E0B";
            item.ActionButtonText = "Aktivieren";
            item.InstallPrompt = "Bitte passe die PowerShell ExecutionPolicy an, damit Skripte ausgeführt werden können: Set-ExecutionPolicy RemoteSigned -Scope CurrentUser";
            item.UninstallPrompt = "Bitte deinstalliere PowerShell 7 von meinem System: winget uninstall Microsoft.PowerShell";
        }
        else
        {
            ApplyVersionStatus(item, true, psVersion, latest,
                "Bitte installiere PowerShell 7 (pwsh) auf meinem Windows-System.",
                $"Bitte aktualisiere PowerShell 7 auf Version {latest}: winget upgrade Microsoft.PowerShell",
                "Bitte deinstalliere PowerShell 7 von meinem System: winget uninstall Microsoft.PowerShell");
        }

        return item;
    }

    private async Task<SystemRequirementItem> CheckAgyCliAsync()
    {
        var item = new SystemRequirementItem
        {
            Id = "agy",
            Name = "Antigravity CLI (agy)",
            Description = "Core Backend Engine für KI-Chats, Modelle & Chat-Verläufe",
            IconSource = "ms-appx:///Assets/Icons/antigravity-logo.svg"
        };

        string agyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy", "bin", "agy.exe");
        string output = File.Exists(agyPath) ? await RunCommandQuietAsync(agyPath, "--version") : await RunCommandQuietAsync("agy", "--version");

        if (!string.IsNullOrWhiteSpace(output))
        {
            item.IsInstalled = true;
            var match = VersionRegex.Match(output);
            string localVer = match.Success ? match.Value : output.Trim().TrimStart('v');
            item.InstalledVersion = $"v{localVer}";

            string latestVer = await FetchLatestAgyVersionAsync(agyPath) ?? localVer;
            item.LatestVersion = $"v{latestVer}";

            if (CompareVersions(localVer, latestVer) >= 0)
            {
                item.IsUpToDate = true;
                item.StatusText = "Installiert";
                item.StatusColorHex = "#10B981";
            }
            else
            {
                item.IsUpToDate = false;
                item.StatusText = "Update verfügbar";
                item.StatusColorHex = "#F59E0B";
                item.ActionButtonText = "Aktualisieren";
                item.InstallPrompt = $"Bitte führe das Update der Antigravity CLI (agy) auf die neueste Version v{latestVer} aus: agy update";
            }
        }
        else
        {
            item.IsInstalled = false;
            item.InstalledVersion = "Nicht gefunden";
            item.LatestVersion = "Unbekannt";
            item.IsUpToDate = false;
            item.StatusText = "Fehlt";
            item.StatusColorHex = "#EF4444";
            item.ActionButtonText = "Installieren";
            item.InstallPrompt = "Bitte installiere bzw. führe das Setup der Antigravity CLI (agy) auf meinem System aus.";
        }

        return item;
    }

    private async Task<SystemRequirementItem> CheckNodeJsAsync()
    {
        string output = await RunCommandQuietAsync("node", "--version");
        bool isInstalled = !string.IsNullOrWhiteSpace(output);

        string ver = "Fehlt im PATH";
        if (isInstalled)
        {
            var match = VersionRegex.Match(output);
            ver = match.Success ? $"v{match.Value}" : output.Trim();
        }

        string latest = await FetchLatestNodeJsLtsVersionAsync() ?? "v24.19.0 (LTS)";

        var item = new SystemRequirementItem
        {
            Category = "JavaScript / Node.js",
            Id = "nodejs",
            Name = "Node.js & npm / npx",
            Description = "JavaScript/TypeScript Runtime für Web-Apps & Vite-Projekte",
            IconSource = "ms-appx:///Assets/Icons/globe.svg"
        };
        ApplyVersionStatus(item, isInstalled, ver, latest,
            "Bitte installiere Node.js (LTS) und npm auf meinem Windows-System.",
            $"Bitte aktualisiere Node.js auf die neueste LTS-Version ({latest}): winget upgrade OpenJS.NodeJS.LTS",
            "Bitte deinstalliere Node.js von meinem System: winget uninstall OpenJS.NodeJS.LTS");
        return item;
    }

    private async Task<SystemRequirementItem> CheckPythonAsync()
    {
        string output = await RunCommandQuietAsync("python", "--version");
        if (string.IsNullOrWhiteSpace(output)) output = await RunCommandQuietAsync("py", "--version");

        bool isInstalled = !string.IsNullOrWhiteSpace(output);

        string ver = "Fehlt im PATH";
        if (isInstalled)
        {
            var match = VersionRegex.Match(output);
            ver = match.Success ? $"v{match.Value}" : output.Trim();
        }

        string latest = await FetchPythonLatestVersionAsync("3.13.2");

        var item = new SystemRequirementItem
        {
            Category = "Python",
            Id = "python",
            Name = "Python & pip Package Manager",
            Description = "Python Runtime für KI-Skripte, SDKs & Datenverarbeitung",
            IconSource = "ms-appx:///Assets/Icons/cpu.svg"
        };
        ApplyVersionStatus(item, isInstalled, ver, latest,
            "Bitte installiere Python 3.13 und pip auf meinem Windows-System.",
            $"Bitte aktualisiere Python auf Version {latest}: winget upgrade Python.Python.3.13",
            "Bitte deinstalliere Python von meinem System: winget uninstall Python.Python.3.13");
        return item;
    }

    private async Task<SystemRequirementItem> CheckGitHubCliAsync()
    {
        string output = await RunCommandQuietAsync("gh", "--version");
        bool isInstalled = !string.IsNullOrWhiteSpace(output);

        string ver = "Fehlt im PATH";
        if (isInstalled)
        {
            var match = VersionRegex.Match(output);
            ver = match.Success ? $"v{match.Value}" : "v2.67.0";
        }

        string latest = await FetchGitHubLatestReleaseAsync("cli/cli", "2.67.0");

        var item = new SystemRequirementItem
        {
            Id = "github_cli",
            Name = "GitHub CLI (gh)",
            Description = "Kommandozeilen-Tool für Pull Requests, Issues & Releases",
            IconSource = "ms-appx:///Assets/Icons/git-icon-black.svg"
        };
        ApplyVersionStatus(item, isInstalled, ver, latest,
            "Bitte installiere die GitHub CLI (gh) auf meinem Windows-System.",
            $"Bitte aktualisiere die GitHub CLI auf Version {latest}: winget upgrade GitHub.cli",
            "Bitte deinstalliere die GitHub CLI von meinem System: winget uninstall GitHub.cli");
        return item;
    }

    private async Task<SystemRequirementItem> CheckInternetConnectivityAsync()
    {
        var item = new SystemRequirementItem
        {
            Category = "Basics",
            Id = "internet",
            Name = "Internet-Verbindung & API Latenz",
            Description = "Live-Verbindungstest zu GitHub & OpenRouter APIs",
            LeftLabel = "STATUS",
            RightLabel = "API LATENZ",
            IconSource = "ms-appx:///Assets/Icons/globe.svg"
        };

        try
        {
            var sw = Stopwatch.StartNew();
            using var resp = await _httpClient.GetAsync("https://api.github.com");
            sw.Stop();

            if (resp.IsSuccessStatusCode || (int)resp.StatusCode < 500)
            {
                item.IsInstalled = true;
                item.InstalledVersion = "Online";
                item.LatestVersion = $"{sw.ElapsedMilliseconds} ms Latenz";
                item.IsUpToDate = true;
                item.StatusText = "Installiert";
                item.StatusColorHex = "#10B981";
                return item;
            }
        }
        catch { }

        item.IsInstalled = false;
        item.InstalledVersion = "Offline / Timeout";
        item.LatestVersion = "Fehlgeschlagen";
        item.IsUpToDate = false;
        item.StatusText = "Fehlt";
        item.StatusColorHex = "#EF4444";
        item.ActionButtonText = "Prüfen";
        item.InstallPrompt = "Bitte prüfe meine Internetverbindung und Netzwerkeinstellungen.";

        return item;
    }

    private async Task<SystemRequirementItem> CheckGoogleCloudSdkAsync()
    {
        string output = await RunCommandQuietAsync("gcloud", "--version");
        bool isInstalled = !string.IsNullOrWhiteSpace(output);

        string ver = "Nicht im PATH";
        if (isInstalled)
        {
            var match = VersionRegex.Match(output);
            ver = match.Success ? $"v{match.Value}" : "v578.0.0";
        }

        var item = new SystemRequirementItem
        {
            Id = "gcloud",
            Name = "Google Cloud SDK (gcloud)",
            Description = "Kommandozeilen-Tool für Google Cloud Auth & Vertex AI",
            IconSource = "ms-appx:///Assets/Icons/globe.svg"
        };
        ApplyVersionStatus(item, isInstalled, ver, "v578.0.0",
            "Bitte installiere das Google Cloud SDK (gcloud) auf meinem System.",
            "Bitte aktualisiere das Google Cloud SDK: gcloud components update",
            "Bitte deinstalliere Google Cloud SDK von meinem System: winget uninstall Google.CloudSDK");
        return item;
    }

    private SystemRequirementItem CheckWindowsOs()
    {
        var osVersion = Environment.OSVersion.Version;
        bool isWin10OrHigher = osVersion.Major >= 10;
        bool hasConPty = osVersion.Build >= 18362;
        bool isRecommendedBuild = osVersion.Build >= 22631;

        string latestVersionDisplay = isRecommendedBuild
            ? $"Build {osVersion.Build}"
            : "Build 22631+";

        var item = new SystemRequirementItem
        {
            Category = "Basics",
            Id = "windows",
            Name = "Windows OS & ConPTY Terminal",
            Description = "Natives ConPTY Pseudoconsole Subsystem für Terminal-Tabs",
            LeftLabel = "SYSTEM",
            RightLabel = "BUILD",
            IsInstalled = isWin10OrHigher,
            InstalledVersion = $"Build {osVersion.Build}",
            LatestVersion = latestVersionDisplay,
            IsUpToDate = hasConPty,
            StatusText = hasConPty ? "Installiert" : "Update verfügbar",
            StatusColorHex = hasConPty ? "#10B981" : "#EF4444",
            IconSource = "ms-appx:///Assets/Icons/shield-check.svg"
        };

        if (!hasConPty)
        {
            item.ActionButtonText = "Aktualisieren";
            item.InstallPrompt = "Bitte unterstütze mich beim Durchführen eines Windows Updates, um das ConPTY Subsystem auf Build 18362+ bzw. Build 22631+ zu bringen.";
        }

        return item;
    }

    private SystemRequirementItem CheckEasyTerminalControl()
    {
        string ver = "v1.0.20-beta.1";
        try
        {
            var asmVer = typeof(EasyWindowsTerminalControl.EasyTerminalControl).Assembly.GetName().Version;
            if (asmVer != null) ver = $"v{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
        }
        catch { }

        return new SystemRequirementItem
        {
            Id = "easy_terminal",
            Name = "Easy Terminal Control Engine",
            Description = "Natives Terminal Host-Control Subsystem für WinUI 3 Tabs",
            IsInstalled = true,
            InstalledVersion = ver,
            LatestVersion = ver,
            IsUpToDate = true,
            StatusText = "Installiert",
            StatusColorHex = "#10B981",
            IconSource = "ms-appx:///Assets/Icons/terminal.svg"
        };
    }

    private SystemRequirementItem CheckWinAppSdk()
    {
        string ver = "v1.8.260317003";
        try
        {
            var asmVer = typeof(Microsoft.UI.Xaml.Window).Assembly.GetName().Version;
            if (asmVer != null) ver = $"v{asmVer.Major}.{asmVer.Minor}.{asmVer.Build}";
        }
        catch { }

        return new SystemRequirementItem
        {
            Category = "C# / .NET",
            Id = "winappsdk",
            Name = "Windows App SDK & WinUI 3",
            Description = "Nationale Benutzeroberflächen-Engine für das Dashboard",
            IsInstalled = true,
            InstalledVersion = ver,
            LatestVersion = ver,
            IsUpToDate = true,
            StatusText = "Installiert",
            StatusColorHex = "#10B981",
            IconSource = "ms-appx:///Assets/Icons/cpu-indigo.svg"
        };
    }

    private async Task<SystemRequirementItem> CheckRipgrepAsync()
    {
        string output = await RunCommandQuietAsync("rg", "--version");
        if (string.IsNullOrWhiteSpace(output))
        {
            string gitRg = @"C:\Program Files\Git\usr\bin\rg.exe";
            string userGitRg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "usr", "bin", "rg.exe");
            if (File.Exists(gitRg)) output = await RunCommandQuietAsync(gitRg, "--version");
            else if (File.Exists(userGitRg)) output = await RunCommandQuietAsync(userGitRg, "--version");
        }

        bool isInstalled = !string.IsNullOrWhiteSpace(output);
        string ver = "Fehlt im PATH";
        if (isInstalled)
        {
            var match = VersionRegex.Match(output);
            ver = match.Success ? $"v{match.Value}" : "v14.1.1";
        }

        string latest = await FetchGitHubLatestReleaseAsync("BurntSushi/ripgrep", "14.1.1");

        var item = new SystemRequirementItem
        {
            Id = "ripgrep",
            Name = "Ripgrep Engine (rg.exe)",
            Description = "Ultraschnelle Volltext-Code-Suche in Projektdateien",
            IconSource = "ms-appx:///Assets/Icons/search.svg"
        };
        ApplyVersionStatus(item, isInstalled, ver, latest,
            "Bitte installiere Ripgrep (rg.exe) auf meinem System: winget install BurntSushi.ripgrep.MSVC",
            $"Bitte aktualisiere Ripgrep auf Version {latest}: winget upgrade BurntSushi.ripgrep.MSVC",
            "Bitte deinstalliere Ripgrep von meinem System: winget uninstall BurntSushi.ripgrep.MSVC");
        return item;
    }

    private async Task<SystemRequirementItem> CheckFdFindAsync()
    {
        string output = await RunCommandQuietAsync("fd", "--version");
        bool isInstalled = !string.IsNullOrWhiteSpace(output);

        string ver = "Fehlt im PATH";
        if (isInstalled)
        {
            var match = VersionRegex.Match(output);
            ver = match.Success ? $"v{match.Value}" : "v10.4.2";
        }

        string latest = await FetchGitHubLatestReleaseAsync("sharkdp/fd", "10.4.2");

        var item = new SystemRequirementItem
        {
            Id = "fd_find",
            Name = "fd Directory Search (AGY Tool)",
            Description = "Benötigt von AGY für ultraschnelle Dateisuche in großen Repositories",
            IconSource = "ms-appx:///Assets/Icons/search.svg"
        };
        ApplyVersionStatus(item, isInstalled, ver, latest,
            "Bitte installiere fd-find (fd.exe) für noch schnellere Dateisuchen.",
            $"Bitte aktualisiere fd auf Version {latest}: winget upgrade sharkdp.fd",
            "Bitte deinstalliere fd von meinem System: winget uninstall sharkdp.fd");
        return item;
    }

    private SystemRequirementItem CheckOpenRouterApi()
    {
        string? apiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "settings.json");
            if (File.Exists(settingsPath))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                    if (doc.RootElement.TryGetProperty("openRouterApiKey", out var keyProp) &&
                        keyProp.GetString() is { } keyStr && !string.IsNullOrWhiteSpace(keyStr))
                    {
                        apiKey = keyStr;
                    }
                }
                catch { }
            }
        }

        bool hasKey = !string.IsNullOrWhiteSpace(apiKey);

        return new SystemRequirementItem
        {
            Id = "openrouter",
            Name = "OpenRouter KI-Schnittstelle",
            Description = "Online KI-Modell Katalog & Guthaben-Abfrage",
            LeftLabel = "API-KEY",
            RightLabel = "INTEGRATION",
            IsInstalled = true,
            InstalledVersion = hasKey ? "Konfiguriert" : "Kein Key hinterlegt",
            LatestVersion = "API v1",
            IsUpToDate = hasKey,
            StatusText = hasKey ? "Installiert" : "Fehlt",
            StatusColorHex = hasKey ? "#10B981" : "#F59E0B",
            IconSource = "ms-appx:///Assets/Icons/globe.svg",
            ActionButtonText = hasKey ? string.Empty : "Konfigurieren",
            InstallPrompt = hasKey ? string.Empty : "Bitte unterstütze mich beim Konfigurieren meines OpenRouter API Keys in den Einstellungen."
        };
    }

    private SystemRequirementItem CheckSqliteDb()
    {
        string dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "conversation_summaries.db");
        bool dbExists = File.Exists(dbPath);
        string journalMode = "Unbekannt";

        if (dbExists)
        {
            try
            {
                using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode;";
                var result = cmd.ExecuteScalar()?.ToString();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    journalMode = result.ToUpperInvariant();
                }
            }
            catch
            {
                journalMode = "Gesperrt";
            }
        }

        bool isWal = string.Equals(journalMode, "WAL", StringComparison.OrdinalIgnoreCase);

        return new SystemRequirementItem
        {
            Category = "Basics",
            Id = "sqlite",
            Name = "SQLite Datenbank & WAL-Modus",
            Description = "Lokaler Datenspeicher für Chats, Einstellungen & Verlauf",
            LeftLabel = "SPEICHER",
            RightLabel = "MODUS",
            IsInstalled = dbExists,
            InstalledVersion = dbExists ? $"Aktiv ({journalMode})" : "Erstellt bei Start",
            LatestVersion = "WAL-Modus",
            IsUpToDate = !dbExists || isWal,
            StatusText = dbExists ? (isWal ? "Installiert" : "Aktiv") : "Installiert",
            StatusColorHex = isWal || !dbExists ? "#10B981" : "#F59E0B",
            IconSource = "ms-appx:///Assets/Icons/database.svg"
        };
    }

    private SystemRequirementItem CheckWebView2()
    {
        var item = new SystemRequirementItem
        {
            Category = "Co-Browser",
            Id = "webview2",
            Name = "Microsoft Edge WebView2 Runtime",
            Description = "Benötigt für den integrierten KI-Co-Browser & Web-Vorschau",
            IconSource = "ms-appx:///Assets/Icons/globe.svg"
        };

        try
        {
            string version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
            item.IsInstalled = true;
            item.InstalledVersion = $"v{version}";
            item.LatestVersion = "Bereit";
            item.IsUpToDate = true;
            item.StatusText = "Installiert";
            item.StatusColorHex = "#10B981";
        }
        catch
        {
            item.IsInstalled = false;
            item.InstalledVersion = "Nicht installiert";
            item.LatestVersion = "Erforderlich";
            item.IsUpToDate = false;
            item.StatusText = "Fehlt";
            item.StatusColorHex = "#EF4444";
            item.ActionButtonText = "Installieren";
            item.InstallPrompt = "Bitte installiere die Microsoft Edge WebView2 Runtime auf meinem System: winget install Microsoft.EdgeWebView2Runtime";
        }

        return item;
    }

    private void ApplyVersionStatus(SystemRequirementItem item, bool isInstalled, string installedVer, string latestVer, string installPrompt, string updatePrompt, string uninstallPrompt = "")
    {
        item.IsInstalled = isInstalled;
        item.InstalledVersion = installedVer;
        item.LatestVersion = latestVer;
        item.UninstallPrompt = uninstallPrompt;

        if (isInstalled)
        {
            int cmp = CompareVersions(installedVer, latestVer);
            if (cmp >= 0)
            {
                if (cmp > 0 && !string.IsNullOrEmpty(installedVer) && !installedVer.StartsWith("Fehlt", StringComparison.OrdinalIgnoreCase) && !installedVer.StartsWith("Nicht", StringComparison.OrdinalIgnoreCase))
                {
                    item.LatestVersion = installedVer;
                }
                item.IsUpToDate = true;
                item.StatusText = "Installiert";
                item.StatusColorHex = "#10B981";
                item.ActionButtonText = string.Empty;
                item.InstallPrompt = string.Empty;
            }
            else
            {
                item.IsUpToDate = false;
                item.StatusText = "Update verfügbar";
                item.StatusColorHex = "#F59E0B";
                item.ActionButtonText = "Aktualisieren";
                item.InstallPrompt = updatePrompt;
            }
        }
        else
        {
            item.IsUpToDate = false;
            item.StatusText = "Optional";
            item.StatusColorHex = "#6E7681";
            item.ActionButtonText = "Installieren";
            item.InstallPrompt = installPrompt;
        }
    }

    private async Task<string?> FetchLatestAgyVersionAsync(string agyExe)
    {
        if (!System.IO.File.Exists(agyExe)) agyExe = "agy";
        string updateOutput = await RunCommandQuietAsync(agyExe, "update");
        var latestMatch = VersionRegex.Match(updateOutput);
        if (latestMatch.Success)
        {
            return latestMatch.Value;
        }
        return null;
    }

    private async Task<string?> FetchLatestDotNetVersionAsync()
    {
        try
        {
            string json = await _httpClient.GetStringAsync("https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("latest-release", out var elem))
            {
                return elem.GetString();
            }
        }
        catch { }
        return null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _versionCache = new();

    private async Task<string> FetchGitHubLatestReleaseAsync(string repo, string fallback)
    {
        if (_versionCache.TryGetValue("gh:" + repo, out var cached)) return cached;
        try
        {
            string url = $"https://api.github.com/repos/{repo}/releases/latest";
            string json = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tag_name", out var tagElem) && tagElem.GetString() is { } tagStr)
            {
                string clean = tagStr.Trim().TrimStart('v', 'V');
                var m = VersionRegex.Match(clean);
                string res = m.Success ? "v" + m.Value : "v" + clean;
                _versionCache["gh:" + repo] = res;
                return res;
            }
        }
        catch { }
        return fallback.StartsWith('v') ? fallback : "v" + fallback;
    }

    private async Task<string?> FetchLatestNodeJsLtsVersionAsync()
    {
        if (_versionCache.TryGetValue("nodejs_lts", out var cached)) return cached;
        try
        {
            string json = await _httpClient.GetStringAsync("https://nodejs.org/dist/index.json");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.TryGetProperty("lts", out var ltsProp) && ltsProp.ValueKind != JsonValueKind.False &&
                        el.TryGetProperty("version", out var verProp) && verProp.GetString() is { } vStr)
                    {
                        string res = $"{vStr.Trim()} (LTS)";
                        _versionCache["nodejs_lts"] = res;
                        return res;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string> FetchPythonLatestVersionAsync(string fallback)
    {
        if (_versionCache.TryGetValue("python_latest", out var cached)) return cached;
        try
        {
            string json = await _httpClient.GetStringAsync("https://endoflife.date/api/python.json");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                if (first.TryGetProperty("latest", out var vElem) && vElem.GetString() is { } vStr)
                {
                    string res = "v" + vStr;
                    _versionCache["python_latest"] = res;
                    return res;
                }
            }
        }
        catch { }
        return fallback.StartsWith('v') ? fallback : "v" + fallback;
    }

    private static string? ResolveExecutablePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        if (Path.IsPathRooted(fileName))
        {
            if (File.Exists(fileName)) return fileName;
            foreach (var ext in ExecutableExtensions)
            {
                string candidate = fileName + ext;
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        var pathEnv = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Process) ?? string.Empty;
        var dirs = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool hasExtension = Path.HasExtension(fileName);

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;

            if (hasExtension)
            {
                string directCandidate = Path.Combine(dir, fileName);
                if (File.Exists(directCandidate)) return directCandidate;
            }

            foreach (var ext in ExecutableExtensions)
            {
                string candidate = Path.Combine(dir, fileName + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }

    private async Task<string> RunCommandQuietAsync(string fileName, string args, CancellationToken cancellationToken = default)
    {
        string? resolvedPath = ResolveExecutablePath(fileName);

        if (!string.IsNullOrEmpty(resolvedPath))
        {
            string ext = Path.GetExtension(resolvedPath).ToLowerInvariant();

            if (ext is ".cmd" or ".bat")
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = @"C:\Windows\System32\cmd.exe",
                        Arguments = $"/d /c \"\"{resolvedPath}\" {args}\""
                    };
                    var result = await ProcessRunner.RunAsync(psi, TimeSpan.FromMilliseconds(4000), cancellationToken);
                    if (!result.TimedOut && !string.IsNullOrWhiteSpace(result.StandardOutput))
                    {
                        return result.StandardOutput;
                    }
                }
                catch { }
            }
            else
            {
                string outDirect = await ExecuteProcessAsync(resolvedPath, args, cancellationToken);
                if (!string.IsNullOrWhiteSpace(outDirect))
                {
                    return outDirect;
                }
            }
        }

        // pwsh fallback
        try
        {
            var psiPwsh = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = $"-NoProfile -NonInteractive -Command \"{fileName} {args}\""
            };
            var resultPwsh = await ProcessRunner.RunAsync(psiPwsh, TimeSpan.FromMilliseconds(4000), cancellationToken);
            if (!resultPwsh.TimedOut && !string.IsNullOrWhiteSpace(resultPwsh.StandardOutput))
            {
                return resultPwsh.StandardOutput;
            }
        }
        catch { }

        // cmd fallback
        try
        {
            var psiCmd = new ProcessStartInfo
            {
                FileName = @"C:\Windows\System32\cmd.exe",
                Arguments = $"/d /c \"{fileName} {args}\""
            };
            var result = await ProcessRunner.RunAsync(psiCmd, TimeSpan.FromMilliseconds(4000), cancellationToken);
            if (!result.TimedOut && !string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return result.StandardOutput;
            }
        }
        catch { }

        return string.Empty;
    }

    private static async Task<string> ExecuteProcessAsync(string targetFile, string args, CancellationToken cancellationToken = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetFile,
                Arguments = args
            };
            var result = await ProcessRunner.RunAsync(psi, TimeSpan.FromMilliseconds(4000), cancellationToken);
            if (!result.TimedOut)
            {
                return result.StandardOutput;
            }
        }
        catch { }
        return string.Empty;
    }

    private int CompareVersions(string? v1Str, string? v2Str)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(v1Str)) return -1;
            if (string.IsNullOrWhiteSpace(v2Str)) return 1;

            string s1 = v1Str.Trim().TrimStart('v', 'V');
            string s2 = v2Str.Trim().TrimStart('v', 'V');

            // Strip parenthesized annotations like (LTS) or (Windows PS)
            s1 = Regex.Replace(s1, @"\s*\(.*?\)", "");
            s2 = Regex.Replace(s2, @"\s*\(.*?\)", "");

            // Normalize Git for Windows .windows. segment (e.g. 2.55.0.windows.3 -> 2.55.0.3)
            s1 = Regex.Replace(s1, @"\.windows\.", ".", RegexOptions.IgnoreCase);
            s2 = Regex.Replace(s2, @"\.windows\.", ".", RegexOptions.IgnoreCase);

            var clean1 = Regex.Replace(s1, @"[^\d\.\-a-zA-Z]", "");
            var clean2 = Regex.Replace(s2, @"[^\d\.\-a-zA-Z]", "");

            var parts1 = clean1.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var parts2 = clean2.Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries);

            int minLen = Math.Min(parts1.Length, parts2.Length);
            for (int i = 0; i < minLen; i++)
            {
                string p1 = parts1[i];
                string p2 = parts2[i];

                bool isNum1 = long.TryParse(p1, out long n1);
                bool isNum2 = long.TryParse(p2, out long n2);

                if (isNum1 && isNum2)
                {
                    int cmp = n1.CompareTo(n2);
                    if (cmp != 0) return cmp;
                }
                else if (isNum1 && !isNum2)
                {
                    return 1;
                }
                else if (!isNum1 && isNum2)
                {
                    return -1;
                }
                else
                {
                    int strCmp = string.Compare(p1, p2, StringComparison.OrdinalIgnoreCase);
                    if (strCmp != 0) return strCmp;
                }
            }

            if (parts1.Length > parts2.Length)
            {
                bool allRemainingZero = true;
                for (int i = minLen; i < parts1.Length; i++)
                {
                    if (!long.TryParse(parts1[i], out long val) || val != 0)
                    {
                        allRemainingZero = false;
                        break;
                    }
                }
                if (allRemainingZero) return 0;

                string extra = parts1[minLen];
                if (extra.StartsWith("alpha", StringComparison.OrdinalIgnoreCase) ||
                    extra.StartsWith("beta", StringComparison.OrdinalIgnoreCase) ||
                    extra.StartsWith("rc", StringComparison.OrdinalIgnoreCase) ||
                    extra.StartsWith("preview", StringComparison.OrdinalIgnoreCase))
                {
                    return -1;
                }
                return 1;
            }
            else if (parts2.Length > parts1.Length)
            {
                bool allRemainingZero = true;
                for (int i = minLen; i < parts2.Length; i++)
                {
                    if (!long.TryParse(parts2[i], out long val) || val != 0)
                    {
                        allRemainingZero = false;
                        break;
                    }
                }
                if (allRemainingZero) return 0;

                string extra = parts2[minLen];
                if (extra.StartsWith("alpha", StringComparison.OrdinalIgnoreCase) ||
                    extra.StartsWith("beta", StringComparison.OrdinalIgnoreCase) ||
                    extra.StartsWith("rc", StringComparison.OrdinalIgnoreCase) ||
                    extra.StartsWith("preview", StringComparison.OrdinalIgnoreCase))
                {
                    return 1;
                }
                return -1;
            }

            return 0;
        }
        catch { }
        return -1;
    }
}
