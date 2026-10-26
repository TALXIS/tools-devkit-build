using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Combines the entityImportOrder lists of several CMT packages into one order that keeps the relative order of every list.
/// </summary>
public static class CmtImportOrderMerger
{
    /// <summary>
    /// Returns the merged order, or an empty list with a message in <paramref name="errors"/> when the lists contradict each other.
    /// <paramref name="lookups"/> (entity name to the entities it looks up) only breaks ties: where the lists leave the order open,
    /// an entity goes after the entities it looks up.
    /// </summary>
    public static IReadOnlyList<string> Merge(
        IReadOnlyList<(string Source, IReadOnlyList<string> Order)> orders,
        ICollection<string> errors,
        IReadOnlyDictionary<string, IReadOnlyList<string>> lookups = null)
    {
        var firstSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        var successors = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var predecessorCount = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (source, order) in orders)
        {
            var names = order.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();
            foreach (var name in names)
            {
                if (firstSeen.ContainsKey(name)) continue;
                firstSeen.Add(name, firstSeen.Count);
                successors.Add(name, new Dictionary<string, string>(StringComparer.Ordinal));
                predecessorCount.Add(name, 0);
            }

            for (var i = 1; i < names.Count; i++)
            {
                if (successors[names[i - 1]].ContainsKey(names[i])) continue;
                successors[names[i - 1]].Add(names[i], source);
                predecessorCount[names[i]]++;
            }
        }

        // Among the entities that are free to go next, one whose lookups are already placed wins, then the one seen first,
        // so the lists add no rule that overrides a lookup and compatible lists keep their first-seen layout.
        var merged = new List<string>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var ready = firstSeen.Keys.Where(n => predecessorCount[n] == 0).ToList();
        while (ready.Count > 0)
        {
            var next = ready.OrderBy(n => WaitsForLookup(n, lookups, firstSeen, placed) ? 1 : 0).ThenBy(n => firstSeen[n]).First();
            ready.Remove(next);
            merged.Add(next);
            placed.Add(next);
            foreach (var successor in successors[next].Keys)
            {
                if (--predecessorCount[successor] == 0) ready.Add(successor);
            }
        }

        if (merged.Count == firstSeen.Count) return merged;

        errors.Add(DescribeConflict(FindCycle(successors, predecessorCount, firstSeen)));
        return Array.Empty<string>();
    }

    private static bool WaitsForLookup(string entity, IReadOnlyDictionary<string, IReadOnlyList<string>> lookups, Dictionary<string, int> firstSeen, HashSet<string> placed)
    {
        if (lookups == null || !lookups.TryGetValue(entity, out var targets)) return false;
        return targets.Any(t => t != entity && firstSeen.ContainsKey(t) && !placed.Contains(t));
    }

    private static List<(string From, string To, string Source)> FindCycle(
        Dictionary<string, Dictionary<string, string>> successors,
        Dictionary<string, int> predecessorCount,
        Dictionary<string, int> firstSeen)
    {
        // Every entity left unplaced still has an unplaced predecessor, so walking backwards from any of them must repeat an entity.
        var unplaced = new HashSet<string>(predecessorCount.Where(p => p.Value > 0).Select(p => p.Key), StringComparer.Ordinal);
        var predecessor = unplaced.ToDictionary(
            n => n,
            n => successors.Where(s => unplaced.Contains(s.Key) && s.Value.ContainsKey(n)).OrderBy(s => firstSeen[s.Key]).First().Key,
            StringComparer.Ordinal);

        var path = new List<string>();
        var current = unplaced.OrderBy(n => firstSeen[n]).First();
        while (!path.Contains(current))
        {
            path.Add(current);
            current = predecessor[current];
        }

        var cycle = path.Skip(path.IndexOf(current)).Reverse().ToList();
        return cycle.Select((from, i) =>
        {
            var to = cycle[(i + 1) % cycle.Count];
            return (from, to, successors[from][to]);
        }).ToList();
    }

    private static string DescribeConflict(IEnumerable<(string From, string To, string Source)> cycle)
    {
        var rules = cycle.Select(e => $"'{e.From}' before '{e.To}' in {e.Source}");
        return $"The entityImportOrder lists of the CMT packages contradict each other: {string.Join(", ", rules)}. Change one of the lists so that they agree.";
    }
}
