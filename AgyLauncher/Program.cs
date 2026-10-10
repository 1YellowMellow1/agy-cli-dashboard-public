using System;
using System.Diagnostics;
using System.IO;

namespace AgyLauncher;

static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string localExe = Path.Combine(baseDir, "Antigravity CLI Dashboard.exe");
            if (File.Exists(localExe))
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = localExe,
                    WorkingDirectory = baseDir,
                    UseShellExecute = false
                });
                return;
            }

            DirectoryInfo? current = new DirectoryInfo(baseDir);
            string projectDir = baseDir;
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AgyCliDashboard.csproj")))
                {
                    projectDir = current.FullName;
                    break;
                }
                current = current.Parent;
            }

            string debugDir = Path.Combine(projectDir, "bin", "Debug", "net10.0-windows10.0.26100.0", "win-x64");
            string releaseDir = Path.Combine(projectDir, "bin", "Release", "net10.0-windows10.0.26100.0", "win-x64");

            string debugExe = Path.Combine(debugDir, "Antigravity CLI Dashboard.exe");
            string releaseExe = Path.Combine(releaseDir, "Antigravity CLI Dashboard.exe");

            string targetExe = File.Exists(debugExe) ? debugExe : releaseExe;
            string workingDir = File.Exists(debugExe) ? debugDir : releaseDir;

            if (File.Exists(targetExe))
            {
                using var targetProc = Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false
                });
            }
            else
            {
                LogLauncherError("Target executable not found", new FileNotFoundException($"Exe not found at {debugExe} or {releaseExe}"));
            }
        }
        catch (Exception ex)
        {
            LogLauncherError("Main launcher process failed", ex);
        }
    }

    private static void LogLauncherError(string context, Exception ex)
    {
        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_error.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { }
    }
}
