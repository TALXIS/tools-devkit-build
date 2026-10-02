using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Reads the packages restored for a set of projects and when each of them was packed.
/// </summary>
internal static class ResolvedPackageReader
{
    public static IReadOnlyList<ResolvedPackage> Read(IEnumerable<string> projectDirectories, TaskLoggingHelper log)
    {
        var packages = new Dictionary<string, ResolvedPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectDirectory in projectDirectories)
        {
            var assetsFile = Path.Combine(projectDirectory, "obj", "project.assets.json");
            if (!File.Exists(assetsFile))
            {
                log.LogMessage(MessageImportance.Low, $"No restore output at {assetsFile}; its PackageReferences are not considered for the version.");
                continue;
            }

            try
            {
                foreach (var package in ReadAssetsFile(assetsFile, log))
                {
                    packages.TryAdd(package.Key, package);
                }
            }
            catch (Exception ex)
            {
                log.LogMessage(MessageImportance.High, $"Could not read packages from {assetsFile}: {ex.Message}");
            }
        }

        return packages.Values.ToList();
    }

    private static IEnumerable<ResolvedPackage> ReadAssetsFile(string assetsFile, TaskLoggingHelper log)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var root = document.RootElement;

        var packageFolders = root.TryGetProperty("packageFolders", out var folders)
            ? folders.EnumerateObject().Select(folder => folder.Name).ToList()
            : new List<string>();

        if (!root.TryGetProperty("libraries", out var libraries)) yield break;

        foreach (var library in libraries.EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package") continue;

            var nameAndVersion = library.Name.Split('/');
            var packagePath = library.Value.GetProperty("path").GetString();
            var packedAt = FindPackTime(packageFolders, packagePath);
            if (packedAt == null)
            {
                log.LogMessage(MessageImportance.Low, $"Package {library.Name}: pack time not found, not considered for the version.");
                continue;
            }

            yield return new ResolvedPackage(nameAndVersion[0], nameAndVersion[1], packedAt.Value);
        }
    }

    private static DateTime? FindPackTime(IEnumerable<string> packageFolders, string packagePath)
    {
        foreach (var packageFolder in packageFolders)
        {
            var packageDirectory = Path.Combine(packageFolder, packagePath);
            if (!Directory.Exists(packageDirectory)) continue;

            var nupkg = Directory.GetFiles(packageDirectory, "*.nupkg").FirstOrDefault();
            if (nupkg != null) return ReadPackTime(nupkg);
        }

        return null;
    }

    // Packers rewrite the .nuspec on every pack, so its entry time is the pack time; other entries
    // can keep the timestamps of the source files.
    private static DateTime? ReadPackTime(string nupkg)
    {
        using var archive = ZipFile.OpenRead(nupkg);
        var nuspec = archive.Entries.FirstOrDefault(entry =>
            !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        if (nuspec == null) return null;

        // Zip stores the packer's wall-clock time without a time zone, so it is taken as-is.
        return nuspec.LastWriteTime.DateTime;
    }
}
