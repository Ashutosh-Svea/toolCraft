using System.Text.Json;
using FluentAssertions;
using IncidentSandbox.Data;

namespace IncidentSandbox.Tests;

public class DatasetGeneratorTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static SandboxDataset Generate(int seed = DatasetGenerator.DefaultSeed)
        => DatasetGenerator.Generate(seed, Anchor);

    [Fact]
    public void SameSeedAndAnchor_ProducesByteIdenticalData()
    {
        var first = Generate();
        var second = Generate();

        Serialize(first).Should().Be(Serialize(second));
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentNoise()
    {
        var first = Generate(seed: 1);
        var second = Generate(seed: 2);

        Serialize(first).Should().NotBe(Serialize(second));
    }

    [Fact]
    public void Counts_AreExactlyAsDocumented()
    {
        var data = Generate();

        data.Services.Should().HaveCount(10);
        data.Telemetry.Should().HaveCount(200);
        data.Tickets.Should().HaveCount(30);
    }

    [Fact]
    public void TelemetryIsOrderedByTimestamp_WithSequentialUniqueIds()
    {
        var data = Generate();

        data.Telemetry.Should().BeInAscendingOrder(e => e.Timestamp);
        data.Telemetry.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        data.Telemetry[0].Id.Should().Be("EVT-0001");
        data.Telemetry[^1].Id.Should().Be("EVT-0200");
    }

    [Fact]
    public void TicketIds_AreSequentialAndUnique_AndListedNewestFirst()
    {
        var data = Generate();

        data.Tickets.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        data.Tickets.Select(t => t.Id).Should().AllSatisfy(id => id.Should().MatchRegex("^TCK-10(0[1-9]|[12][0-9]|30)$"));
        data.Tickets.Should().BeInDescendingOrder(t => t.OpenedAt);
    }

    [Fact]
    public void AllTelemetryAndTickets_ReferenceKnownServices()
    {
        var data = Generate();
        var known = data.ServiceNames;

        data.Telemetry.Select(e => e.Service).Should().OnlyContain(s => known.Contains(s));
        data.Tickets.SelectMany(t => t.Services).Should().OnlyContain(s => known.Contains(s));
    }

    [Fact]
    public void CertExpiryArc_IsPresentAndOngoing()
    {
        var data = Generate();

        var ticket = data.Tickets.Single(t => t.Title == "Users cannot log in");
        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.Services.Should().Contain("auth").And.Contain("gateway");

        var certErrors = data.Telemetry
            .Where(e => e.Message.Contains("certificate for auth.sandbox.internal expired"))
            .ToArray();
        certErrors.Should().HaveCount(12);
        certErrors.Should().OnlyContain(e => e.Service == "auth" && e.Level == Severity.Error);
        certErrors.Should().OnlyContain(e => e.Timestamp > Anchor.AddHours(-2));

        data.Telemetry.Should().ContainSingle(e =>
            e.Service == "auth" && e.Level == Severity.Critical);
    }

    [Fact]
    public void BadDeployArc_IsPresentWithRollback()
    {
        var data = Generate();

        data.Telemetry.Should().ContainSingle(e => e.Message.Contains("deploy 2026.08.31.4 rolled out"));
        data.Telemetry.Should().ContainSingle(e => e.Message.Contains("rolled back to 2026.08.31.3"));
        data.Telemetry.Count(e => e.Service == "checkout" && e.Message.Contains("p95 latency") && e.Level == Severity.Warning)
            .Should().Be(6);
        data.Tickets.Should().ContainSingle(t => t.Title == "Checkout slow, orders timing out")
            .Which.Status.Should().Be(TicketStatus.Resolved);
    }

    [Fact]
    public void QueueBacklogArc_IsPresentAndDrained()
    {
        var data = Generate();

        data.Telemetry.Should().ContainSingle(e => e.Message.Contains("queue depth 16000"));
        data.Telemetry.Should().ContainSingle(e => e.Message.Contains("backlog drained"));
        data.Tickets.Should().ContainSingle(t => t.Title == "Order confirmation emails delayed by an hour")
            .Which.Services.Should().Contain("email-worker");
    }

    [Fact]
    public void RelatedServices_ExpandsDependenciesInBothDirections()
    {
        var data = Generate();
        var ticket = data.Tickets.Single(t => t.Title == "Order confirmation emails delayed by an hour");

        var related = data.RelatedServices(ticket);

        // The ticket names notifications and email-worker; both depend on queue-broker.
        related.Should().Contain("notifications");
        related.Should().Contain("email-worker");
        related.Should().Contain("queue-broker");
        related.Should().NotContain("catalog");
    }

    private static string Serialize(SandboxDataset data)
        => JsonSerializer.Serialize(new { data.Anchor, data.Seed, data.Services, data.Telemetry, data.Tickets });
}
