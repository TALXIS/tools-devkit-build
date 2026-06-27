using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class PostProcessImportConfig : Task
{
    [Required]
    public string ImportConfigPath { get; set; } = "";

    public ITaskItem[] Solutions { get; set; }

    public string CmtDataFileName { get; set; } = "";

    public string CsprojPath { get; set; } = "";

    public bool CreateSkeletonIfMissing { get; set; }

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
                if (CreateSkeletonIfMissing)
                {
                    WriteSkeleton(configPath);
                    UpdatedImportConfig = configPath;
                    return !Log.HasLoggedErrors;
                }

                Log.LogError($"ImportConfig file not found: {configPath}");
                return false;
            }

            if (CreateSkeletonIfMissing)
            {
                UpdatedImportConfig = configPath;
                return true;
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
            ReorderSolutionFiles(root);
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

    private void WriteSkeleton(string configPath)
    {
        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var doc = new XmlDocument();
        var decl = doc.CreateXmlDeclaration("1.0", "utf-8", null);
        doc.AppendChild(decl);

        var root = doc.CreateElement("configdatastorage");
        root.SetAttribute("installsampledata", "false");
        root.SetAttribute("waitforsampledatatoinstall", "true");
        doc.AppendChild(root);

        root.AppendChild(doc.CreateElement("solutions"));
        root.AppendChild(doc.CreateElement("filestoimport"));
        root.AppendChild(doc.CreateElement("filesmapstoimport"));

        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new System.Text.UTF8Encoding(false),
        };
        using (var writer = XmlWriter.Create(configPath, settings))
        {
            doc.Save(writer);
        }

        Log.LogMessage(MessageImportance.High,
            $"Generated skeleton ImportConfig.xml at: {configPath}");
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

    private void ReorderSolutionFiles(XmlElement root)
    {
        var csproj = (CsprojPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(csproj) || !File.Exists(csproj))
        {
            Log.LogMessage(MessageImportance.Low,
                "CsprojPath not provided or missing — skipping configsolutionfile reorder.");
            return;
        }

        var orderedNames = ReadReferenceOrderFromCsproj(csproj);
        if (orderedNames.Count == 0)
            return;

        var container = root.SelectSingleNode("solutions") as XmlElement;
        if (container == null)
            return;

        var bundles = new List<Bundle>();
        XmlNode pendingWhitespace = null;
        XmlNode trailingWhitespace = null;

        foreach (XmlNode child in container.ChildNodes)
        {
            if (child is XmlElement el &&
                string.Equals(el.LocalName, "configsolutionfile", StringComparison.OrdinalIgnoreCase))
            {
                bundles.Add(new Bundle(pendingWhitespace, el));
                pendingWhitespace = null;
            }
            else if (IsWhitespaceNode(child))
            {
                pendingWhitespace = child;
            }
        }
        trailingWhitespace = pendingWhitespace;

        if (bundles.Count == 0)
            return;

        var ordered = bundles
            .Select((b, idx) => new
            {
                Bundle = b,
                Rank = RankConfigSolutionFile(b.Element, orderedNames),
                OriginalIndex = idx
            })
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.OriginalIndex)
            .Select(x => x.Bundle)
            .ToList();

        bool alreadyOrdered = true;
        for (int i = 0; i < bundles.Count; i++)
        {
            if (!ReferenceEquals(bundles[i].Element, ordered[i].Element))
            {
                alreadyOrdered = false;
                break;
            }
        }
        if (alreadyOrdered)
            return;

        foreach (var bundle in bundles)
        {
            if (bundle.Whitespace != null && bundle.Whitespace.ParentNode == container)
                container.RemoveChild(bundle.Whitespace);
            if (bundle.Element.ParentNode == container)
                container.RemoveChild(bundle.Element);
        }

        foreach (var bundle in ordered)
        {
            if (bundle.Whitespace != null)
                container.InsertBefore(bundle.Whitespace, trailingWhitespace);
            container.InsertBefore(bundle.Element, trailingWhitespace);
        }

        Log.LogMessage(MessageImportance.High,
            "Reordered configsolutionfile elements to match the order of " +
            "PackageReference / ProjectReference items in the csproj.");
    }

    private static bool IsWhitespaceNode(XmlNode node)
    {
        if (node == null) return false;
        return node.NodeType == XmlNodeType.Whitespace
            || node.NodeType == XmlNodeType.SignificantWhitespace
            || (node.NodeType == XmlNodeType.Text && string.IsNullOrWhiteSpace(node.Value));
    }

    private sealed class Bundle
    {
        public XmlNode Whitespace { get; }
        public XmlElement Element { get; }
        public Bundle(XmlNode whitespace, XmlElement element)
        {
            Whitespace = whitespace;
            Element = element;
        }
    }

    private static int RankConfigSolutionFile(XmlElement node, List<string> orderedNames)
    {
        var unique = ExtractUniqueName(node);
        if (string.IsNullOrWhiteSpace(unique))
            return int.MaxValue;

        for (int i = 0; i < orderedNames.Count; i++)
        {
            if (string.Equals(orderedNames[i], unique, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return int.MaxValue;
    }

    private static string ExtractUniqueName(XmlElement configSolutionFile)
    {
        var unique = configSolutionFile.GetAttribute("solutionpackageuniquename");
        if (!string.IsNullOrWhiteSpace(unique))
            return unique;

        var filename = configSolutionFile.GetAttribute("solutionpackagefilename");
        if (!string.IsNullOrWhiteSpace(filename))
            return Path.GetFileNameWithoutExtension(filename);

        return "";
    }

    private static List<string> ReadReferenceOrderFromCsproj(string csprojPath)
    {
        var result = new List<string>();
        var doc = new XmlDocument();
        doc.Load(csprojPath);

        var itemGroups = doc.GetElementsByTagName("ItemGroup");
        foreach (XmlNode ig in itemGroups)
        {
            foreach (XmlNode child in ig.ChildNodes)
            {
                if (!(child is XmlElement el))
                    continue;

                var include = el.GetAttribute("Include");
                if (string.IsNullOrWhiteSpace(include))
                    continue;

                if (string.Equals(el.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(include);
                }
                else if (string.Equals(el.LocalName, "ProjectReference", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(Path.GetFileNameWithoutExtension(include));
                }
            }
        }

        return result;
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
