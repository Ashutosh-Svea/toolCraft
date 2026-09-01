using System.Text.Json.Serialization;
using IncidentSandbox.Data;

namespace IncidentSandbox.Tools;

/// <summary>Overall health classification for a service.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HealthStatus>))]
public enum HealthStatus
{
    [JsonStringEnumMemberName("healthy")]
    Healthy,

    [JsonStringEnumMemberName("degraded")]
    Degraded,

    [JsonStringEnumMemberName("down")]
    Down,
}

/// <summary>Computed health for one service over a recent window.</summary>
public sealed record ServiceHealth
{
    [JsonPropertyName("service")]
    public required string Service { get; init; }

    [JsonPropertyName("status")]
    public required HealthStatus Status { get; init; }

    [JsonPropertyName("critical_count")]
    public required int CriticalCount { get; init; }

    [JsonPropertyName("error_count")]
    public required int ErrorCount { get; init; }

    [JsonPropertyName("warning_count")]
    public required int WarningCount { get; init; }

    /// <summary>Tickets tagged with this service that are open or investigating.</summary>
    [JsonPropertyName("active_ticket_count")]
    public required int ActiveTicketCount { get; init; }

    [JsonPropertyName("window_from")]
    public required DateTimeOffset WindowFrom { get; init; }

    [JsonPropertyName("window_to")]
    public required DateTimeOffset WindowTo { get; init; }
}

/// <summary>Telemetry correlated with one ticket's services and time window.</summary>
public sealed record CorrelationResult
{
    [JsonPropertyName("ticket")]
    public required Ticket Ticket { get; init; }

    /// <summary>The correlation window: ticket lifetime padded by 30 minutes on each side.</summary>
    [JsonPropertyName("window_from")]
    public required DateTimeOffset WindowFrom { get; init; }

    [JsonPropertyName("window_to")]
    public required DateTimeOffset WindowTo { get; init; }

    /// <summary>The ticket's services plus direct dependencies in both directions.</summary>
    [JsonPropertyName("services_considered")]
    public required IReadOnlyList<string> ServicesConsidered { get; init; }

    /// <summary>Overlapping telemetry in story order, oldest first.</summary>
    [JsonPropertyName("events")]
    public required IReadOnlyList<TelemetryEvent> Events { get; init; }

    /// <summary>Number of overlapping events per service, including services beyond any cut.</summary>
    [JsonPropertyName("event_counts_by_service")]
    public required IReadOnlyDictionary<string, int> EventCountsByService { get; init; }
}
