using FluentValidation;
using IncidentSandbox.Data;
using ToolCraft.Mcp;
using Severity = IncidentSandbox.Data.Severity;

namespace IncidentSandbox.Tools;

/// <summary>Arguments for search_tickets.</summary>
public sealed record SearchTicketsArgs(string? Query, string? Status, string? Service, int? Limit);

/// <summary>Arguments for get_ticket.</summary>
public sealed record GetTicketArgs(string? TicketId);

/// <summary>Arguments for query_telemetry.</summary>
public sealed record QueryTelemetryArgs(
    string? Service,
    string? Level,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Text,
    int? Limit,
    bool ScanAllHistory);

/// <summary>Arguments for get_service_health.</summary>
public sealed record GetServiceHealthArgs(string? Service);

/// <summary>Arguments for correlate_incident.</summary>
public sealed record CorrelateIncidentArgs(string? TicketId);

/// <summary>The severity names accepted by query_telemetry's level parameter.</summary>
public static class SeverityNames
{
    /// <summary>All accepted names, lowest severity first.</summary>
    public static readonly IReadOnlyList<string> All = ["debug", "info", "warning", "error", "critical"];

    /// <summary>Parses an accepted name, case insensitive.</summary>
    public static Severity Parse(string name)
        => Enum.Parse<Severity>(name, ignoreCase: true);
}

/// <summary>The status names accepted by search_tickets' status parameter.</summary>
public static class TicketStatusNames
{
    /// <summary>All accepted names.</summary>
    public static readonly IReadOnlyList<string> All = ["open", "investigating", "resolved"];

    /// <summary>Parses an accepted name, case insensitive.</summary>
    public static TicketStatus Parse(string name)
        => Enum.Parse<TicketStatus>(name, ignoreCase: true);
}

/// <summary>Validator for search_tickets: rejects unknown statuses and services with corrective guidance.</summary>
public sealed class SearchTicketsValidator : AbstractValidator<SearchTicketsArgs>
{
    /// <summary>Creates the validator against the dataset's known service names.</summary>
    public SearchTicketsValidator(SandboxDataset data)
    {
        RuleFor(a => a.Status).MustBeOneOf("status", () => TicketStatusNames.All);
        RuleFor(a => a.Service).MustBeOneOf("service", () => data.ServiceNames);
    }
}

/// <summary>Validator for get_ticket: the ticket id is required.</summary>
public sealed class GetTicketValidator : AbstractValidator<GetTicketArgs>
{
    /// <summary>Creates the validator.</summary>
    public GetTicketValidator()
        => RuleFor(a => a.TicketId)
            .NotEmpty()
            .WithCorrectiveError((_, _) => new CorrectiveError
            {
                Code = "missing_parameter",
                Parameter = "ticketId",
                Message = "ticketId is required",
                Guidance = "Pass a ticket id such as one returned by search_tickets.",
            });
}

/// <summary>Validator for query_telemetry: rejects unknown services and levels with corrective guidance.</summary>
public sealed class QueryTelemetryValidator : AbstractValidator<QueryTelemetryArgs>
{
    /// <summary>Creates the validator against the dataset's known service names.</summary>
    public QueryTelemetryValidator(SandboxDataset data)
    {
        RuleFor(a => a.Service).MustBeOneOf("service", () => data.ServiceNames);
        RuleFor(a => a.Level).MustBeOneOf("level", () => SeverityNames.All);
    }
}

/// <summary>Validator for get_service_health: rejects unknown services with corrective guidance.</summary>
public sealed class GetServiceHealthValidator : AbstractValidator<GetServiceHealthArgs>
{
    /// <summary>Creates the validator against the dataset's known service names.</summary>
    public GetServiceHealthValidator(SandboxDataset data)
        => RuleFor(a => a.Service).MustBeOneOf("service", () => data.ServiceNames);
}

/// <summary>Validator for correlate_incident: the ticket id is required.</summary>
public sealed class CorrelateIncidentValidator : AbstractValidator<CorrelateIncidentArgs>
{
    /// <summary>Creates the validator.</summary>
    public CorrelateIncidentValidator()
        => RuleFor(a => a.TicketId)
            .NotEmpty()
            .WithCorrectiveError((_, _) => new CorrectiveError
            {
                Code = "missing_parameter",
                Parameter = "ticketId",
                Message = "ticketId is required",
                Guidance = "Pass a ticket id such as one returned by search_tickets.",
            });
}
