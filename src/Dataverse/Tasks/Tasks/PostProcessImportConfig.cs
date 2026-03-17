using System;
using System.IO;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class PostProcessImportConfig : Task
{
    [Required]
    public string ImportConfigPath { get; set; } = "";

    public ITaskItem[] Solutions { get; set; }

    public string CmtDataFileName { get; set; } = "";

    [Output]
    public string UpdatedImportConfig { get; private set; } = "";

    public override bool Execute()
    {
        try
        {
            var configPath = ImportConfigPath?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(configPath))
            {
                Log.LogError("ImportConfigPath is empty.");
                return false;
            }

            configPath = Path.GetFullPath(configPath);
            if (!File.Exists(configPath))
            {
                Log.LogError($"ImportConfig file not found: {configPath}");
                return false;
            }

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(configPath);

            var root = doc.DocumentElement;
            if (root == null)
            {
                Log.LogError("ImportConfig has no root element.");
                return false;
            }

            AnnotateSolutionFiles(doc, root);
            SetCmtDataImportFile(root);

            doc.Save(configPath);
            UpdatedImportConfig = configPath;
            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private void AnnotateSolutionFiles(XmlDocument doc, XmlElement root)
    {
        var solutionNodes = root.SelectNodes("//configsolutionfile");
        if (solutionNodes == null || solutionNodes.Count == 0)
        {
            Log.LogMessage(MessageImportance.Low, "No configsolutionfile elements found in ImportConfig.");
            return;
        }

        foreach (XmlElement node in solutionNodes)
        {
            var existingFilename = node.GetAttribute("solutionpackagefilename");
            if (string.IsNullOrWhiteSpace(existingFilename))
            {
                var solutionName = node.GetAttribute("solutionpackageuniquename");
                if (!string.IsNullOrWhiteSpace(solutionName))
                {
                    var zipFilename = LookupSolutionZipFilename(solutionName);
                    if (!string.IsNullOrWhiteSpace(zipFilename))
                    {
                        node.SetAttribute("solutionpackagefilename", zipFilename);
                        Log.LogMessage(MessageImportance.Normal,
                            $"Set solutionpackagefilename='{zipFilename}' for solution '{solutionName}'.");
                    }
                }
            }

            node.SetAttribute("requiredimportmode", "async");
            Log.LogMessage(MessageImportance.Low, "Set requiredimportmode='async' on configsolutionfile.");
        }
    }

    private string LookupSolutionZipFilename(string uniqueName)
    {
        if (Solutions == null)
            return "";

        foreach (var solution in Solutions)
        {
            var filename = Path.GetFileNameWithoutExtension(solution.ItemSpec);
            if (string.Equals(filename, uniqueName, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(solution.ItemSpec);
            }

            var metadataName = solution.GetMetadata("SolutionUniqueName");
            if (!string.IsNullOrWhiteSpace(metadataName) &&
                string.Equals(metadataName, uniqueName, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileName(solution.ItemSpec);
            }
        }

        return uniqueName + ".zip";
    }

    private void SetCmtDataImportFile(XmlElement root)
    {
        var cmtFileName = (CmtDataFileName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(cmtFileName))
            return;

        root.SetAttribute("crmmigdataimportfile", cmtFileName);
        Log.LogMessage(MessageImportance.Normal,
            $"Set crmmigdataimportfile='{cmtFileName}' on configdatastorage.");
    }
}
