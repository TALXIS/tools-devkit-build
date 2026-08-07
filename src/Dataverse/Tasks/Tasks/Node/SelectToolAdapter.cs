using System;
using System.Globalization;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Selects one registered adapter candidate by priority without hardcoding adapter names in the
/// SDK core. Ecosystem-agnostic: used by NodeRestore today, reusable by PythonRestore etc.
/// </summary>
public sealed class SelectToolAdapter : Task
{
    [Required]
    public ITaskItem[] Candidates { get; set; } = Array.Empty<ITaskItem>();

    public string RequestedAdapter { get; set; } = string.Empty;

    [Output]
    public string AdapterName { get; private set; } = string.Empty;

    [Output]
    public string WorkspaceRoot { get; private set; } = string.Empty;

    public override bool Execute()
    {
        var duplicate = Candidates
            .GroupBy(candidate => candidate.ItemSpec, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            Log.LogError(
                $"Adapter '{duplicate.Key}' was registered more than once: " +
                string.Join(", ", duplicate.Select(DescribeSource)));
            return false;
        }

        var eligible = string.IsNullOrWhiteSpace(RequestedAdapter)
            ? Candidates
            : Candidates.Where(candidate =>
                string.Equals(candidate.ItemSpec, RequestedAdapter, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (eligible.Length == 0)
        {
            var registeredText = Candidates.Length == 0
                ? "none"
                : string.Join(", ", Candidates.Select(candidate => candidate.ItemSpec));
            var requestedText = string.IsNullOrWhiteSpace(RequestedAdapter)
                ? "No registered adapter matched this project."
                : $"Requested adapter '{RequestedAdapter}' did not match this project.";
            Log.LogError($"{requestedText} Registered candidates: {registeredText}.");
            return false;
        }

        var ranked = eligible
            .Select(candidate => new
            {
                Candidate = candidate,
                Priority = ParsePriority(candidate)
            })
            .OrderByDescending(entry => entry.Priority)
            .ToArray();
        if (Log.HasLoggedErrors)
        {
            return false;
        }

        var winner = ranked[0];
        var tied = ranked.Where(entry => entry.Priority == winner.Priority).ToArray();
        if (tied.Length > 1)
        {
            Log.LogError(
                $"Multiple adapters matched with priority {winner.Priority}: " +
                string.Join(", ", tied.Select(entry => $"{entry.Candidate.ItemSpec} ({DescribeSource(entry.Candidate)})")));
            return false;
        }

        WorkspaceRoot = winner.Candidate.GetMetadata("WorkspaceRoot");
        if (string.IsNullOrWhiteSpace(WorkspaceRoot))
        {
            Log.LogError($"Adapter '{winner.Candidate.ItemSpec}' did not provide required WorkspaceRoot metadata.");
            return false;
        }

        AdapterName = winner.Candidate.ItemSpec;
        return true;
    }

    private int ParsePriority(ITaskItem candidate)
    {
        var raw = candidate.GetMetadata("Priority");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priority))
        {
            return priority;
        }

        Log.LogError($"Adapter '{candidate.ItemSpec}' has invalid Priority '{raw}' ({DescribeSource(candidate)}).");
        return 0;
    }

    private static string DescribeSource(ITaskItem candidate)
    {
        var source = candidate.GetMetadata("Source");
        return string.IsNullOrWhiteSpace(source) ? "unknown source" : source;
    }
}
