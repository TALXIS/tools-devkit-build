using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.DataMigration;

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
        var reader = new CmtPackageXmlReader();
        var target = new CmtDataSchema();
        var sourceOrders = new List<(string Source, IReadOnlyList<string> Order)>();
        var warnings = new List<string>();

        foreach (var file in files)
        {
            var source = reader.Load(file, null);
            foreach (var error in source.LoadErrors)
            {
                Log.LogError(null, null, null, error.FilePath, error.Line ?? 0, error.Column ?? 0, 0, 0, error.Message);
            }
            if (source.LoadErrors.Count > 0) continue;

            target.DateMode ??= source.Schema.DateMode;
            if (source.Schema.EntityImportOrder.Count > 0) sourceOrders.Add((file, source.Schema.EntityImportOrder.ToList()));

            foreach (var entity in source.Schema.Entities)
            {
                if (string.IsNullOrWhiteSpace(entity.Name))
                {
                    Log.LogWarning($"Entity without a name skipped in {file}.");
                    continue;
                }

                CmtSchemaBuilder.MergeEntity(target, entity, warnings);
            }
        }

        if (Log.HasLoggedErrors) return;
        if (target.Entities.Count == 0) throw new InvalidOperationException("No entities were merged.");

        // Without any import order in the sources the merged schema keeps the old first-seen layout and gets no order element.
        if (sourceOrders.Count > 0)
        {
            var orderErrors = new List<string>();
            var lookups = target.Entities.ToDictionary(e => e.Name, e => e.ReferencedEntities(), StringComparer.Ordinal);
            var manualOrder = CmtImportOrderMerger.Merge(sourceOrders, orderErrors, lookups);
            foreach (var error in orderErrors)
            {
                Log.LogError(error);
            }
            if (orderErrors.Count > 0) return;

            CmtSchemaBuilder.ResolveImportOrder(target, warnings, manualOrder);
        }
        foreach (var warning in warnings)
        {
            Log.LogWarning(warning);
        }

        new CmtPackageXmlWriter().SaveSchema(new CmtPackage(target), outputPath);
    }
}
