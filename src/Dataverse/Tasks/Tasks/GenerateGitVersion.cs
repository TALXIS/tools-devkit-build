using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Threading;

public class GenerateGitVersion : Task
{
    [Required]
    public ushort VersionMajor { get; set; }

    [Required]
    public ushort VersionMinor { get; set; }

    [Required]
    public string ProjectPath { get; set; }
    [Required]
    public string ProjectFileName { get; set; }
    public string ApplyToBranches { get; set; } // template "master;hotfix;develop:1;pr:3;other:0"
    public string LocalBranchBuildVersionNumber { get; set; }

    [Output]
    public string VersionOutput { get; private set; }

    private IEnumerable<BranchVersioning> _branches;

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "Preparing to generate version number...");

        if (LocalBranchBuildVersionNumber == null)
        {
            Log.LogWarning("LocalBranchBuildVersionNumber is null, setting to default.");
            LocalBranchBuildVersionNumber = "0.0.0.1";
        }

        // Prepare for running git commands
        var gitInfo = CreateGitProcessInfo(ProjectPath);
        if (!IsGitRepository(gitInfo))
        {
            Log.LogWarning($"Git repository not found for ProjectPath '{ProjectPath}'. Falling back to LocalBranchBuildVersionNumber.");
            VersionOutput = LocalBranchBuildVersionNumber;
            return true;
        }

        var currentBranch = GetCurrentBranch(gitInfo);
        if (string.IsNullOrWhiteSpace(ApplyToBranches))
        {
            Log.LogWarning("ApplyToBranches is empty. Falling back to LocalBranchBuildVersionNumber.");
            VersionOutput = LocalBranchBuildVersionNumber;
            return true;
        }

        _branches = ApplyToBranches.Split(';').Select(BranchVersioning.Parse);
        if (_branches == null || !_branches.Any())
        {
            Log.LogWarning($"No valid branches found in ApplyToBranches '{ApplyToBranches}'.");
            VersionOutput = LocalBranchBuildVersionNumber;
            return true;
        }
        var branch = _branches.FirstOrDefault(b =>
            string.Equals(b.BranchName, currentBranch, StringComparison.OrdinalIgnoreCase) ||
            // Basic wildcard support, e.g. feature/*
            (b.BranchName.EndsWith("*") && currentBranch.StartsWith(b.BranchName.TrimEnd('*'), StringComparison.OrdinalIgnoreCase))
        );
        if (branch == null)
        {
            Log.LogWarning($"The current branch '{currentBranch}' is not enabled for automatic Git versioning.");
            VersionOutput = LocalBranchBuildVersionNumber;
            return true;
        }
        else
        {
            Log.LogMessage($"The current branch '{currentBranch}' is enabled for automatic Git versioning.");

            var projects = new List<string>
            {
                ProjectPath
            };
            RetrieveAllProjectReferences(ProjectPath, projects);
            Log.LogMessage(MessageImportance.High, $"Got number of projects: {projects.Count}");

            var totalComitCount = 0;
            var latestCommitDate = new DateTime(1900, 1, 1);

            foreach (var project in projects)
            {
                Log.LogMessage(MessageImportance.High, $"Project: {project}");
                var (commitCount, lastCommitDate) = GetNumberOfCommits(project);
                Log.LogMessage(MessageImportance.High, $"{project}: Last commit: {lastCommitDate:yyyy-MM-dd}, count: {commitCount}");
                if (latestCommitDate < lastCommitDate)
                {
                    totalComitCount = commitCount;
                    latestCommitDate = lastCommitDate;
                }
                else if (latestCommitDate == lastCommitDate)
                {
                    totalComitCount += commitCount;
                }
            }
            Log.LogMessage(MessageImportance.High, $"Commit count for the month: {totalComitCount}");
            if (totalComitCount > 999)
            {
                throw new Exception($"Too many commits ({totalComitCount} > 999), cannot generate version number. Please reach out to the author.");
            }

            // Convert the latest commit date to build number
            // DateTime lastCommitDateTime = DateTime.ParseExact(latestCommitDate, "yyyy-MM-dd HH:mm:ss K", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            var build = ushort.Parse(latestCommitDate.ToString("yyMM"));
            if (branch.Prefix.HasValue)
            {
                build = ushort.Parse($"{branch.Prefix}{latestCommitDate:yyMM}");
            }

            // Get the revision number as the last commit day and commit count for the month (reduce risk of deploying lower version after refactoring)
            var revision = ushort.Parse($"{latestCommitDate:dd}{totalComitCount:000}");

            // Combine the version parts into final version number
            VersionOutput = $"{VersionMajor}.{VersionMinor}.{build}.{revision}";

            return true;
        }
    }

    private (int, DateTime) GetNumberOfCommits(string projectPath)
    {
        ProcessStartInfo gitInfo = CreateGitProcessInfo(projectPath);
        // Retrieve latest commit date
        string lastCommitDate = GetLatestCommitDate(gitInfo);
        string filterSinceDate = lastCommitDate.Substring(0, 10);

        // Retrieve and process commit hashes
        string[] commits = ExecuteGitCommand(gitInfo, $"log --pretty=format:\"%H|||%ad|||%s\" --date=iso --since={filterSinceDate}T00:00 -- {projectPath}").Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int commitCountInMonth = ProcessCommits(commits, gitInfo, lastCommitDate);

        Log.LogMessage(MessageImportance.High, $"Commit count for the month: {commitCountInMonth}");
        DateTime lastCommitDateTime = DateTime.ParseExact(lastCommitDate, "yyyy-MM-dd HH:mm:ss K", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

        return (commitCountInMonth, new DateTime(lastCommitDateTime.Year, lastCommitDateTime.Month, lastCommitDateTime.Day));
    }

    private string GetCurrentBranch(ProcessStartInfo gitInfo)
    {
        //var repositoryPath = Repository.Discover(ProjectPath);
        //using (var repo = new Repository(repositoryPath))
        //{

        //}
        string branchCommand = "rev-parse --abbrev-ref HEAD";
        string output = ExecuteGitCommand(gitInfo, branchCommand);
        return output.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private string GetLatestCommitDate(ProcessStartInfo gitInfo)
    {
        string commitDateCommand = "log -1 --pretty=format:\"%ad\" --date=iso-local";
        string output = ExecuteGitCommand(gitInfo, commitDateCommand);
        return output.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
    }

    private int ProcessCommits(string[] commits, ProcessStartInfo gitInfo, string lastCommitDate)
    {
        int commitCount = 0;

        Log.LogMessage(MessageImportance.High, $"The following commits were detected in this period:");
        // Loop over all commits in the project
        foreach (string commitData in commits)
        {
            string[] data = commitData.Split(new string[] { "|||" }, StringSplitOptions.None);
            string commitDate = data[1];
            string commitMessage = data[2];

            Log.LogMessage(MessageImportance.High, $" > {commitDate} - {commitMessage}");

            // Try to parse commit date and update commit count for the month
            if (DateTime.TryParseExact(commitDate, "yyyy-MM-dd HH:mm:ss K", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime commitDateTime) && commitDateTime.ToString("yyyyMMdd") == lastCommitDate.Substring(0, 10).Replace("-", ""))
            {
                commitCount++;
            }
            else
            {
                Log.LogMessage(MessageImportance.High, $"Error parsing date for commit: Date - {commitDate}, Message - {commitMessage}");
            }
        }

        return commitCount;
    }

    private static ProcessStartInfo CreateGitProcessInfo(string projectPath)
    {
        return new ProcessStartInfo("git")
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = projectPath
        };
    }

    private string ExecuteGitCommand(ProcessStartInfo gitInfo, string command)
    {
        gitInfo.Arguments = command;

        Log.LogMessage(MessageImportance.High, $"Executing git command: {command}");

        using (Process process = Process.Start(gitInfo))
        {
            using (StreamReader reader = process.StandardOutput)
            {
                string stderr = process.StandardError.ReadToEnd();  // Read the error stream first and then stdout.
                string result = reader.ReadToEnd();  // Read stdout
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new Exception("Git command failed with error message: " + stderr);
                }

                return result;
            }
        }
    }

    private bool IsGitRepository(ProcessStartInfo gitInfo)
    {
        try
        {
            string output = ExecuteGitCommand(gitInfo, "rev-parse --is-inside-work-tree");
            return output.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.LogMessage(MessageImportance.Low, $"Git repository check failed: {ex.Message}");
            return false;
        }
    }
    private void RetrieveAllProjectReferences(string projectPath, List<string> projects)
    {
        var projectFile = "";

        DirectoryInfo folder = new DirectoryInfo(projectPath);
        if (!folder.Exists)
        {
            return;
        }

        FileInfo[] files = folder.GetFiles("*.cdsproj");
        if (files.Length == 0)
        {
            files = folder.GetFiles("*.csproj");
        }
        if (files.Length == 0)
        {
            files = folder.GetFiles("*.pcfproj");
        }
        foreach (FileInfo file in files)
        {
            projectFile = file.FullName;
        }

        if (string.IsNullOrWhiteSpace(projectFile) || !File.Exists(projectFile))
        {
            return;
        }

        var projectDir = Path.GetDirectoryName(projectFile);
        var doc = XDocument.Load(projectFile);

        Log.LogMessage(MessageImportance.High, $"{projectFile} - {projectDir} - {doc.Descendants()}");

        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        var descendants = doc.Descendants(ns + "ProjectReference");
        if (descendants == null || !descendants.Any())
        {
            ns = "";
            descendants = doc.Descendants(ns + "ProjectReference");
        }

        foreach (var reference in descendants)
        {
            var referencedProjectPath = Directory.GetParent(Path.Combine(projectDir, reference.Attribute("Include").Value)).FullName;
            if (!projects.Exists(p => string.Equals(p, referencedProjectPath, StringComparison.OrdinalIgnoreCase)))
            {
                projects.Add(referencedProjectPath);
                RetrieveAllProjectReferences(referencedProjectPath, projects);
            }
        }
    }
    private class BranchVersioning
    {
        public string BranchName { get; set; }
        public int? Prefix { get; set; }
        public static BranchVersioning Parse(string branchDefinition)
        {
            var parts = branchDefinition.Split(':');
            var branchName = parts[0].Trim();
            int prefix = 0;
            if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out int parsedPrefix))
            {
                if (parsedPrefix < 0 || parsedPrefix > 5)
                {
                    throw new ArgumentOutOfRangeException($"Branch prefix must be between 0 and 5, but got {parsedPrefix} for branch '{branchName}'.");
                }
                prefix = parsedPrefix;
            }
            return new BranchVersioning { BranchName = branchName, Prefix = prefix };
        }
    }
}
