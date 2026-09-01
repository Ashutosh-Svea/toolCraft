namespace ToolCraft.Mcp;

/// <summary>
/// A closed time interval used for telemetry-style queries.
/// </summary>
/// <param name="From">Inclusive start of the window.</param>
/// <param name="To">Inclusive end of the window.</param>
public readonly record struct TimeWindow(DateTimeOffset From, DateTimeOffset To)
{
    /// <summary>The length of the window.</summary>
    public TimeSpan Length => To - From;

    /// <summary>Whether <paramref name="instant"/> falls inside the window, boundaries included.</summary>
    public bool Contains(DateTimeOffset instant) => instant >= From && instant <= To;

    /// <summary>Whether this window and <paramref name="other"/> share any instant.</summary>
    public bool Overlaps(TimeWindow other) => From <= other.To && other.From <= To;
}

/// <summary>
/// Resolves unset parameters to bounded defaults and records every substitution,
/// so a tool is cheap by default and the agent can see which defaults shaped the result.
/// </summary>
public static class SafeDefaults
{
    /// <summary>
    /// Resolves a requested page size against a default and a hard maximum. Unset falls
    /// back to the default; values above the maximum are reduced; values below one are
    /// raised to one. Every substitution is appended to <paramref name="appliedDefaults"/>.
    /// </summary>
    /// <param name="requested">The caller's requested limit, or null when unset.</param>
    /// <param name="defaultLimit">The limit used when the caller left it unset.</param>
    /// <param name="maxLimit">The hard ceiling a caller cannot exceed.</param>
    /// <param name="appliedDefaults">Collector for human-readable notes about applied substitutions.</param>
    public static int ClampLimit(int? requested, int defaultLimit, int maxLimit, ICollection<string> appliedDefaults)
    {
        ArgumentNullException.ThrowIfNull(appliedDefaults);
        ArgumentOutOfRangeException.ThrowIfLessThan(defaultLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLimit, defaultLimit);

        if (requested is null)
        {
            appliedDefaults.Add($"limit defaulted to {defaultLimit}");
            return defaultLimit;
        }

        if (requested > maxLimit)
        {
            appliedDefaults.Add($"limit reduced from {requested} to the maximum of {maxLimit}");
            return maxLimit;
        }

        if (requested < 1)
        {
            appliedDefaults.Add($"limit raised from {requested} to 1");
            return 1;
        }

        return requested.Value;
    }

    /// <summary>
    /// Resolves an optional time range to a bounded window. Unset boundaries default to
    /// a recent lookback ending now; a reversed range is swapped rather than rejected.
    /// Every substitution is appended to <paramref name="appliedDefaults"/>.
    /// </summary>
    /// <param name="from">Requested start, or null to default to <paramref name="now"/> minus <paramref name="defaultLookback"/>.</param>
    /// <param name="to">Requested end, or null to default to <paramref name="now"/>.</param>
    /// <param name="defaultLookback">How far back the window reaches when the start is unset.</param>
    /// <param name="now">The current instant, passed in so callers and tests control the clock.</param>
    /// <param name="appliedDefaults">Collector for human-readable notes about applied substitutions.</param>
    public static TimeWindow ResolveTimeWindow(
        DateTimeOffset? from,
        DateTimeOffset? to,
        TimeSpan defaultLookback,
        DateTimeOffset now,
        ICollection<string> appliedDefaults)
    {
        ArgumentNullException.ThrowIfNull(appliedDefaults);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(defaultLookback, TimeSpan.Zero);

        var resolvedTo = to ?? now;
        if (to is null)
        {
            appliedDefaults.Add("end of time range defaulted to now");
        }

        var resolvedFrom = from ?? resolvedTo - defaultLookback;
        if (from is null)
        {
            appliedDefaults.Add($"start of time range defaulted to {FormatLookback(defaultLookback)} before the end");
        }

        if (resolvedFrom > resolvedTo)
        {
            (resolvedFrom, resolvedTo) = (resolvedTo, resolvedFrom);
            appliedDefaults.Add("time range was reversed and has been swapped");
        }

        return new TimeWindow(resolvedFrom, resolvedTo);
    }

    private static string FormatLookback(TimeSpan lookback)
        => lookback switch
        {
            { TotalHours: >= 1 } when lookback.TotalHours == Math.Floor(lookback.TotalHours)
                => $"{(int)lookback.TotalHours} hours",
            { TotalMinutes: >= 1 } when lookback.TotalMinutes == Math.Floor(lookback.TotalMinutes)
                => $"{(int)lookback.TotalMinutes} minutes",
            _ => lookback.ToString(),
        };
}
