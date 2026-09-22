using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
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
                    if (!ValidatePackagerResult(packager.Pack(SolutionRootDirectory, PathToZipFile, options))) return false;
                    Log.LogMessage(MessageImportance.High, "Solution packed successfully.");
                    return true;
                case "unpack":
                    Log.LogMessage(MessageImportance.High, $"Unpacking solution from '{PathToZipFile}' to '{SolutionRootDirectory}'...");
                    if (!ValidatePackagerResult(packager.Unpack(PathToZipFile, SolutionRootDirectory, options))) return false;
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
            LogFullLogPointer();
            return false;
        }
    }

    // SolutionPackagerLib's RootComponentsValidation checks each declared RootComponent against
    // a hardcoded allowlist of component types; type 372 ("ECConnector" in its own enum, but the
    // type this package actually emits for a working Dataverse connector - Solution.Connector.
    // targets, Type="372") isn't in that allowlist, so it's always reported missing regardless of
    // correctness. ConnectorDescriptorExists() independently confirms the connector's descriptor
    // was actually staged before treating that specific warning as a known false positive, so a
    // genuinely stale RootComponent (e.g. left behind after its Connector project is removed)
    // still fails the build as a real error.
    private static readonly Regex MissingRootComponentPattern =
        new Regex(@"Type='([^']+)',\s*Id \(or schema name\)='([^']+)'", RegexOptions.Compiled);
    private static readonly HashSet<string> KnownFalsePositiveComponentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ECConnector" };

    private bool ValidatePackagerResult(SolutionPackagerResult result)
    {
        var (falsePositiveConnectorWarnings, realMissingRootComponentWarnings) =
            PartitionMissingRootComponentWarnings(result.MissingRootComponentWarnings);

        foreach (var warning in result.Warnings.Except(result.MissingRootComponentWarnings).Concat(falsePositiveConnectorWarnings))
        {
            Log.LogWarning(warning);
        }

        // Missing root components are only a packager warning, but (except for the known
        // Connector/ECConnector false positive above) they mean the zip is incomplete.
        var errors = result.Errors.Concat(realMissingRootComponentWarnings).ToList();
        if (errors.Count == 0) return true;

        foreach (var error in errors)
        {
            Log.LogError(error);
        }

        // Delete the incomplete zip so a later import cannot pick up a stale artifact.
        if (string.Equals(Action, "pack", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(PathToZipFile))
        {
            System.IO.File.Delete(PathToZipFile);
        }

        Log.LogError($"SolutionPackager {Action.ToLowerInvariant()} failed validation. Add the missing components to the solution source or remove them from Solution.xml.");
        LogFullLogPointer();
        return false;
    }

    // Each entry can list more than one missing component; a warning is only reclassified as a
    // known false positive when every component it names is both ECConnector-typed and has its
    // descriptor staged on disk - a real, unrelated missing component in the same message keeps
    // it an error.
    private (IReadOnlyList<string> FalsePositives, IReadOnlyList<string> Real) PartitionMissingRootComponentWarnings(
        IEnumerable<string> missingRootComponentWarnings)
    {
        var falsePositives = new List<string>();
        var real = new List<string>();

        foreach (var warning in missingRootComponentWarnings ?? Enumerable.Empty<string>())
        {
            var components = MissingRootComponentPattern.Matches(warning ?? string.Empty)
                .Cast<Match>()
                .Select(m => (Type: m.Groups[1].Value, Id: m.Groups[2].Value))
                .ToList();

            var isKnownFalsePositive = components.Count > 0 && components.All(c =>
                KnownFalsePositiveComponentTypes.Contains(c.Type) && ConnectorDescriptorExists(c.Type, c.Id));

            (isKnownFalsePositive ? falsePositives : real).Add(warning);
        }

        return (falsePositives, real);
    }

    // A Connector RootComponent is keyed by schema name rather than a GUID, so the warning's own
    // "Id (or schema name)" text is actually "<Type>-<schemaname>" - strip that prefix before
    // checking whether Solution.Connector.targets staged that descriptor under
    // SolutionRootDirectory/Connectors/<schemaname>.xml (the same directory this task packs
    // from), independent evidence the connector is genuinely there.
    private bool ConnectorDescriptorExists(string type, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(SolutionRootDirectory))
        {
            return false;
        }

        var schemaName = id.Trim();
        var prefix = (type ?? string.Empty).Trim() + "-";
        if (schemaName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            schemaName = schemaName.Substring(prefix.Length);
        }

        var connectorsDir = System.IO.Path.Combine(SolutionRootDirectory, "Connectors");
        return System.IO.File.Exists(System.IO.Path.Combine(connectorsDir, schemaName.Trim() + ".xml"));
    }

    private void LogFullLogPointer()
    {
        if (!string.IsNullOrWhiteSpace(LogFilePath) && System.IO.File.Exists(LogFilePath))
        {
            Log.LogError($"Full log available at: {LogFilePath}");
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
