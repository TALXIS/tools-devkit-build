using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class RunNodeTool : Task
{
    [Required]
    public string FileName { get; set; } = "";

    public string Arguments { get; set; } = "";

    public string WorkingDirectory { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 600;

    public bool IgnoreExitCode { get; set; }

    public string StandardOutputImportance { get; set; } = "Normal";

    public string StandardErrorImportance { get; set; } = "High";

    /// <summary>
    /// Additional environment variables to set for the process, in KEY=VALUE format.
    /// </summary>
    public string[] EnvironmentVariables { get; set; } = Array.Empty<string>();

    [Output]
    public int ExitCode { get; set; }

    [Output]
    public bool TimedOut { get; set; }

    public override bool Execute()
    {
        var result = NodeProcessRunner.Run(
            Log,
            FileName,
            Arguments,
            WorkingDirectory,
            TimeoutSeconds,
            IgnoreExitCode,
            NodeProcessRunner.ParseImportance(StandardOutputImportance, MessageImportance.Normal),
            NodeProcessRunner.ParseImportance(StandardErrorImportance, MessageImportance.High),
            EnvironmentVariables);

        ExitCode = result.ExitCode;
        TimedOut = result.TimedOut;

        return result.Succeeded || IgnoreExitCode;
    }
}

internal sealed class NodeProcessResult
{
    public int ExitCode { get; set; } = -1;

    public bool TimedOut { get; set; }

    public bool Succeeded { get; set; }
}

internal static class NodeProcessRunner
{
    public static NodeProcessResult Run(
        TaskLoggingHelper log,
        string fileName,
        string arguments,
        string workingDirectory,
        int timeoutSeconds,
        bool ignoreExitCode,
        MessageImportance standardOutputImportance,
        MessageImportance standardErrorImportance,
        string[] environmentVariables = null)
    {
        var result = new NodeProcessResult();
        Process process = null;

        try
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                LogFailure(log, ignoreExitCode, "Node tool FileName is required.");
                return result;
            }

            var effectiveWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(workingDirectory);

            if (!Directory.Exists(effectiveWorkingDirectory))
            {
                LogFailure(log, ignoreExitCode, $"Working directory does not exist: {effectiveWorkingDirectory}");
                return result;
            }

            var resolvedFileName = ResolveExecutable(fileName, effectiveWorkingDirectory);
            var timeoutMilliseconds = timeoutSeconds <= 0
                ? 600000
                : checked(timeoutSeconds * 1000);

            log.LogMessage(MessageImportance.High, $"Running {resolvedFileName} {arguments}".Trim());

            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = resolvedFileName,
                    Arguments = arguments ?? string.Empty,
                    WorkingDirectory = effectiveWorkingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = false
            };

            foreach (var envVar in environmentVariables ?? Array.Empty<string>())
            {
                var eqIdx = envVar.IndexOf('=');
                if (eqIdx <= 0)
                    continue;

                var key = envVar.Substring(0, eqIdx);
                var value = envVar.Substring(eqIdx + 1);
                process.StartInfo.EnvironmentVariables[key] = value;
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    log.LogMessage(standardOutputImportance, e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    log.LogMessage(standardErrorImportance, e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMilliseconds))
            {
                result.TimedOut = true;
                KillProcessTree(process, log);
                process.WaitForExit(10000);
                LogFailure(log, ignoreExitCode, $"Command timed out after {timeoutSeconds} seconds: {resolvedFileName} {arguments}".Trim());
                return result;
            }

            process.WaitForExit();
            result.ExitCode = process.ExitCode;
            result.Succeeded = process.ExitCode == 0;

            if (!result.Succeeded)
                LogFailure(log, ignoreExitCode, $"Command exited with code {process.ExitCode}: {resolvedFileName} {arguments}".Trim());

            return result;
        }
        catch (Exception ex)
        {
            if (process != null)
                KillProcessTree(process, log);

            LogFailure(log, ignoreExitCode, $"Could not run command '{fileName} {arguments}'. {ex.Message}".Trim());
            return result;
        }
        finally
        {
            if (process != null)
                process.Dispose();
        }
    }

    public static MessageImportance ParseImportance(string value, MessageImportance defaultValue)
    {
        MessageImportance parsed;
        return Enum.TryParse(value, true, out parsed) ? parsed : defaultValue;
    }

    public static bool TrySplitCommandLine(string command, out string fileName, out string arguments, out string error)
    {
        fileName = string.Empty;
        arguments = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(command))
        {
            error = "Command is empty.";
            return false;
        }

        var trimmed = command.Trim();
        if (trimmed[0] == '"')
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote < 0)
            {
                error = "Quoted command executable is missing a closing quote.";
                return false;
            }

            fileName = trimmed.Substring(1, closingQuote - 1);
            arguments = trimmed.Substring(closingQuote + 1).TrimStart();
            return !string.IsNullOrWhiteSpace(fileName);
        }

        var firstWhitespace = -1;
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (char.IsWhiteSpace(trimmed[i]))
            {
                firstWhitespace = i;
                break;
            }
        }

        if (firstWhitespace < 0)
        {
            fileName = trimmed;
            return true;
        }

        fileName = trimmed.Substring(0, firstWhitespace);
        arguments = trimmed.Substring(firstWhitespace + 1).TrimStart();
        return !string.IsNullOrWhiteSpace(fileName);
    }

    private static void LogFailure(TaskLoggingHelper log, bool ignoreExitCode, string message)
    {
        if (ignoreExitCode)
            log.LogMessage(MessageImportance.High, message);
        else
            log.LogError(message);
    }

    private static string ResolveExecutable(string fileName, string workingDirectory)
    {
        var hasDirectory = fileName.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                           fileName.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                           Path.IsPathRooted(fileName);

        if (hasDirectory)
        {
            if (File.Exists(fileName))
                return Path.GetFullPath(fileName);

            var rootedCandidate = Path.Combine(workingDirectory, fileName);
            if (File.Exists(rootedCandidate))
                return Path.GetFullPath(rootedCandidate);

            if (IsWindows() && string.IsNullOrEmpty(Path.GetExtension(fileName)))
            {
                foreach (var extension in GetWindowsExecutableExtensions())
                {
                    if (File.Exists(fileName + extension))
                        return Path.GetFullPath(fileName + extension);

                    var workingDirectoryCandidate = Path.Combine(workingDirectory, fileName + extension);
                    if (File.Exists(workingDirectoryCandidate))
                        return Path.GetFullPath(workingDirectoryCandidate);
                }
            }

            return fileName;
        }

        foreach (var directory in GetPathDirectories())
        {
            if (IsWindows())
            {
                var extensions = string.IsNullOrEmpty(Path.GetExtension(fileName))
                    ? GetWindowsExecutableExtensions()
                    : new[] { string.Empty };

                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(directory, fileName + extension);
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            else
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return fileName;
    }

    private static string[] GetPathDirectories()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] GetWindowsExecutableExtensions()
    {
        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        if (string.IsNullOrWhiteSpace(pathExt))
            pathExt = ".COM;.EXE;.BAT;.CMD";

        return pathExt.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool IsWindows()
    {
        return Path.DirectorySeparatorChar == '\\';
    }

    private static void KillProcessTree(Process process, TaskLoggingHelper log)
    {
        try
        {
            if (process.HasExited)
                return;

#if NET6_0_OR_GREATER
            process.Kill(entireProcessTree: true);
#else
            if (IsWindows())
                KillWindowsProcessTree(process.Id, log);
            else
                KillUnixProcessTree(process, log);
#endif
        }
        catch (Exception ex)
        {
            log.LogWarning($"Failed to kill timed-out command process tree for PID {SafeGetProcessId(process)}: {ex.Message}");
        }
    }

#if !NET6_0_OR_GREATER
    private static void KillWindowsProcessTree(int processId, TaskLoggingHelper log)
    {
        using (var taskKill = new Process())
        {
            taskKill.StartInfo = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/PID {processId} /T /F",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            taskKill.Start();
            if (!taskKill.WaitForExit(10000))
            {
                taskKill.Kill();
                log.LogWarning($"taskkill timed out while killing PID {processId}.");
            }
        }
    }

    private static void KillUnixProcessTree(Process process, TaskLoggingHelper log)
    {
        TryRunKillCommand($"-TERM -{process.Id}", log);
        Thread.Sleep(1000);

        if (!process.HasExited)
            process.Kill();
    }

    private static void TryRunKillCommand(string arguments, TaskLoggingHelper log)
    {
        try
        {
            using (var kill = new Process())
            {
                kill.StartInfo = new ProcessStartInfo
                {
                    FileName = "kill",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                kill.Start();
                kill.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            log.LogMessage(MessageImportance.Low, $"Could not signal Unix process group with kill {arguments}: {ex.Message}");
        }
    }
#endif

    private static int SafeGetProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return -1;
        }
    }
}
