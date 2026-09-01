using IncidentSandbox.Data;
using IncidentSandbox.Tools;
using ModelContextProtocol.Protocol;
using Serilog;
using Serilog.Events;
using ToolCraft.Mcp;

// The sandbox serves invented, deterministic data anchored at process start.
// The anchor is the dataset's frozen "now", so a demo plays out identically
// no matter when the tools are called during the session.
var dataset = DatasetGenerator.Generate(DatasetGenerator.DefaultSeed, DateTimeOffset.UtcNow);

const string ServerName = "incident-sandbox";
const string ServerVersion = "0.1.0";
const string ServerInstructions =
    "An invented incident-triage sandbox. Start wide with get_service_health or "
    + "search_tickets, then narrow with query_telemetry and correlate_incident. "
    + "Every result carries a summary, diagnostics, and suggested next steps; "
    + "read the diagnostics before concluding that data does not exist.";

if (args.Contains("--http"))
{
    // Streamable HTTP mode, stateless: no session affinity, every request
    // self-contained, so instances can sit behind any load balancer.
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console()
        .CreateLogger();

    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();
    builder.Services.AddSingleton(dataset);
    builder.Services.AddSingleton<IToolAuditor>(new SerilogToolAuditor(Log.Logger));
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = ServerName, Version = ServerVersion };
            options.ServerInstructions = ServerInstructions;
        })
        .WithHttpTransport(options => options.Stateless = true)
        .WithTools<IncidentTools>();

    var app = builder.Build();
    app.MapMcp("/mcp");

    var url = builder.Configuration["urls"] ?? "http://localhost:8931";
    Log.Information("IncidentSandbox listening on {Url}/mcp (streamable HTTP, stateless)", url);
    app.Run(url);
}
else
{
    // Stdio mode for local clients. Stdout carries the protocol, so every log,
    // including the audit trail, must go to stderr.
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
        .CreateLogger();

    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();
    builder.Services.AddSingleton(dataset);
    builder.Services.AddSingleton<IToolAuditor>(new SerilogToolAuditor(Log.Logger));
    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = ServerName, Version = ServerVersion };
            options.ServerInstructions = ServerInstructions;
        })
        .WithStdioServerTransport()
        .WithTools<IncidentTools>();

    await builder.Build().RunAsync();
}
