using System.Text.Json.Serialization;

namespace IncidentSandbox.Data;

/// <summary>Severity of a telemetry event, lowest to highest.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
public enum Severity
{
    [JsonStringEnumMemberName("debug")]
    Debug,

    [JsonStringEnumMemberName("info")]
    Info,

    [JsonStringEnumMemberName("warning")]
    Warning,

    [JsonStringEnumMemberName("error")]
    Error,

    [JsonStringEnumMemberName("critical")]
    Critical,
}

/// <summary>Lifecycle state of a support ticket.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TicketStatus>))]
public enum TicketStatus
{
    [JsonStringEnumMemberName("open")]
    Open,

    [JsonStringEnumMemberName("investigating")]
    Investigating,

    [JsonStringEnumMemberName("resolved")]
    Resolved,
}

/// <summary>One invented service in the sandbox topology.</summary>
public sealed record ServiceInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>Services this one calls directly.</summary>
    [JsonPropertyName("depends_on")]
    public IReadOnlyList<string> DependsOn { get; init; } = [];
}

/// <summary>One invented telemetry event.</summary>
public sealed record TelemetryEvent
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("service")]
    public required string Service { get; init; }

    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("level")]
    public required Severity Level { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

/// <summary>One invented support ticket.</summary>
public sealed record Ticket
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("status")]
    public required TicketStatus Status { get; init; }

    /// <summary>Services the reporter or triager associated with the ticket.</summary>
    [JsonPropertyName("services")]
    public IReadOnlyList<string> Services { get; init; } = [];

    [JsonPropertyName("opened_at")]
    public required DateTimeOffset OpenedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Invented reporter handle, for example "user-3517".</summary>
    [JsonPropertyName("reporter")]
    public required string Reporter { get; init; }
}
