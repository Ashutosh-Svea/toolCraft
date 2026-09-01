# Defensive tool-design patterns

Five patterns for MCP tools that unreliable agents cannot easily misuse. Each
section names the failure mode, shows an agent misusing the naive version, and
shows how the defensive version changes the outcome. All examples come from the
IncidentSandbox server in this repository.

The common thread: an agent only knows what the tool result tells it. Every
surprise a result does not explain becomes a wrong sentence in the agent's
answer.

## 1. Result envelope

Library types: `ToolResult<T>`, `ToolResult`, `ResultDiagnostics` in
[src/ToolCraft.Mcp](../src/ToolCraft.Mcp).

Failure mode prevented: the agent misreads a bare payload because the payload
cannot say what it means. An empty list reads as "there is no data". A full
list reads as "this is everything". A raw object leaves the agent to invent
its own one-line interpretation, and weak agents invent wrong ones.

Before, with a naive tool that returns the payload alone:

```
tool: query_telemetry {service: "auth"}   ->   []
agent: "Telemetry shows no problems with the auth service."
```

The agent asserted a false negative. Nothing told it the query only covered a
recent window, or what to try next.

After, with every result wrapped in the envelope:

```json
{
  "data": [],
  "summary": "No telemetry events matched for service auth between 2026-09-01 08:00:00Z and 2026-09-01 10:00:00Z.",
  "diagnostics": {
    "status": "empty",
    "reason": "nothing matched, and the window defaulted to only the last 2 hours",
    "applied_defaults": ["end of time range defaulted to now", "start of time range defaulted to 2 hours before the end"],
    "errors": []
  },
  "suggested_next_steps": [
    "Widen the window with from/to, or set scanAllHistory to true for the full history.",
    "Drop the level or text filter to see what is there."
  ]
}
```

The summary is one plain sentence the agent can quote verbatim instead of
paraphrasing the data wrongly. The diagnostics explain why the result is
empty. The next steps turn a dead end into a plan.

## 2. Corrective errors

Library types: `CorrectiveError`, `ClosestMatches`, `CorrectiveValidation` in
[src/ToolCraft.Mcp](../src/ToolCraft.Mcp), built on FluentValidation.

Failure mode prevented: the agent passes a plausible but wrong parameter,
gets a bare failure, and either retries the same call verbatim or gives up.
Agents guess identifiers constantly: "checkout-svc" for a service named
"checkout", a ticket id off by one digit, "opne" for "open".

Before:

```
tool: query_telemetry {service: "checkout-svc"}   ->   Error: invalid service
agent: retries with {service: "checkout-svc"}     ->   Error: invalid service
agent: "The telemetry system appears to be unavailable."
```

After, the rejection is a structured envelope, not an exception:

```json
{
  "summary": "Call rejected: unknown service 'checkout-svc'; closest matches: checkout, checkout-v2.",
  "diagnostics": {
    "status": "invalid",
    "errors": [
      {
        "code": "unknown_value",
        "parameter": "service",
        "message": "unknown service 'checkout-svc'; closest matches: checkout, checkout-v2",
        "guidance": "Set service to one of the closest matches and retry.",
        "closest_matches": ["checkout", "checkout-v2"]
      }
    ]
  },
  "suggested_next_steps": ["Set service to one of the closest matches and retry."]
}
```

A weak agent does not need to understand the error model. The next correct
call is written out for it. Closest matches are ranked by edit distance and
containment, and when nothing is plausibly close the list stays empty, because
a wrong suggestion is worse than none.

## 3. Safe defaults

Library types: `SafeDefaults`, `TimeWindow` in
[src/ToolCraft.Mcp](../src/ToolCraft.Mcp).

Failure mode prevented: the agent asks for everything because nothing stopped
it. Unbounded queries flood the context window, slow the session, and bury the
relevant rows in noise the agent then reasons over badly.

The rules the sandbox applies:

- Pagination is capped by default: search_tickets returns 10 unless asked for
  more, and never more than 50. query_telemetry returns 25, never more
  than 100.
- Time ranges default to recent: query_telemetry looks at the last 2 hours
  unless told otherwise.
- Expensive scans need explicit opt-in: a window wider than 6 hours is
  rejected with a corrective error naming `scanAllHistory`, the parameter that
  allows it. Reaching for everything becomes a deliberate act instead of an
  accident.

Every applied default is recorded in `diagnostics.applied_defaults`, so a
narrow result is never mistaken for the whole truth. The opt-in rejection
looks like this:

```json
{
  "code": "opt_in_required",
  "parameter": "scanAllHistory",
  "message": "this request needs scanAllHistory=true because the requested range covers 24 hours, more than the 6 hour cap",
  "guidance": "Narrow from/to to the window you actually need. Or set scanAllHistory to true if the full scan is intended."
}
```

## 4. Truncation transparency

Library types: `Truncation`, `TruncationInfo` in
[src/ToolCraft.Mcp](../src/ToolCraft.Mcp).

Failure mode prevented: the agent treats a cut list as a complete list.
Capped results are the flip side of safe defaults; without an explicit marker
the cap silently changes the meaning of the result.

Before:

```
tool: search_tickets {}   ->   [10 tickets]
agent: "There are 10 tickets in the system, none about payments."
```

There were 30 tickets. The 10 returned happened not to mention payments.

After, the envelope states the cut and how to narrow:

```json
{
  "summary": "Found 30 tickets, returning 10 newest first.",
  "diagnostics": {
    "status": "truncated",
    "reason": "20 of 30 matching items were dropped to stay within the limit",
    "truncation": {
      "returned": 10,
      "total_matched": 30,
      "dropped": 20,
      "how_to_narrow": ["add a status or service filter", "use a more specific query term", "raise limit up to 50"]
    }
  }
}
```

`total_matched` also answers counting questions honestly: the agent can say
"30 tickets, of which I inspected 10" instead of "10 tickets".

## 5. Audit hook

Library types: `IToolAuditor`, `ToolAuditEntry`, `SerilogToolAuditor`,
`ToolRunner` in [src/ToolCraft.Mcp](../src/ToolCraft.Mcp).

Failure mode prevented: an agent session goes wrong and nobody can say why.
Which tools ran? With what arguments? What did each call return, and how long
did it take? Without a per-call record, evaluating or debugging an agent is
guesswork over chat logs.

Every call through `ToolRunner` records one `ToolAuditEntry`: tool name,
caller, arguments as JSON, start time, duration, outcome, and the summary
sentence the agent saw. The sandbox writes each entry as one structured
Serilog event:

```
Tool query_telemetry called by claude-desktop/1.0 finished Invalid in 7.2 ms;
arguments {"service":"checkout-svc", ...};
summary Call rejected: unknown service 'checkout-svc'; closest matches: checkout, checkout-v2.
```

Because the entry carries the outcome (`ok`, `empty`, `truncated`, `invalid`,
`error`, `canceled`), a session's audit trail doubles as an evaluation trace:
count the `invalid` outcomes per tool to find the parameters agents guess
wrong most, and the summaries reconstruct what the agent was told, in order.

The hook is one small interface, so a production deployment can swap Serilog
for anything else without touching tool code. Auditing never affects the
call: a throwing auditor is swallowed by design, because observability must
not turn a working tool into a broken one.

## How the pieces fit

`ToolRunner.RunAsync` is where the patterns meet: it validates arguments into
corrective errors (pattern 2), runs the body only when the arguments are
valid, converts escaped exceptions into safe failure envelopes (pattern 1),
and records the audit entry (pattern 5). The body itself applies safe
defaults (pattern 3) and truncation (pattern 4) with the helpers, and returns
an envelope. A tool built this way cannot return a bare failure and cannot
skip the audit trail.
