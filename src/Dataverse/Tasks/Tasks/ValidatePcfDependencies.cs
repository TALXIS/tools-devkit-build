using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class ValidatePcfDependencies : Task
{
    [Required]
    public ITaskItem[] SolutionZipFiles { get; set; }

    public string IgnoredPcfPrefixes { get; set; }

    public override bool Execute()
    {
        try
        {
            if (SolutionZipFiles == null || SolutionZipFiles.Length == 0)
                return true;

            var ignoredPrefixes = ParsePrefixes(IgnoredPcfPrefixes);
            var providedControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedControls = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var zipItem in SolutionZipFiles)
            {
                var zipPath = zipItem.ItemSpec;
                if (!File.Exists(zipPath))
                    continue;

                try
                {
                    ScanSolutionZip(zipPath, providedControls, usedControls);
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"ValidatePcfDependencies: could not read {Path.GetFileName(zipPath)}: {ex.Message}");
                }
            }

            int missingCount = 0;
            foreach (var entry in usedControls)
            {
                var controlName = entry.Key;

                if (providedControls.Contains(controlName))
                    continue;

                if (IsIgnored(controlName, ignoredPrefixes))
                    continue;

                missingCount++;
                var solutions = string.Join(", ", entry.Value.Distinct());
                Log.LogWarning(
                    subcategory: "pcf",
                    warningCode: "TALXISPCF001",
                    helpKeyword: null,
                    file: null,
                    lineNumber: 0,
                    columnNumber: 0,
                    endLineNumber: 0,
                    endColumnNumber: 0,
                    message: $"PCF control \"{controlName}\" is used in forms (solutions: {solutions}) " +
                             $"but no solution in the PDPackage provides it (RootComponent type=\"66\"). " +
                             $"Add the solution containing this control, or add its prefix to TalxisIgnoredPcfPrefixes.");
            }

            if (missingCount > 0)
            {
                Log.LogWarning($"ValidatePcfDependencies: {missingCount} PCF control(s) used in forms but not provided by any solution in the package.");
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }

    private void ScanSolutionZip(string zipPath, HashSet<string> providedControls, Dictionary<string, List<string>> usedControls)
    {
        var solutionName = Path.GetFileNameWithoutExtension(zipPath);

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');

                if (name.Equals("solution.xml", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Other/Solution.xml", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = entry.Open())
                    {
                        var doc = XDocument.Load(stream);
                        ExtractProvidedControls(doc, providedControls);
                    }
                }
                else if (name.Equals("customizations.xml", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("Other/customizations.xml", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = entry.Open())
                    {
                        var doc = XDocument.Load(stream);
                        ExtractUsedControls(doc, solutionName, usedControls);
                    }
                }
            }
        }
    }

    private static void ExtractProvidedControls(XDocument solutionXml, HashSet<string> provided)
    {
        foreach (var rc in solutionXml.Descendants("RootComponent"))
        {
            var type = rc.Attribute("type")?.Value;
            var schemaName = rc.Attribute("schemaName")?.Value;

            if (type == "66" && !string.IsNullOrEmpty(schemaName))
                provided.Add(schemaName);
        }
    }

    private static void ExtractUsedControls(XDocument formXml, string solutionName, Dictionary<string, List<string>> used)
    {
        foreach (var cc in formXml.Descendants("customControl"))
        {
            var name = cc.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name))
                continue;

            if (!used.TryGetValue(name, out var solutions))
            {
                solutions = new List<string>();
                used[name] = solutions;
            }
            if (!solutions.Contains(solutionName))
                solutions.Add(solutionName);
        }
    }



    private static bool IsIgnored(string controlName, HashSet<string> ignoredPrefixes)
    {
        if (ignoredPrefixes.Count == 0)
            return false;

        var underscoreIdx = controlName.IndexOf('_');
        if (underscoreIdx <= 0)
            return false;

        var prefix = controlName.Substring(0, underscoreIdx);
        return ignoredPrefixes.Contains(prefix);
    }

    private static HashSet<string> ParsePrefixes(string raw)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return result;

        foreach (var part in raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                result.Add(trimmed);
        }
        return result;
    }
}
