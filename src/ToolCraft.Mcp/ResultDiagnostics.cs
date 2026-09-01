using System.Text.Json.Serialization;

namespace ToolCraft.Mcp;

/// <summary>
/// Explains how a result came to be: what happened, which defaults were applied,
/// what was cut, and what was wrong. An empty or truncated result without this
/// explanation reads to an agent like "there is no data", which is how weak agents
/// end up asserting false negatives.
/// </summary>
public sealed class ResultDiagnostics
{
    /// <summary>The overall outcome of the call.</summary>
    [JsonPropertyName("status")]
    public ToolOutcome Status { get; init; } = ToolOutcome.Ok;

    /// <summary>
    /// One plain sentence explaining why the result is empty, truncated, or failed.
    /// Null when the status is ok and nothing needs explaining.
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>
    /// Defaults the tool applied because the caller left parameters unset, for example
    /// "time range defaulted to the last 2 hours". Listing them tells the agent that a
    /// narrow result may be a consequence of the defaults, not of the data.
    /// </summary>
    [JsonPropertyName("applied_defaults")]
    public IReadOnlyList<string> AppliedDefaults { get; init; } = [];

    /// <summary>Details of what was cut, when the result was truncated. Null otherwise.</summary>
    [JsonPropertyName("truncation")]
    public TruncationInfo? Truncation { get; init; }

    /// <summary>Corrective errors, when parameters were rejected or execution failed. Empty otherwise.</summary>
    [JsonPropertyName("errors")]
    public IReadOnlyList<CorrectiveError> Errors { get; init; } = [];
}

/// <summary>
/// States exactly what a truncated result dropped and how to narrow the query.
/// A silent cut reads as "this is everything", which is how agents draw wrong
/// conclusions from partial data.
/// </summary>
public sealed class TruncationInfo
{
    /// <summary>How many items the result contains.</summary>
    [JsonPropertyName("returned")]
    public required int Returned { get; init; }

    /// <summary>How many items matched in total before the cut.</summary>
    [JsonPropertyName("total_matched")]
    public required int TotalMatched { get; init; }

    /// <summary>How many matched items were dropped from the result.</summary>
    [JsonPropertyName("dropped")]
    public required int Dropped { get; init; }

    /// <summary>Concrete ways to narrow the query so the full match set fits, for example "filter by service" or "shorten the time range".</summary>
    [JsonPropertyName("how_to_narrow")]
    public IReadOnlyList<string> HowToNarrow { get; init; } = [];
}
