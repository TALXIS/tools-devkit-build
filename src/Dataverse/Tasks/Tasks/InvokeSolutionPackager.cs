using System;
using System.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Crm.Tools.SolutionPackager;

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
            var arguments = BuildArguments();
            if (arguments == null)
            {
                return false;
            }

            var packager = new SolutionPackager(arguments);
            packager.Run();
            return true;
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

    private PackagerArguments BuildArguments()
    {
        if (!TryParsePackageType(out var packageType))
        {
            return null;
        }

        if (!Enum.TryParse(ErrorLevel, ignoreCase: true, out TraceLevel errorLevel))
        {
            Log.LogError($"Unsupported error level: {ErrorLevel}");
            return null;
        }

        PackagerArguments arguments;

        switch (Action.ToLowerInvariant())
        {
            case "pack":
                arguments = new PackagerArguments
                {
                    Action = CommandAction.Pack,
                    PathToZipFile = PathToZipFile,
                    Folder = SolutionRootDirectory,
                    PackageType = packageType,
                    ErrorLevel = errorLevel,
                    Localize = Localize,
                    UseUnmanagedFileForManaged = UseUnmanagedFileForMissingManaged,
                };
                break;
            case "unpack":
                arguments = new PackagerArguments
                {
                    Action = CommandAction.Extract,
                    PathToZipFile = PathToZipFile,
                    Folder = SolutionRootDirectory,
                    PackageType = packageType,
                    AllowDeletes = AllowDelete.Yes,
                    AllowWrites = AllowWrite.Yes,
                    ErrorLevel = errorLevel,
                    Localize = Localize,
                };
                break;
            default:
                Log.LogError($"Unsupported action: {Action}");
                return null;
        }

        if (!string.IsNullOrWhiteSpace(MappingFilePath))
        {
            arguments.MappingFile = MappingFilePath;
        }

        if (!string.IsNullOrWhiteSpace(LogFilePath))
        {
            arguments.LogFile = LogFilePath;
        }

        if (!string.IsNullOrWhiteSpace(LocalTemplate))
        {
            arguments.LocaleTemplate = LocalTemplate;
        }

        return arguments;
    }

    private bool TryParsePackageType(out SolutionPackageType packageType)
    {
        if (string.IsNullOrWhiteSpace(PackageType) ||
            string.Equals(PackageType, "Unmanaged", StringComparison.OrdinalIgnoreCase))
        {
            packageType = SolutionPackageType.Unmanaged;
            return true;
        }

        if (string.Equals(PackageType, "Managed", StringComparison.OrdinalIgnoreCase))
        {
            packageType = SolutionPackageType.Managed;
            return true;
        }

        Log.LogError($"Unsupported package type: {PackageType}");
        packageType = default;
        return false;
    }
}
