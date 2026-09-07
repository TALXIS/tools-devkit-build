using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.Validation;

public class ValidatePdPackage : Task
{
    // Source folders of the solutions referenced by the PD package project.
    public ITaskItem[] SolutionRoots { get; set; } = Array.Empty<ITaskItem>();

    // data_schema.xml files of the CMT packages discovered in the PD package project.
    public ITaskItem[] CmtDataSchemaFiles { get; set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        try
        {
            bool hasErrors = false;

            var roots = (SolutionRoots ?? Array.Empty<ITaskItem>())
                .Select(i => i?.ItemSpec)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (roots.Count > 0)
            {
                var report = new WorkspaceValidator().ValidateRelationships(roots);
                foreach (var result in report.Results)
                {
                    LogResult(result, result.FilePath ?? roots[0], ref hasErrors);
                }
            }

            var cmtValidator = new CmtDataSchemaValidator();
            foreach (var item in CmtDataSchemaFiles ?? Array.Empty<ITaskItem>())
            {
                var filePath = item?.ItemSpec;
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) continue;

                foreach (var result in cmtValidator.ValidateFile(filePath))
                {
                    // CMT results come from the validator directly, so they carry
                    // no stage label yet; keep the output consistent with reports.
                    var labeled = result with { Message = $"[CMT] {result.Message}" };
                    LogResult(labeled, filePath, ref hasErrors);
                }
            }

            return !hasErrors;
        }
        catch (Exception ex)
        {
            Log.LogError($"ValidatePdPackage failed: {ex.Message}");
            return false;
        }
    }

    private void LogResult(ValidationResult result, string fallbackFile, ref bool hasErrors)
    {
        var file = result.FilePath ?? fallbackFile;
        var line = result.Line ?? 0;
        var column = result.Column ?? 0;

        // Rule-specific codes (TXM001, ...) let consumers suppress individual rules;
        // uncoded findings fall back to the task-wide codes.
        var hasRuleCode = !string.IsNullOrEmpty(result.Code) && result.Code != ValidationDiagnostics.Unclassified;

        if (result.Severity == ValidationSeverity.Error)
        {
            Log.LogError(null, hasRuleCode ? result.Code : "TXVAL001", null, file, line, column, 0, 0, result.Message);
            hasErrors = true;
        }
        else
        {
            Log.LogWarning(null, hasRuleCode ? result.Code : "TXVAL002", null, file, line, column, 0, 0, result.Message);
        }
    }
}
