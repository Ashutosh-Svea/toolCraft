using System.Text;
using FluentValidation;
using FluentValidation.Results;

namespace ToolCraft.Mcp;

/// <summary>
/// Bridges FluentValidation to corrective errors: rules can attach a
/// <see cref="CorrectiveError"/> to a failure, and any validation result can be
/// converted into the corrective list an envelope carries. Failures without an
/// attached error still convert, so nothing ever degrades to a bare failure.
/// </summary>
public static class CorrectiveValidation
{
    /// <summary>
    /// Requires the value, when set, to be one of the known values (case insensitive).
    /// A rejected value fails with an "unknown_value" corrective error that lists the
    /// closest known values. Null passes: optional parameters stay optional.
    /// </summary>
    /// <param name="rule">The rule builder for an optional string parameter.</param>
    /// <param name="parameterName">The schema name of the parameter, used in the error text.</param>
    /// <param name="knownValues">Provider of the accepted values, evaluated at validation time.</param>
    public static IRuleBuilderOptions<T, string?> MustBeOneOf<T>(
        this IRuleBuilder<T, string?> rule,
        string parameterName,
        Func<IEnumerable<string>> knownValues)
    {
        ArgumentNullException.ThrowIfNull(knownValues);
        return rule
            .Must(value => value is null
                || knownValues().Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithState((_, value) => CorrectiveError.UnknownValue(parameterName, value!, knownValues()))
            .WithMessage((_, value) => CorrectiveError.UnknownValue(parameterName, value!, knownValues()).Message);
    }

    /// <summary>
    /// Attaches a corrective error to a rule, replacing the failure message with the
    /// error's message so text and structure never disagree.
    /// </summary>
    /// <param name="rule">The rule to attach the error to.</param>
    /// <param name="factory">Builds the corrective error from the validated instance and the rejected value.</param>
    public static IRuleBuilderOptions<T, TProperty> WithCorrectiveError<T, TProperty>(
        this IRuleBuilderOptions<T, TProperty> rule,
        Func<T, TProperty, CorrectiveError> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return rule
            .WithState((instance, value) => factory(instance, value))
            .WithMessage((instance, value) => factory(instance, value).Message);
    }

    /// <summary>
    /// Converts a validation result into corrective errors. Failures that carry a
    /// <see cref="CorrectiveError"/> as custom state are passed through; all others are
    /// mapped to a generic corrective error with a snake_case code and camelCase parameter.
    /// </summary>
    /// <param name="result">The FluentValidation result to convert.</param>
    public static IReadOnlyList<CorrectiveError> ToCorrectiveErrors(this ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Errors.Select(ToCorrectiveError).ToArray();
    }

    private static CorrectiveError ToCorrectiveError(ValidationFailure failure)
        => failure.CustomState as CorrectiveError
            ?? new CorrectiveError
            {
                Code = ToSnakeCase(TrimValidatorSuffix(failure.ErrorCode ?? "invalid_value")),
                Parameter = string.IsNullOrEmpty(failure.PropertyName)
                    ? null
                    : ToCamelCase(failure.PropertyName),
                Message = failure.ErrorMessage,
                Guidance = "Fix the parameter and retry.",
            };

    private static string TrimValidatorSuffix(string errorCode)
        => errorCode.EndsWith("Validator", StringComparison.Ordinal)
            ? errorCode[..^"Validator".Length]
            : errorCode;

    private static string ToCamelCase(string name)
        => string.Join('.', name.Split('.').Select(segment =>
            segment.Length == 0 ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
