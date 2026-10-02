using System;

/// <summary>
/// A package restored for a project, with the time it last changed.
/// </summary>
internal sealed class ResolvedPackage
{
    public ResolvedPackage(string id, string version, DateTime changedAt, string changeKind)
    {
        Id = id;
        Version = version;
        ChangedAt = changedAt;
        ChangeKind = changeKind;
    }

    public string Id { get; }

    public string Version { get; }

    public DateTime ChangedAt { get; }

    /// <summary>
    /// "published" when the time comes from the feed, "packed" when it comes from the .nupkg.
    /// </summary>
    public string ChangeKind { get; }

    public string Key => $"{Id}/{Version}";
}
