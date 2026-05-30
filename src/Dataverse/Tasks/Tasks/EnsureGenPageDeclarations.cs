using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public sealed class EnsureGenPageDeclarations : Task
{
    [Required]
    public string SolutionRoot { get; set; } = "";

    [Required]
    public ITaskItem[] Pages { get; set; } = Array.Empty<ITaskItem>();

    public string CustomizationsXmlPath { get; set; } = "";

    [Output]
    public ITaskItem[] DeclaredPages { get; private set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            var solutionRoot = Path.GetFullPath(SolutionRoot);
            var uxRoot = Path.Combine(solutionRoot, "uxagentprojects");
            Directory.CreateDirectory(uxRoot);

            var duplicates = Pages.GroupBy(p => p.GetMetadata("PageName"), StringComparer.OrdinalIgnoreCase)
                .Where(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1)
                .Select(g => string.IsNullOrWhiteSpace(g.Key) ? "<empty>" : g.Key)
                .ToArray();
            if (duplicates.Length > 0)
            {
                Log.LogError($"Duplicate GenPage page name(s) across referenced projects: {string.Join(", ", duplicates)}");
                return false;
            }

            var pagesByName = Pages.ToDictionary(p => p.GetMetadata("PageName"), StringComparer.OrdinalIgnoreCase);
            var existing = ReadExistingDeclarations(uxRoot);

            foreach (var group in existing.Values.GroupBy(d => d.PageName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                Log.LogError($"Duplicate uxagentproject schema name '{group.Key}' found in solution source.");

            foreach (var declaration in existing.Values)
            {
                if (!pagesByName.ContainsKey(declaration.PageName))
                    Log.LogError($"Orphan GenPage declaration '{declaration.PageName}' at {declaration.ProjectXmlPath}; no referenced GenPage project emits this page.");
            }

            if (Log.HasLoggedErrors)
                return false;

            ValidateSitemapReferences(pagesByName.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
            if (Log.HasLoggedErrors)
                return false;

            var declared = new List<ITaskItem>();
            foreach (var page in Pages.OrderBy(p => p.GetMetadata("PageName"), StringComparer.OrdinalIgnoreCase))
            {
                var pageName = page.GetMetadata("PageName");
                if (!existing.TryGetValue(pageName, out var declaration))
                {
                    declaration = CreateDeclaration(uxRoot, pageName);
                    existing.Add(pageName, declaration);
                }

                var item = new TaskItem(page.ItemSpec);
                page.CopyMetadataTo(item);
                item.SetMetadata("PageGuid", declaration.PageGuid);
                item.SetMetadata("FileGuid", declaration.FileGuid);
                item.SetMetadata("ProjectXmlPath", declaration.ProjectXmlPath);
                item.SetMetadata("FileXmlPath", declaration.FileXmlPath);
                declared.Add(item);
            }

            DeclaredPages = declared.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private Dictionary<string, Declaration> ReadExistingDeclarations(string uxRoot)
    {
        var result = new Dictionary<string, Declaration>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(uxRoot))
            return result;

        foreach (var projectXml in Directory.GetFiles(uxRoot, "uxagentproject.xml", SearchOption.AllDirectories))
        {
            var doc = XDocument.Load(projectXml);
            var root = doc.Root;
            if (root == null || root.Name.LocalName != "uxagentproject")
                continue;

            var pageName = root.Element("name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(pageName))
            {
                Log.LogError($"GenPage declaration is missing a name: {projectXml}");
                continue;
            }

            var pageGuid = NormalizeGuid(root.Attribute("uxagentprojectid")?.Value);
            if (string.IsNullOrWhiteSpace(pageGuid))
                pageGuid = NormalizeGuid(Path.GetFileName(Path.GetDirectoryName(projectXml) ?? ""));
            if (string.IsNullOrWhiteSpace(pageGuid))
            {
                Log.LogError($"GenPage declaration has no valid page GUID: {projectXml}");
                continue;
            }

            var fileXml = Directory.GetFiles(Path.GetDirectoryName(projectXml) ?? uxRoot, "uxagentprojectfile.xml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (fileXml == null)
            {
                Log.LogError($"GenPage declaration '{pageName}' has no uxagentprojectfile.xml under {Path.GetDirectoryName(projectXml)}.");
                continue;
            }

            var fileDoc = XDocument.Load(fileXml);
            var fileGuid = NormalizeGuid(fileDoc.Root?.Attribute("uxagentprojectfileid")?.Value) ?? NormalizeGuid(Path.GetFileName(Path.GetDirectoryName(fileXml) ?? ""));
            if (string.IsNullOrWhiteSpace(fileGuid))
            {
                Log.LogError($"GenPage declaration '{pageName}' has no valid file GUID: {fileXml}");
                continue;
            }

            if (result.ContainsKey(pageName))
                Log.LogError($"Duplicate GenPage declaration name '{pageName}' at {projectXml}.");
            else
                result.Add(pageName, new Declaration(pageName, pageGuid, fileGuid, projectXml, fileXml));
        }

        return result;
    }

    private Declaration CreateDeclaration(string uxRoot, string pageName)
    {
        var pageGuid = Guid.NewGuid().ToString("D");
        var fileGuid = Guid.NewGuid().ToString("D");
        var pageDir = Path.Combine(uxRoot, pageGuid);
        var fileDir = Path.Combine(pageDir, fileGuid);
        Directory.CreateDirectory(fileDir);

        var projectXml = Path.Combine(pageDir, "uxagentproject.xml");
        var fileXml = Path.Combine(fileDir, "uxagentprojectfile.xml");

        SaveXml(new XDocument(new XElement("uxagentproject",
            new XAttribute("uxagentprojectid", pageGuid),
            new XElement("iscustomizable", "1"),
            new XElement("name", pageName),
            new XElement("statecode", "0"),
            new XElement("statuscode", "1"))), projectXml);

        SaveXml(new XDocument(new XElement("uxagentprojectfile",
            new XAttribute("uxagentprojectfileid", fileGuid),
            new XElement("filecontent", new XAttribute("mimetype", "application/octet-stream"), "src/pages/page.compiled"),
            new XElement("filename", "src/pages/page.compiled"),
            new XElement("filetype", "200000001"),
            new XElement("iscustomizable", "1"),
            new XElement("statecode", "0"),
            new XElement("statuscode", "1"))), fileXml);

        Log.LogMessage(MessageImportance.High, $"Created GenPage declaration '{pageName}' ({pageGuid}) in {pageDir}");
        return new Declaration(pageName, pageGuid, fileGuid, projectXml, fileXml);
    }

    private void ValidateSitemapReferences(HashSet<string> knownPageNames)
    {
        var path = string.IsNullOrWhiteSpace(CustomizationsXmlPath) ? Path.Combine(SolutionRoot, "Other", "Customizations.xml") : CustomizationsXmlPath;
        if (!File.Exists(path))
            return;

        var doc = XDocument.Load(path);
        foreach (var element in doc.Descendants().Where(e => string.Equals(e.Name.LocalName, "SubArea", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var attrName in new[] { "PageName", "GenPage" })
            {
                var value = element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, attrName, StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(value) && !knownPageNames.Contains(value.Trim()))
                    Log.LogError($"Sitemap GenPage reference '{value}' in {path} does not match any referenced GenPage page.");
            }
        }
    }

    private static void SaveXml(XDocument doc, string path)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = true,
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace
        };
        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }

    private static string NormalizeGuid(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return Guid.TryParse(value.Trim().Trim('{', '}'), out var guid) ? guid.ToString("D") : "";
    }

    private sealed class Declaration
    {
        public Declaration(string pageName, string pageGuid, string fileGuid, string projectXmlPath, string fileXmlPath)
        {
            PageName = pageName;
            PageGuid = pageGuid;
            FileGuid = fileGuid;
            ProjectXmlPath = projectXmlPath;
            FileXmlPath = fileXmlPath;
        }

        public string PageName { get; }
        public string PageGuid { get; }
        public string FileGuid { get; }
        public string ProjectXmlPath { get; }
        public string FileXmlPath { get; }
    }
}
