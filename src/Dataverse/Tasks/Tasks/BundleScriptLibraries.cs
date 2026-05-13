using System;
using System.IO;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Concatenates a list of script library source files into a single output
/// file. Used when a ScriptLibrary project pulls in another ScriptLibrary
/// reference with `ScriptLibraryMode=Bundle` — the referenced project's
/// compiled .js is prepended to the consumer's main .js so a single
/// web resource ships both projects.
///
/// Safe when one of the sources is the destination itself: all source files
/// are buffered into memory before the destination is opened for writing.
/// </summary>
public sealed class BundleScriptLibraries : Task
{
    /// <summary>Files to concatenate, in the order they will appear in the output.</summary>
    [Required]
    public ITaskItem[] Sources { get; set; }

    /// <summary>Path of the combined .js file to write.</summary>
    [Required]
    public string Destination { get; set; } = "";

    public override bool Execute()
    {
        try
        {
            if (Sources == null || Sources.Length == 0)
            {
                Log.LogError("BundleScriptLibraries: Sources is empty.");
                return false;
            }

            var dir = Path.GetDirectoryName(Destination);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Read every source into memory FIRST. Writing back into one of the
            // source paths (the common case where Destination == one of Sources)
            // would otherwise truncate that file before we read it, or collide
            // on the file handle.
            var buffers = new byte[Sources.Length][];
            for (int i = 0; i < Sources.Length; i++)
            {
                var src = Sources[i].ItemSpec;
                if (!File.Exists(src))
                {
                    Log.LogError("BundleScriptLibraries: source file not found: " + src);
                    return false;
                }
                buffers[i] = File.ReadAllBytes(src);
            }

            // Defensive separator between concatenated files: newline + semicolon
            // + newline. Guards against ASI hazards across TS namespace IIFEs that
            // may not have a trailing semicolon.
            var separator = Encoding.UTF8.GetBytes("\n;\n");

            using (var output = new FileStream(Destination, FileMode.Create, FileAccess.Write))
            {
                for (int i = 0; i < buffers.Length; i++)
                {
                    output.Write(buffers[i], 0, buffers[i].Length);
                    if (i < buffers.Length - 1)
                    {
                        output.Write(separator, 0, separator.Length);
                    }
                }
            }

            Log.LogMessage(MessageImportance.High,
                $"BundleScriptLibraries: combined {Sources.Length} file(s) -> {Destination}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex);
            return false;
        }
    }
}
