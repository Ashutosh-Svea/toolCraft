using FluentAssertions;
using ToolCraft.Mcp;

namespace ToolCraft.Mcp.Tests;

public class ClosestMatchesTests
{
    private static readonly string[] Services =
    [
        "auth", "checkout", "checkout-v2", "payments", "catalog",
        "search", "notifications", "email-worker", "queue-broker", "reporting",
    ];

    [Fact]
    public void Find_SuffixedVariant_RanksTheBaseNameFirst()
    {
        var matches = ClosestMatches.Find("checkout-svc", Services);

        matches.Should().NotBeEmpty();
        matches[0].Should().Be("checkout");
        matches.Should().Contain("checkout-v2");
    }

    [Fact]
    public void Find_SmallTypo_FindsTheIntendedValue()
    {
        var matches = ClosestMatches.Find("auht", Services);

        matches.Should().Contain("auth");
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        var matches = ClosestMatches.Find("CHECKOUT", Services);

        matches.Should().Contain("checkout");
    }

    [Fact]
    public void Find_UnrelatedInput_ReturnsNothingRatherThanAWildGuess()
    {
        var matches = ClosestMatches.Find("zzzzzzzzzz", Services);

        matches.Should().BeEmpty();
    }

    [Fact]
    public void Find_RespectsTheMaximum()
    {
        var matches = ClosestMatches.Find("checkout", Services, max: 1);

        matches.Should().HaveCount(1);
    }

    [Fact]
    public void Find_BlankInput_ReturnsNothing()
    {
        ClosestMatches.Find("", Services).Should().BeEmpty();
        ClosestMatches.Find("   ", Services).Should().BeEmpty();
    }
}
