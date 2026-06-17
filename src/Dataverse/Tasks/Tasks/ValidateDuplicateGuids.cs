using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.Validation;

public class ValidateDuplicateGuids : Task
{
    private const string _managedSuffix = "_managed";

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
                .Where(r => r.FilePath == null || !HasManagedUnmanagedTwin(r.FilePath))
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

    /// <summary>
    /// Returns true if <paramref name="filePath"/> is one half of a
    /// managed/unmanaged pair, i.e. the corresponding `*_managed.xml` (or
    /// non-managed `*.xml`) sibling exists next to it in the same directory.
    /// </summary>
    private static bool HasManagedUnmanagedTwin(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;

        var directory = Path.GetDirectoryName(filePath);

        if (string.IsNullOrEmpty(directory)) return false;

        var nameNoExt = Path.GetFileNameWithoutExtension(filePath);
        var ext = Path.GetExtension(filePath);

        string twinName;

        if (nameNoExt.EndsWith(_managedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            twinName = nameNoExt.Substring(0, nameNoExt.Length - _managedSuffix.Length);
        }
        else
        {
            twinName = nameNoExt + _managedSuffix;
        }

        var twinPath = Path.Combine(directory, twinName + ext);

        return File.Exists(twinPath);
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
