using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to add an EvidenceDefinition to an Application's library.</summary>
public record CreateEvidenceDefinitionRequest(
    [Required, MaxLength(200)] string Name,
    [Required(AllowEmptyStrings = true), MaxLength(500)] string Description,
    [Required(AllowEmptyStrings = true), MaxLength(500)] string ExampleValue
);
