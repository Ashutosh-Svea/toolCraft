using System.Text.Json.Serialization;

namespace ToolCraft.Mcp;

/// <summary>
/// The result envelope every tool returns: the data itself, one quotable sentence,
/// diagnostics that explain surprises, and suggested next steps. The envelope gives
/// an agent everything it needs to report honestly and decide what to do next,
/// instead of guessing at the meaning of a bare list.
/// </summary>
/// <typeparam name="T">The payload type of the tool.</typeparam>
public sealed class ToolResult<T>
{
    /// <summary>The payload. May be null or empty when nothing matched; diagnostics say why.</summary>
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    /// <summary>One plain sentence an agent can quote verbatim, for example "Found 3 open tickets for service auth in the last 2 hours."</summary>
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>Why the result looks the way it does: status, applied defaults, truncation, errors.</summary>
    [JsonPropertyName("diagnostics")]
    public ResultDiagnostics Diagnostics { get; init; } = new();

    /// <summary>Concrete follow-up calls or refinements, most useful first. Empty when nothing is worth suggesting.</summary>
    [JsonPropertyName("suggested_next_steps")]
    public IReadOnlyList<string> SuggestedNextSteps { get; init; } = [];
}

/// <summary>
/// Factory methods that build <see cref="ToolResult{T}"/> envelopes with consistent
/// status, reason, and truncation wiring, so tools cannot forget the explanation half
/// of the contract.
/// </summary>
public static class ToolResult
{
    /// <summary>
    /// Builds a successful result. When <paramref name="truncation"/> is set, the status
    /// becomes <see cref="ToolOutcome.Truncated"/> and the reason states the cut.
    /// </summary>
    /// <param name="data">The payload.</param>
    /// <param name="summary">One quotable sentence describing the result.</param>
    /// <param name="truncation">Truncation details when the result was cut, from <see cref="Truncation.Apply{T}"/>.</param>
    /// <param name="appliedDefaults">Defaults the tool applied to unset parameters.</param>
    /// <param name="nextSteps">Concrete follow-up suggestions, most useful first.</param>
    public static ToolResult<T> Ok<T>(
        T data,
        string summary,
        TruncationInfo? truncation = null,
        IReadOnlyList<string>? appliedDefaults = null,
        IReadOnlyList<string>? nextSteps = null)
        => new()
        {
            Data = data,
            Summary = summary,
            Diagnostics = new ResultDiagnostics
            {
                Status = truncation is null ? ToolOutcome.Ok : ToolOutcome.Truncated,
                Reason = truncation is null
                    ? null
                    : $"{truncation.Dropped} of {truncation.TotalMatched} matching items were dropped to stay within the limit",
                AppliedDefaults = appliedDefaults ?? [],
                Truncation = truncation,
            },
            SuggestedNextSteps = nextSteps ?? [],
        };

    /// <summary>
    /// Builds an empty result whose diagnostics say why nothing matched. The reason is
    /// required: an unexplained empty result is indistinguishable from a broken tool.
    /// </summary>
    /// <param name="data">The empty payload, for example an empty list.</param>
    /// <param name="summary">One quotable sentence, for example "No telemetry matched service auth in the last 2 hours."</param>
    /// <param name="reason">Why the result is empty, for example "the default time range only covers the last 2 hours".</param>
    /// <param name="appliedDefaults">Defaults the tool applied to unset parameters.</param>
    /// <param name="nextSteps">Concrete ways to widen or redirect the query.</param>
    public static ToolResult<T> Empty<T>(
        T data,
        string summary,
        string reason,
        IReadOnlyList<string>? appliedDefaults = null,
        IReadOnlyList<string>? nextSteps = null)
        => new()
        {
            Data = data,
            Summary = summary,
            Diagnostics = new ResultDiagnostics
            {
                Status = ToolOutcome.Empty,
                Reason = reason,
                AppliedDefaults = appliedDefaults ?? [],
            },
            SuggestedNextSteps = nextSteps ?? [],
        };

    /// <summary>
    /// Builds a rejection carrying corrective errors. The summary quotes the first error
    /// so an agent that only reads the summary still learns what to fix.
    /// </summary>
    /// <param name="errors">The corrective errors, most important first. Must not be empty.</param>
    /// <param name="nextSteps">Optional follow-up suggestions beyond the per-error guidance.</param>
    public static ToolResult<T> Invalid<T>(
        IReadOnlyList<CorrectiveError> errors,
        IReadOnlyList<string>? nextSteps = null)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one corrective error is required.", nameof(errors));
        }

        return new ToolResult<T>
        {
            Data = default,
            Summary = $"Call rejected: {errors[0].Message}.",
            Diagnostics = new ResultDiagnostics
            {
                Status = ToolOutcome.Invalid,
                Reason = errors[0].Message,
                Errors = errors,
            },
            SuggestedNextSteps = nextSteps ?? errors
                .Where(e => e.Guidance is not null)
                .Select(e => e.Guidance!)
                .ToArray(),
        };
    }

    /// <summary>
    /// Builds a failure envelope for a tool that failed while executing. The message must
    /// already be safe to show to clients: no exception text, paths, queries, or other
    /// internals, because everything in the envelope lands in agent context.
    /// <see cref="ToolRunner"/> passes a stable message with a reference id and routes the
    /// exception detail to the audit entry instead.
    /// </summary>
    /// <param name="message">A client-safe, single-sentence description of the failure.</param>
    public static ToolResult<T> Failed<T>(string message)
        => new()
        {
            Data = default,
            Summary = $"The tool failed: {message}.",
            Diagnostics = new ResultDiagnostics
            {
                Status = ToolOutcome.Error,
                Reason = message,
                Errors =
                [
                    new CorrectiveError
                    {
                        Code = CorrectiveError.ToolFailureCode,
                        Message = message,
                        Guidance = "Retry once; if it fails again, report the failure instead of retrying further.",
                    },
                ],
            },
        };
}
