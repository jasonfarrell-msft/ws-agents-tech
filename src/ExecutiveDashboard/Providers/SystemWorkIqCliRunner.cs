using System.Diagnostics;
using System.Text;

namespace ExecutiveDashboard.Providers;

public sealed class SystemWorkIqCliRunner : IWorkIqCliRunner
{
    public async Task<WorkIqCliExecutionResult> RunAsync(WorkIqCliInvocation invocation, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                return WorkIqCliExecutionResult.FailedToStart("Work IQ CLI could not be started.");
            }
        }
        catch (FileNotFoundException)
        {
            return WorkIqCliExecutionResult.MissingExecutable("Work IQ CLI executable was not found. Install it with `npm install -g @microsoft/workiq` or configure npx.");
        }
        catch (PlatformNotSupportedException)
        {
            return WorkIqCliExecutionResult.FailedToStart("Work IQ CLI is not supported on this platform.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return WorkIqCliExecutionResult.FailedToStart("Work IQ CLI could not be started. Verify the executable path and local execute permissions.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        var timeoutTask = Task.Delay(invocation.Timeout);
        var exitTask = process.WaitForExitAsync(cancellationToken);

        try
        {
            var completedTask = await Task.WhenAny(exitTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                TryKill(process);
                return WorkIqCliExecutionResult.TimedOut("Work IQ CLI timed out before returning a valid response.");
            }

            await exitTask;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return WorkIqCliExecutionResult.Canceled("Work IQ CLI request was canceled before a valid response was received.");
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            return WorkIqCliExecutionResult.NonZeroExit(
                process.ExitCode,
                standardError,
                $"Work IQ CLI exited with code {process.ExitCode}.");
        }

        return WorkIqCliExecutionResult.Available(standardOutput);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
