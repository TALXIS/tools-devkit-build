using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class MergeCmtDataSchemaXml : Task
{
    [Required]
    public ITaskItem[] DataSchemaFiles { get; set; } = Array.Empty<ITaskItem>();

    public string CmtPackageName { get; set; } = "";

    public string ProjectDirectory { get; set; } = "";

    public string OutputDirectory { get; set; } = "";

    [Output]
    public string OutputDataSchemaXml { get; private set; } = "";

    public override bool Execute()
    {
        try
        {
            var files = NormalizeFiles(DataSchemaFiles);
            if (files.Count == 0)
            {
                Log.LogError("No data_schema.xml files were provided.");
                return false;
            }

            var missing = files.Where(f => !File.Exists(f)).ToList();
            if (missing.Any())
            {
                foreach (var path in missing)
                {
                    Log.LogError($"data_schema.xml not found: {path}");
                }
                return false;
            }

            var packageName = GetPackageName();
            var baseDir = ResolveOutputDirectory(packageName);
            Directory.CreateDirectory(baseDir);

            OutputDataSchemaXml = Path.Combine(baseDir, "data_schema.xml");

            MergeFiles(files, OutputDataSchemaXml);

            Log.LogMessage(MessageImportance.High,
                $"Merged {files.Count} data_schema.xml file(s) into {OutputDataSchemaXml}");

            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private List<string> NormalizeFiles(ITaskItem[] items)
    {
        return (items ?? Array.Empty<ITaskItem>())
            .Select(i => i?.ItemSpec)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string GetPackageName()
    {
        var name = string.IsNullOrWhiteSpace(CmtPackageName)
            ? "MainCmtPackage"
            : CmtPackageName.Trim();

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "MainCmtPackage" : sanitized;
    }

    private string ResolveOutputDirectory(string packageName)
    {
        if (!string.IsNullOrWhiteSpace(OutputDirectory))
            return Path.GetFullPath(OutputDirectory);

        var root = string.IsNullOrWhiteSpace(ProjectDirectory)
            ? Directory.GetCurrentDirectory()
            : ProjectDirectory;

        return Path.GetFullPath(Path.Combine(root, "obj", "metadata", packageName));
    }

    private void MergeFiles(IReadOnlyCollection<string> files, string outputPath)
    {
        XDocument outputDoc = null;
        XElement outputRoot = null;
        var entities = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var doc = XDocument.Load(file, LoadOptions.PreserveWhitespace | LoadOptions.SetBaseUri | LoadOptions.SetLineInfo);
            var root = doc.Root ?? throw new InvalidDataException($"Root element is missing in {file}");

            if (outputDoc == null)
            {
                outputDoc = CreateOutputDocument(root);
                outputRoot = outputDoc.Root ?? throw new InvalidDataException("Failed to initialize merged document root.");
            }
            else
            {
                AddMissingAttributes(outputRoot, root);
            }

            foreach (var entity in root.Elements("entity"))
            {
                var entityName = entity.Attribute("name")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(entityName))
                {
                    Log.LogWarning($"Entity without a name skipped in {file}.");
                    continue;
                }

                if (!entities.TryGetValue(entityName, out var targetEntity))
                {
                    var cloned = new XElement(entity);
                    entities[entityName] = cloned;
                    outputRoot.Add(cloned);
                }
                else
                {
                    MergeEntity(targetEntity, entity);
                }
            }
        }

        if (outputDoc == null || outputRoot == null)
            throw new InvalidOperationException("No entities were merged.");

        WriteDocument(outputDoc, outputPath);
    }

    private static XDocument CreateOutputDocument(XElement templateRoot)
    {
        var outputRoot = new XElement(templateRoot.Name);
        foreach (var attr in templateRoot.Attributes())
        {
            outputRoot.Add(attr);
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), outputRoot);
        return doc;
    }

    private void AddMissingAttributes(XElement targetRoot, XElement sourceRoot)
    {
        foreach (var attr in sourceRoot.Attributes())
        {
            if (attr.IsNamespaceDeclaration)
            {
                var existing = targetRoot.Attributes()
                    .FirstOrDefault(a => a.IsNamespaceDeclaration && a.Name == attr.Name);
                if (existing == null)
                    targetRoot.Add(attr);
            }
            else if (targetRoot.Attribute(attr.Name) == null)
            {
                targetRoot.SetAttributeValue(attr.Name, attr.Value);
            }
        }
    }

    private void MergeEntity(XElement targetEntity, XElement sourceEntity)
    {
        MergeEntityAttributes(targetEntity, sourceEntity);
        MergeChildElements(targetEntity, sourceEntity, "fields", "field", "name");
        MergeChildElements(targetEntity, sourceEntity, "relationships", "relationship", "name");
    }

    private void MergeEntityAttributes(XElement targetEntity, XElement sourceEntity)
    {
        foreach (var attr in sourceEntity.Attributes())
        {
            if (attr.IsNamespaceDeclaration)
            {
                var existing = targetEntity.Attributes()
                    .FirstOrDefault(a => a.IsNamespaceDeclaration && a.Name == attr.Name);
                if (existing == null)
                    targetEntity.Add(attr);
            }
            else if (targetEntity.Attribute(attr.Name) == null)
            {
                targetEntity.SetAttributeValue(attr.Name, attr.Value);
            }
        }
    }

    private void MergeChildElements(
        XElement targetEntity,
        XElement sourceEntity,
        string containerName,
        string itemName,
        string keyAttribute)
    {
        var sourceContainer = sourceEntity.Element(containerName);
        if (sourceContainer == null)
            return;

        var targetContainer = targetEntity.Element(containerName);
        if (targetContainer == null)
        {
            targetContainer = new XElement(containerName);
            targetEntity.Add(targetContainer);
        }

        var existing = targetContainer.Elements(itemName)
            .Select(e => new { Element = e, Key = e.Attribute(keyAttribute)?.Value?.Trim() })
            .Where(e => !string.IsNullOrWhiteSpace(e.Key))
            .ToDictionary(e => e.Key, e => e.Element, StringComparer.OrdinalIgnoreCase);

        foreach (var item in sourceContainer.Elements(itemName))
        {
            var key = item.Attribute(keyAttribute)?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && existing.ContainsKey(key))
                continue;

            var cloned = new XElement(item);
            targetContainer.Add(cloned);

            if (!string.IsNullOrWhiteSpace(key))
                existing[key] = cloned;
        }
    }

    private static void WriteDocument(XDocument doc, string outputPath)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace
        };

        using (var writer = XmlWriter.Create(outputPath, settings))
        {
            doc.Save(writer);
        }
    }
}
