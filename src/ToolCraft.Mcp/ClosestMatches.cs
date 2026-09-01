namespace ToolCraft.Mcp;

/// <summary>
/// Ranks known values by similarity to a rejected input so corrective errors can
/// offer concrete replacements. Matching is case insensitive and favors, in order:
/// small edit distance, shared prefix, and substring containment.
/// </summary>
public static class ClosestMatches
{
    /// <summary>
    /// Returns up to <paramref name="max"/> candidates similar to <paramref name="value"/>,
    /// best match first. Returns an empty list when nothing is plausibly close, because a
    /// wrong suggestion is worse for an agent than no suggestion.
    /// </summary>
    /// <param name="value">The rejected input.</param>
    /// <param name="candidates">The set of accepted values.</param>
    /// <param name="max">Maximum number of suggestions to return.</param>
    public static IReadOnlyList<string> Find(string value, IEnumerable<string> candidates, int max = 3)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (string.IsNullOrWhiteSpace(value) || max <= 0)
        {
            return [];
        }

        var needle = value.Trim().ToLowerInvariant();
        var scored = new List<(string Candidate, int Score)>();

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hay = candidate.ToLowerInvariant();
            var distance = EditDistance(needle, hay);

            // Accept a candidate when the edit distance is small relative to its length,
            // or when one string contains the other (catches suffixes like "-svc").
            var threshold = Math.Max(2, Math.Min(needle.Length, hay.Length) / 2);
            var contained = needle.Contains(hay, StringComparison.Ordinal)
                || hay.Contains(needle, StringComparison.Ordinal);

            if (distance <= threshold || contained)
            {
                // Containment beats raw distance so "checkout-svc" ranks "checkout" high.
                var score = contained ? Math.Min(distance, 2) : distance;
                scored.Add((candidate, score));
            }
        }

        return scored
            .OrderBy(s => s.Score)
            .ThenBy(s => s.Candidate, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(s => s.Candidate)
            .ToArray();
    }

    private static int EditDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
