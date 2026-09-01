using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to start a Run of one or more Scenarios against an Environment.</summary>
public record CreateRunRequest
{
    [Required, MinLength(1), MaxLength(100)]
    public required IReadOnlyList<Guid> ScenarioIds { get; init; }

    [Required]
    public required Guid EnvironmentId { get; init; }
}
