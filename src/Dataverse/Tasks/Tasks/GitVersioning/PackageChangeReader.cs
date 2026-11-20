using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Reads the packages restored for a set of projects and when each of them last changed.
/// </summary>
internal static class PackageChangeReader
{
    public static IReadOnlyList<PackageChange> Read(IEnumerable<string> projectDirectories, string settingsRoot, TaskLoggingHelper log)
    {
        var restored = new Dictionary<string, RestoredPackage>(StringComparer.OrdinalIgnoreCase);

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
                foreach (var package in ReadAssetsFile(assetsFile))
                {
                    if (restored.TryGetValue(package.Key, out var known))
                    {
                        known.IsFloating |= package.IsFloating;
                    }
                    else
                    {
                        restored.Add(package.Key, package);
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogMessage(MessageImportance.High, $"Could not read packages from {assetsFile}: {ex.Message}");
            }
        }

        PackagePublishTimeReader publishTimeReader = null;
        try
        {
            var packages = new List<PackageChange>();
            foreach (var package in restored.Values)
            {
                // A fixed version is published before the commit that pins it, so only floating ones are worth a feed request.
                if (package.IsFloating)
                {
                    var signedAt = package.Nupkg == null ? null : ReadRepositorySignatureTime(package, log);
                    if (signedAt != null)
                    {
                        packages.Add(new PackageChange(package.Id, package.Version, signedAt.Value, "repository-signed"));
                        continue;
                    }

                    if (package.Source != null)
                    {
                        publishTimeReader ??= new PackagePublishTimeReader(settingsRoot, log);
                        var publishedAt = publishTimeReader.GetPublishedAt(package.Id, package.Version, package.Source);
                        if (publishedAt != null)
                        {
                            packages.Add(new PackageChange(package.Id, package.Version, publishedAt.Value, "published"));
                            continue;
                        }
                    }
                }

                var packedAt = package.Nupkg == null ? null : ReadPackTime(package.Nupkg);
                if (packedAt == null)
                {
                    log.LogMessage(MessageImportance.Low, $"Package {package.Key}: change time not found, not considered for the version.");
                    continue;
                }

                packages.Add(new PackageChange(package.Id, package.Version, packedAt.Value, "packed"));
            }

            return packages;
        }
        finally
        {
            publishTimeReader?.Dispose();
        }
    }

    private static IEnumerable<RestoredPackage> ReadAssetsFile(string assetsFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var root = document.RootElement;

        var packageFolders = root.TryGetProperty("packageFolders", out var folders)
            ? folders.EnumerateObject().Select(folder => folder.Name).ToList()
            : new List<string>();
        var floatingIds = ReadFloatingDependencyIds(root);

        if (!root.TryGetProperty("libraries", out var libraries)) yield break;

        foreach (var library in libraries.EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package") continue;

            var nameAndVersion = library.Name.Split('/');
            var packageDirectory = packageFolders
                .Select(folder => Path.Combine(folder, library.Value.GetProperty("path").GetString()))
                .FirstOrDefault(Directory.Exists);

            yield return new RestoredPackage
            {
                Id = nameAndVersion[0],
                Version = nameAndVersion[1],
                IsFloating = floatingIds.Contains(nameAndVersion[0]),
                Nupkg = packageDirectory == null ? null : Directory.GetFiles(packageDirectory, "*.nupkg").FirstOrDefault(),
                Source = packageDirectory == null ? null : ReadSource(packageDirectory),
            };
        }
    }

    private static HashSet<string> ReadFloatingDependencyIds(JsonElement root)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("project", out var project) || !project.TryGetProperty("frameworks", out var frameworks)) return ids;

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("dependencies", out var dependencies)) continue;

            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (dependency.Value.TryGetProperty("version", out var range) && range.GetString().Contains('*'))
                {
                    ids.Add(dependency.Name);
                }
            }
        }

        return ids;
    }

    private static DateTime? ReadRepositorySignatureTime(RestoredPackage package, TaskLoggingHelper log)
    {
        try
        {
            return RepositorySignatureReader.GetSignedAt(package.Nupkg);
        }
        catch (Exception ex)
        {
            log.LogMessage(MessageImportance.Low, $"Package {package.Key}: could not read its signature: {ex.Message}");
            return null;
        }
    }

    // Restore records the feed each package came from next to the .nupkg.
    private static string ReadSource(string packageDirectory)
    {
        var metadataFile = Path.Combine(packageDirectory, ".nupkg.metadata");
        if (!File.Exists(metadataFile)) return null;

        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataFile));
        return metadata.RootElement.TryGetProperty("source", out var source) ? source.GetString() : null;
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
