using System.Text.Json.Serialization;

namespace ToolCraft.Mcp;

/// <summary>
/// The overall outcome of a tool call. Used both in <see cref="ResultDiagnostics.Status"/>
/// (so agents can branch on it) and in <see cref="ToolAuditEntry.Outcome"/> (so operators
/// can aggregate it).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ToolOutcome>))]
public enum ToolOutcome
{
    /// <summary>The call succeeded and returned complete data.</summary>
    [JsonStringEnumMemberName("ok")]
    Ok,

    /// <summary>The call succeeded but matched nothing. Diagnostics explain why.</summary>
    [JsonStringEnumMemberName("empty")]
    Empty,

    /// <summary>The call succeeded but results were cut. Diagnostics say what was dropped.</summary>
    [JsonStringEnumMemberName("truncated")]
    Truncated,

    /// <summary>Parameters were rejected. Diagnostics carry corrective guidance.</summary>
    [JsonStringEnumMemberName("invalid")]
    Invalid,

    /// <summary>The tool failed while executing. Diagnostics carry a safe error description.</summary>
    [JsonStringEnumMemberName("error")]
    Error,

    /// <summary>The call was canceled before it completed.</summary>
    [JsonStringEnumMemberName("canceled")]
    Canceled,
}
