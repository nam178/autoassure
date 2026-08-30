using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to update an existing Environment's Name/Classification.</summary>
public record UpdateEnvironmentRequest(
    [Required, MaxLength(100)] string Name,
    [EnumDataType(typeof(EnvironmentClassification))] EnvironmentClassification Classification
);
