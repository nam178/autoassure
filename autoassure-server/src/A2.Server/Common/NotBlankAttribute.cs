using System.ComponentModel.DataAnnotations;

namespace A2.Server.Common;

/// <summary>
///     Validation attribute for a required string that must not be empty or made
///     up only of
///     whitespace. Use in place of/alongside <see cref="RequiredAttribute" />,
///     which alone does not
///     reject whitespace-only strings.
/// </summary>
public class NotBlankAttribute()
    : ValidationAttribute("{0} must not be empty or whitespace.")
{
    // When the value is null, empty, or made up only of whitespace, then it is invalid.
    public override bool IsValid(object? value)
    {
        return !string.IsNullOrWhiteSpace(value as string);
    }
}