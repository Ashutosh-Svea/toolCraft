namespace ToolCraft.Mcp;

/// <summary>
/// Applies a size limit to a match set and records exactly what the cut dropped,
/// so no tool ever returns a silently shortened list.
/// </summary>
public static class Truncation
{
    /// <summary>
    /// Returns at most <paramref name="limit"/> items. When the match set is larger,
    /// the second tuple element describes the cut; when everything fits, it is null.
    /// </summary>
    /// <param name="matches">All matching items, already ordered most relevant first.</param>
    /// <param name="limit">The maximum number of items to return.</param>
    /// <param name="howToNarrow">Concrete ways the caller can narrow the query, for example "filter by service".</param>
    public static (IReadOnlyList<T> Items, TruncationInfo? Info) Apply<T>(
        IReadOnlyList<T> matches,
        int limit,
        params string[] howToNarrow)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        if (matches.Count <= limit)
        {
            return (matches, null);
        }

        var items = matches.Take(limit).ToArray();
        var info = new TruncationInfo
        {
            Returned = items.Length,
            TotalMatched = matches.Count,
            Dropped = matches.Count - items.Length,
            HowToNarrow = howToNarrow,
        };
        return (items, info);
    }
}
