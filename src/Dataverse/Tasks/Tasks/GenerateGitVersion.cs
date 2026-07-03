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
    [Required]
    public string Version { get; set; }
    public string GitVersionNumberBranchPrefixes { get; set; } // e.g. "develop:1;feature/*:3;hotfix/*:4"
    public string GitVersionNumberProductionBranches { get; set; } // e.g. "main;master;hotfix/*;release/*"
    public string LocalBuildVersionNumber { get; set; }
    public string IsRunningInCI { get; set; }
    public string GitVersionBranch { get; set; }

    [Output]
    public string VersionOutput { get; private set; }
    [Output]
    public string SemVerOutput { get; private set; }
    [Output]
    public string LastCommitDateTimeOutput { get; private set; }

    private IEnumerable<BranchVersioning> _branches;

    public override bool Execute()
    {
        Log.LogMessage(MessageImportance.High, "Preparing to generate version number...");

        // Ensure repository is connected to Git before running commands
        if (!TryFindGitRoot(ProjectPath, out var gitRoot))
        {
            Log.LogMessage(MessageImportance.High, "Git repository not found; skipping automatic Git versioning.");
            VersionOutput = LocalBuildVersionNumber;
            SemVerOutput = ToSemVer(LocalBuildVersionNumber);
            LastCommitDateTimeOutput = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            return true;
        }

        if (!DetectIsRunningInCI())
        {
            Log.LogMessage(MessageImportance.High, "Not running in CI; using LocalBuildVersionNumber.");
            VersionOutput = LocalBuildVersionNumber;
            SemVerOutput = ToSemVer(LocalBuildVersionNumber);
            LastCommitDateTimeOutput = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            return true;
        }

        // Prepare for running git commands
        var gitInfo = CreateGitProcessInfo(gitRoot);

        try
        {
            var currentBranch = !string.IsNullOrWhiteSpace(GitVersionBranch)
                ? GitVersionBranch
                : GetCurrentBranch(gitInfo);
            if (!string.IsNullOrWhiteSpace(GitVersionBranch))
            {
                Log.LogMessage(MessageImportance.High, $"Branch overridden via GitVersionBranch property: {GitVersionBranch}");
            }
            if (string.IsNullOrWhiteSpace(GitVersionNumberBranchPrefixes) && string.IsNullOrWhiteSpace(GitVersionNumberProductionBranches))
            {
                Log.LogWarning("Neither GitVersionNumberBranchPrefixes nor GitVersionNumberProductionBranches is set; skipping automatic Git versioning.");
                VersionOutput = Version;
                SemVerOutput = ToSemVer(Version);
                LastCommitDateTimeOutput = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                return true;
            }
            _branches = string.IsNullOrWhiteSpace(GitVersionNumberBranchPrefixes)
                ? Enumerable.Empty<BranchVersioning>()
                : GitVersionNumberBranchPrefixes.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).Select(BranchVersioning.Parse);

            var productionBranches = string.IsNullOrWhiteSpace(GitVersionNumberProductionBranches)
                ? Array.Empty<string>()
                : GitVersionNumberProductionBranches.Split(';').Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            var isProduction = productionBranches.Any(p =>
                string.Equals(p, currentBranch, StringComparison.OrdinalIgnoreCase) ||
                (p.EndsWith("*") && currentBranch.StartsWith(p.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)));

            var branch = _branches.FirstOrDefault(b =>
                string.Equals(b.BranchName, currentBranch, StringComparison.OrdinalIgnoreCase) ||
                (b.BranchName.EndsWith("*") && currentBranch.StartsWith(b.BranchName.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)));

            if (!isProduction && branch == null)
            {
                Log.LogWarning($"The current branch '{currentBranch}' is not in GitVersionNumberProductionBranches or GitVersionNumberBranchPrefixes.");
                VersionOutput = LocalBuildVersionNumber;
                SemVerOutput = ToSemVer(LocalBuildVersionNumber);
                LastCommitDateTimeOutput = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                return true;
            }

            Log.LogMessage($"The current branch '{currentBranch}' is enabled for automatic Git versioning (production={isProduction}).");

            var projects = new List<string> { ProjectPath };
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
            Log.LogMessage(MessageImportance.High, $"Commit count for the day: {totalComitCount}");
            if (totalComitCount > 999)
            {
                throw new Exception($"Too many commits ({totalComitCount} > 999), cannot generate version number. Please reach out to the author.");
            }

            // Build = [prefix]YYMM; prefix is optional. Prefix 2 is reserved for local builds.
            var build = ushort.Parse(latestCommitDate.ToString("yyMM"));
            if (branch?.Prefix.HasValue == true)
            {
                if (branch.Prefix.Value == 2)
                    Log.LogWarning($"Branch prefix 2 is reserved for local builds; using it on branch '{currentBranch}' may cause version conflicts.");
                build = ushort.Parse($"{branch.Prefix.Value}{latestCommitDate:yyMM}");
            }

            // Revision = DDddd (day + 3-digit same-day commit count)
            var revision = ushort.Parse($"{latestCommitDate:dd}{totalComitCount:000}");

            // Major.Minor: production branches use the csproj version; non-production uses 0.0
            var major = isProduction ? VersionMajor : (ushort)0;
            var minor = isProduction ? VersionMinor : (ushort)0;

            VersionOutput = $"{major}.{minor}.{build}.{revision}";
            SemVerOutput = $"{major}.{minor}.{build}+{revision}";
            LastCommitDateTimeOutput = latestCommitDate.ToString("yyyy-MM-ddTHH:mm:ssZ");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogMessage(MessageImportance.High, $"Git versioning skipped: {ex.Message}");
            VersionOutput = LocalBuildVersionNumber;
            SemVerOutput = ToSemVer(LocalBuildVersionNumber);
            LastCommitDateTimeOutput = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            return true;
        }
    }

    private static string ToSemVer(string dotNetVersion)
    {
        if (string.IsNullOrWhiteSpace(dotNetVersion))
            return dotNetVersion;
        var parts = dotNetVersion.Split('.');
        var major    = parts.Length > 0 ? parts[0] : "0";
        var minor    = parts.Length > 1 ? parts[1] : "0";
        var patch    = parts.Length > 2 ? parts[2] : "0";
        var revision = parts.Length > 3 ? parts[3] : "0";
        return $"{major}.{minor}.{patch}+{revision}";
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
        string branchCommand = "rev-parse --abbrev-ref HEAD";
        string output = ExecuteGitCommand(gitInfo, branchCommand).Trim();
        string branch = output.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];

        if (!string.IsNullOrEmpty(branch) && branch != "HEAD")
        {
            Log.LogMessage(MessageImportance.High, $"Branch from git: {branch}");

            return branch;
        }

        Log.LogMessage(MessageImportance.High, "Detached HEAD detected, trying CI environment variables...");

        // Azure DevOps
        string fullRef = Environment.GetEnvironmentVariable("BUILD_SOURCEBRANCH");

        if (!string.IsNullOrEmpty(fullRef))
        {
            const string prefix = "refs/heads/";
            string envBranch = fullRef.StartsWith(prefix) ? fullRef.Substring(prefix.Length) : fullRef;
            
            Log.LogMessage(MessageImportance.High, $"Branch from BUILD_SOURCEBRANCH: {envBranch}");

            return envBranch;
        }

        // GitHub Actions
        string ghBranch = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");

        if (!string.IsNullOrEmpty(ghBranch))
        {
            Log.LogMessage(MessageImportance.High, $"Branch from GITHUB_REF_NAME: {ghBranch}");

            return ghBranch;
        }

        // GitLab CI
        string glBranch = Environment.GetEnvironmentVariable("CI_COMMIT_REF_NAME");

        if (!string.IsNullOrEmpty(glBranch))
        {
            Log.LogMessage(MessageImportance.High, $"Branch from CI_COMMIT_REF_NAME: {glBranch}");

            return glBranch;
        }

        Log.LogWarning("Could not determine branch name from git or CI environment variables.");

        return branch;
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
    private bool TryFindGitRoot(string path, out string gitRoot)
    {
        var directory = new DirectoryInfo(path);
        while (directory != null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                gitRoot = directory.FullName;
                return true;
            }
            directory = directory.Parent;
        }
        gitRoot = null;
        return false;
    }
    private bool DetectIsRunningInCI()
    {
        if (!string.IsNullOrEmpty(IsRunningInCI))
        {
            if (bool.TryParse(IsRunningInCI, out var overrideValue))
            {
                Log.LogMessage(MessageImportance.High, $"IsRunningInCI overridden to: {overrideValue}");
                return overrideValue;
            }
            Log.LogWarning($"IsRunningInCI value '{IsRunningInCI}' is not a valid boolean; falling back to auto-detection.");
        }

        // Boolean-style vars: only treat explicit "true" as CI
        var booleanCiVars = new[]
        {
            "CI",             // Generic (GitHub Actions, GitLab, Travis, CircleCI, etc.)
            "TF_BUILD",       // Azure DevOps
            "GITHUB_ACTIONS", // GitHub Actions
            "GITLAB_CI",      // GitLab CI
            "CIRCLECI",       // CircleCI
        };

        foreach (var varName in booleanCiVars)
        {
            var value = Environment.GetEnvironmentVariable(varName);
            if (bool.TryParse(value, out var boolValue) && boolValue)
            {
                Log.LogMessage(MessageImportance.High, $"CI environment detected via {varName}");
                return true;
            }
        }

        // Non-boolean vars: any non-empty value indicates CI
        var presenceCiVars = new[]
        {
            "JENKINS_URL",     // Jenkins
            "TEAMCITY_VERSION" // TeamCity
        };

        foreach (var varName in presenceCiVars)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(varName)))
            {
                Log.LogMessage(MessageImportance.High, $"CI environment detected via {varName}");
                return true;
            }
        }

        Log.LogMessage(MessageImportance.High, "No CI environment detected; treating as local build.");
        return false;
    }
    private void RetrieveAllProjectReferences(string projectPath, List<string> projects)
    {
        var projectFile = ProjectReferenceHelper.FindProjectFile(projectPath);
        if (projectFile == null)
            return;

        var projectDir = Path.GetDirectoryName(projectFile);
        var doc = XDocument.Load(projectFile);

        foreach (var includeValue in ProjectReferenceHelper.GetProjectReferenceIncludes(doc))
        {
            var referencedProjectPath = ProjectReferenceHelper.ResolveReferencedProjectDirectory(projectDir, includeValue);
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
            int? prefix = null;
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