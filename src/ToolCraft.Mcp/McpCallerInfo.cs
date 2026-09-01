using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace ToolCraft.Mcp;

/// <summary>
/// Derives an audit-friendly caller string from an MCP server session, so audit
/// entries can distinguish which client made each call.
/// </summary>
public static class McpCallerInfo
{
    /// <summary>The caller string used when no client information is available.</summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// Describes the connected client as "name/version", falling back to
    /// <see cref="Unknown"/> when the session or its client information is missing.
    /// </summary>
    /// <param name="server">The server session for the current call, or null.</param>
    public static string Describe(McpServer? server)
        => server?.ClientInfo is Implementation client
            ? $"{client.Name}/{client.Version}"
            : Unknown;
}
