using System.ComponentModel.DataAnnotations;
using A2.Server.Common;

namespace A2.Server.Contracts;

/// <summary>Request body to update an existing Environment's Name/Classification.</summary>
public record UpdateEnvironmentRequest
{
    [Required]
    [NotBlank]
    [MaxLength(100)]
    public required string Name { get; init; }

    [EnumDataType(typeof(EnvironmentClassification))]
    public required EnvironmentClassification Classification { get; init; }
}