using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Resolves a Node project against Rush's project and subspace configuration.
/// </summary>
public sealed class ResolveRushProject : Task
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    [Required]
    public string WorkspaceRoot { get; set; } = string.Empty;

    [Required]
    public string ProjectRoot { get; set; } = string.Empty;

    [Output]
    public bool IsRegistered { get; private set; }

    [Output]
    public bool SubspacesEnabled { get; private set; }

    [Output]
    public string SubspaceName { get; private set; } = string.Empty;

    [Output]
    public string SubspaceConfigurationRoot { get; private set; } = string.Empty;

    [Output]
    public string SubspaceTempRoot { get; private set; } = string.Empty;

    [Output]
    public ITaskItem[] InstallPackageJsonPaths { get; private set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            var workspaceRoot = NormalizeDirectory(WorkspaceRoot);
            var projectRoot = NormalizeDirectory(ProjectRoot);
            var rushJsonPath = Path.Combine(workspaceRoot, "rush.json");
            if (!File.Exists(rushJsonPath))
            {
                Log.LogError($"Rush configuration was not found at '{rushJsonPath}'.");
                return false;
            }

            using var rushJson = JsonDocument.Parse(File.ReadAllText(rushJsonPath), JsonOptions);
            if (rushJson.RootElement.ValueKind != JsonValueKind.Object)
            {
                Log.LogError($"Rush configuration '{rushJsonPath}' must contain a JSON object.");
                return false;
            }

            if (!rushJson.RootElement.TryGetProperty("projects", out var projectsElement) ||
                projectsElement.ValueKind != JsonValueKind.Array)
            {
                Log.LogError($"Rush configuration '{rushJsonPath}' does not contain a valid 'projects' array.");
                return false;
            }

            var projects = ReadProjects(projectsElement, workspaceRoot, rushJsonPath);
            if (Log.HasLoggedErrors)
            {
                return false;
            }

            var matchingProjects = projects.Where(project => PathsEqual(project.FullPath, projectRoot)).ToArray();
            if (matchingProjects.Length > 1)
            {
                Log.LogError($"Rush configuration '{rushJsonPath}' registers project folder '{projectRoot}' more than once.");
                return false;
            }

            var currentProject = matchingProjects.SingleOrDefault();
            IsRegistered = currentProject != null;

            var subspacesJsonPath = Path.Combine(workspaceRoot, "common", "config", "rush", "subspaces.json");
            var subspaceNames = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(subspacesJsonPath))
            {
                using var subspacesJson = JsonDocument.Parse(File.ReadAllText(subspacesJsonPath), JsonOptions);
                if (subspacesJson.RootElement.ValueKind != JsonValueKind.Object)
                {
                    Log.LogError($"Rush subspace configuration '{subspacesJsonPath}' must contain a JSON object.");
                    return false;
                }

                SubspacesEnabled = ReadOptionalBoolean(subspacesJson.RootElement, "subspacesEnabled");
                if (SubspacesEnabled)
                {
                    if (ReadOptionalBoolean(subspacesJson.RootElement, "splitWorkspaceCompatibility"))
                    {
                        Log.LogError(
                            $"Rush subspace configuration '{subspacesJsonPath}' enables deprecated splitWorkspaceCompatibility. " +
                            "Migrate the repository to standard common/config/subspaces/<name> configuration.");
                        return false;
                    }

                    subspaceNames.Add("default");
                    if (!subspacesJson.RootElement.TryGetProperty("subspaceNames", out var namesElement) ||
                        namesElement.ValueKind != JsonValueKind.Array)
                    {
                        Log.LogError($"Rush subspace configuration '{subspacesJsonPath}' does not contain a valid 'subspaceNames' array.");
                        return false;
                    }

                    foreach (var nameElement in namesElement.EnumerateArray())
                    {
                        if (nameElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nameElement.GetString()))
                        {
                            Log.LogError($"Rush subspace configuration '{subspacesJsonPath}' contains an invalid subspace name.");
                            return false;
                        }
                        subspaceNames.Add(nameElement.GetString()!);
                    }
                }
            }

            if (SubspacesEnabled)
            {
                foreach (var project in projects)
                {
                    var projectSubspace = string.IsNullOrWhiteSpace(project.SubspaceName) ? "default" : project.SubspaceName;
                    if (!subspaceNames.Contains(projectSubspace))
                    {
                        Log.LogError(
                            $"Rush project '{project.RelativePath}' references unknown subspace '{projectSubspace}'. " +
                            $"Register it in '{subspacesJsonPath}'.");
                    }
                }
                if (Log.HasLoggedErrors)
                {
                    return false;
                }
            }

            if (!IsRegistered)
            {
                return true;
            }

            SubspaceName = SubspacesEnabled
                ? string.IsNullOrWhiteSpace(currentProject!.SubspaceName) ? "default" : currentProject.SubspaceName
                : string.Empty;

            SubspaceConfigurationRoot = SubspacesEnabled
                ? Path.Combine(workspaceRoot, "common", "config", "subspaces", SubspaceName)
                : Path.Combine(workspaceRoot, "common", "config", "rush");

            SubspaceTempRoot = SubspacesEnabled
                ? Path.Combine(workspaceRoot, "common", "temp", SubspaceName)
                : Path.Combine(workspaceRoot, "common", "temp");

            InstallPackageJsonPaths = projects
                .Select(project => (ITaskItem)new TaskItem(Path.Combine(project.FullPath, "package.json")))
                .ToArray();

            return true;
        }
        catch (JsonException ex)
        {
            Log.LogError($"Invalid Rush JSON configuration: {ex.Message}");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.LogError(ex.Message);
            return false;
        }
    }

    private List<RushProject> ReadProjects(JsonElement projectsElement, string workspaceRoot, string rushJsonPath)
    {
        var projects = new List<RushProject>();
        foreach (var projectElement in projectsElement.EnumerateArray())
        {
            if (projectElement.ValueKind != JsonValueKind.Object ||
                !projectElement.TryGetProperty("projectFolder", out var folderElement) ||
                folderElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(folderElement.GetString()))
            {
                Log.LogError($"Rush configuration '{rushJsonPath}' contains a project without a valid 'projectFolder'.");
                continue;
            }

            var relativePath = folderElement.GetString()!;
            var subspaceName = projectElement.TryGetProperty("subspaceName", out var subspaceElement) &&
                               subspaceElement.ValueKind == JsonValueKind.String
                ? subspaceElement.GetString() ?? string.Empty
                : string.Empty;

            projects.Add(new RushProject(relativePath, NormalizeDirectory(Path.Combine(workspaceRoot, relativePath)), subspaceName));
        }
        return projects;
    }

    private static bool ReadOptionalBoolean(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static string NormalizeDirectory(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private sealed record RushProject(string RelativePath, string FullPath, string SubspaceName);
}
