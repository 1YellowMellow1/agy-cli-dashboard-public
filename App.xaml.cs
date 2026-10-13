using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace AgyCliDashboard;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [ThreadStatic]
    private static bool _isLoggingFirstChance;

    private Window? _window;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID("Antigravity.CLI.Dashboard");
        }
        catch { }

        this.UnhandledException += App_UnhandledException;

        // FirstChanceException: Fängt JEDE Exception ab, BEVOR sie zum nativen
        // Stowed Exception (0xc000027b in CoreMessagingXP.dll) eskaliert wird.
        // Das ist der einzige Weg, die echte Crash-Ursache zu demaskieren.
        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
        {
            if (_isLoggingFirstChance) return;
            try
            {
                _isLoggingFirstChance = true;
                string msg = e.Exception?.Message ?? "null";
                string type = e.Exception?.GetType().FullName ?? "unknown";
                // Nur relevante Exceptions loggen (COM, InvalidOperation, NullRef, Access)
                if (type.Contains("COM") || type.Contains("InvalidOperation") ||
                    type.Contains("NullReference") || type.Contains("Access") ||
                    type.Contains("Argument") || type.Contains("ObjectDisposed") ||
                    msg.Contains("0x") || msg.Contains("HRESULT"))
                {
                    TelemetryLogger.Log($"[FirstChance] {type}: {msg}\n{e.Exception?.StackTrace}");
                }
            }
            catch { }
            finally
            {
                _isLoggingFirstChance = false;
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            LogCrash("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        InitializeComponent();
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            LogCrash($"WinUI UnhandledException: {e.Message}", e.Exception);
            e.Handled = true; // Attempt to keep WinUI 3 application process alive!
        }
        catch { }
    }

    private static void LogCrash(string category, Exception? ex)
    {
        string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}]\nMessage: {ex?.Message}\nStack:\n{ex?.StackTrace}\n\n";
        try
        {
            TelemetryLogger.Log($"[CRASH] {category}: {ex?.Message}\n{ex?.StackTrace}");
            string localLog = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            System.IO.File.AppendAllText(localLog, entry);
        }
        catch { }

        try
        {
            string globalLogDir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli");
            if (!System.IO.Directory.Exists(globalLogDir)) System.IO.Directory.CreateDirectory(globalLogDir);
            string globalLog = System.IO.Path.Combine(globalLogDir, "crash_log.txt");
            System.IO.File.AppendAllText(globalLog, entry);
        }
        catch { }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Closed += (s, e) =>
            {
                _window = null;
            };
            _window.Activate();
        }
        catch (Exception ex)
        {
            try
            {
                TelemetryLogger.Log($"[CRASH] OnLaunched Exception: {ex.Message}\n{ex.StackTrace}");
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] OnLaunched Exception: {ex.Message}\n{ex.StackTrace}\n\n");
            }
            catch { }
            throw;
        }
    }
}
