using System.ComponentModel.DataAnnotations;
using A2.Server.Common;

namespace A2.Server.Contracts;

/// <summary>Request body to add a Precondition to an Application's library.</summary>
public record CreatePreconditionRequest
{
    [Required]
    [NotBlank]
    [MaxLength(200)]
    public required string Name { get; init; }

    [EnumDataType(typeof(PreconditionValueSource))]
    public required PreconditionValueSource ValueSource { get; init; }

    [Required(AllowEmptyStrings = true)]
    [MaxLength(10000)]
    public required string ExampleValue { get; init; }
}