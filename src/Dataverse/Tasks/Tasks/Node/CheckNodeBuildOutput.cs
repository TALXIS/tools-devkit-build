using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Checks JavaScript deliverables of a production Node build before they are packaged:
/// no inline source map, no webpack "eval" devtool output, and a size below the Dataverse limit.
/// A development bundle or one over the limit builds fine but fails at solution import, so the
/// build reports it instead.
/// </summary>
public sealed class CheckNodeBuildOutput : Task
{
    private const string InlineSourceMapMarker = "sourceMappingURL=data:";
    private const string WebpackEvalMarker = "//# sourceURL=webpack";

    [Required]
    public ITaskItem[] Files { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>Largest allowed file size in bytes; 0 disables the size check.</summary>
    public long MaxSize { get; set; }

    /// <summary>Report violations as errors (true) or warnings (false).</summary>
    public bool TreatViolationsAsErrors { get; set; } = true;

    public override bool Execute()
    {
        foreach (var item in Files)
        {
            var path = item.GetMetadata("FullPath");
            if (!File.Exists(path) ||
                !string.Equals(Path.GetExtension(path), ".js", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var size = new FileInfo(path).Length;
            if (MaxSize > 0 && size > MaxSize)
            {
                Report($"'{path}' is {size:N0} bytes, over the {MaxSize:N0}-byte limit (NodeBuildMaxOutputSize). Dataverse rejects web resources over its size limit at import.");
            }
            else if (MaxSize > 0 && size > MaxSize * 8 / 10)
            {
                Log.LogWarning($"'{path}' is {size:N0} bytes, above 80% of the {MaxSize:N0}-byte limit (NodeBuildMaxOutputSize).");
            }

            var text = File.ReadAllText(path);
            if (text.Contains(InlineSourceMapMarker, StringComparison.Ordinal))
            {
                Report($"'{path}' contains an inline source map. Production builds must not embed source maps; make the build script turn them off when NODE_ENV is 'production'.");
            }

            if (text.Contains(WebpackEvalMarker, StringComparison.Ordinal))
            {
                Report($"'{path}' is a webpack development bundle (eval devtool). Production builds must run webpack in production mode.");
            }
        }

        return !Log.HasLoggedErrors;
    }

    private void Report(string message)
    {
        if (TreatViolationsAsErrors)
        {
            Log.LogError(message);
        }
        else
        {
            Log.LogWarning(message);
        }
    }
}
