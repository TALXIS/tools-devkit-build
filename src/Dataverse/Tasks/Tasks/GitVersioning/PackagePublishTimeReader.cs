using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Credentials;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

/// <summary>
/// Asks the feed a package was restored from when that package version was published.
/// </summary>
internal sealed class PackagePublishTimeReader : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly TaskLoggingHelper _log;
    private readonly IReadOnlyList<PackageSource> _configuredSources;
    private readonly Dictionary<string, PackageMetadataResource> _resources = new Dictionary<string, PackageMetadataResource>(StringComparer.OrdinalIgnoreCase);
    private readonly SourceCacheContext _cache = new SourceCacheContext();

    public PackagePublishTimeReader(string settingsRoot, TaskLoggingHelper log)
    {
        _log = log;
        _configuredSources = new PackageSourceProvider(Settings.LoadDefaultSettings(settingsRoot)).LoadPackageSources().ToList();
        // Same nuget.config credentials and credential providers as restore, so a feed restore could read needs no extra login.
        DefaultCredentialServiceUtility.SetupDefaultCredentialService(NullLogger.Instance, nonInteractive: true);
    }

    public DateTime? GetPublishedAt(string id, string version, string source)
    {
        var packageSource = _configuredSources.FirstOrDefault(s => string.Equals(s.Source, source, StringComparison.OrdinalIgnoreCase))
            ?? new PackageSource(source);
        if (!packageSource.IsHttp) return null;

        try
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            var resource = GetMetadataResource(packageSource, timeout.Token);
            var metadata = resource
                .GetMetadataAsync(new PackageIdentity(id, NuGetVersion.Parse(version)), _cache, NullLogger.Instance, timeout.Token)
                .GetAwaiter().GetResult();

            // Unlisted packages report 1900-01-01 instead of their publish time.
            var published = metadata?.Published;
            return published == null || published.Value.Year <= 1900 ? null : published.Value.UtcDateTime;
        }
        catch (Exception ex)
        {
            _log.LogMessage(MessageImportance.High, $"Could not get the publish time of {id} {version} from {source}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    private PackageMetadataResource GetMetadataResource(PackageSource packageSource, CancellationToken cancellationToken)
    {
        if (!_resources.TryGetValue(packageSource.Source, out var resource))
        {
            resource = Repository.Factory.GetCoreV3(packageSource)
                .GetResourceAsync<PackageMetadataResource>(cancellationToken)
                .GetAwaiter().GetResult();
            _resources[packageSource.Source] = resource;
        }

        return resource;
    }
}
