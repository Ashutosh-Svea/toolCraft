# Design note: per-user auth for an enterprise deployment

The runnable sandbox deliberately ships without authentication: it serves
invented data, binds to localhost, and exists to demonstrate tool-design
patterns. This note describes how per-user auth would slot into the same
architecture in an enterprise deployment, with Microsoft Entra ID as the
worked example. It is a design note only; none of this is implemented here.

## The setting

An MCP server like IncidentSandbox deployed inside a company would front real
systems: a ticketing system, a telemetry store, a service catalog. Those
systems already enforce per-user permissions. The MCP server must not become
a bypass: when an agent acting for a support engineer queries tickets, the
backing systems should see the engineer's identity, not a service account
with broad read access.

## Token flow with OAuth2 on-behalf-of

The streamable HTTP transport is the natural place for this, and the
stateless mode in the sample (every request self-contained, no session
affinity) is exactly the shape a load-balanced enterprise deployment wants.

1. The MCP client obtains an access token for the MCP server. With Entra ID
   the server is registered as an application with its own scope, for example
   `api://incident-mcp/access`. MCP's authorization specification describes
   the discovery and consent handshake; mature clients handle it for the
   user.
2. Every HTTP request to the server carries that bearer token. ASP.NET Core's
   JWT bearer middleware validates issuer, audience, signature, and expiry
   before any MCP handling runs. The validated principal, with its object id,
   UPN, tenant, and group claims, is available on the request context.
3. When a tool needs a downstream system, the server exchanges the incoming
   token for a downstream token using Entra ID's on-behalf-of flow: a JWT
   bearer assertion grant with `requested_token_use=on_behalf_of`. (The
   vendor-neutral mechanism for this shape of delegation is OAuth2 token
   exchange, RFC 8693, which is a distinct grant type; an implementer
   targeting Entra ID should follow the on-behalf-of documentation, not
   RFC 8693.) The downstream API then sees the original user, and its own
   permission model applies unchanged. The server holds no standing
   credentials for the downstream systems beyond its own client secret or
   certificate, and no secrets for individual users, since exchanged tokens
   are short lived and cacheable per user.

Stdio mode does not carry HTTP headers; local single-user clients run under
the identity of whoever launched the process. Enterprise per-user auth is an
HTTP-mode concern.

## Where identity meets the audit hook

The library's audit entry already has the seam: `ToolAuditEntry.Caller` is a
plain string that the sandbox fills with the MCP client name and version. In
an authenticated deployment the tool layer would fill it from the validated
principal instead: a stable user identifier such as the Entra object id,
optionally prefixed with the tenant. That single change upgrades the audit
trail from "which client called" to "which person's authority was used",
which is the question a security review actually asks.

Three properties fall out of routing identity through the audit hook:

- Attribution: every tool call, its arguments, duration, and outcome is tied
  to a person, which is what incident reviews and access audits need.
- Rate and anomaly analysis: outcome counts per user surface both struggling
  agents (many `invalid` outcomes) and misuse patterns (many `opt_in_required`
  rejections followed by full scans).
- Data minimization checks: because arguments are logged as JSON, an auditor
  can verify that agents acting for a user only queried what the user's task
  required.

The auditor implementation would also change: production would replace the
console Serilog sink with the organization's log pipeline, and the audit
entry is small and structured precisely so that swap is boring.

Argument capture needs a deliberate decision once real user data flows
through the tools. The sandbox logs full arguments because its data is
invented; a production deployment should pass `ToolRunner`'s redaction hook
a projection that keeps operationally useful fields and drops or masks
anything sensitive, and should give the audit log the same retention and
access controls as any other store that pairs a person with what they
queried.

## Per-user permissions inside tools

Token validation answers "who is calling". Authorization inside tools
answers "what may they see". The clean seam is the same one the sandbox
already uses for its dataset: tools receive their data source through
dependency injection. An enterprise version would resolve a per-request data
source bound to the caller's identity, so a tool like search_tickets can
only ever query what the on-behalf-of token can reach. Tools themselves stay
permission-unaware, which keeps them testable with plain fakes.

Two defensive-pattern interactions need care in an authorized deployment:

- A permission-filtered empty result should say so in the diagnostics
  (`reason: "your account cannot see tickets for this service"`), because an
  agent that cannot distinguish "no data" from "no access" reports false
  negatives to its user.
- Corrective errors must not leak: closest-match suggestions should be
  computed over the values the caller is allowed to see, never over the full
  namespace.

## Summary

Validate the bearer token at the ASP.NET Core layer, exchange it on-behalf-of
for downstream calls so existing permission models keep working, stamp the
audit entry with the validated user, and inject permission-scoped data
sources into tools. The library's envelope, corrective errors, and audit
hook need no structural change to support this; the seams are already where
an enterprise deployment needs them.
