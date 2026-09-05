using System.ComponentModel.DataAnnotations;
using A2.Server.Common;

namespace A2.Server.Contracts;

/// <summary>Request body to create a new Environment for an Application. No Variables at creation —
/// set those afterward via <c>PUT /environments/{id}/variables/{key}</c>.</summary>
public record CreateEnvironmentRequest
{
    [Required, NotBlank, MaxLength(100)]
    public required string Name { get; init; }

    [EnumDataType(typeof(EnvironmentClassification))]
    public required EnvironmentClassification Classification { get; init; }
}
