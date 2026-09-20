using System.ComponentModel.DataAnnotations;
using A2.Server.Common;

namespace A2.Server.Contracts;

/// <summary>
///     Request body to start a Manual Run of one or more Scenarios against an
///     Environment. Every id
///     in ScenarioIds must reference a Scenario belonging to the Application named
///     in the URL.
/// </summary>
public record CreateRunRequest
{
    [Required]
    [MinLength(1)]
    [MaxLength(Quota.MaxScenariosPerRun)]
    public required IReadOnlyList<Guid> ScenarioIds { get; init; }

    public required Guid EnvironmentId { get; init; }
}