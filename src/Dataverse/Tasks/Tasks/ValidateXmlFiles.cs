using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.Validation;

public class ValidateXmlFiles : Task
{
    [Required]
    public ITaskItem[] FilesForValidation { get; set; }

    // Kept for backwards compatibility with existing .targets files.
    // Schemas are now loaded from embedded resources in the package;
    // this property is accepted but ignored.
    public ITaskItem[] SchemaFiles { get; set; }

    public override bool Execute()
    {
        try
        {
            var validator = new SchemaValidator();
            int total  = FilesForValidation?.Length ?? 0;
            int failed = 0;

            for (int i = 0; i < total; i++)
            {
                var filePath = FilesForValidation[i].ItemSpec;
                if (!File.Exists(filePath))
                {
                    Log.LogError($"ValidateXmlFiles: file not found: {filePath}");
                    failed++;
                    continue;
                }

                var results = validator.ValidateFile(filePath);
                bool fileHasError = false;

                foreach (var result in results)
                {
                    int line = result.Line ?? 0;
                    int col  = result.Column ?? 0;

                    if (result.Severity == ValidationSeverity.Error)
                    {
                        Log.LogError(
                            subcategory: "schema",
                            errorCode:   "TALXISXSD001",
                            helpKeyword: null,
                            file:        result.FilePath ?? filePath,
                            lineNumber:  line,
                            columnNumber: col,
                            endLineNumber: 0,
                            endColumnNumber: 0,
                            message:     result.Message);
                        fileHasError = true;
                    }
                    else
                    {
                        Log.LogWarning(
                            subcategory: "schema",
                            warningCode: "TALXISXSD001",
                            helpKeyword: null,
                            file:        result.FilePath ?? filePath,
                            lineNumber:  line,
                            columnNumber: col,
                            endLineNumber: 0,
                            endColumnNumber: 0,
                            message:     result.Message);
                    }
                }

                if (fileHasError) failed++;
            }

            if (failed > 0)
            {
                Log.LogError($"ValidateXmlFiles: {failed} of {total} XML file(s) failed schema validation.");
                return false;
            }

            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }
}
