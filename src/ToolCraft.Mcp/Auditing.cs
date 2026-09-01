using Serilog;

namespace ToolCraft.Mcp;

/// <summary>
/// One completed tool call, as recorded by an <see cref="IToolAuditor"/>.
/// The entry is what makes an agent session reconstructible afterwards: which tools
/// ran, with what arguments, how long they took, and how they ended.
/// </summary>
public sealed record ToolAuditEntry
{
    /// <summary>The tool name as exposed to clients, for example "search_tickets".</summary>
    public required string Tool { get; init; }

    /// <summary>The calling client, for example "claude-desktop/1.0", or "unknown".</summary>
    public required string Caller { get; init; }

    /// <summary>The call arguments serialized as JSON, or null when unavailable.</summary>
    public string? ArgumentsJson { get; init; }

    /// <summary>When the call started, in UTC.</summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>How long the call took end to end.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>How the call ended.</summary>
    public required ToolOutcome Outcome { get; init; }

    /// <summary>The summary sentence the envelope returned, or null when the call produced no envelope.</summary>
    public string? Summary { get; init; }

    /// <summary>
    /// The exception type and message when the outcome is <see cref="ToolOutcome.Error"/>,
    /// prefixed with the reference id the client received. This detail exists only in the
    /// audit trail; the client envelope never carries it.
    /// </summary>
    public string? FailureDetail { get; init; }
}

/// <summary>
/// Pluggable per-call audit hook. Implementations must be fast and must not throw;
/// auditing is observability, not control flow.
/// </summary>
public interface IToolAuditor
{
    /// <summary>Records one completed tool call.</summary>
    /// <param name="entry">The completed call.</param>
    void Record(ToolAuditEntry entry);
}

/// <summary>
/// Audit hook that writes each call as one structured Serilog event, so agent runs
/// can be filtered and aggregated by tool, caller, outcome, and duration.
/// </summary>
public sealed class SerilogToolAuditor : IToolAuditor
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates an auditor writing to <paramref name="logger"/>, or to the Serilog
    /// static logger when omitted.
    /// </summary>
    /// <param name="logger">The Serilog logger to write audit events to.</param>
    public SerilogToolAuditor(ILogger? logger = null)
        => _logger = (logger ?? Log.Logger).ForContext<SerilogToolAuditor>();

    /// <inheritdoc />
    public void Record(ToolAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _logger.Information(
            "Tool {Tool} called by {Caller} finished {Outcome} in {DurationMs} ms; arguments {Arguments}; summary {Summary}; failure {FailureDetail}",
            entry.Tool,
            entry.Caller,
            entry.Outcome,
            entry.Duration.TotalMilliseconds,
            entry.ArgumentsJson,
            entry.Summary,
            entry.FailureDetail);
    }
}
