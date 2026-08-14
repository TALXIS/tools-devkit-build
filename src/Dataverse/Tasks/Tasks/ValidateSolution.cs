using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.Validation;

public class ValidateSolution : Task
{
    [Required]
    public string ProjectDir { get; set; }

    public override bool Execute()
    {
        try
        {
            if (!Directory.Exists(ProjectDir))
            {
                Log.LogError($"ValidateSolution: directory not found: {ProjectDir}");
                return false;
            }

            var validator = new SolutionValidator();
            var report = validator.Validate(ProjectDir);

            bool hasErrors = false;

            foreach (var result in report.Results)
            {
                var file = result.FilePath ?? ProjectDir;
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

            if (report.LoadedComponents != null)
            {
                Log.LogMessage(MessageImportance.Normal, $"Solution loaded: {report.LoadedComponents}");
            }

            return !hasErrors;
        }
        catch (Exception ex)
        {
            Log.LogError($"ValidateSolution failed: {ex.Message}");
            return false;
        }
    }
}
