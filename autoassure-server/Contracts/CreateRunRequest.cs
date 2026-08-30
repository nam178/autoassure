using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to start a Run of one or more Scenarios against an Environment.</summary>
public record CreateRunRequest(
    [Required, MinLength(1)] IReadOnlyList<Guid> ScenarioIds,
    [Required] Guid EnvironmentId
);
