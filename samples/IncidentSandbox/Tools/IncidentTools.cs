using System.ComponentModel;
using IncidentSandbox.Data;
using ModelContextProtocol.Server;
using ToolCraft.Mcp;

namespace IncidentSandbox.Tools;

/// <summary>
/// The five sandbox tools. Every tool runs through <see cref="ToolRunner"/>, so
/// parameters are validated into corrective errors, results arrive in the
/// ToolCraft envelope, and each call lands in the audit log.
/// </summary>
[McpServerToolType]
public sealed class IncidentTools
{
    private const int DefaultTicketLimit = 10;
    private const int MaxTicketLimit = 50;
    private const int DefaultTelemetryLimit = 25;
    private const int MaxTelemetryLimit = 100;
    private const int MaxCorrelationEvents = 50;

    private static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(2);
    private static readonly TimeSpan OptInThreshold = TimeSpan.FromHours(6);
    private static readonly TimeSpan HealthWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan CorrelationPadding = TimeSpan.FromMinutes(30);

    private readonly SandboxDataset _data;
    private readonly IToolAuditor _auditor;
    private readonly SearchTicketsValidator _searchTicketsValidator;
    private readonly GetTicketValidator _getTicketValidator = new();
    private readonly QueryTelemetryValidator _queryTelemetryValidator;
    private readonly GetServiceHealthValidator _getServiceHealthValidator;
    private readonly CorrelateIncidentValidator _correlateIncidentValidator = new();

    /// <summary>Creates the tool set over the sandbox dataset.</summary>
    public IncidentTools(SandboxDataset data, IToolAuditor auditor)
    {
        _data = data;
        _auditor = auditor;
        _searchTicketsValidator = new SearchTicketsValidator(data);
        _queryTelemetryValidator = new QueryTelemetryValidator(data);
        _getServiceHealthValidator = new GetServiceHealthValidator(data);
    }

    [McpServerTool(Name = "search_tickets", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Searches support tickets by text, status, and service, newest first. Results are capped by default; the envelope says when they were cut and how to narrow the search.")]
    public Task<ToolResult<IReadOnlyList<Ticket>>> SearchTickets(
        McpServer server,
        [Description("Text matched against title and description, case insensitive. Omit to match every ticket.")] string? query = null,
        [Description("Filter by status: open, investigating, or resolved. Omit to include all statuses.")] string? status = null,
        [Description("Filter to tickets tagged with this service name.")] string? service = null,
        [Description("Maximum tickets to return. Default 10, maximum 50.")] int? limit = null,
        CancellationToken cancellationToken = default)
        => ToolRunner.RunAsync(
            "search_tickets",
            new SearchTicketsArgs(query, status, service, limit),
            (args, _) =>
            {
                var appliedDefaults = new List<string>();
                var effectiveLimit = SafeDefaults.ClampLimit(args.Limit, DefaultTicketLimit, MaxTicketLimit, appliedDefaults);

                var matches = _data.Tickets
                    .Where(t => args.Status is null || t.Status == TicketStatusNames.Parse(args.Status))
                    .Where(t => args.Service is null
                        || t.Services.Contains(args.Service, StringComparer.OrdinalIgnoreCase))
                    .Where(t => args.Query is null
                        || t.Title.Contains(args.Query, StringComparison.OrdinalIgnoreCase)
                        || t.Description.Contains(args.Query, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (matches.Length == 0)
                {
                    return Task.FromResult(ToolResult.Empty<IReadOnlyList<Ticket>>(
                        [],
                        "No tickets matched the search.",
                        "no ticket matched the combination of query, status, and service filters",
                        appliedDefaults,
                        nextSteps:
                        [
                            "Retry with fewer filters, for example omit status to include resolved tickets.",
                            "Try a shorter query term; matching is simple substring matching.",
                        ]));
                }

                var (page, truncation) = Truncation.Apply(
                    matches,
                    effectiveLimit,
                    "add a status or service filter",
                    "use a more specific query term",
                    "raise limit up to 50");

                var filterText = DescribeTicketFilters(args);
                return Task.FromResult(ToolResult.Ok<IReadOnlyList<Ticket>>(
                    page,
                    $"Found {Count(matches.Length, "ticket")}{filterText}, returning {page.Count} newest first.",
                    truncation,
                    appliedDefaults,
                    nextSteps: ["Call get_ticket with an id for full detail, or correlate_incident to pull overlapping telemetry."]));
            },
            _searchTicketsValidator,
            _auditor,
            McpCallerInfo.Describe(server),
            cancellationToken);

    [McpServerTool(Name = "get_ticket", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Fetches one support ticket by id. An unknown id returns the closest known ids so a mistyped id can be corrected.")]
    public Task<ToolResult<Ticket>> GetTicket(
        McpServer server,
        [Description("The ticket id, for example TCK-1024.")] string? ticketId = null,
        CancellationToken cancellationToken = default)
        => ToolRunner.RunAsync(
            "get_ticket",
            new GetTicketArgs(ticketId),
            (args, _) =>
            {
                var ticket = _data.FindTicket(args.TicketId!);
                if (ticket is null)
                {
                    return Task.FromResult(ToolResult.Invalid<Ticket>(
                        [CorrectiveError.UnknownValue("ticketId", args.TicketId!, _data.TicketIds)],
                        nextSteps: ["Use search_tickets to find valid ticket ids."]));
                }

                return Task.FromResult(ToolResult.Ok(
                    ticket,
                    $"Ticket {ticket.Id} '{ticket.Title}' is {StatusWord(ticket.Status)} and involves {string.Join(", ", ticket.Services)}.",
                    nextSteps: [$"Call correlate_incident with ticketId {ticket.Id} to fetch telemetry overlapping this ticket."]));
            },
            _getTicketValidator,
            _auditor,
            McpCallerInfo.Describe(server),
            cancellationToken);

    [McpServerTool(Name = "query_telemetry", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Queries telemetry events by service, minimum level, time range, and message text. The time range defaults to the recent past; scanning more than 6 hours requires scanAllHistory=true.")]
    public Task<ToolResult<IReadOnlyList<TelemetryEvent>>> QueryTelemetry(
        McpServer server,
        [Description("Filter to one service name, for example auth.")] string? service = null,
        [Description("Minimum severity to include: debug, info, warning, error, or critical. Default debug (everything).")] string? level = null,
        [Description("Start of the time range, ISO 8601. Defaults to 2 hours before the end of the range.")] DateTimeOffset? from = null,
        [Description("End of the time range, ISO 8601. Defaults to now.")] DateTimeOffset? to = null,
        [Description("Substring matched against the event message, case insensitive.")] string? text = null,
        [Description("Maximum events to return. Default 25, maximum 100.")] int? limit = null,
        [Description("Set true to allow scanning the full history instead of a recent window. Expensive; prefer a narrow time range.")] bool scanAllHistory = false,
        CancellationToken cancellationToken = default)
        => ToolRunner.RunAsync(
            "query_telemetry",
            new QueryTelemetryArgs(service, level, from, to, text, limit, scanAllHistory),
            (args, _) =>
            {
                var appliedDefaults = new List<string>();
                var effectiveLimit = SafeDefaults.ClampLimit(args.Limit, DefaultTelemetryLimit, MaxTelemetryLimit, appliedDefaults);

                TimeWindow window;
                if (args.ScanAllHistory)
                {
                    var oldest = _data.Telemetry.Count > 0 ? _data.Telemetry[0].Timestamp : _data.Anchor;
                    window = new TimeWindow(
                        args.From ?? oldest,
                        args.To ?? _data.Anchor);
                    appliedDefaults.Add("scanAllHistory is set, so the window spans the full retained history");
                }
                else
                {
                    window = SafeDefaults.ResolveTimeWindow(args.From, args.To, DefaultLookback, _data.Anchor, appliedDefaults);
                    if (window.Length > OptInThreshold)
                    {
                        return Task.FromResult(ToolResult.Invalid<IReadOnlyList<TelemetryEvent>>(
                            [
                                CorrectiveError.OptInRequired(
                                    "scanAllHistory",
                                    $"the requested range covers {window.Length.TotalHours:0.#} hours, more than the 6 hour cap",
                                    "Narrow from/to to the window you actually need."),
                            ]));
                    }
                }

                var minLevel = args.Level is null ? Severity.Debug : SeverityNames.Parse(args.Level);
                var matches = _data.Telemetry
                    .Where(e => window.Contains(e.Timestamp))
                    .Where(e => args.Service is null || string.Equals(e.Service, args.Service, StringComparison.OrdinalIgnoreCase))
                    .Where(e => e.Level >= minLevel)
                    .Where(e => args.Text is null || e.Message.Contains(args.Text, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.Timestamp)
                    .ToArray();

                var filterText = DescribeTelemetryFilters(args);
                if (matches.Length == 0)
                {
                    return Task.FromResult(ToolResult.Empty<IReadOnlyList<TelemetryEvent>>(
                        [],
                        $"No telemetry events matched{filterText} between {window.From:u} and {window.To:u}.",
                        args.From is null && !args.ScanAllHistory
                            ? "nothing matched, and the window defaulted to only the last 2 hours"
                            : "nothing matched the filters inside the requested window",
                        appliedDefaults,
                        nextSteps:
                        [
                            "Widen the window with from/to, or set scanAllHistory to true for the full history.",
                            "Drop the level or text filter to see what is there.",
                        ]));
                }

                var (page, truncation) = Truncation.Apply(
                    matches,
                    effectiveLimit,
                    "filter by service",
                    "raise level to warning or error",
                    "shorten the time range");

                return Task.FromResult(ToolResult.Ok<IReadOnlyList<TelemetryEvent>>(
                    page,
                    $"Found {Count(matches.Length, "telemetry event")}{filterText} between {window.From:u} and {window.To:u}, returning {page.Count} newest first.",
                    truncation,
                    appliedDefaults,
                    nextSteps: ["Call get_service_health to classify the affected service, or narrow this query to the loudest service."]));
            },
            _queryTelemetryValidator,
            _auditor,
            McpCallerInfo.Describe(server),
            cancellationToken);

    [McpServerTool(Name = "get_service_health", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Classifies services as healthy, degraded, or down from telemetry in the last 2 hours plus active tickets. Omit service to get all services.")]
    public Task<ToolResult<IReadOnlyList<ServiceHealth>>> GetServiceHealth(
        McpServer server,
        [Description("One service name to check, for example auth. Omit for all services.")] string? service = null,
        CancellationToken cancellationToken = default)
        => ToolRunner.RunAsync(
            "get_service_health",
            new GetServiceHealthArgs(service),
            (args, _) =>
            {
                var window = new TimeWindow(_data.Anchor - HealthWindow, _data.Anchor);
                var selected = args.Service is null
                    ? _data.Services
                    : _data.Services.Where(s => string.Equals(s.Name, args.Service, StringComparison.OrdinalIgnoreCase)).ToArray();

                var health = selected.Select(s => ComputeHealth(s.Name, window)).ToArray();
                var unhealthy = health.Where(h => h.Status != HealthStatus.Healthy).ToArray();

                var summary = args.Service is not null
                    ? DescribeSingleServiceHealth(health[0])
                    : unhealthy.Length == 0
                        ? $"All {health.Length} services look healthy over the last 2 hours."
                        : $"{unhealthy.Length} of {health.Length} services are not healthy: {string.Join(", ", unhealthy.Select(h => $"{h.Service} is {StatusWord(h.Status)}"))}.";

                return Task.FromResult(ToolResult.Ok<IReadOnlyList<ServiceHealth>>(
                    health,
                    summary,
                    nextSteps: unhealthy.Length == 0
                        ? []
                        :
                        [
                            $"Call query_telemetry with service={unhealthy[0].Service} and level=error to see what is failing.",
                            $"Call search_tickets with service={unhealthy[0].Service} and status=open to find affected users.",
                        ]));
            },
            _getServiceHealthValidator,
            _auditor,
            McpCallerInfo.Describe(server),
            cancellationToken);

    [McpServerTool(Name = "correlate_incident", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Given a ticket id, returns telemetry that overlaps the ticket's time window and its services (including direct dependencies), so the likely cause can be read in story order.")]
    public Task<ToolResult<CorrelationResult>> CorrelateIncident(
        McpServer server,
        [Description("The ticket id to correlate, for example TCK-1024.")] string? ticketId = null,
        CancellationToken cancellationToken = default)
        => ToolRunner.RunAsync(
            "correlate_incident",
            new CorrelateIncidentArgs(ticketId),
            (args, _) =>
            {
                var ticket = _data.FindTicket(args.TicketId!);
                if (ticket is null)
                {
                    return Task.FromResult(ToolResult.Invalid<CorrelationResult>(
                        [CorrectiveError.UnknownValue("ticketId", args.TicketId!, _data.TicketIds)],
                        nextSteps: ["Use search_tickets to find valid ticket ids."]));
                }

                var window = new TimeWindow(
                    ticket.OpenedAt - CorrelationPadding,
                    (ticket.UpdatedAt > ticket.OpenedAt ? ticket.UpdatedAt : ticket.OpenedAt) + CorrelationPadding);
                var services = _data.RelatedServices(ticket);

                var matches = _data.Telemetry
                    .Where(e => window.Contains(e.Timestamp))
                    .Where(e => services.Contains(e.Service, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(e => e.Timestamp)
                    .ToArray();

                var counts = matches
                    .GroupBy(e => e.Service, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                var (page, truncation) = Truncation.Apply(
                    matches,
                    MaxCorrelationEvents,
                    "query_telemetry with a single service and level=error",
                    "query_telemetry with a narrower from/to inside this window");

                var result = new CorrelationResult
                {
                    Ticket = ticket,
                    WindowFrom = window.From,
                    WindowTo = window.To,
                    ServicesConsidered = services,
                    Events = page,
                    EventCountsByService = counts,
                };

                if (matches.Length == 0)
                {
                    return Task.FromResult(ToolResult.Empty(
                        result,
                        $"No telemetry overlaps ticket {ticket.Id} on services {string.Join(", ", services)}.",
                        "no events fall inside the ticket's padded time window for the related services",
                        nextSteps:
                        [
                            "Query telemetry around the ticket's opened_at with scanAllHistory if the incident predates the window.",
                        ]));
                }

                var loudest = counts.OrderByDescending(c => c.Value).First();
                return Task.FromResult(ToolResult.Ok(
                    result,
                    $"Found {Count(matches.Length, "telemetry event")} across {Count(counts.Count, "service")} overlapping ticket {ticket.Id}; {loudest.Key} is loudest with {Count(loudest.Value, "event")}.",
                    truncation,
                    nextSteps:
                    [
                        $"Call get_service_health with service={loudest.Key} to classify the impact.",
                        "Read the events oldest first; the earliest error usually points at the cause.",
                    ]));
            },
            _correlateIncidentValidator,
            _auditor,
            McpCallerInfo.Describe(server),
            cancellationToken);

    private ServiceHealth ComputeHealth(string serviceName, TimeWindow window)
    {
        var events = _data.Telemetry
            .Where(e => string.Equals(e.Service, serviceName, StringComparison.OrdinalIgnoreCase))
            .Where(e => window.Contains(e.Timestamp))
            .ToArray();

        var criticals = events.Count(e => e.Level == Severity.Critical);
        var errors = events.Count(e => e.Level == Severity.Error);
        var warnings = events.Count(e => e.Level == Severity.Warning);
        var activeTickets = _data.Tickets.Count(t =>
            t.Status != TicketStatus.Resolved
            && t.Services.Contains(serviceName, StringComparer.OrdinalIgnoreCase));

        var status = criticals > 0
            ? HealthStatus.Down
            : errors > 0 || warnings >= 5
                ? HealthStatus.Degraded
                : HealthStatus.Healthy;

        return new ServiceHealth
        {
            Service = serviceName,
            Status = status,
            CriticalCount = criticals,
            ErrorCount = errors,
            WarningCount = warnings,
            ActiveTicketCount = activeTickets,
            WindowFrom = window.From,
            WindowTo = window.To,
        };
    }

    private static string DescribeSingleServiceHealth(ServiceHealth health)
        => health.Status switch
        {
            HealthStatus.Healthy => $"{health.Service} looks healthy over the last 2 hours.",
            HealthStatus.Degraded => $"{health.Service} is degraded: {health.ErrorCount} errors and {health.WarningCount} warnings in the last 2 hours.",
            _ => $"{health.Service} is down: {health.CriticalCount} critical and {health.ErrorCount} error events in the last 2 hours.",
        };

    private static string DescribeTicketFilters(SearchTicketsArgs args)
    {
        var parts = new List<string>();
        if (args.Query is not null)
        {
            parts.Add($"matching '{args.Query}'");
        }

        if (args.Status is not null)
        {
            parts.Add($"with status {args.Status.ToLowerInvariant()}");
        }

        if (args.Service is not null)
        {
            parts.Add($"for service {args.Service.ToLowerInvariant()}");
        }

        return parts.Count == 0 ? string.Empty : " " + string.Join(" ", parts);
    }

    private static string DescribeTelemetryFilters(QueryTelemetryArgs args)
    {
        var parts = new List<string>();
        if (args.Service is not null)
        {
            parts.Add($"for service {args.Service.ToLowerInvariant()}");
        }

        if (args.Level is not null)
        {
            parts.Add($"at level {args.Level.ToLowerInvariant()} or above");
        }

        if (args.Text is not null)
        {
            parts.Add($"containing '{args.Text}'");
        }

        return parts.Count == 0 ? string.Empty : " " + string.Join(" ", parts);
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string StatusWord(TicketStatus status) => status switch
    {
        TicketStatus.Open => "open",
        TicketStatus.Investigating => "investigating",
        _ => "resolved",
    };

    private static string StatusWord(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        _ => "down",
    };
}
