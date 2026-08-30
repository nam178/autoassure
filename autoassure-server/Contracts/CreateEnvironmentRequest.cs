using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to create a new Environment for an Application. No Variables at creation —
/// set those afterward via <c>PUT /environments/{id}/variables/{key}</c>.</summary>
public record CreateEnvironmentRequest(
    [Required, MaxLength(100)] string Name,
    [EnumDataType(typeof(EnvironmentClassification))] EnvironmentClassification Classification
);
