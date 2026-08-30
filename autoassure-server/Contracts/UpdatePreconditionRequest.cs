using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to edit an existing Precondition.</summary>
public record UpdatePreconditionRequest(
    [Required, MaxLength(200)] string Name,
    [EnumDataType(typeof(PreconditionValueSource))] PreconditionValueSource ValueSource,
    [Required(AllowEmptyStrings = true), MaxLength(500)] string ExampleValue
);
