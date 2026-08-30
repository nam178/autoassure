using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to edit an existing EvidenceDefinition.</summary>
public record UpdateEvidenceDefinitionRequest(
    [Required, MaxLength(200)] string Name,
    [Required(AllowEmptyStrings = true), MaxLength(500)] string Description,
    [Required(AllowEmptyStrings = true), MaxLength(500)] string ExampleValue
);
