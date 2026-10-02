/// <summary>
/// A package as restore recorded it, before its change time is known.
/// </summary>
internal sealed class RestoredPackage
{
    public string Id { get; init; }

    public string Version { get; init; }

    public bool IsFloating { get; set; }

    public string Nupkg { get; init; }

    public string Source { get; init; }

    public string Key => $"{Id}/{Version}";
}
