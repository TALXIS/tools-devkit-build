using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Runs a shell command for the Node build with the guarantees a parallel .NET build needs:
/// <list type="bullet">
/// <item>Commands sharing <see cref="MutexName"/> never run at the same time, across MSBuild nodes
/// and processes. The wait is bounded and reported.</item>
/// <item>After the lock is acquired, <see cref="UpToDateStampPath"/> is checked again, so builds
/// queued behind the one that just installed skip their own install.</item>
/// <item>A failure whose output contains <see cref="LockMessage"/> (the tool's own "already running"
/// lock) is retried with backoff; the lock is released while waiting.</item>
/// <item>A failure whose output contains <see cref="FallbackMessage"/> runs
/// <see cref="FallbackCommand"/> once instead.</item>
/// <item>Cancellation (Ctrl+C) kills the whole process tree and never waits on a pipe held open
/// by a grandchild.</item>
/// <item>Output streams at low importance; on failure the full output is logged at high
/// importance, so the real error shows at default verbosity.</item>
/// </list>
/// </summary>
public class ExecWithRetry : Task, ICancelableTask
{
    private static readonly int[] DefaultDelaysMilliseconds = { 1000, 2000, 4000, 8000, 16000 };
    private const int DefaultMaxAttempts = 10;
    private const int DefaultFallbackDelayMilliseconds = 30000;
    private const int DefaultMutexTimeoutSeconds = 1800;
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource cancellationSource = new();
    private readonly object processLock = new();
    private Process currentProcess;

    [Required]
    public string Command { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>"NAME=value" entries for the spawned process, like Exec.EnvironmentVariables.</summary>
    public string[] EnvironmentVariables { get; set; } = Array.Empty<string>();

    /// <summary>Output text meaning another process holds the tool's own lock; such failures are retried.</summary>
    public string LockMessage { get; set; } = string.Empty;

    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>Semicolon-separated backoff delays in milliseconds for the first retries.</summary>
    public string DelaysMilliseconds { get; set; } = string.Empty;

    public int FallbackDelayMilliseconds { get; set; } = DefaultFallbackDelayMilliseconds;

    /// <summary>Key of the cross-process lock; commands with the same key are serialized.</summary>
    public string MutexName { get; set; } = string.Empty;

    /// <summary>How long to wait for the lock before failing.</summary>
    public int MutexTimeoutSeconds { get; set; } = DefaultMutexTimeoutSeconds;

    /// <summary>
    /// Stamp written after a successful run by the caller. When it is newer than every file in
    /// <see cref="UpToDateInputs"/> once the lock is held, the command is skipped.
    /// </summary>
    public string UpToDateStampPath { get; set; } = string.Empty;

    public ITaskItem[] UpToDateInputs { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Touch <see cref="UpToDateStampPath"/> after a successful run, while the lock is still held,
    /// so a build waiting for the lock sees it. Leave false when the tool writes its own stamp.
    /// </summary>
    public bool WriteUpToDateStamp { get; set; }

    /// <summary>Command to run once when the first command fails with <see cref="FallbackMessage"/> in its output.</summary>
    public string FallbackCommand { get; set; } = string.Empty;

    public string FallbackMessage { get; set; } = string.Empty;

    [Output]
    public bool Skipped { get; private set; }

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
        Mutex mutex = null;
        var mutexHeld = false;
        try
        {
            var delays = ParseDelays(DelaysMilliseconds);
            var platformMutexName = string.IsNullOrEmpty(MutexName) ? string.Empty : CreatePlatformMutexName(MutexName);
            if (platformMutexName.Length > 0)
            {
                mutex = new Mutex(initiallyOwned: false, platformMutexName);
                mutexHeld = AcquireMutex(mutex);
                if (!mutexHeld)
                {
                    return false;
                }
            }

            if (IsUpToDate())
            {
                Skipped = true;
                Log.LogMessage(MessageImportance.Normal, $"Up to date (completed by another build while waiting): {Command}");
                return true;
            }

            var command = Command;
            var fallbackUsed = false;
            var attempt = 0;
            while (true)
            {
                cancellationSource.Token.ThrowIfCancellationRequested();
                attempt++;
                Log.LogMessage(MessageImportance.Normal, $"Running in '{EffectiveWorkingDirectory()}': {command}");
                var result = RunOnce(command);
                if (result.ExitCode == 0)
                {
                    if (WriteUpToDateStamp && UpToDateStampPath.Length > 0)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(UpToDateStampPath))!);
                        File.WriteAllText(UpToDateStampPath, string.Empty);
                        File.SetLastWriteTimeUtc(UpToDateStampPath, DateTime.UtcNow);
                    }
                    return true;
                }

                if (!fallbackUsed && FallbackCommand.Length > 0 && FallbackMessage.Length > 0 &&
                    result.Output.Contains(FallbackMessage, StringComparison.Ordinal))
                {
                    fallbackUsed = true;
                    Log.LogMessage(MessageImportance.High, $"'{command}' failed with \"{FallbackMessage}\"; running '{FallbackCommand}' instead.");
                    command = FallbackCommand;
                    attempt = 0;
                    continue;
                }

                var lockConflict = LockMessage.Length > 0 && result.Output.Contains(LockMessage, StringComparison.Ordinal);
                if (!lockConflict || attempt >= MaxAttempts)
                {
                    Log.LogMessage(MessageImportance.High, result.Output);
                    Log.LogError(lockConflict
                        ? $"Still locked by another process after {attempt} attempts, giving up: {command}"
                        : $"Command failed with exit code {result.ExitCode}: {command}");
                    return false;
                }

                var delay = attempt - 1 < delays.Length ? delays[attempt - 1] : FallbackDelayMilliseconds;
                Log.LogMessage(MessageImportance.High, $"Another process holds the tool's lock; retry {attempt}/{MaxAttempts} in {delay / 1000}s.");
                if (mutexHeld)
                {
                    mutex.ReleaseMutex();
                    mutexHeld = false;
                }
                WaitForRetryDelay(delay);
                if (mutex != null)
                {
                    mutexHeld = AcquireMutex(mutex);
                    if (!mutexHeld)
                    {
                        return false;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.LogWarning($"Cancelled: {Command}");
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or Win32Exception or UnauthorizedAccessException or WaitHandleCannotBeOpenedException or InvalidOperationException)
        {
            Log.LogError($"Could not run '{Command}': {ex.Message}");
            return false;
        }
        finally
        {
            if (mutexHeld)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }
            mutex?.Dispose();
        }
    }

    private bool AcquireMutex(Mutex mutex)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, MutexTimeoutSeconds));
        var nextReport = DateTime.UtcNow;
        while (true)
        {
            cancellationSource.Token.ThrowIfCancellationRequested();
            try
            {
                if (mutex.WaitOne(250))
                {
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous holder died without releasing; ownership passed to this process.
                return true;
            }

            var now = DateTime.UtcNow;
            if (now >= deadline)
            {
                Log.LogError($"Waited {MutexTimeoutSeconds}s for another build that is running a Node command for '{MutexName}'. If no other build is running, a build may be stuck; stop it and retry.");
                return false;
            }

            if (now >= nextReport)
            {
                Log.LogMessage(MessageImportance.High, $"Waiting for another build that is running a Node command for '{MutexName}'...");
                nextReport = now.AddMinutes(1);
            }
        }
    }

    private bool IsUpToDate()
    {
        if (UpToDateStampPath.Length == 0 || !File.Exists(UpToDateStampPath))
        {
            return false;
        }

        var stampTime = File.GetLastWriteTimeUtc(UpToDateStampPath);
        return UpToDateInputs
            .Select(input => input.GetMetadata("FullPath"))
            .Where(File.Exists)
            .All(path => File.GetLastWriteTimeUtc(path) <= stampTime);
    }

    // Global\ makes the lock machine-wide on Windows (other sessions too); POSIX names are already
    // shared by every process of the user. The key is hashed to a valid, fixed-length name.
    private static string CreatePlatformMutexName(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Replace('\\', '/').TrimEnd('/').ToLowerInvariant())));
        return OperatingSystem.IsWindows() ? $"Global\\TALXIS.Node.{hash}" : $"TALXIS.Node.{hash}";
    }

    private static int[] ParseDelays(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultDelaysMilliseconds;
        }

        return raw.Split(';').Select(part =>
            int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var delay)
                ? delay
                : throw new ArgumentException($"Invalid DelaysMilliseconds value '{raw}'. Expected non-negative integers separated by semicolons, for example '1000;2000;4000'.")).ToArray();
    }

    private string EffectiveWorkingDirectory() =>
        string.IsNullOrWhiteSpace(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory;

    private (int ExitCode, string Output) RunOnce(string command)
    {
        var buffer = new StringBuilder();
        var bufferLock = new object();
        using var outputClosed = new ManualResetEventSlim();
        using var errorClosed = new ManualResetEventSlim();
        using var process = new Process { StartInfo = CreateShellStartInfo(command) };

        void Capture(string data, ManualResetEventSlim closed)
        {
            if (data == null)
            {
                closed.Set();
                return;
            }
            Log.LogMessage(MessageImportance.Low, data);
            lock (bufferLock)
            {
                buffer.AppendLine(data);
            }
        }

        process.OutputDataReceived += (_, e) => Capture(e.Data, outputClosed);
        process.ErrorDataReceived += (_, e) => Capture(e.Data, errorClosed);

        try
        {
            lock (processLock)
            {
                cancellationSource.Token.ThrowIfCancellationRequested();
                process.Start();
                currentProcess = process;
            }

            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            while (!process.WaitForExit(250))
            {
                cancellationSource.Token.ThrowIfCancellationRequested();
            }

            // A grandchild that inherited the pipes can keep them open after the shell exits; wait
            // for the remaining output only for a bounded time instead of WaitForExit().
            WaitHandle.WaitAll(new[] { outputClosed.WaitHandle, errorClosed.WaitHandle }, OutputDrainTimeout);

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
        if (delayMilliseconds > 0)
        {
            cancellationSource.Token.WaitHandle.WaitOne(delayMilliseconds);
        }
        cancellationSource.Token.ThrowIfCancellationRequested();
    }

    private static void TryTerminateProcess(Process process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private ProcessStartInfo CreateShellStartInfo(string command)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo { FileName = "cmd.exe", Arguments = "/d /s /c \"" + command + "\"" }
            : new ProcessStartInfo { FileName = "/bin/sh", ArgumentList = { "-c", command } };

        startInfo.WorkingDirectory = EffectiveWorkingDirectory();
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        foreach (var entry in EnvironmentVariables ?? Array.Empty<string>())
        {
            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex > 0)
            {
                startInfo.EnvironmentVariables[entry.Substring(0, separatorIndex)] = entry.Substring(separatorIndex + 1);
            }
        }

        return startInfo;
    }
}
