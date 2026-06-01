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

            var files = Directory.GetFiles(root, "*.tsx", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var duplicates = files
                .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();

            if (duplicates.Length > 0)
            {
                Log.LogError($"Duplicate GenPage page name(s): {string.Join(", ", duplicates)}");
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
                var siblingPrompt = Path.Combine(root, pageName + ".firstPrompt.json");
                var sharedPrompt = Path.Combine(root, "firstPrompt.json");

                var item = new TaskItem(file);
                item.SetMetadata("PageName", pageName);
                item.SetMetadata("EntryFile", file);
                item.SetMetadata("ConfigJsonPath", File.Exists(siblingConfig) ? siblingConfig : (File.Exists(sharedConfig) ? sharedConfig : ""));
                item.SetMetadata("FirstPromptJsonPath", File.Exists(siblingPrompt) ? siblingPrompt : (File.Exists(sharedPrompt) ? sharedPrompt : ""));
                items.Add(item);
            }

            if (items.Count == 0)
                Log.LogWarning($"No GenPage root *.tsx files found in {root}.");

            GenPages = items.ToArray();
            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }
}
