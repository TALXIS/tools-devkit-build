using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

#nullable enable

public sealed class PatchSolutionXml : Task
{
    [Required] public string ProjectDir { get; set; } = "";

    public string? Version { get; set; }
    public string? Managed { get; set; }
    public string? PublisherName { get; set; }
    public string? PublisherPrefix { get; set; }

    public bool FailOnManyMatches { get; set; } = true;
    public int MaxMatches { get; set; } = 5;

    public override bool Execute()
    {
        var solutionXmlPath = FindSolutionXml(ProjectDir);
        if (solutionXmlPath == null)
        {
            Log.LogMessage(MessageImportance.Low, $"solution.xml not found under '{ProjectDir}'. Skip.");
            return true;
        }

        var originalText = File.ReadAllText(solutionXmlPath);
        var encoding = DetectEncoding(originalText) ?? Encoding.UTF8;

        var doc = new XmlDocument { PreserveWhitespace = true };
        try
        {
            doc.LoadXml(originalText);
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to load XML '{solutionXmlPath}': {ex.Message}");
            return false;
        }

        bool changed = false;

        if (!string.IsNullOrWhiteSpace(Version))
            changed |= PatchInnerText(doc, "//*[local-name()='Version']", Version!.Trim());

        if (!string.IsNullOrWhiteSpace(Managed))
            changed |= PatchInnerText(doc, "//*[local-name()='Managed']", Managed!.Trim());

        if (!string.IsNullOrWhiteSpace(PublisherName))
        {
            var name = PublisherName!.Trim();

            changed |= PatchInnerText(doc,
                "//*[local-name()='Publisher']/*[local-name()='UniqueName']",
                name);

            changed |= PatchAttribute(doc,
                "//*[local-name()='Publisher']//*[local-name()='LocalizedName']/@description",
                name);

            changed |= PatchAttribute(doc,
                "//*[local-name()='Publisher']//*[local-name()='Description']/@description",
                name);
        }

        if (!string.IsNullOrWhiteSpace(PublisherPrefix))
        {
            var prefix = PublisherPrefix!.Trim().ToLowerInvariant();

            changed |= PatchInnerText(doc,
                "//*[local-name()='Publisher']/*[local-name()='CustomizationPrefix']",
                prefix);
        }

        if (!changed)
            return !Log.HasLoggedErrors;

        var settings = new XmlWriterSettings
        {
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            OmitXmlDeclaration = false,
            Encoding = encoding
        };

        try
        {
            using var fs = new FileStream(solutionXmlPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var xw = XmlWriter.Create(fs, settings);
            doc.Save(xw);
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to save XML '{solutionXmlPath}': {ex.Message}");
            return false;
        }

        Log.LogMessage(MessageImportance.High, $"Patched solution.xml: {solutionXmlPath}");
        return !Log.HasLoggedErrors;
    }

    private bool PatchInnerText(XmlDocument doc, string xpath, string value)
    {
        var nodes = doc.SelectNodes(xpath);
        if (nodes == null || nodes.Count == 0)
            return false;

        EnforceSafety(xpath, nodes.Count);

        bool changed = false;
        foreach (XmlNode n in nodes)
        {
            if (n.InnerText != value)
            {
                n.InnerText = value;
                changed = true;
            }
        }
        return changed;
    }

    private bool PatchAttribute(XmlDocument doc, string xpath, string value)
    {
        var nodes = doc.SelectNodes(xpath);
        if (nodes == null || nodes.Count == 0)
            return false;

        EnforceSafety(xpath, nodes.Count);

        bool changed = false;
        foreach (XmlNode n in nodes)
        {
            if (n is XmlAttribute a && a.Value != value)
            {
                a.Value = value;
                changed = true;
            }
        }
        return changed;
    }

    private void EnforceSafety(string xpath, int matches)
    {
        if (matches <= MaxMatches) return;

        var msg = $"Too many matches ({matches}) for XPath: {xpath}. MaxMatches={MaxMatches}. Fix mapping.";
        if (FailOnManyMatches) Log.LogError(msg);
        else Log.LogWarning(msg);
    }

    private static Encoding? DetectEncoding(string xmlText)
    {
        var m = Regex.Match(xmlText,
            @"<\?xml\s+version\s*=\s*[""'][^""']+[""']\s+encoding\s*=\s*[""'](?<e>[^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!m.Success) return null;

        try { return Encoding.GetEncoding(m.Groups["e"].Value); }
        catch { return null; }
    }

    private string? FindSolutionXml(string projectDir)
    {
        var candidates = new[]
        {
            Path.Combine(projectDir, "solution.xml"),
            Path.Combine(projectDir, "Solution", "solution.xml"),
            Path.Combine(projectDir, "Solution", "Other", "solution.xml"),
            Path.Combine(projectDir, "Other", "solution.xml"),
        };

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return FindByScan(projectDir, "solution.xml", maxDepth: 4);
    }

    private static string? FindByScan(string root, string fileName, int maxDepth)
    {
        bool SkipDir(string d)
        {
            var name = Path.GetFileName(d);
            return name.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || name.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase);
        }

        string? Scan(string dir, int depth)
        {
            if (depth > maxDepth) return null;

            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, fileName, SearchOption.TopDirectoryOnly))
                    return f;

                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    if (SkipDir(sub)) continue;
                    var found = Scan(sub, depth + 1);
                    if (found != null) return found;
                }
            }
            catch
            {
                // ignore access errors
            }

            return null;
        }

        return Scan(root, 0);
    }
}
