using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class MergeCmtDataXml : Task
{
    [Required]
    public ITaskItem[] DataXmlFiles { get; set; } = Array.Empty<ITaskItem>();

    public string CmtPackageName { get; set; } = "";

    public string ProjectDirectory { get; set; } = "";

    public string OutputDirectory { get; set; } = "";

    [Output]
    public string OutputDataXml { get; private set; } = "";

    public override bool Execute()
    {
        try
        {
            var files = NormalizeFiles(DataXmlFiles);
            if (files.Count == 0)
            {
                Log.LogError("No data.xml files were provided.");
                return false;
            }

            var missing = files.Where(f => !File.Exists(f)).ToList();
            if (missing.Any())
            {
                foreach (var path in missing)
                {
                    Log.LogError($"data.xml not found: {path}");
                }
                return false;
            }

            var packageName = GetPackageName();
            var baseDir = ResolveOutputDirectory(packageName);
            Directory.CreateDirectory(baseDir);

            OutputDataXml = Path.Combine(baseDir, "data.xml");

            MergeFiles(files, OutputDataXml);

            Log.LogMessage(MessageImportance.High,
                $"Merged {files.Count} data.xml file(s) into {OutputDataXml}");

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
        var recordKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var doc = XDocument.Load(file, LoadOptions.PreserveWhitespace | LoadOptions.SetBaseUri | LoadOptions.SetLineInfo);
            var root = doc.Root ?? throw new InvalidDataException($"Root element is missing in {file}");

            if (outputDoc == null)
            {
                outputDoc = CreateOutputDocument(root);
                outputRoot = outputDoc.Root ?? throw new InvalidDataException("Failed to initialize merged document root.");
            }

            foreach (var entity in root.Elements())
            {
                if (entity.NodeType != XmlNodeType.Element)
                    continue;

                var entityName = entity.Attribute("name")?.Value?.Trim();
                var effectiveEntityName = string.IsNullOrWhiteSpace(entityName)
                    ? Guid.NewGuid().ToString("N")
                    : entityName;

                if (!entities.TryGetValue(effectiveEntityName, out var targetEntity))
                {
                    var cloned = new XElement(entity);
                    entities[effectiveEntityName] = cloned;
                    outputRoot.Add(cloned);
                    RegisterRecordKeys(cloned, effectiveEntityName, recordKeys);
                }
                else
                {
                    MergeEntityRecords(targetEntity, entity, effectiveEntityName, recordKeys);
                }
            }
        }

        if (outputDoc == null || outputRoot == null)
            throw new InvalidOperationException("No entities were merged.");

        outputRoot.SetAttributeValue("timestamp", DateTime.UtcNow.ToString("o"));

        WriteDocument(outputDoc, outputPath);
    }

    private static XDocument CreateOutputDocument(XElement templateRoot)
    {
        var outputRoot = new XElement(templateRoot.Name);
        foreach (var attr in templateRoot.Attributes())
        {
            if (attr.IsNamespaceDeclaration)
            {
                outputRoot.Add(attr);
            }
            else
            {
                outputRoot.SetAttributeValue(attr.Name, attr.Value);
            }
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), outputRoot);
        return doc;
    }

    private void MergeEntityRecords(
        XElement targetEntity,
        XElement sourceEntity,
        string entityName,
        HashSet<string> recordKeys)
    {
        var targetRecords = EnsureRecordsContainer(targetEntity);
        var sourceRecords = sourceEntity.Element("records");
        if (sourceRecords == null)
            return;

        foreach (var record in sourceRecords.Elements("record"))
        {
            var recordId = record.Attribute("id")?.Value?.Trim();
            var recordKey = string.IsNullOrWhiteSpace(recordId) ? null : BuildRecordKey(entityName, recordId);

            if (recordKey != null && recordKeys.Contains(recordKey))
                continue;

            var cloned = new XElement(record);
            targetRecords.Add(cloned);

            if (recordKey != null)
                recordKeys.Add(recordKey);
        }
    }

    private void RegisterRecordKeys(
        XElement entity,
        string entityName,
        HashSet<string> recordKeys)
    {
        var records = entity.Element("records");
        if (records == null)
            return;

        foreach (var record in records.Elements("record"))
        {
            var recordId = record.Attribute("id")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(recordId))
                continue;

            recordKeys.Add(BuildRecordKey(entityName, recordId));
        }
    }

    private static XElement EnsureRecordsContainer(XElement entity)
    {
        var records = entity.Element("records");
        if (records == null)
        {
            records = new XElement("records");
            entity.Add(records);
        }
        return records;
    }

    private static string BuildRecordKey(string entityName, string recordId)
    {
        return entityName + "|" + recordId;
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
