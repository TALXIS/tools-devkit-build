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
    private static readonly GenPageFileDefinition CompiledFile = new("Compiled", "src/pages/page.compiled", "page.compiled", "application/octet-stream", "200000001");
    private static readonly GenPageFileDefinition SourceFile = new("Source", "src/pages/page.tsx", "page.tsx", "application/octet-stream", "200000000");
    private static readonly GenPageFileDefinition ConfigFile = new("Config", "config.json", "config.json", "application/json", "200000000");
    private static readonly GenPageFileDefinition FirstPromptFile = new("FirstPrompt", "firstPrompt.json", "firstPrompt.json", "application/json", "200000000");

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

            RemoveUxAgentProjectsPlaceholder();
            RemoveGenPageRootComponents(solutionRoot);

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
                Log.LogError($"Duplicate uxagentproject name '{group.Key}' found in solution source.");

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

                NormalizeDeclaration(declaration, pageName, GetDesiredFiles(page).ToArray());

                var item = new TaskItem(page.ItemSpec);
                page.CopyMetadataTo(item);
                item.SetMetadata("PageGuid", declaration.PageGuid);
                item.SetMetadata("ProjectXmlPath", declaration.ProjectXmlPath);
                foreach (var file in declaration.Files.Values)
                {
                    item.SetMetadata(file.Definition.Prefix + "FileGuid", file.FileGuid);
                    item.SetMetadata(file.Definition.Prefix + "FileXmlPath", file.FileXmlPath);
                }

                // Backwards-compatible metadata for the compiled payload.
                if (declaration.Files.TryGetValue(CompiledFile.LogicalPath, out var compiled))
                {
                    item.SetMetadata("FileGuid", compiled.FileGuid);
                    item.SetMetadata("FileXmlPath", compiled.FileXmlPath);
                }

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

            var declaration = new Declaration(pageName, pageGuid, projectXml);
            var projectDir = Path.GetDirectoryName(projectXml) ?? uxRoot;
            foreach (var fileXml in Directory.GetFiles(projectDir, "uxagentprojectfile.xml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var fileDoc = XDocument.Load(fileXml);
                var fileGuid = NormalizeGuid(fileDoc.Root?.Attribute("uxagentprojectfileid")?.Value) ?? NormalizeGuid(Path.GetFileName(Path.GetDirectoryName(fileXml) ?? ""));
                if (string.IsNullOrWhiteSpace(fileGuid))
                {
                    Log.LogError($"GenPage declaration '{pageName}' has no valid file GUID: {fileXml}");
                    continue;
                }

                var logicalPath = NormalizeLogicalPath(fileDoc.Root?.Element("filename")?.Value, fileDoc.Root?.Element("filecontent")?.Value);
                var definition = GetDefinition(logicalPath);
                if (definition == null)
                    continue;

                if (!declaration.Files.ContainsKey(definition.LogicalPath))
                    declaration.Files.Add(definition.LogicalPath, new FileDeclaration(definition, fileGuid, fileXml));
            }

            if (result.ContainsKey(pageName))
                Log.LogError($"Duplicate GenPage declaration name '{pageName}' at {projectXml}.");
            else
                result.Add(pageName, declaration);
        }

        return result;
    }

    private Declaration CreateDeclaration(string uxRoot, string pageName)
    {
        var pageGuid = Guid.NewGuid().ToString("D");
        var pageDir = Path.Combine(uxRoot, pageGuid);
        Directory.CreateDirectory(pageDir);

        var projectXml = Path.Combine(pageDir, "uxagentproject.xml");
        var declaration = new Declaration(pageName, pageGuid, projectXml);

        Log.LogMessage(MessageImportance.High, $"Created GenPage declaration '{pageName}' ({pageGuid}) in {pageDir}");
        return declaration;
    }

    private void NormalizeDeclaration(Declaration declaration, string pageName, GenPageFileDefinition[] desiredFiles)
    {
        var pageDir = Path.Combine(Path.GetDirectoryName(declaration.ProjectXmlPath) ?? Path.Combine(Path.GetFullPath(SolutionRoot), "uxagentprojects"), "");
        Directory.CreateDirectory(pageDir);

        SaveProjectXml(declaration.ProjectXmlPath, declaration.PageGuid, pageName);

        var desired = new HashSet<string>(desiredFiles.Select(f => f.LogicalPath), StringComparer.OrdinalIgnoreCase);
        foreach (var definition in desiredFiles)
        {
            if (!declaration.Files.TryGetValue(definition.LogicalPath, out var file))
            {
                var fileGuid = Guid.NewGuid().ToString("D");
                var fileXml = Path.Combine(pageDir, "uxagentprojectfiles", fileGuid, "uxagentprojectfile.xml");
                file = new FileDeclaration(definition, fileGuid, fileXml);
                declaration.Files.Add(definition.LogicalPath, file);
            }
            else
            {
                file.Definition = definition;
                file.FileXmlPath = Path.Combine(pageDir, "uxagentprojectfiles", file.FileGuid, "uxagentprojectfile.xml");
            }

            SaveFileXml(file.FileXmlPath, file.FileGuid, definition);
        }

        foreach (var stale in declaration.Files.Values.Where(f => !desired.Contains(f.Definition.LogicalPath)).ToArray())
        {
            TryDeleteDirectory(Path.GetDirectoryName(stale.FileXmlPath));
            declaration.Files.Remove(stale.Definition.LogicalPath);
        }

        foreach (var legacyFileXml in Directory.GetFiles(pageDir, "uxagentprojectfile.xml", SearchOption.AllDirectories))
        {
            var normalized = Path.GetFullPath(legacyFileXml);
            if (!declaration.Files.Values.Any(f => string.Equals(Path.GetFullPath(f.FileXmlPath), normalized, StringComparison.OrdinalIgnoreCase)))
                TryDeleteDirectory(Path.GetDirectoryName(legacyFileXml));
        }
    }

    private IEnumerable<GenPageFileDefinition> GetDesiredFiles(ITaskItem page)
    {
        yield return CompiledFile;
        yield return SourceFile;
        yield return ConfigFile;
        yield return FirstPromptFile;
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

    private void RemoveUxAgentProjectsPlaceholder()
    {
        var path = string.IsNullOrWhiteSpace(CustomizationsXmlPath) ? Path.Combine(SolutionRoot, "Other", "Customizations.xml") : CustomizationsXmlPath;
        if (!File.Exists(path))
            return;

        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var nodes = doc.Root?.Elements().Where(e => string.Equals(e.Name.LocalName, "uxagentprojects", StringComparison.OrdinalIgnoreCase)).ToArray() ?? Array.Empty<XElement>();
        if (nodes.Length == 0)
            return;

        foreach (var node in nodes)
            node.Remove();
        SaveXml(doc, path, omitDeclaration: false);
        Log.LogMessage(MessageImportance.High, $"Removed uxagentprojects placeholder from {path}");
    }

    private void RemoveGenPageRootComponents(string solutionRoot)
    {
        var path = Path.Combine(solutionRoot, "Other", "Solution.xml");
        if (!File.Exists(path))
            return;

        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        var nodes = doc.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "RootComponent", StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Attribute("type")?.Value, "10090", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (nodes.Length == 0)
            return;

        foreach (var node in nodes)
            node.Remove();
        SaveXml(doc, path, omitDeclaration: false);
        Log.LogMessage(MessageImportance.High, $"Removed {nodes.Length} GenPage root component declaration(s) from {path}");
    }

    private static GenPageFileDefinition? GetDefinition(string logicalPath)
    {
        return new[] { CompiledFile, SourceFile, ConfigFile, FirstPromptFile }
            .FirstOrDefault(f => string.Equals(f.LogicalPath, logicalPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.BaseFileName, logicalPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLogicalPath(string? filename, string? filecontent)
    {
        var value = string.IsNullOrWhiteSpace(filename) ? filecontent : filename;
        value = (value ?? "").Trim().Replace('\\', '/');
        return GetDefinition(value)?.LogicalPath ?? value;
    }

    private static void SaveProjectXml(string path, string pageGuid, string pageName)
    {
        SaveXml(new XDocument(new XElement("uxagentproject",
            new XAttribute("uxagentprojectid", pageGuid),
            new XElement("iscustomizable", "1"),
            new XElement("name", pageName.ToLowerInvariant()),
            new XElement("statecode", "0"),
            new XElement("statuscode", "1"))), path, omitDeclaration: true);
    }

    private static void SaveFileXml(string path, string fileGuid, GenPageFileDefinition definition)
    {
        SaveXml(new XDocument(new XElement("uxagentprojectfile",
            new XAttribute("uxagentprojectfileid", fileGuid),
            new XElement("filecontent", new XAttribute("mimetype", definition.MimeType), definition.BaseFileName),
            new XElement("filename", definition.LogicalPath),
            new XElement("filetype", definition.FileType),
            new XElement("iscustomizable", "1"),
            new XElement("statecode", "0"),
            new XElement("statuscode", "1"))), path, omitDeclaration: true);
    }

    private static void SaveXml(XDocument doc, string path, bool omitDeclaration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = omitDeclaration,
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace
        };
        using var writer = XmlWriter.Create(path, settings);
        doc.Save(writer);
    }

    private static void TryDeleteDirectory(string? directory)
    {
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            Directory.Delete(directory, true);
    }

    private static string NormalizeGuid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return Guid.TryParse(value.Trim().Trim('{', '}'), out var guid) ? guid.ToString("D") : "";
    }

    private sealed class Declaration
    {
        public Declaration(string pageName, string pageGuid, string projectXmlPath)
        {
            PageName = pageName;
            PageGuid = pageGuid;
            ProjectXmlPath = projectXmlPath;
        }

        public string PageName { get; }
        public string PageGuid { get; }
        public string ProjectXmlPath { get; }
        public Dictionary<string, FileDeclaration> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FileDeclaration
    {
        public FileDeclaration(GenPageFileDefinition definition, string fileGuid, string fileXmlPath)
        {
            Definition = definition;
            FileGuid = fileGuid;
            FileXmlPath = fileXmlPath;
        }

        public GenPageFileDefinition Definition { get; set; }
        public string FileGuid { get; }
        public string FileXmlPath { get; set; }
    }

    private sealed class GenPageFileDefinition
    {
        public GenPageFileDefinition(string prefix, string logicalPath, string baseFileName, string mimeType, string fileType)
        {
            Prefix = prefix;
            LogicalPath = logicalPath;
            BaseFileName = baseFileName;
            MimeType = mimeType;
            FileType = fileType;
        }

        public string Prefix { get; }
        public string LogicalPath { get; }
        public string BaseFileName { get; }
        public string MimeType { get; }
        public string FileType { get; }
    }
}
