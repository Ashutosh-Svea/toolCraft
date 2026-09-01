using FluentAssertions;
using ToolCraft.Mcp;

namespace ToolCraft.Mcp.Tests;

public class TruncationTests
{
    [Fact]
    public void Apply_UnderTheLimit_ReturnsEverythingAndNoInfo()
    {
        var matches = new[] { "a", "b", "c" };

        var (items, info) = Truncation.Apply(matches, limit: 5);

        items.Should().Equal(matches);
        info.Should().BeNull();
    }

    [Fact]
    public void Apply_AtTheLimit_ReturnsEverythingAndNoInfo()
    {
        var matches = new[] { "a", "b" };

        var (items, info) = Truncation.Apply(matches, limit: 2);

        items.Should().Equal(matches);
        info.Should().BeNull();
    }

    [Fact]
    public void Apply_OverTheLimit_CutsAndSaysExactlyWhatWasDropped()
    {
        var matches = Enumerable.Range(1, 90).ToArray();

        var (items, info) = Truncation.Apply(matches, limit: 25, "filter by service", "shorten the time range");

        items.Should().HaveCount(25);
        items[0].Should().Be(1);
        info.Should().NotBeNull();
        info!.Returned.Should().Be(25);
        info.TotalMatched.Should().Be(90);
        info.Dropped.Should().Be(65);
        info.HowToNarrow.Should().Equal("filter by service", "shorten the time range");
    }

    [Fact]
    public void Apply_KeepsTheFirstItems_SoCallersControlRelevanceByOrdering()
    {
        var matches = new[] { "most-relevant", "second", "third" };

        var (items, _) = Truncation.Apply(matches, limit: 1);

        items.Should().ContainSingle().Which.Should().Be("most-relevant");
    }

    [Fact]
    public void Apply_NegativeLimit_Throws()
    {
        var act = () => Truncation.Apply(new[] { "a" }, limit: -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
