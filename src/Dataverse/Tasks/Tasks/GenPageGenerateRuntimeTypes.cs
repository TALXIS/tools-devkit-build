using System;
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

    public int TimeoutSeconds { get; set; } = 600;

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

            var commandSucceeded = TryRun(command, projectDirectory);
            if (commandSucceeded && File.Exists(outputPath))
            {
                Log.LogMessage(MessageImportance.High, $"Generated GenPage runtime types: {outputPath}");
                return true;
            }

            if (!commandSucceeded)
                Log.LogWarning($"PAC GenPage runtime type generation command did not complete successfully: {command}");

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
        if (!NodeProcessRunner.TrySplitCommandLine(command, out var fileName, out var arguments, out var error))
        {
            Log.LogWarning($"Could not parse GenPage runtime type command '{command}': {error}");
            return false;
        }

        var result = NodeProcessRunner.Run(
            Log,
            fileName,
            arguments,
            workingDirectory,
            TimeoutSeconds,
            ignoreExitCode: true,
            standardOutputImportance: MessageImportance.Normal,
            standardErrorImportance: MessageImportance.High);

        if (result.TimedOut)
            Log.LogWarning($"GenPage runtime type command timed out after {TimeoutSeconds} seconds: {command}");
        else if (result.ExitCode != 0)
            Log.LogWarning($"GenPage runtime type command exited with code {result.ExitCode}: {command}");

        return result.Succeeded;
    }
}
