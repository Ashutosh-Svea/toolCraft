using FluentAssertions;
using FluentValidation;
using ToolCraft.Mcp;

namespace ToolCraft.Mcp.Tests;

public class CorrectiveValidationTests
{
    private static readonly string[] Services = ["auth", "checkout", "checkout-v2"];

    private sealed record QueryArgs(string? Service, int? Limit, string? TicketId);

    private sealed class QueryArgsValidator : AbstractValidator<QueryArgs>
    {
        public QueryArgsValidator()
        {
            RuleFor(a => a.Service).MustBeOneOf("service", () => Services);
            RuleFor(a => a.Limit).LessThanOrEqualTo(100);
            RuleFor(a => a.TicketId)
                .NotEmpty()
                .WithCorrectiveError((_, _) => new CorrectiveError
                {
                    Code = "missing_parameter",
                    Parameter = "ticketId",
                    Message = "ticketId is required",
                    Guidance = "Pass the ticket id from search_tickets.",
                });
        }
    }

    [Fact]
    public void MustBeOneOf_RejectsUnknownValues_WithClosestMatches()
    {
        var result = new QueryArgsValidator().Validate(new QueryArgs("checkout-svc", 10, "T-1"));

        result.IsValid.Should().BeFalse();
        var errors = result.ToCorrectiveErrors();
        var error = errors.Should().ContainSingle().Subject;
        error.Code.Should().Be(CorrectiveError.UnknownValueCode);
        error.Parameter.Should().Be("service");
        error.Message.Should().Contain("closest matches: checkout");
        error.ClosestMatches.Should().Contain("checkout").And.Contain("checkout-v2");
    }

    [Fact]
    public void MustBeOneOf_AcceptsNull_SoOptionalParametersStayOptional()
    {
        var result = new QueryArgsValidator().Validate(new QueryArgs(null, 10, "T-1"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void MustBeOneOf_IsCaseInsensitive()
    {
        var result = new QueryArgsValidator().Validate(new QueryArgs("AUTH", 10, "T-1"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void WithCorrectiveError_ReplacesMessageAndState()
    {
        var result = new QueryArgsValidator().Validate(new QueryArgs("auth", 10, ""));

        var errors = result.ToCorrectiveErrors();
        var error = errors.Should().ContainSingle().Subject;
        error.Code.Should().Be("missing_parameter");
        error.Message.Should().Be("ticketId is required");
        error.Guidance.Should().Contain("search_tickets");
    }

    [Fact]
    public void ToCorrectiveErrors_MapsPlainFailures_ToSnakeCaseCodesAndCamelCaseParameters()
    {
        var result = new QueryArgsValidator().Validate(new QueryArgs("auth", 500, "T-1"));

        var errors = result.ToCorrectiveErrors();
        var error = errors.Should().ContainSingle().Subject;
        error.Code.Should().Be("less_than_or_equal");
        error.Parameter.Should().Be("limit");
        error.Message.Should().NotBeNullOrWhiteSpace();
        error.Guidance.Should().NotBeNullOrWhiteSpace();
    }
}
