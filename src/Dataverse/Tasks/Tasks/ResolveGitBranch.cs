using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class ResolveGitBranch : Task
{
    [Required]
    public string ProjectPath { get; set; }

    [Output]
    public string GitBranch { get; private set; }

    public override bool Execute()
    {
        if (!TryFindGitRoot(ProjectPath, out var gitRoot))
        {
            Log.LogMessage(MessageImportance.High, "Git repository not found; cannot resolve branch.");
            GitBranch = "";
            return true;
        }

        try
        {
            var gitInfo = new ProcessStartInfo("git")
            {
                Arguments = "rev-parse --abbrev-ref HEAD",
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = gitRoot
            };

            using (var process = Process.Start(gitInfo))
            {
                string stderr = process.StandardError.ReadToEnd();
                string result = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    Log.LogWarning($"Git command failed: {stderr}");
                    GitBranch = "";
                    return true;
                }

                GitBranch = result.Trim();
            }

            Log.LogMessage(MessageImportance.High, $"Resolved Git branch: {GitBranch}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to resolve Git branch: {ex.Message}");
            GitBranch = "";
        }

        return true;
    }

    private bool TryFindGitRoot(string path, out string gitRoot)
    {
        var directory = new DirectoryInfo(path);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                gitRoot = directory.FullName;
                return true;
            }
            directory = directory.Parent;
        }
        gitRoot = null;
        return false;
    }
}
