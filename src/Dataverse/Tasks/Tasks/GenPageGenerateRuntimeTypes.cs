using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class GenPageGenerateRuntimeTypes : Task
{
    [Required]
    public string ProjectDirectory { get; set; } = "";

    [Required]
    public string OutputPath { get; set; } = "";

    public string Command { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            var projectDirectory = Path.GetFullPath(ProjectDirectory);
            var outputPath = Path.GetFullPath(OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? projectDirectory);

            var command = string.IsNullOrWhiteSpace(Command)
                ? $"pac generate-types --output \"{outputPath}\""
                : Command.Replace("$(OutputPath)", outputPath).Replace("$(ProjectDirectory)", projectDirectory);

            if (TryRun(command, projectDirectory) && File.Exists(outputPath))
            {
                Log.LogMessage(MessageImportance.High, $"Generated GenPage runtime types: {outputPath}");
                return true;
            }

            if (!File.Exists(outputPath))
            {
                File.WriteAllText(outputPath,
                    "// Generated placeholder. Run PAC GenPage type generation to refresh runtime types.\n" +
                    "export {};\n",
                    new UTF8Encoding(false));
                Log.LogWarning($"PAC GenPage runtime type generation did not produce {outputPath}; wrote a placeholder RuntimeTypes.ts.");
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private bool TryRun(string command, string workingDirectory)
    {
        try
        {
            var shell = Environment.OSVersion.Platform == PlatformID.Win32NT ? "cmd.exe" : "/bin/sh";
            var args = Environment.OSVersion.Platform == PlatformID.Win32NT ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(shell, args)
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Log.LogMessage(MessageImportance.Low, e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Log.LogMessage(MessageImportance.Low, e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            if (process.ExitCode != 0)
                Log.LogMessage(MessageImportance.Low, $"GenPage runtime type command exited with code {process.ExitCode}: {command}");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.LogMessage(MessageImportance.Low, $"Could not run GenPage runtime type command '{command}': {ex.Message}");
            return false;
        }
    }
}
