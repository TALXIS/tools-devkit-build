using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.Validation;

public class ValidateWorkspace : Task
{
    [Required]
    public string ProjectDir { get; set; }

    public override bool Execute()
    {
        try
        {
            if (!Directory.Exists(ProjectDir))
            {
                Log.LogError($"ValidateWorkspace: directory not found: {ProjectDir}");
                return false;
            }

            var validator = new WorkspaceValidator();
            var report = validator.ValidateDirectory(ProjectDir);

            bool hasErrors = false;

            foreach (var result in report.Results)
            {
                var file = result.FilePath ?? ProjectDir;
                var line = result.Line ?? 0;
                var column = result.Column ?? 0;

                if (result.Severity == ValidationSeverity.Error)
                {
                    Log.LogError(null, "TXVAL001", null, file, line, column, 0, 0, result.Message);
                    hasErrors = true;
                }
                else
                {
                    Log.LogWarning(null, "TXVAL002", null, file, line, column, 0, 0, result.Message);
                }
            }

            if (report.LoadedComponents != null)
            {
                Log.LogMessage(MessageImportance.Normal, $"Workspace loaded: {report.LoadedComponents}");
            }

            return !hasErrors;
        }
        catch (Exception ex)
        {
            Log.LogError($"ValidateWorkspace failed: {ex.Message}");
            return false;
        }
    }
}
