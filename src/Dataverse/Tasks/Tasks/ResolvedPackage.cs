using System;

/// <summary>
/// A package restored for a project, with the time it was packed.
/// </summary>
internal sealed class ResolvedPackage
{
    public ResolvedPackage(string id, string version, DateTime packedAt)
    {
        Id = id;
        Version = version;
        PackedAt = packedAt;
    }

    public string Id { get; }

    public string Version { get; }

    public DateTime PackedAt { get; }

    public string Key => $"{Id}/{Version}";
}
