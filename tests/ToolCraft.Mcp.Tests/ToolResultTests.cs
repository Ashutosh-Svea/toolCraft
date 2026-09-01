using System.Text.Json;
using FluentAssertions;
using ToolCraft.Mcp;

namespace ToolCraft.Mcp.Tests;

public class ToolResultTests
{
    [Fact]
    public void Ok_WithoutTruncation_HasOkStatusAndNoReason()
    {
        var result = ToolResult.Ok(new[] { "a", "b" }, "Found 2 items.");

        result.Data.Should().Equal("a", "b");
        result.Summary.Should().Be("Found 2 items.");
        result.Diagnostics.Status.Should().Be(ToolOutcome.Ok);
        result.Diagnostics.Reason.Should().BeNull();
        result.Diagnostics.Truncation.Should().BeNull();
        result.Diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Ok_WithTruncation_HasTruncatedStatusAndExplainsTheCut()
    {
        var truncation = new TruncationInfo
        {
            Returned = 25,
            TotalMatched = 90,
            Dropped = 65,
            HowToNarrow = ["filter by service"],
        };

        var result = ToolResult.Ok(new[] { "a" }, "Found 90 items, returning 25.", truncation);

        result.Diagnostics.Status.Should().Be(ToolOutcome.Truncated);
        result.Diagnostics.Reason.Should().Contain("65").And.Contain("90");
        result.Diagnostics.Truncation.Should().BeSameAs(truncation);
    }

    [Fact]
    public void Empty_CarriesTheReasonAndAppliedDefaults()
    {
        var result = ToolResult.Empty(
            Array.Empty<string>(),
            "No telemetry matched.",
            "the default time range only covers the last 2 hours",
            appliedDefaults: ["time range defaulted to the last 2 hours"],
            nextSteps: ["Widen the time range."]);

        result.Data.Should().BeEmpty();
        result.Diagnostics.Status.Should().Be(ToolOutcome.Empty);
        result.Diagnostics.Reason.Should().Contain("default time range");
        result.Diagnostics.AppliedDefaults.Should().ContainSingle();
        result.SuggestedNextSteps.Should().ContainSingle().Which.Should().Be("Widen the time range.");
    }

    [Fact]
    public void Invalid_QuotesTheFirstErrorInTheSummary_AndPromotesGuidanceToNextSteps()
    {
        var errors = new[]
        {
            new CorrectiveError
            {
                Code = "unknown_value",
                Parameter = "service",
                Message = "unknown service 'checkout-svc'",
                Guidance = "Set service to one of the closest matches and retry.",
            },
        };

        var result = ToolResult.Invalid<string[]>(errors);

        result.Data.Should().BeNull();
        result.Summary.Should().Contain("unknown service 'checkout-svc'");
        result.Diagnostics.Status.Should().Be(ToolOutcome.Invalid);
        result.Diagnostics.Errors.Should().Equal(errors);
        result.SuggestedNextSteps.Should().ContainSingle()
            .Which.Should().Contain("closest matches");
    }

    [Fact]
    public void Invalid_WithoutErrors_Throws()
    {
        var act = () => ToolResult.Invalid<string>([]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Failed_ProducesErrorStatusWithRetryGuidance()
    {
        var result = ToolResult.Failed<string>("the dataset is not loaded");

        result.Diagnostics.Status.Should().Be(ToolOutcome.Error);
        result.Diagnostics.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(CorrectiveError.ToolFailureCode);
        result.Summary.Should().Contain("the dataset is not loaded");
    }

    [Fact]
    public void Serialization_UsesTheDocumentedSnakeCasePropertyNames()
    {
        var result = ToolResult.Ok(new[] { 1, 2 }, "Found 2 numbers.", nextSteps: ["Stop here."]);

        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.TryGetProperty("data", out _).Should().BeTrue();
        root.TryGetProperty("summary", out _).Should().BeTrue();
        root.TryGetProperty("diagnostics", out var diagnostics).Should().BeTrue();
        root.TryGetProperty("suggested_next_steps", out _).Should().BeTrue();
        diagnostics.TryGetProperty("status", out var status).Should().BeTrue();
        diagnostics.TryGetProperty("applied_defaults", out _).Should().BeTrue();
        status.GetString().Should().Be("ok");
    }

    [Fact]
    public void Serialization_WritesStatusValuesAsLowercaseWords()
    {
        var truncated = ToolResult.Ok(
            new[] { 1 },
            "Cut.",
            new TruncationInfo { Returned = 1, TotalMatched = 2, Dropped = 1 });

        var json = JsonSerializer.Serialize(truncated.Diagnostics);
        json.Should().Contain("\"status\":\"truncated\"");
    }
}
