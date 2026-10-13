using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AgyCliDashboard.Helpers;

public record ProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = true;
        psi.StandardOutputEncoding = System.Text.Encoding.UTF8;
        psi.StandardErrorEncoding = System.Text.Encoding.UTF8;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WindowStyle = ProcessWindowStyle.Hidden;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var proc = new Process { StartInfo = psi };
            if (!proc.Start())
            {
                return new ProcessResult(-1, string.Empty, "Process start failed.", false);
            }

            try
            {
                proc.StandardInput.Close();
            }
            catch { }

            Task<string>? stdOutTask = null;
            Task<string>? stdErrTask = null;
            try
            {
                stdOutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
                stdErrTask = proc.StandardError.ReadToEndAsync(cts.Token);

                await proc.WaitForExitAsync(cts.Token);
                string stdOut = await stdOutTask;
                string stdErr = await stdErrTask;

                return new ProcessResult(proc.ExitCode, stdOut, stdErr, false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                }
                catch { }

                if (stdOutTask != null) { try { await stdOutTask; } catch { } }
                if (stdErrTask != null) { try { await stdErrTask; } catch { } }

                bool isTimedOut = !cancellationToken.IsCancellationRequested;
                string msg = isTimedOut ? "Process execution timed out." : "Process execution was cancelled.";
                return new ProcessResult(-1, string.Empty, msg, isTimedOut);
            }
            catch (Exception ex)
            {
                try
                {
                    if (!proc.HasExited) proc.Kill(entireProcessTree: true);
                }
                catch { }

                if (stdOutTask != null) { try { await stdOutTask; } catch { } }
                if (stdErrTask != null) { try { await stdErrTask; } catch { } }

                return new ProcessResult(-1, string.Empty, ex.Message, false);
            }
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, false);
        }
    }

    public static async Task<ProcessResult> RunAsync(string fileName, string arguments, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments
        };
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            psi.WorkingDirectory = workingDirectory;
        }
        return await RunAsync(psi, timeout, cancellationToken);
    }
}
