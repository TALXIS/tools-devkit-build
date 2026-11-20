using System;
using System.Linq;
using System.Threading;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

/// <summary>
/// Reads when a repository signed a package, which for nuget.org is the moment it accepted the package.
/// </summary>
internal static class RepositorySignatureReader
{
    public static DateTime? GetSignedAt(string nupkg)
    {
        using var reader = new PackageArchiveReader(nupkg);
        if (!reader.IsSignedAsync(CancellationToken.None).GetAwaiter().GetResult()) return null;

        var primary = reader.GetPrimarySignatureAsync(CancellationToken.None).GetAwaiter().GetResult();
        // The repository signs either as the primary signature or as a countersignature on the author's one.
        Signature repositorySignature = primary.Type == SignatureType.Repository
            ? primary
            : RepositoryCountersignature.GetRepositoryCountersignature(primary);

        return repositorySignature?.Timestamps.FirstOrDefault()?.GeneralizedTime.UtcDateTime;
    }
}
