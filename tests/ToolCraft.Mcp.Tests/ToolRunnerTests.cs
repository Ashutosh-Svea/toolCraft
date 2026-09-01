using FluentAssertions;
using FluentValidation;
using Moq;
using ToolCraft.Mcp;

namespace ToolCraft.Mcp.Tests;

public class ToolRunnerTests
{
    private sealed record EchoArgs(string? Text);

    private sealed class EchoArgsValidator : AbstractValidator<EchoArgs>
    {
        public EchoArgsValidator()
            => RuleFor(a => a.Text).MustBeOneOf("text", () => ["hello", "goodbye"]);
    }

    private sealed class RecordingAuditor : IToolAuditor
    {
        public List<ToolAuditEntry> Entries { get; } = [];

        public void Record(ToolAuditEntry entry) => Entries.Add(entry);
    }

    [Fact]
    public async Task RunAsync_HappyPath_ReturnsTheBodyResultAndAuditsIt()
    {
        var auditor = new RecordingAuditor();

        var result = await ToolRunner.RunAsync(
            "echo",
            new EchoArgs("hello"),
            (args, _) => Task.FromResult(ToolResult.Ok(args.Text!, "Echoed hello.")),
            validator: new EchoArgsValidator(),
            auditor: auditor,
            caller: "test-client/1.0");

        result.Data.Should().Be("hello");
        result.Diagnostics.Status.Should().Be(ToolOutcome.Ok);

        var entry = auditor.Entries.Should().ContainSingle().Subject;
        entry.Tool.Should().Be("echo");
        entry.Caller.Should().Be("test-client/1.0");
        entry.Outcome.Should().Be(ToolOutcome.Ok);
        entry.Summary.Should().Be("Echoed hello.");
        entry.ArgumentsJson.Should().Contain("hello");
        entry.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
    }

    [Fact]
    public async Task RunAsync_InvalidArguments_SkipsTheBodyAndReturnsCorrectiveErrors()
    {
        var bodyRan = false;

        var result = await ToolRunner.RunAsync<EchoArgs, string>(
            "echo",
            new EchoArgs("helo"),
            (_, _) =>
            {
                bodyRan = true;
                return Task.FromResult(ToolResult.Ok("never", "never"));
            },
            validator: new EchoArgsValidator());

        bodyRan.Should().BeFalse();
        result.Diagnostics.Status.Should().Be(ToolOutcome.Invalid);
        result.Diagnostics.Errors.Should().ContainSingle()
            .Which.ClosestMatches.Should().Contain("hello");
    }

    [Fact]
    public async Task RunAsync_InvalidArguments_AuditsTheRejection()
    {
        var auditor = new Mock<IToolAuditor>();

        await ToolRunner.RunAsync<EchoArgs, string>(
            "echo",
            new EchoArgs("nope-nope-nope"),
            (_, _) => Task.FromResult(ToolResult.Ok("never", "never")),
            validator: new EchoArgsValidator(),
            auditor: auditor.Object);

        auditor.Verify(
            a => a.Record(It.Is<ToolAuditEntry>(e =>
                e.Tool == "echo" && e.Outcome == ToolOutcome.Invalid)),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_BodyThrows_ReturnsASafeFailureEnvelopeInsteadOfThrowing()
    {
        var auditor = new RecordingAuditor();

        var result = await ToolRunner.RunAsync<EchoArgs, string>(
            "echo",
            new EchoArgs("hello"),
            (_, _) => throw new InvalidOperationException("the dataset is not loaded"),
            auditor: auditor);

        result.Diagnostics.Status.Should().Be(ToolOutcome.Error);
        result.Summary.Should().Contain("the dataset is not loaded");
        result.Summary.Should().NotContain("InvalidOperationException");
        auditor.Entries.Should().ContainSingle().Which.Outcome.Should().Be(ToolOutcome.Error);
    }

    [Fact]
    public async Task RunAsync_Cancellation_PropagatesAndAuditsCanceled()
    {
        var auditor = new RecordingAuditor();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => ToolRunner.RunAsync<EchoArgs, string>(
            "echo",
            new EchoArgs("hello"),
            async (_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                return ToolResult.Ok("never", "never");
            },
            auditor: auditor,
            cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        auditor.Entries.Should().ContainSingle().Which.Outcome.Should().Be(ToolOutcome.Canceled);
    }

    [Fact]
    public async Task RunAsync_BrokenAuditor_DoesNotBreakTheCall()
    {
        var auditor = new Mock<IToolAuditor>();
        auditor.Setup(a => a.Record(It.IsAny<ToolAuditEntry>()))
            .Throws(new InvalidOperationException("audit sink is down"));

        var result = await ToolRunner.RunAsync(
            "echo",
            new EchoArgs("hello"),
            (args, _) => Task.FromResult(ToolResult.Ok(args.Text!, "Echoed hello.")),
            auditor: auditor.Object);

        result.Data.Should().Be("hello");
        result.Diagnostics.Status.Should().Be(ToolOutcome.Ok);
    }

    [Fact]
    public async Task RunAsync_WithoutValidatorOrAuditor_JustRunsTheBody()
    {
        var result = await ToolRunner.RunAsync(
            "echo",
            new EchoArgs("anything"),
            (args, _) => Task.FromResult(ToolResult.Ok(args.Text!, "Echoed.")));

        result.Data.Should().Be("anything");
    }
}
