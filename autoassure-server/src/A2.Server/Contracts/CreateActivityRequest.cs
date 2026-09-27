using System.ComponentModel.DataAnnotations;
using A2.Server.Common;

namespace A2.Server.Contracts;

/// <summary>
/// Request body to add a new Activity to a Scenario.
/// PreconditionIds/EvidenceIds must
/// each reference existing library rows in the Scenario's Application.
/// </summary>
public record CreateActivityRequest
{
    [Required]
    [MaxLength(2000)]
    public required string Description { get; init; }

    /// <summary>
    /// Zero-based display position within the Scenario, chosen by the caller.
    /// Not checked for uniqueness -- use the reorder endpoint to fix up positions.
    /// </summary>
    [Range(0, Quota.MaxActivityCountPerScenario - 1)]
    public required int Order { get; init; }

    [MaxLength(15)]
    public IReadOnlyList<Guid>? PreconditionIds { get; init; }

    [MaxLength(15)]
    public IReadOnlyList<Guid>? EvidenceIds { get; init; }
}
