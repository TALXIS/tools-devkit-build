using System;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.DataMigration;
using TALXIS.Platform.Metadata.Validation;

/// <summary>
/// Runs the CMT XML schema, schema and package rules on every source CMT package before the packages are merged.
/// </summary>
public class ValidateCmtPackage : Task
{
    /// <summary>
    /// Package folders that contain data_schema.xml and data.xml.
    /// </summary>
    [Required]
    public ITaskItem[] PackageDirectories { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            var xmlSchemaValidator = new SchemaValidator();
            var reader = new CmtPackageXmlReader();
            var schemaValidator = new CmtDataSchemaValidator();
            var packageValidator = new CmtPackageValidator();
            var hasErrors = false;

            foreach (var directory in PackageDirectories.Select(d => d.GetMetadata("FullPath")).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // The reader turns any boolean token other than true/1 into false, so a value like disableplugins="yes" has to be caught here.
                var xmlSchemaResults = new[] { CmtPackageLayout.SchemaFileName, CmtPackageLayout.DataFileName }
                    .SelectMany(file => xmlSchemaValidator.ValidateFile(Path.Combine(directory, file)))
                    .ToList();
                foreach (var result in xmlSchemaResults)
                {
                    hasErrors |= LogResult(result, directory);
                }
                if (xmlSchemaResults.Any(r => r.Severity == ValidationSeverity.Error)) continue;

                var package = reader.LoadDirectory(directory);
                foreach (var error in package.LoadErrors)
                {
                    Log.LogError(null, "TXVAL001", null, error.FilePath, error.Line ?? 0, error.Column ?? 0, 0, 0, error.Message);
                    hasErrors = true;
                }
                if (package.LoadErrors.Count > 0) continue;

                foreach (var result in schemaValidator.Validate(package.Schema).Concat(packageValidator.Validate(package)))
                {
                    hasErrors |= LogResult(result, directory);
                }
            }

            return !hasErrors;
        }
        catch (Exception ex)
        {
            Log.LogError($"ValidateCmtPackage failed: {ex.Message}");
            return false;
        }
    }

    private bool LogResult(ValidationResult result, string directory)
    {
        var file = result.FilePath ?? directory;
        var hasRuleCode = !string.IsNullOrEmpty(result.Code) && result.Code != ValidationDiagnostics.Unclassified;
        if (result.Severity == ValidationSeverity.Error)
        {
            Log.LogError(null, hasRuleCode ? result.Code : "TXVAL001", null, file, result.Line ?? 0, result.Column ?? 0, 0, 0, result.Message);
            return true;
        }

        Log.LogWarning(null, hasRuleCode ? result.Code : "TXVAL002", null, file, result.Line ?? 0, result.Column ?? 0, 0, 0, result.Message);
        return false;
    }
}
