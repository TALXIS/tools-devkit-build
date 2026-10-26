using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

/// <summary>
/// Shared helper for resolving ProjectReference paths from .csproj/.cdsproj/.pcfproj files.
/// Used by GenerateGitVersion and RetrieveProjectReferences to avoid code duplication.
/// </summary>
internal static class ProjectReferenceHelper
{
    /// <summary>
    /// Normalizes a ProjectReference Include value for cross-platform path resolution.
    /// Converts Windows backslashes to the OS directory separator.
    /// </summary>
    public static string NormalizeIncludePath(string includeValue)
    {
        return includeValue.Replace('\\', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Resolves the directory of a referenced project from a ProjectReference Include value.
    /// Handles cross-platform separators, null from Path.GetDirectoryName, and .. segments.
    /// </summary>
    public static string ResolveReferencedProjectDirectory(string projectDir, string includeValue)
    {
        var normalized = NormalizeIncludePath(includeValue);
        var refDir = Path.GetDirectoryName(normalized);
        return Path.GetFullPath(
            string.IsNullOrEmpty(refDir) ? projectDir : Path.Combine(projectDir, refDir));
    }

    /// <summary>
    /// Finds a project file (*.cdsproj, *.csproj, *.pcfproj) in the given directory.
    /// Returns null if no project file is found.
    /// </summary>
    public static string FindProjectFile(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        var dir = new DirectoryInfo(directory);
        var files = dir.GetFiles("*.cdsproj");
        if (files.Length == 0) files = dir.GetFiles("*.csproj");
        if (files.Length == 0) files = dir.GetFiles("*.pcfproj");

        if (files.Length == 0) return null;
        if (files.Length == 1) return files[0].FullName;
        return files.OrderBy(f => f.Name).First().FullName;
    }

    /// <summary>
    /// Extracts ProjectReference Include values from a project file XML document.
    /// Handles both namespaced (old-style) and non-namespaced (SDK-style) project formats.
    /// </summary>
    public static IEnumerable<string> GetProjectReferenceIncludes(XDocument doc)
    {
        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        var descendants = doc.Descendants(ns + "ProjectReference");
        if (descendants == null || !descendants.Any())
        {
            ns = "";
            descendants = doc.Descendants(ns + "ProjectReference");
        }

        return descendants
            .Select(r => r.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrEmpty(v));
    }
}
