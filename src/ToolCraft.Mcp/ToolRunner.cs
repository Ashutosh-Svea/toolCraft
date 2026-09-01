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
    /// the body never runs and the caller receives corrective errors. When the body
    /// throws, the caller receives a safe failure envelope instead of an exception.
    /// Every path records exactly one audit entry when <paramref name="auditor"/> is set.
    /// </summary>
    /// <param name="toolName">The tool name as exposed to clients.</param>
    /// <param name="arguments">The parsed tool arguments.</param>
    /// <param name="execute">The tool body, invoked only with valid arguments.</param>
    /// <param name="validator">Optional FluentValidation validator for the arguments.</param>
    /// <param name="auditor">Optional per-call audit hook.</param>
    /// <param name="caller">The calling client, for the audit trail.</param>
    /// <param name="cancellationToken">Cancels the call. Cancellation propagates to the caller.</param>
    public static async Task<ToolResult<TData>> RunAsync<TArgs, TData>(
        string toolName,
        TArgs arguments,
        Func<TArgs, CancellationToken, Task<ToolResult<TData>>> execute,
        IValidator<TArgs>? validator = null,
        IToolAuditor? auditor = null,
        string caller = "unknown",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(execute);

        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        if (validator is not null)
        {
            var validation = await validator.ValidateAsync(arguments, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                var invalid = ToolResult.Invalid<TData>(validation.ToCorrectiveErrors());
                Audit(auditor, toolName, caller, arguments, startedAt, stopwatch.Elapsed, invalid.Diagnostics.Status, invalid.Summary);
                return invalid;
            }
        }

        try
        {
            var result = await execute(arguments, cancellationToken).ConfigureAwait(false);
            Audit(auditor, toolName, caller, arguments, startedAt, stopwatch.Elapsed, result.Diagnostics.Status, result.Summary);
            return result;
        }
        catch (OperationCanceledException)
        {
            Audit(auditor, toolName, caller, arguments, startedAt, stopwatch.Elapsed, ToolOutcome.Canceled, null);
            throw;
        }
        catch (Exception exception)
        {
            var failed = ToolResult.Failed<TData>(exception.Message);
            Audit(auditor, toolName, caller, arguments, startedAt, stopwatch.Elapsed, ToolOutcome.Error, failed.Summary);
            return failed;
        }
    }

    private static void Audit<TArgs>(
        IToolAuditor? auditor,
        string toolName,
        string caller,
        TArgs arguments,
        DateTimeOffset startedAt,
        TimeSpan duration,
        ToolOutcome outcome,
        string? summary)
    {
        if (auditor is null)
        {
            return;
        }

        // Auditing is observability. A broken auditor or unserializable argument type
        // must never turn a successful tool call into a failed one.
        try
        {
            auditor.Record(new ToolAuditEntry
            {
                Tool = toolName,
                Caller = caller,
                ArgumentsJson = SerializeArguments(arguments),
                StartedAt = startedAt,
                Duration = duration,
                Outcome = outcome,
                Summary = summary,
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
