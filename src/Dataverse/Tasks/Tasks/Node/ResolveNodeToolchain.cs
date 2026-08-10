using System;
using System.Globalization;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

/// <summary>
/// Resolves the package manager and orchestrator that govern a Node project.
/// </summary>
public sealed class ResolveNodeToolchain : Task
{
    [Required]
    public ITaskItem[] PackageManagerCandidates { get; set; } = Array.Empty<ITaskItem>();

    public ITaskItem[] OrchestratorCandidates { get; set; } = Array.Empty<ITaskItem>();

    public string RequestedPackageManager { get; set; } = string.Empty;

    public string RequestedOrchestrator { get; set; } = string.Empty;

    [Output]
    public ITaskItem PackageManager { get; private set; }

    [Output]
    public ITaskItem Orchestrator { get; private set; }

    public override bool Execute()
    {
        PackageManager = SelectCandidate(
            PackageManagerCandidates,
            RequestedPackageManager,
            "Node package manager",
            required: !IsNone(RequestedPackageManager));

        Orchestrator = SelectCandidate(
            OrchestratorCandidates,
            RequestedOrchestrator,
            "Node orchestrator",
            required: false);

        return !Log.HasLoggedErrors;
    }

    private ITaskItem SelectCandidate(
        ITaskItem[] candidates,
        string requestedName,
        string roleName,
        bool required)
    {
        if (IsNone(requestedName))
        {
            return null;
        }

        var duplicate = candidates
            .GroupBy(candidate => candidate.ItemSpec, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            Log.LogError(
                $"{roleName} '{duplicate.Key}' was registered more than once: " +
                string.Join(", ", duplicate.Select(DescribeSource)));
            return null;
        }

        var eligible = string.IsNullOrWhiteSpace(requestedName)
            ? candidates
            : candidates.Where(candidate =>
                string.Equals(candidate.ItemSpec, requestedName, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (eligible.Length == 0)
        {
            if (!required && string.IsNullOrWhiteSpace(requestedName))
            {
                return null;
            }

            var registeredText = candidates.Length == 0
                ? "none"
                : string.Join(", ", candidates.Select(candidate => candidate.ItemSpec));
            var requestedText = string.IsNullOrWhiteSpace(requestedName)
                ? $"No registered {roleName.ToLowerInvariant()} matched this project."
                : $"Requested {roleName.ToLowerInvariant()} '{requestedName}' did not match this project.";
            Log.LogError($"{requestedText} Registered candidates: {registeredText}.");
            return null;
        }

        var ranked = eligible
            .Select(candidate => new
            {
                Candidate = candidate,
                Priority = ParsePriority(candidate, roleName)
            })
            .OrderByDescending(entry => entry.Priority)
            .ToArray();
        if (Log.HasLoggedErrors)
        {
            return null;
        }

        var winner = ranked[0];
        var tied = ranked.Where(entry => entry.Priority == winner.Priority).ToArray();
        if (tied.Length > 1)
        {
            Log.LogError(
                $"Multiple {roleName.ToLowerInvariant()} candidates matched with priority {winner.Priority}: " +
                string.Join(", ", tied.Select(entry => $"{entry.Candidate.ItemSpec} ({DescribeSource(entry.Candidate)})")));
            return null;
        }

        if (string.IsNullOrWhiteSpace(winner.Candidate.GetMetadata("RootPath")))
        {
            Log.LogError(
                $"{roleName} '{winner.Candidate.ItemSpec}' did not provide required RootPath metadata " +
                $"({DescribeSource(winner.Candidate)}).");
            return null;
        }

        return winner.Candidate;
    }

    private int ParsePriority(ITaskItem candidate, string roleName)
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

        Log.LogError(
            $"{roleName} '{candidate.ItemSpec}' has invalid Priority '{raw}' ({DescribeSource(candidate)}).");
        return 0;
    }

    private static bool IsNone(string value) =>
        string.Equals(value, "None", StringComparison.OrdinalIgnoreCase);

    private static string DescribeSource(ITaskItem candidate)
    {
        var source = candidate.GetMetadata("Source");
        return string.IsNullOrWhiteSpace(source) ? "unknown source" : source;
    }
}
