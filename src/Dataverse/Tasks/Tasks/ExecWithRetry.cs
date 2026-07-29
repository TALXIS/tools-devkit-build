using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Runs a shell command, retrying with backoff whenever the process's combined stdout/stderr
/// contains a specific message and the process fails. Any other failure propagates immediately,
/// with its full output surfaced at high importance so it remains visible at default MSBuild
/// verbosity.
/// </summary>
public class ExecWithRetry : Task, ICancelableTask
{
    private static readonly int[] DefaultDelaysMilliseconds = { 1000, 2000, 4000, 8000, 16000 };
    private const int DefaultMaxAttempts = 45;
    private const int DefaultFallbackDelayMilliseconds = 30000;

    private readonly CancellationTokenSource cancellationSource = new();
    private readonly object processLock = new();
    private Process currentProcess;

    [Required]
    public string Command { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Additional environment variables for the spawned process, one "NAME=value" entry per
    /// item - matching MSBuild's own Exec.EnvironmentVariables convention.
    /// </summary>
    public string[] EnvironmentVariables { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Substring to look for in the process's combined stdout+stderr. When a failed attempt's
    /// output contains this message, the command is retried with backoff. When empty, the
    /// command runs once and any failure is propagated immediately.
    /// </summary>
    public string LockMessage { get; set; } = string.Empty;

    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>
    /// Semicolon-separated backoff delays in milliseconds for the first attempts. Any attempt
    /// beyond the list uses <see cref="FallbackDelayMilliseconds"/>.
    /// </summary>
    public string DelaysMilliseconds { get; set; } = string.Empty;

    public int FallbackDelayMilliseconds { get; set; } = DefaultFallbackDelayMilliseconds;

    /// <summary>
    /// Signals cancellation to the running command and interrupts any backoff delay immediately.
    /// </summary>
    public void Cancel()
    {
        Process processToTerminate;
        lock (processLock)
        {
            if (cancellationSource.IsCancellationRequested)
            {
                return;
            }

            cancellationSource.Cancel();
            processToTerminate = currentProcess;
        }

        TryTerminateProcess(processToTerminate);
    }

    public override bool Execute()
    {
        try
        {
            var delays = ParseDelays(DelaysMilliseconds);
            var attempt = 0;

            while (true)
            {
                cancellationSource.Token.ThrowIfCancellationRequested();
                attempt++;
                var result = RunOnce(Command, WorkingDirectory, EnvironmentVariables);

                if (result.ExitCode == 0)
                {
                    return true;
                }

                var isRetryableConflict = !string.IsNullOrEmpty(LockMessage) && result.Output.Contains(LockMessage);

                if (!isRetryableConflict)
                {
                    Log.LogMessage(MessageImportance.High, result.Output);
                    Log.LogError($"Command failed with exit code {result.ExitCode}: {Command}");
                    return false;
                }

                if (attempt >= MaxAttempts)
                {
                    Log.LogMessage(MessageImportance.High, result.Output);
                    Log.LogError($"Command is still locked by another process after {attempt} attempts, giving up: {Command}");
                    return false;
                }

                var delay = attempt - 1 < delays.Length ? delays[attempt - 1] : FallbackDelayMilliseconds;
                Log.LogMessage(MessageImportance.Normal, $"Waiting for another process to finish, retrying ({attempt}/{MaxAttempts}) in {delay / 1000}s...");
                WaitForRetryDelay(delay);
            }
        }
        catch (ArgumentException ex)
        {
            Log.LogError(ex.Message);
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static int[] ParseDelays(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultDelaysMilliseconds;
        }

        var parts = raw.Split(';');
        var delays = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var delay) || delay < 0)
            {
                throw new ArgumentException($"Invalid DelaysMilliseconds value '{raw}'. Expected a semicolon-separated list of non-negative integer millisecond values, for example '1000;2000;4000'.");
            }

            delays[i] = delay;
        }
        return delays;
    }

    private (int ExitCode, string Output) RunOnce(string command, string workingDirectory, string[] environmentVariables)
    {
        var startInfo = CreateShellStartInfo(command, workingDirectory, environmentVariables);
        var buffer = new StringBuilder();
        var bufferLock = new object();

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }
            Log.LogMessage(MessageImportance.Low, e.Data);
            lock (bufferLock)
            {
                buffer.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }
            Log.LogMessage(MessageImportance.Low, e.Data);
            lock (bufferLock)
            {
                buffer.AppendLine(e.Data);
            }
        };

        try
        {
            lock (processLock)
            {
                cancellationSource.Token.ThrowIfCancellationRequested();
                process.Start();
                currentProcess = process;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            WaitForExit(process);
            process.WaitForExit();

            lock (bufferLock)
            {
                return (process.ExitCode, buffer.ToString());
            }
        }
        catch
        {
            TryTerminateProcess(process);
            throw;
        }
        finally
        {
            lock (processLock)
            {
                if (ReferenceEquals(currentProcess, process))
                {
                    currentProcess = null;
                }
            }
        }
    }

    private void WaitForRetryDelay(int delayMilliseconds)
    {
        if (delayMilliseconds <= 0)
        {
            cancellationSource.Token.ThrowIfCancellationRequested();
            return;
        }

        cancellationSource.Token.WaitHandle.WaitOne(delayMilliseconds);
        cancellationSource.Token.ThrowIfCancellationRequested();
    }

    private void WaitForExit(Process process)
    {
        while (!process.WaitForExit(250))
        {
            cancellationSource.Token.ThrowIfCancellationRequested();
        }
    }

    private static void TryTerminateProcess(Process process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static ProcessStartInfo CreateShellStartInfo(string command, string workingDirectory, string[] environmentVariables)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/d", "/s", "/c", command }
            }
            : new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { "-c", command }
            };

        startInfo.WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        foreach (var entry in environmentVariables ?? Array.Empty<string>())
        {
            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            startInfo.EnvironmentVariables[entry.Substring(0, separatorIndex)] = entry.Substring(separatorIndex + 1);
        }

        return startInfo;
    }
}
