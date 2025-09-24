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
    public UInt16 VersionMajor { get; set; }

    [Required]
    public UInt16 VersionMinor { get; set; }

    [Required]
    public string ProjectPath { get; set; }
    [Required]
    public string ProjectFileName { get; set; }
    public string ApplyToBranches { get; set; } // template "master,hotfix,develop:1,pr:3,other:0"
    public string LocalBranchBuildVersionNumber { get; set; }

    [Output]
    public string VersionOutput { get; private set; }

    public override bool Execute()
    {
        try
        {
            Log.LogMessage(MessageImportance.High, "Preparing to generate version number...");

            // Prepare for running git commands
            ProcessStartInfo gitInfo = CreateGitProcessInfo(ProjectPath);

            if (!string.IsNullOrEmpty(ApplyToBranches))
            {
                string currentBranch = GetCurrentBranch(gitInfo);
                string[] enabledBranches = ApplyToBranches.Split(',');
                if (!enabledBranches.Any(branch => branch.Trim().ToLower() == currentBranch.ToLower()))
                {
                    Log.LogWarning($"The current branch '{currentBranch}' is enabled for automatic Git versioning.");
                    if (!string.IsNullOrEmpty(LocalBranchBuildVersionNumber))
                    {
                        VersionOutput = $"0.0.{LocalBranchBuildVersionNumber}.0";
                    }
                    else
                    {
                        VersionOutput = $"0.0.0.0";
                    }
                    return true;
                }
            }

            var projects = new List<string>();
            projects.Add(ProjectPath);
            RetrieveAllProjectReferences(ProjectPath, projects);
            Log.LogMessage(MessageImportance.High, $"Got number of projects: {projects.Count}");

            var totalComitCount = 0;
            DateTime latestCommitDate = new DateTime(1900, 1, 1);

            foreach (var project in projects)
            {
                Log.LogMessage(MessageImportance.High, $"Project: {project}");
                var (commitCountInMonth, lastCommitDate) = GetNumberOfCommits(project);
                totalComitCount += commitCountInMonth;
                if (latestCommitDate < lastCommitDate)
                {
                    latestCommitDate = lastCommitDate;
                }
                Log.LogMessage(MessageImportance.High, $"Commit count for the month: {commitCountInMonth}, last commit: {lastCommitDate}");
            }
            Log.LogMessage(MessageImportance.High, $"Commit count for the month: {totalComitCount}");

            // Convert the latest commit date to build number
            // DateTime lastCommitDateTime = DateTime.ParseExact(latestCommitDate, "yyyy-MM-dd HH:mm:ss K", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            UInt16 build = UInt16.Parse(latestCommitDate.ToString("yyMM"));

            // Get the revision number as the last commit day and commit count for the month (reduce risk of deploying lower version after refactoring)
            UInt16 revision = UInt16.Parse(latestCommitDate.ToString("dd") + totalComitCount);

            // Combine the version parts into final version number
            VersionOutput = $"{VersionMajor}.{VersionMinor}.{build}.{revision}";
        }
        catch (Exception ex)
        {
            Log.LogMessage(MessageImportance.High, $"Error generating version number: {ex.Message}");
            // If there's any error, fall back to a version number without build and revision
            VersionOutput = $"{VersionMajor}.{VersionMinor}.0.0";
        }
        return true;
    }

    private (int, DateTime) GetNumberOfCommits(string projectPath)
    {
        ProcessStartInfo gitInfo = CreateGitProcessInfo(projectPath);
        // Retrieve latest commit date
        string lastCommitDate = GetLatestCommitDate(gitInfo);
        string filterSinceDate = lastCommitDate.Substring(0, 7) + "-01";

        // Retrieve and process commit hashes
        string[] commits = ExecuteGitCommand(gitInfo, $"log --pretty=format:\"%H|||%ad|||%s\" --date=iso --since={filterSinceDate} -- {projectPath}").Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int commitCountInMonth = ProcessCommits(commits, gitInfo, lastCommitDate);

        Log.LogMessage(MessageImportance.High, $"Commit count for the month: {commitCountInMonth}");
        DateTime lastCommitDateTime = DateTime.ParseExact(lastCommitDate, "yyyy-MM-dd HH:mm:ss K", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

        return (commitCountInMonth, lastCommitDateTime);
    }

    private string GetCurrentBranch(ProcessStartInfo gitInfo)
    {
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
        int commitCountInMonth = 0;

        Log.LogMessage(MessageImportance.High, $"The following commits were detected in this period:");
        // Loop over all commits in the project
        foreach (string commitData in commits)
        {
            string[] data = commitData.Split(new string[] { "|||" }, StringSplitOptions.None);
            string commitDate = data[1];
            string commitMessage = data[2];

            Log.LogMessage(MessageImportance.High, $" > {commitDate} - {commitMessage}");

            // Try to parse commit date and update commit count for the month
            if (DateTime.TryParseExact(commitDate, "yyyy-MM-dd HH:mm:ss K", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime commitDateTime) && commitDateTime.ToString("yyyyMM") == lastCommitDate.Substring(0, 7).Replace("-", ""))
            {
                commitCountInMonth++;
            }
            else
            {
                Log.LogMessage(MessageImportance.High, $"Error parsing date for commit: Date - {commitDate}, Message - {commitMessage}");
            }
        }

        return commitCountInMonth;
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
    private void RetrieveAllProjectReferences(string projectPath, List<string> projects)
    {
        var projectFile = "";

        DirectoryInfo folder = new DirectoryInfo(projectPath);
        if (!folder.Exists)
            return;

        FileInfo[] files = folder.GetFiles("*.cdsproj");
        if (files.Length == 0)
            files = folder.GetFiles("*.csproj");
        if (files.Length == 0)
            files = folder.GetFiles("*.pcfproj");
        foreach (FileInfo file in files)
        {
            projectFile = file.FullName;
        }

        if (string.IsNullOrWhiteSpace(projectFile) || !File.Exists(projectFile))
            return;

        var projectDir = Path.GetDirectoryName(projectFile);
        var doc = XDocument.Load(projectFile);

        Log.LogMessage(MessageImportance.High, $"{projectFile} - {projectDir} - {doc.Descendants()}");

        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        var descendants = doc.Descendants(ns + "ProjectReference");
        if( descendants == null || !descendants.Any())
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
}