using FluentAssertions;
using IncidentSandbox.Data;
using IncidentSandbox.Tools;
using ToolCraft.Mcp;

namespace IncidentSandbox.Tests;

public class IncidentToolsTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly SandboxDataset Data = DatasetGenerator.Generate(DatasetGenerator.DefaultSeed, Anchor);

    private sealed class RecordingAuditor : IToolAuditor
    {
        public List<ToolAuditEntry> Entries { get; } = [];

        public void Record(ToolAuditEntry entry) => Entries.Add(entry);
    }

    private readonly RecordingAuditor _auditor = new();

    private IncidentTools CreateTools() => new(Data, _auditor);

    [Fact]
    public async Task SearchTickets_WithoutFilters_CapsAtTenAndExplainsTheCut()
    {
        var result = await CreateTools().SearchTickets(server: null!);

        result.Diagnostics.Status.Should().Be(ToolOutcome.Truncated);
        result.Data.Should().HaveCount(10);
        result.Diagnostics.Truncation!.TotalMatched.Should().Be(30);
        result.Diagnostics.Truncation.Dropped.Should().Be(20);
        result.Diagnostics.Truncation.HowToNarrow.Should().NotBeEmpty();
        result.Diagnostics.AppliedDefaults.Should().Contain(n => n.Contains("defaulted to 10"));
    }

    [Fact]
    public async Task SearchTickets_FindsTheLoginArcTicket()
    {
        var result = await CreateTools().SearchTickets(server: null!, query: "log in", status: "open");

        result.Diagnostics.Status.Should().BeOneOf(ToolOutcome.Ok, ToolOutcome.Truncated);
        result.Data.Should().Contain(t => t.Title == "Users cannot log in");
        result.Summary.Should().Contain("open");
    }

    [Fact]
    public async Task SearchTickets_UnknownStatus_ReturnsCorrectiveErrorWithClosestMatch()
    {
        var result = await CreateTools().SearchTickets(server: null!, status: "opne");

        result.Diagnostics.Status.Should().Be(ToolOutcome.Invalid);
        var error = result.Diagnostics.Errors.Should().ContainSingle().Subject;
        error.Code.Should().Be(CorrectiveError.UnknownValueCode);
        error.Parameter.Should().Be("status");
        error.ClosestMatches.Should().Contain("open");
    }

    [Fact]
    public async Task SearchTickets_NoMatches_ExplainsWhyAndSuggestsLoosening()
    {
        var result = await CreateTools().SearchTickets(server: null!, query: "kubernetes the great");

        result.Diagnostics.Status.Should().Be(ToolOutcome.Empty);
        result.Data.Should().BeEmpty();
        result.Diagnostics.Reason.Should().NotBeNullOrWhiteSpace();
        result.SuggestedNextSteps.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetTicket_UnknownId_SuggestsClosestIds()
    {
        var result = await CreateTools().GetTicket(server: null!, ticketId: "TCK-9999");

        result.Diagnostics.Status.Should().Be(ToolOutcome.Invalid);
        var error = result.Diagnostics.Errors.Should().ContainSingle().Subject;
        error.Code.Should().Be(CorrectiveError.UnknownValueCode);
        error.ClosestMatches.Should().NotBeEmpty();
        error.ClosestMatches.Should().OnlyContain(id => id.StartsWith("TCK-"));
    }

    [Fact]
    public async Task GetTicket_MissingId_ReturnsMissingParameterGuidance()
    {
        var result = await CreateTools().GetTicket(server: null!);

        result.Diagnostics.Status.Should().Be(ToolOutcome.Invalid);
        result.Diagnostics.Errors.Should().ContainSingle()
            .Which.Code.Should().Be("missing_parameter");
    }

    [Fact]
    public async Task GetTicket_KnownId_ReturnsTheTicketAndPointsAtCorrelation()
    {
        var ticket = Data.Tickets.Single(t => t.Title == "Users cannot log in");

        var result = await CreateTools().GetTicket(server: null!, ticketId: ticket.Id.ToLowerInvariant());

        result.Diagnostics.Status.Should().Be(ToolOutcome.Ok);
        result.Data!.Id.Should().Be(ticket.Id);
        result.SuggestedNextSteps.Should().ContainSingle()
            .Which.Should().Contain("correlate_incident");
    }

    [Fact]
    public async Task QueryTelemetry_Defaults_ToRecentWindow_AndFindsTheOngoingArc()
    {
        var result = await CreateTools().QueryTelemetry(server: null!);

        result.Diagnostics.AppliedDefaults.Should().Contain(n => n.Contains("defaulted to now"));
        result.Diagnostics.AppliedDefaults.Should().Contain(n => n.Contains("2 hours"));
        result.Data.Should().Contain(e => e.Message.Contains("certificate for auth.sandbox.internal expired"));
    }

    [Fact]
    public async Task QueryTelemetry_MinimumLevel_FiltersLowerSeverities()
    {
        var result = await CreateTools().QueryTelemetry(server: null!, level: "error");

        result.Data.Should().NotBeEmpty();
        result.Data.Should().OnlyContain(e => e.Level >= Severity.Error);
    }

    [Fact]
    public async Task QueryTelemetry_WideRangeWithoutOptIn_IsRejectedWithGuidance()
    {
        var result = await CreateTools().QueryTelemetry(
            server: null!,
            from: Anchor.AddHours(-24),
            to: Anchor);

        result.Diagnostics.Status.Should().Be(ToolOutcome.Invalid);
        var error = result.Diagnostics.Errors.Should().ContainSingle().Subject;
        error.Code.Should().Be(CorrectiveError.OptInRequiredCode);
        error.Parameter.Should().Be("scanAllHistory");
        error.Guidance.Should().Contain("Narrow");
    }

    [Fact]
    public async Task QueryTelemetry_WithOptIn_ScansTheFullHistory()
    {
        var result = await CreateTools().QueryTelemetry(
            server: null!,
            text: "queue depth 16000",
            scanAllHistory: true);

        result.Diagnostics.Status.Should().Be(ToolOutcome.Ok);
        result.Data.Should().ContainSingle().Which.Service.Should().Be("queue-broker");
    }

    [Fact]
    public async Task QueryTelemetry_UnknownService_SuggestsTheRealNames()
    {
        var result = await CreateTools().QueryTelemetry(server: null!, service: "checkout-svc");

        result.Diagnostics.Status.Should().Be(ToolOutcome.Invalid);
        var error = result.Diagnostics.Errors.Should().ContainSingle().Subject;
        error.Message.Should().Contain("unknown service 'checkout-svc'");
        error.ClosestMatches.Should().Contain("checkout").And.Contain("checkout-v2");
    }

    [Fact]
    public async Task QueryTelemetry_EmptyWindow_ExplainsTheDefaultWindowInsteadOfJustSayingNo()
    {
        var result = await CreateTools().QueryTelemetry(
            server: null!,
            service: "catalog",
            text: "no such message anywhere");

        result.Diagnostics.Status.Should().Be(ToolOutcome.Empty);
        result.Diagnostics.Reason.Should().Contain("last 2 hours");
        result.SuggestedNextSteps.Should().Contain(s => s.Contains("scanAllHistory"));
    }

    [Fact]
    public async Task GetServiceHealth_AllServices_ReportsTheOngoingIncident()
    {
        var result = await CreateTools().GetServiceHealth(server: null!);

        result.Data.Should().HaveCount(10);
        var auth = result.Data!.Single(h => h.Service == "auth");
        auth.Status.Should().Be(HealthStatus.Down);
        auth.CriticalCount.Should().BeGreaterThan(0);
        auth.ActiveTicketCount.Should().BeGreaterThan(0);

        var gateway = result.Data!.Single(h => h.Service == "gateway");
        gateway.Status.Should().NotBe(HealthStatus.Healthy);

        result.Summary.Should().Contain("auth is down");
        result.SuggestedNextSteps.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetServiceHealth_SingleService_SummarizesItInOneSentence()
    {
        var result = await CreateTools().GetServiceHealth(server: null!, service: "auth");

        result.Data.Should().ContainSingle();
        result.Summary.Should().Contain("auth is down");
    }

    [Fact]
    public async Task CorrelateIncident_LoginTicket_TellsTheCertificateStory()
    {
        var ticket = Data.Tickets.Single(t => t.Title == "Users cannot log in");

        var result = await CreateTools().CorrelateIncident(server: null!, ticketId: ticket.Id);

        result.Diagnostics.Status.Should().BeOneOf(ToolOutcome.Ok, ToolOutcome.Truncated);
        var correlation = result.Data!;
        correlation.ServicesConsidered.Should().Contain("auth").And.Contain("gateway");
        correlation.Events.Should().Contain(e => e.Message.Contains("certificate for auth.sandbox.internal expired"));
        correlation.Events.Should().BeInAscendingOrder(e => e.Timestamp);
        correlation.EventCountsByService.Should().ContainKey("auth");
        result.Summary.Should().Contain(ticket.Id);
    }

    [Fact]
    public async Task CorrelateIncident_UnknownTicket_ReturnsCorrectiveError()
    {
        var result = await CreateTools().CorrelateIncident(server: null!, ticketId: "TCK-0000");

        result.Diagnostics.Status.Should().Be(ToolOutcome.Invalid);
        result.Diagnostics.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(CorrectiveError.UnknownValueCode);
    }

    [Fact]
    public async Task EveryToolCall_LandsInTheAuditTrail()
    {
        var tools = CreateTools();

        await tools.SearchTickets(server: null!, query: "log in");
        await tools.GetTicket(server: null!, ticketId: "TCK-9999");
        await tools.QueryTelemetry(server: null!, level: "error");

        _auditor.Entries.Should().HaveCount(3);
        _auditor.Entries.Select(e => e.Tool).Should().Equal("search_tickets", "get_ticket", "query_telemetry");
        _auditor.Entries.Should().OnlyContain(e => e.Caller == "unknown");
        _auditor.Entries[1].Outcome.Should().Be(ToolOutcome.Invalid);
        _auditor.Entries.Should().OnlyContain(e => e.ArgumentsJson != null);
    }
}
