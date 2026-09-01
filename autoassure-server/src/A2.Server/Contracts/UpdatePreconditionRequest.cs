using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to edit an existing Precondition.</summary>
public record UpdatePreconditionRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [EnumDataType(typeof(PreconditionValueSource))]
    public required PreconditionValueSource ValueSource { get; init; }

    [Required(AllowEmptyStrings = true), MaxLength(500)]
    public required string ExampleValue { get; init; }
}
