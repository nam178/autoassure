using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Request body to append a new Activity to a Scenario. PreconditionIds/EvidenceIds must
/// each reference existing library rows in the Scenario's Application.</summary>
public record CreateActivityRequest
{
    [Required, MaxLength(2000)]
    public required string Description { get; init; }

    [MaxLength(15)]
    public IReadOnlyList<Guid>? PreconditionIds { get; init; }

    [MaxLength(15)]
    public IReadOnlyList<Guid>? EvidenceIds { get; init; }
}
