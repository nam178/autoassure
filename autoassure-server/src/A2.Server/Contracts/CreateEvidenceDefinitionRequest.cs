using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to add an EvidenceDefinition to an Application's library.</summary>
public record CreateEvidenceDefinitionRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    [Required(AllowEmptyStrings = true), MaxLength(500)]
    public required string Description { get; init; }

    [Required(AllowEmptyStrings = true), MaxLength(500)]
    public required string ExampleValue { get; init; }
}
