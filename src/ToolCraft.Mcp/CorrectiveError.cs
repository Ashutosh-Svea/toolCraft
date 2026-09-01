using System.Text.Json.Serialization;

namespace ToolCraft.Mcp;

/// <summary>
/// A machine-readable description of what was wrong with a call and how to fix it.
/// Corrective errors exist so that an agent that passed a bad parameter can repair
/// its next call without human help, instead of retrying the same mistake or giving up.
/// </summary>
public sealed class CorrectiveError
{
    /// <summary>A stable, snake_case error code an agent can branch on, for example "unknown_value".</summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>The parameter that caused the error, in the casing the tool schema uses, or null when the error is not tied to one parameter.</summary>
    [JsonPropertyName("parameter")]
    public string? Parameter { get; init; }

    /// <summary>One plain sentence describing the problem.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>What to change on the next call. Written as an instruction, for example "Set service to one of the closest matches."</summary>
    [JsonPropertyName("guidance")]
    public string? Guidance { get; init; }

    /// <summary>Known-good values closest to the rejected one, best match first. Empty when not applicable.</summary>
    [JsonPropertyName("closest_matches")]
    public IReadOnlyList<string> ClosestMatches { get; init; } = [];

    /// <summary>Error code for a value that is not in the set of known values.</summary>
    public const string UnknownValueCode = "unknown_value";

    /// <summary>Error code for an expensive operation that was attempted without its explicit opt-in parameter.</summary>
    public const string OptInRequiredCode = "opt_in_required";

    /// <summary>Error code for a tool that failed while executing, after its parameters were accepted.</summary>
    public const string ToolFailureCode = "tool_failure";

    /// <summary>
    /// Builds the standard corrective error for a value outside the known set, including
    /// the closest known values so the caller can self-correct.
    /// </summary>
    /// <param name="parameter">The schema name of the offending parameter.</param>
    /// <param name="value">The rejected value.</param>
    /// <param name="knownValues">The full set of accepted values.</param>
    public static CorrectiveError UnknownValue(string parameter, string value, IEnumerable<string> knownValues)
    {
        var matches = Mcp.ClosestMatches.Find(value, knownValues);
        var matchText = matches.Count > 0
            ? $"; closest matches: {string.Join(", ", matches)}"
            : string.Empty;
        return new CorrectiveError
        {
            Code = UnknownValueCode,
            Parameter = parameter,
            Message = $"unknown {parameter} '{value}'{matchText}",
            Guidance = matches.Count > 0
                ? $"Set {parameter} to one of the closest matches and retry."
                : $"Set {parameter} to a known value and retry, or omit it.",
            ClosestMatches = matches,
        };
    }

    /// <summary>
    /// Builds the standard corrective error for an expensive request that needs an explicit opt-in.
    /// </summary>
    /// <param name="parameter">The opt-in parameter the caller must set.</param>
    /// <param name="reason">Why the request is considered expensive.</param>
    /// <param name="cheaperAlternative">The narrower call the caller should usually prefer.</param>
    public static CorrectiveError OptInRequired(string parameter, string reason, string cheaperAlternative)
        => new()
        {
            Code = OptInRequiredCode,
            Parameter = parameter,
            Message = $"this request needs {parameter}=true because {reason}",
            Guidance = $"{cheaperAlternative} Or set {parameter} to true if the full scan is intended.",
        };
}
