# ToolCraft.Mcp

[![CI](https://github.com/Ashutosh-Svea/toolCraft/actions/workflows/ci.yml/badge.svg)](https://github.com/Ashutosh-Svea/toolCraft/actions/workflows/ci.yml)

ToolCraft.Mcp is a small C# library on top of the official ModelContextProtocol
SDK for building MCP tools that unreliable agents cannot easily misuse: every
result is an envelope that explains itself, invalid parameters come back as
machine-readable corrections, and every call lands in an audit log. The repo
includes IncidentSandbox, a runnable incident-triage MCP server that
demonstrates each pattern on invented, deterministic data.

Origin: these patterns were learned building enterprise agent tooling in
production, rebuilt here from scratch on invented data.

## Why

Agents fail around tools in predictable ways. An empty result becomes "there
is no data". A capped list becomes "this is everything". A rejected parameter
gets retried verbatim, then declared an outage. The fixes are tool-design
decisions, not model improvements, and they are small enough to be a habit:
say why a result is empty, say what a cut dropped, and turn every rejection
into the next correct call. This library packages that habit as a handful of
types; [docs/patterns.md](docs/patterns.md) shows each failure and its fix in
detail.

## Quickstart

Requires the .NET 10 SDK.

```bash
git clone https://github.com/Ashutosh-Svea/toolCraft.git && cd toolCraft
```

```bash
dotnet test
```

```bash
npx @modelcontextprotocol/inspector dotnet run --project samples/IncidentSandbox
```

The last command opens the MCP Inspector connected to the sandbox over stdio.
Call `get_service_health` with no arguments and read the envelope that comes
back.

## The demo

The sandbox simulates a small company's incident tooling: 10 invented
services, 200 telemetry events, 30 support tickets, and three scripted
incident story arcs. Generation is deterministic and the sandbox clock is
frozen at process start, so every run plays out identically:

- Arc 1, ongoing: an expired TLS certificate is failing logins on `auth`.
- Arc 2, resolved: a bad deploy caused checkout latency 5 to 8 hours ago.
- Arc 3, resolved: a queue backlog delayed notification emails last night.

Connect a client (config below), then ask:

> why are users seeing login failures?

Watch the agent triage arc 1: `get_service_health` reports auth is down and
gateway degraded, `search_tickets` finds the open ticket TCK-1028 "Users
cannot log in", and `correlate_incident` returns the telemetry trail in story
order: certificate expiry warning, twelve TLS handshake failures, gateway
502s, and a collapsed login success rate. Each result's summary is a sentence
the agent can quote, and each suggests the next call, so even a weak agent
walks the same path.

Placeholder: a demo GIF of this triage session will go here.

### Claude Desktop

Add to `claude_desktop_config.json` (adjust the path):

```json
{
  "mcpServers": {
    "incident-sandbox": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/toolCraft/samples/IncidentSandbox"]
    }
  }
}
```

### Claude Code

```bash
claude mcp add incident-sandbox -- dotnet run --project /path/to/toolCraft/samples/IncidentSandbox
```

### VS Code / Copilot

Add to `.vscode/mcp.json` in a workspace, or to your user `mcp.json`:

```json
{
  "servers": {
    "incident-sandbox": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/toolCraft/samples/IncidentSandbox"]
    }
  }
}
```

### Streamable HTTP mode

The same server also hosts stateless streamable HTTP for remote-style
deployments (no session affinity, every request self-contained):

```bash
dotnet run --project samples/IncidentSandbox -- --http
```

Then talk to `http://localhost:8931/mcp` with any MCP client, or by hand:

```bash
curl -s -X POST http://localhost:8931/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"search_tickets","arguments":{"query":"log in","status":"open"}}}'
```

## The tools

| Tool | What it does |
| --- | --- |
| `search_tickets` | Text, status, and service search over tickets, capped at 10 by default |
| `get_ticket` | One ticket by id; unknown ids return the closest known ids |
| `query_telemetry` | Filter by service, minimum level, time range, and text; defaults to the last 2 hours |
| `get_service_health` | Classifies each service healthy, degraded, or down from recent telemetry |
| `correlate_incident` | Telemetry overlapping a ticket's time window and services, in story order |

Every tool returns the same envelope: `data`, `summary` (one quotable
sentence), `diagnostics` (status, reason, applied defaults, truncation,
errors), and `suggested_next_steps`. A misspelled service name comes back as:

```json
{
  "code": "unknown_value",
  "parameter": "service",
  "message": "unknown service 'checkout-svc'; closest matches: checkout, checkout-v2",
  "guidance": "Set service to one of the closest matches and retry.",
  "closest_matches": ["checkout", "checkout-v2"]
}
```

## The patterns

One section per pattern, each with a before and after of an agent misusing
the naive version, in [docs/patterns.md](docs/patterns.md):

1. [Result envelope](docs/patterns.md#1-result-envelope): results explain
   themselves, so empty never reads as broken.
2. [Corrective errors](docs/patterns.md#2-corrective-errors): rejections
   contain the next correct call.
3. [Safe defaults](docs/patterns.md#3-safe-defaults): cheap by default,
   expensive by explicit opt-in.
4. [Truncation transparency](docs/patterns.md#4-truncation-transparency): a
   cut list says what it dropped and how to narrow.
5. [Audit hook](docs/patterns.md#5-audit-hook): every call recorded with
   arguments, duration, and outcome.

[docs/auth-notes.md](docs/auth-notes.md) is a design note on how per-user
auth (OAuth2 on-behalf-of, Entra ID as the worked example) would slot into
the audit hook in an enterprise deployment. The sandbox itself ships without
auth on purpose.

## Repository layout

```
src/ToolCraft.Mcp          the library: envelope, corrective errors, safe
                           defaults, truncation, audit hook, tool runner
samples/IncidentSandbox    runnable MCP server, stdio and stateless HTTP
tests/                     xUnit suites for the library and the sandbox
docs/                      patterns and the auth design note
```

Built with .NET 10, the official ModelContextProtocol SDK, ASP.NET Core,
Serilog, and FluentValidation. Tests use xUnit, FluentAssertions, and Moq.
The sandbox is fully in-memory: no database, no Docker, no network calls.

## Building

```bash
dotnet build
```

```bash
dotnet test
```

CI runs the same two commands on every push via GitHub Actions.

## License

MIT. See [LICENSE](LICENSE).
