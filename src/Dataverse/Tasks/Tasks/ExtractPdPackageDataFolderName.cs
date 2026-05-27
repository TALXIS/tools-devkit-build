using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class ExtractPdPackageDataFolderName : Task
{
    [Required]
    public string ProjectDirectory { get; set; } = "";

    public string DefaultFolderName { get; set; } = "PkgAssets";

    [Output]
    public string FolderName { get; private set; } = "";

    private static readonly Regex PropertyRegex = new Regex(
        @"GetImportPackageDataFolderName\s*(?:=>\s*""([^""]+)""|\{[^}]*?return\s*""([^""]+)"")",
        RegexOptions.Singleline | RegexOptions.Compiled);

    public override bool Execute()
    {
        FolderName = string.IsNullOrWhiteSpace(DefaultFolderName) ? "PkgAssets" : DefaultFolderName;

        try
        {
            var dir = (ProjectDirectory ?? "").Trim();
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                Log.LogMessage(MessageImportance.Low,
                    $"ProjectDirectory is empty or missing — using default folder name '{FolderName}'.");
                return true;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly))
            {
                string content;
                try { content = File.ReadAllText(file); }
                catch { continue; }

                var match = PropertyRegex.Match(content);
                if (!match.Success) continue;

                var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (string.IsNullOrWhiteSpace(value)) continue;

                FolderName = value;
                Log.LogMessage(MessageImportance.Normal,
                    $"Detected GetImportPackageDataFolderName='{FolderName}' from '{file}'.");
                return true;
            }

            Log.LogMessage(MessageImportance.Low,
                $"GetImportPackageDataFolderName not found in any .cs file under '{dir}'. Using default '{FolderName}'.");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogWarningFromException(ex);
            return true;
        }
    }
}
