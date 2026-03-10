using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.IO;
using System.Text.RegularExpressions;

public class UpdateTsConfigOutFile : Task
{
    [Required]
    public string TsConfigPath { get; set; } = "";

    [Required]
    public string OutFileName { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            if (!File.Exists(TsConfigPath))
            {
                Log.LogError($"tsconfig.json not found: {TsConfigPath}");
                return false;
            }

            string content = File.ReadAllText(TsConfigPath);
            string newOutFile = $"build/{OutFileName}.js";

            string updated = Regex.Replace(
                content,
                @"(""outFile""\s*:\s*"")[^""]+("")",
                $"${{1}}{newOutFile}${{2}}");

            if (updated == content)
            {
                Log.LogMessage(MessageImportance.Normal,
                    $"UpdateTsConfigOutFile: outFile already set or not found in {TsConfigPath}");
                return true;
            }

            File.WriteAllText(TsConfigPath, updated);
            Log.LogMessage(MessageImportance.High,
                $"UpdateTsConfigOutFile: set outFile to \"{newOutFile}\" in {TsConfigPath}");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
    }
}
