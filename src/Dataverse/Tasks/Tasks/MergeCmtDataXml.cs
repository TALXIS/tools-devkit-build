using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.DataMigration;

public class MergeCmtDataXml : Task
{
    [Required]
    public ITaskItem[] DataXmlFiles { get; set; } = Array.Empty<ITaskItem>();

    public string CmtPackageName { get; set; } = "";

    public string ProjectDirectory { get; set; } = "";

    public string OutputDirectory { get; set; } = "";

    /// <summary>
    /// Merged data_schema.xml whose entityImportOrder decides the order of entities in the merged data.xml.
    /// </summary>
    public string DataSchemaXml { get; set; } = "";

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
        var reader = new CmtPackageXmlReader();
        var target = new CmtData();
        var warnings = new List<string>();

        foreach (var file in files)
        {
            var source = reader.Load(Path.Combine(Path.GetDirectoryName(file)!, CmtPackageLayout.SchemaFileName), file);
            foreach (var error in source.LoadErrors)
            {
                Log.LogError(null, null, null, error.FilePath, error.Line ?? 0, error.Column ?? 0, 0, 0, error.Message);
            }
            if (source.LoadErrors.Count > 0 || source.Data is null) continue;

            foreach (var entity in source.Data.Entities)
            {
                CmtDataBuilder.MergeEntity(target, entity, warnings);
            }
        }

        foreach (var warning in warnings)
        {
            Log.LogWarning(warning);
        }

        if (Log.HasLoggedErrors) return;
        if (target.Entities.Count == 0) throw new InvalidOperationException("No entities were merged.");

        OrderEntitiesLikeSchema(target);
        target.Timestamp = DateTime.UtcNow.ToString("o");
        new CmtPackageXmlWriter().SaveData(new CmtPackage(new CmtDataSchema(), target), outputPath);
        WriteRecordCounts(outputPath);
    }

    private void OrderEntitiesLikeSchema(CmtData data)
    {
        if (string.IsNullOrWhiteSpace(DataSchemaXml) || !File.Exists(DataSchemaXml)) return;

        var order = new CmtPackageXmlReader().Load(DataSchemaXml, null).Schema.EntityImportOrder;
        if (order.Count == 0) return;

        var position = order.Select((name, index) => (name, index)).ToDictionary(p => p.name, p => p.index, StringComparer.Ordinal);
        var ordered = data.Entities
            .Select((entity, index) => (entity, index))
            .OrderBy(e => position.TryGetValue(e.entity.Name, out var p) ? p : order.Count)
            .ThenBy(e => e.index)
            .Select(e => e.entity)
            .ToList();

        data.Entities.Clear();
        foreach (var entity in ordered)
        {
            data.Entities.Add(entity);
        }
    }

    // reccount is not part of the CMT format, but the merged data.xml has always carried it for downstream tooling.
    private static void WriteRecordCounts(string path)
    {
        var doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        foreach (var entity in doc.Root!.Elements("entity"))
        {
            entity.SetAttributeValue("reccount", (entity.Element("records")?.Elements("record").Count() ?? 0).ToString());
        }

        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false) };
        using (var writer = XmlWriter.Create(path, settings))
        {
            doc.Save(writer);
        }
    }
}
