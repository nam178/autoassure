using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to add a Precondition to an Application's library.</summary>
public record CreatePreconditionRequest(
    [Required, MaxLength(200)] string Name,
    [EnumDataType(typeof(PreconditionValueSource))] PreconditionValueSource ValueSource,
    [Required(AllowEmptyStrings = true), MaxLength(500)] string ExampleValue
);
