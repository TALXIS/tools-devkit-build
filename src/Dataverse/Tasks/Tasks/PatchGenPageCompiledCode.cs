using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

public class PatchGenPageCompiledCode : Task
{
    [Required]
    public string CompiledJsPath { get; set; }

    public string RuntimeTypesJsPath { get; set; }

    [Required]
    public string OutputPath { get; set; }

    public override bool Execute()
    {
        try
        {
            if (!File.Exists(CompiledJsPath))
            {
                Log.LogError($"Compiled JS file not found: {CompiledJsPath}");
                return false;
            }

            var js = File.ReadAllText(CompiledJsPath, Encoding.UTF8);

            // Strip RuntimeTypes import lines (single or double quotes)
            js = Regex.Replace(js, @"import\s+.*?from\s+['""]\.\/RuntimeTypes['""];?\s*", "");

            var result = js;

            if (!string.IsNullOrEmpty(RuntimeTypesJsPath) && File.Exists(RuntimeTypesJsPath))
            {
                var rt = File.ReadAllText(RuntimeTypesJsPath, Encoding.UTF8);
                result = "// --- BEGIN GENERATED RUNTIME TYPES ---\n\n"
                       + rt
                       + "\n// --- END GENERATED RUNTIME TYPES ---\n\n"
                       + js;
            }

            var directory = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(OutputPath, result, new UTF8Encoding(false));

            Log.LogMessage(MessageImportance.High, $"Patched GenPage compiled code: {OutputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }
}
