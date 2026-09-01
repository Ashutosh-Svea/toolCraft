using FluentAssertions;
using ToolCraft.Mcp;

namespace ToolCraft.Mcp.Tests;

public class SafeDefaultsTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ClampLimit_Unset_UsesTheDefaultAndSaysSo()
    {
        var notes = new List<string>();

        var limit = SafeDefaults.ClampLimit(null, defaultLimit: 25, maxLimit: 100, notes);

        limit.Should().Be(25);
        notes.Should().ContainSingle().Which.Should().Contain("defaulted to 25");
    }

    [Fact]
    public void ClampLimit_AboveTheMaximum_ReducesAndSaysSo()
    {
        var notes = new List<string>();

        var limit = SafeDefaults.ClampLimit(500, defaultLimit: 25, maxLimit: 100, notes);

        limit.Should().Be(100);
        notes.Should().ContainSingle().Which.Should().Contain("reduced from 500");
    }

    [Fact]
    public void ClampLimit_WithinBounds_IsUsedAsIsWithoutNotes()
    {
        var notes = new List<string>();

        var limit = SafeDefaults.ClampLimit(40, defaultLimit: 25, maxLimit: 100, notes);

        limit.Should().Be(40);
        notes.Should().BeEmpty();
    }

    [Fact]
    public void ClampLimit_BelowOne_IsRaisedToOne()
    {
        var notes = new List<string>();

        var limit = SafeDefaults.ClampLimit(0, defaultLimit: 25, maxLimit: 100, notes);

        limit.Should().Be(1);
        notes.Should().ContainSingle().Which.Should().Contain("raised");
    }

    [Fact]
    public void ResolveTimeWindow_Unset_DefaultsToARecentLookbackEndingNow()
    {
        var notes = new List<string>();

        var window = SafeDefaults.ResolveTimeWindow(null, null, TimeSpan.FromHours(2), Now, notes);

        window.To.Should().Be(Now);
        window.From.Should().Be(Now.AddHours(-2));
        notes.Should().HaveCount(2);
        notes.Should().Contain(n => n.Contains("defaulted to now"));
        notes.Should().Contain(n => n.Contains("2 hours"));
    }

    [Fact]
    public void ResolveTimeWindow_Explicit_IsUsedAsIsWithoutNotes()
    {
        var notes = new List<string>();
        var from = Now.AddHours(-6);
        var to = Now.AddHours(-3);

        var window = SafeDefaults.ResolveTimeWindow(from, to, TimeSpan.FromHours(2), Now, notes);

        window.Should().Be(new TimeWindow(from, to));
        notes.Should().BeEmpty();
    }

    [Fact]
    public void ResolveTimeWindow_Reversed_IsSwappedTransparently()
    {
        var notes = new List<string>();
        var from = Now;
        var to = Now.AddHours(-3);

        var window = SafeDefaults.ResolveTimeWindow(from, to, TimeSpan.FromHours(2), Now, notes);

        window.From.Should().Be(to);
        window.To.Should().Be(from);
        notes.Should().ContainSingle().Which.Should().Contain("swapped");
    }

    [Fact]
    public void TimeWindow_OverlapsAndContains_BehaveAsClosedIntervals()
    {
        var window = new TimeWindow(Now, Now.AddHours(1));

        window.Contains(Now).Should().BeTrue();
        window.Contains(Now.AddHours(1)).Should().BeTrue();
        window.Contains(Now.AddHours(2)).Should().BeFalse();

        window.Overlaps(new TimeWindow(Now.AddHours(1), Now.AddHours(2))).Should().BeTrue();
        window.Overlaps(new TimeWindow(Now.AddMinutes(90), Now.AddHours(2))).Should().BeFalse();
    }
}
