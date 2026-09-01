using FluentAssertions;
using ToolCraft.Mcp;

namespace ToolCraft.Mcp.Tests;

public class CorrectiveErrorTests
{
    private static readonly string[] Services = ["auth", "checkout", "checkout-v2", "payments"];

    [Fact]
    public void UnknownValue_ProducesTheDocumentedMessageShape()
    {
        var error = CorrectiveError.UnknownValue("service", "checkout-svc", Services);

        error.Code.Should().Be(CorrectiveError.UnknownValueCode);
        error.Parameter.Should().Be("service");
        error.Message.Should().StartWith("unknown service 'checkout-svc'");
        error.Message.Should().Contain("closest matches: checkout");
        error.ClosestMatches.Should().NotBeEmpty();
        error.ClosestMatches[0].Should().Be("checkout");
        error.Guidance.Should().Contain("closest matches");
    }

    [Fact]
    public void UnknownValue_WithNothingClose_StillGuidesTheCaller()
    {
        var error = CorrectiveError.UnknownValue("service", "zzzzzzz", Services);

        error.ClosestMatches.Should().BeEmpty();
        error.Message.Should().NotContain("closest matches");
        error.Guidance.Should().Contain("known value");
    }

    [Fact]
    public void OptInRequired_NamesTheParameterAndTheCheaperAlternative()
    {
        var error = CorrectiveError.OptInRequired(
            "scanAllHistory",
            "the requested range covers more than 24 hours",
            "Narrow the time range to the incident window.");

        error.Code.Should().Be(CorrectiveError.OptInRequiredCode);
        error.Parameter.Should().Be("scanAllHistory");
        error.Message.Should().Contain("scanAllHistory=true");
        error.Guidance.Should().Contain("Narrow the time range");
        error.Guidance.Should().Contain("scanAllHistory to true");
    }
}
