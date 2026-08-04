using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.Serialization.Xml;
using TALXIS.Platform.Metadata.Validation;

public class ValidateSolutionManifest : Task
{
    [Required]
    public string SolutionRoot { get; set; }

    public override bool Execute()
    {
        try
        {
            if (!Directory.Exists(SolutionRoot))
            {
                Log.LogError($"ValidateSolutionManifest: directory not found: {SolutionRoot}");
                return false;
            }

            var workspace = new XmlWorkspaceReader().Load(SolutionRoot);
            var results = new SolutionManifestValidator().Validate(workspace);

            foreach (var result in results)
            {
                var line = result.Line ?? 0;
                var col = result.Column ?? 0;

                if (result.Severity == ValidationSeverity.Error)
                {
                    Log.LogError(
                        subcategory: "manifest",
                        errorCode: result.Code,
                        helpKeyword: null,
                        file: result.FilePath ?? SolutionRoot,
                        lineNumber: line,
                        columnNumber: col,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: result.Message);
                }
                else
                {
                    Log.LogWarning(
                        subcategory: "manifest",
                        warningCode: result.Code,
                        helpKeyword: null,
                        file: result.FilePath ?? SolutionRoot,
                        lineNumber: line,
                        columnNumber: col,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: result.Message);
                }
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
