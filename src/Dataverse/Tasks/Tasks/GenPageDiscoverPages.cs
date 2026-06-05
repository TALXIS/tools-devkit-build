using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class GenPageDiscoverPages : Task
{
    [Required]
    public string ProjectDirectory { get; set; } = "";

    [Output]
    public ITaskItem[] GenPages { get; private set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            var root = Path.GetFullPath(ProjectDirectory);
            if (!Directory.Exists(root))
                throw new DirectoryNotFoundException($"GenPage project directory not found: {root}");

            var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "node_modules", "dist", "build", "bin", "obj", ".git"
            };

            var files = Directory.EnumerateFiles(root, "*.tsx", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var relative = MakeRelative(root, f);
                    var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return !segments.Take(segments.Length - 1).Any(s => excludedDirs.Contains(s));
                })
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var duplicates = files
                .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToArray();

            if (duplicates.Length > 0)
            {
                foreach (var dup in duplicates)
                    Log.LogError($"Duplicate GenPage page name '{dup.Key}': {string.Join(", ", dup.Select(f => MakeRelative(root, f)))}");
                return false;
            }

            var items = new List<ITaskItem>();
            foreach (var file in files)
            {
                var pageName = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(pageName))
                {
                    Log.LogError($"Invalid GenPage page file name: {file}");
                    continue;
                }

                var siblingConfig = Path.Combine(root, pageName + ".config.json");
                var sharedConfig = Path.Combine(root, "genpage.config.json");

                var item = new TaskItem(file);
                item.SetMetadata("PageName", pageName);
                item.SetMetadata("EntryFile", file);
                item.SetMetadata("ConfigJsonPath", File.Exists(siblingConfig) ? siblingConfig : (File.Exists(sharedConfig) ? sharedConfig : ""));
                items.Add(item);
            }

            if (items.Count == 0)
                Log.LogWarning($"No *.tsx files found in {root} (excluding node_modules/dist/build/bin/obj).");

            GenPages = items.ToArray();
            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private static string MakeRelative(string root, string path)
    {
        return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
