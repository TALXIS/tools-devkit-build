using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.Validation;

public class ValidateDuplicateGuids : Task
{
    [Required]
    public ITaskItem[] FilesForValidation { get; set; }

    public override bool Execute()
    {
        try
        {
            if (FilesForValidation == null || FilesForValidation.Length == 0)
                return true;

            // GuidValidator.ValidateDirectory works on a root directory path.
            // Derive the common workspace root from the supplied file list.
            var filePaths = FilesForValidation.Select(f => f.ItemSpec).Where(File.Exists).ToList();
            if (filePaths.Count == 0)
                return true;

            var pathComparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            var workspacePath = GetCommonDirectory(filePaths, pathComparer, pathComparison);

            // Only check files that were explicitly provided — the .targets
            // curates specific subdirectories (FormXml, SavedQueries, etc.)
            var allowedFiles = new System.Collections.Generic.HashSet<string>(
                filePaths.Select(Path.GetFullPath),
                pathComparer);

            var validator = new GuidValidator();
            var results = validator.ValidateDirectory(workspacePath)
                .Where(r => r.FilePath == null || allowedFiles.Contains(Path.GetFullPath(r.FilePath)))
                .ToList();

            foreach (var result in results)
            {
                int line = result.Line ?? 0;
                int col  = result.Column ?? 0;

                if (result.Severity == ValidationSeverity.Error)
                {
                    Log.LogError(
                        subcategory: "guid",
                        errorCode:   "TALXISGUID001",
                        helpKeyword: null,
                        file:        result.FilePath,
                        lineNumber:  line,
                        columnNumber: col,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message:     result.Message);
                }
                else
                {
                    Log.LogWarning(
                        subcategory: "guid",
                        warningCode: "TALXISGUID001",
                        helpKeyword: null,
                        file:        result.FilePath,
                        lineNumber:  line,
                        columnNumber: col,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message:     result.Message);
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

    private static string GetCommonDirectory(System.Collections.Generic.List<string> filePaths, StringComparer comparer, StringComparison comparison)
    {
        var directories = filePaths
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p)))
            .Distinct(comparer)
            .ToList();

        if (directories.Count == 1)
            return directories[0];

        var common = directories[0];
        while (!string.IsNullOrEmpty(common))
        {
            var prefix = common.EndsWith(Path.DirectorySeparatorChar.ToString()) || common.EndsWith(Path.AltDirectorySeparatorChar.ToString())
                ? common
                : common + Path.DirectorySeparatorChar;
            if (directories.All(d => d.Equals(common, comparison) || d.StartsWith(prefix, comparison)))
                return common;
            common = Path.GetDirectoryName(common);
        }

        return directories[0];
    }
}
