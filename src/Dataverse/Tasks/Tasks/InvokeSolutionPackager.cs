using System;
using System.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using TALXIS.Platform.Metadata.Packaging;

public class InvokeSolutionPackager : Task
{
    [Required]
    public string Action { get; set; } = string.Empty;

    public string PackageType { get; set; } = string.Empty;

    public string SolutionRootDirectory { get; set; } = string.Empty;

    [Required]
    public string PathToZipFile { get; set; } = string.Empty;

    public string ErrorLevel { get; set; } = nameof(TraceLevel.Info);

    public string LogFilePath { get; set; } = string.Empty;

    public string MappingFilePath { get; set; } = string.Empty;

    public bool Localize { get; set; }

    public string LocalTemplate { get; set; } = string.Empty;

    public bool UseUnmanagedFileForMissingManaged { get; set; }

    public override bool Execute()
    {
        try
        {
            var options = BuildOptions();
            if (options == null)
            {
                return false;
            }

            var packager = new SolutionPackagerService();

            switch (Action.ToLowerInvariant())
            {
                case "pack":
                    Log.LogMessage(MessageImportance.High, $"Packing solution from '{SolutionRootDirectory}' to '{PathToZipFile}'...");
                    packager.Pack(SolutionRootDirectory, PathToZipFile, options);
                    Log.LogMessage(MessageImportance.High, "Solution packed successfully.");
                    return true;
                case "unpack":
                    Log.LogMessage(MessageImportance.High, $"Unpacking solution from '{PathToZipFile}' to '{SolutionRootDirectory}'...");
                    packager.Unpack(PathToZipFile, SolutionRootDirectory, options);
                    Log.LogMessage(MessageImportance.High, "Solution unpacked successfully.");
                    return true;
                default:
                    Log.LogError($"Unsupported action: {Action}");
                    return false;
            }
        }
        catch (Exception ex)
        {
            Log.LogError($"SolutionPackager {Action.ToLowerInvariant()} failed.");
            Log.LogErrorFromException(ex, showStackTrace: true);

            if (!string.IsNullOrWhiteSpace(LogFilePath) && System.IO.File.Exists(LogFilePath))
            {
                Log.LogError($"Full log available at: {LogFilePath}");
            }

            return false;
        }
    }

    private SolutionPackagerOptions BuildOptions()
    {
        if (!TryParseManaged(out var managed))
        {
            return null;
        }

        if (!Enum.TryParse(ErrorLevel, ignoreCase: true, out TraceLevel errorLevel))
        {
            Log.LogError($"Unsupported error level: {ErrorLevel}");
            return null;
        }

        var options = new SolutionPackagerOptions
        {
            Managed = managed,
            ErrorLevel = errorLevel,
            Localize = Localize,
            UseUnmanagedFileForMissingManaged = UseUnmanagedFileForMissingManaged,
        };

        if (!string.IsNullOrWhiteSpace(LogFilePath))
        {
            options.LogFilePath = LogFilePath;
        }

        if (!string.IsNullOrWhiteSpace(MappingFilePath))
        {
            options.MappingFilePath = MappingFilePath;
        }

        if (!string.IsNullOrWhiteSpace(LocalTemplate))
        {
            options.SourceLocale = LocalTemplate;
        }

        if (string.Equals(Action, "unpack", StringComparison.OrdinalIgnoreCase))
        {
            options.AllowDeletes = true;
            options.AllowWrites = true;
        }

        return options;
    }

    private bool TryParseManaged(out bool managed)
    {
        if (string.IsNullOrWhiteSpace(PackageType) ||
            string.Equals(PackageType, "Unmanaged", StringComparison.OrdinalIgnoreCase))
        {
            managed = false;
            return true;
        }

        if (string.Equals(PackageType, "Managed", StringComparison.OrdinalIgnoreCase))
        {
            managed = true;
            return true;
        }

        Log.LogError($"Unsupported package type: {PackageType}");
        managed = false;
        return false;
    }
}
