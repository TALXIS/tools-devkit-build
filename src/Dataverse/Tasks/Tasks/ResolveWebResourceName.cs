using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;

public class ResolveWebResourceName : Task
{
    [Required]
    public ITaskItem[] Files { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string PublisherPrefix { get; set; } = "";

    [Output]
    public ITaskItem[] ResolvedFiles { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            string prefix = PublisherPrefix.Trim().ToLowerInvariant();
            var results = new List<ITaskItem>();

            foreach (var file in Files)
            {
                string filePath = file.ItemSpec;
                string fileName = System.IO.Path.GetFileName(filePath);

                string resolvedName;
                string displayName;

                int underscoreIndex = fileName.IndexOf('_');

                if (underscoreIndex > 0)
                {
                    string existingPrefix = fileName.Substring(0, underscoreIndex).ToLowerInvariant();

                    if (existingPrefix.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedName = fileName;
                        displayName = fileName.Substring(underscoreIndex + 1);
                    }
                    else
                    {
                        resolvedName = prefix + "_" + fileName;
                        displayName = fileName;
                    }
                }
                else
                {
                    resolvedName = prefix + "_" + fileName;
                    displayName = fileName;
                }

                var resultItem = new TaskItem(filePath);
                resultItem.SetMetadata("ResolvedName", resolvedName);
                resultItem.SetMetadata("DisplayName", displayName);
                results.Add(resultItem);

                Log.LogMessage(MessageImportance.Normal,
                    $"ResolveWebResourceName: {fileName} -> {resolvedName} (DisplayName: {displayName})");
            }

            ResolvedFiles = results.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }
}
