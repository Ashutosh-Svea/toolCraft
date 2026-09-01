using System.Diagnostics;
using System.Text.Json;
using FluentValidation;

namespace ToolCraft.Mcp;

/// <summary>
/// Runs a tool body behind the full defensive pipeline: validate parameters into
/// corrective errors, shield the caller from raw exceptions, and audit every call
/// with its duration and outcome. Tools built on this cannot return a bare failure
/// and cannot skip the audit trail.
/// </summary>
public static class ToolRunner
{
    private static readonly JsonSerializerOptions ArgumentJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Executes one tool call. When <paramref name="validator"/> rejects the arguments,
    /// the body never runs and the caller receives corrective errors. When the validator
    /// or the body throws, the caller receives a safe failure envelope carrying only a
    /// generated reference id; the exception's type and message go to the audit entry,
    /// never to the client. Every path records exactly one audit entry when
    /// <paramref name="auditor"/> is set.
    /// </summary>
    /// <param name="toolName">The tool name as exposed to clients.</param>
    /// <param name="arguments">The parsed tool arguments.</param>
    /// <param name="execute">The tool body, invoked only with valid arguments.</param>
    /// <param name="validator">Optional FluentValidation validator for the arguments.</param>
    /// <param name="auditor">Optional per-call audit hook.</param>
    /// <param name="caller">The calling client, for the audit trail.</param>
    /// <param name="redactArguments">
    /// Optional projection applied to the arguments before they enter the audit entry.
    /// Use it to drop or mask sensitive fields, or return null to disable argument
    /// capture entirely. When omitted, the full arguments are serialized as JSON, which
    /// is appropriate only when the arguments are known not to carry sensitive data.
    /// </param>
    /// <param name="cancellationToken">Cancels the call. Cancellation propagates to the caller.</param>
    public static async Task<ToolResult<TData>> RunAsync<TArgs, TData>(
        string toolName,
        TArgs arguments,
        Func<TArgs, CancellationToken, Task<ToolResult<TData>>> execute,
        IValidator<TArgs>? validator = null,
        IToolAuditor? auditor = null,
        string caller = "unknown",
        Func<TArgs, string?>? redactArguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(execute);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (validator is not null)
            {
                var validation = await validator.ValidateAsync(arguments, cancellationToken).ConfigureAwait(false);
                if (!validation.IsValid)
                {
                    var invalid = ToolResult.Invalid<TData>(validation.ToCorrectiveErrors());
                    Audit(auditor, toolName, caller, arguments, redactArguments, startedAt, stopwatch.Elapsed, invalid.Diagnostics.Status, invalid.Summary);
                    return invalid;
                }
            }

            var result = await execute(arguments, cancellationToken).ConfigureAwait(false);
            Audit(auditor, toolName, caller, arguments, redactArguments, startedAt, stopwatch.Elapsed, result.Diagnostics.Status, result.Summary);
            return result;
        }
        catch (OperationCanceledException)
        {
            Audit(auditor, toolName, caller, arguments, redactArguments, startedAt, stopwatch.Elapsed, ToolOutcome.Canceled, null);
            throw;
        }
        catch (Exception exception)
        {
            // The client sees a stable message and a reference id only. Exception text
            // can carry paths, queries, connection strings, or other internals that do
            // not belong in agent context; the detail goes to the audit trail instead.
            var referenceId = Guid.NewGuid().ToString("N")[..8];
            var failed = ToolResult.Failed<TData>($"an unexpected error occurred (reference {referenceId})");
            Audit(
                auditor, toolName, caller, arguments, redactArguments, startedAt, stopwatch.Elapsed,
                ToolOutcome.Error, failed.Summary,
                failureDetail: $"reference {referenceId}: {exception.GetType().Name}: {exception.Message}");
            return failed;
        }
    }

    private static void Audit<TArgs>(
        IToolAuditor? auditor,
        string toolName,
        string caller,
        TArgs arguments,
        Func<TArgs, string?>? redactArguments,
        DateTimeOffset startedAt,
        TimeSpan duration,
        ToolOutcome outcome,
        string? summary,
        string? failureDetail = null)
    {
        if (auditor is null)
        {
            return;
        }

        // Auditing is observability. A broken auditor, redaction hook, or
        // unserializable argument type must never turn a successful tool call
        // into a failed one.
        try
        {
            auditor.Record(new ToolAuditEntry
            {
                Tool = toolName,
                Caller = caller,
                ArgumentsJson = redactArguments is not null
                    ? redactArguments(arguments)
                    : SerializeArguments(arguments),
                StartedAt = startedAt,
                Duration = duration,
                Outcome = outcome,
                Summary = summary,
                FailureDetail = failureDetail,
            });
        }
        catch
        {
            // Intentionally swallowed; see comment above.
        }
    }

    private static string? SerializeArguments<TArgs>(TArgs arguments)
    {
        try
        {
            return JsonSerializer.Serialize(arguments, ArgumentJsonOptions);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
