using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>A single step within a Scenario, as submitted by the client. PreconditionIds/EvidenceIds
/// must reference existing library rows in the same Application.</summary>
public record ActivityRequest
{
    [Required, MaxLength(2000)]
    public required string Description { get; init; }

    [MaxLength(50)]
    public IReadOnlyList<Guid>? PreconditionIds { get; init; }

    [MaxLength(50)]
    public IReadOnlyList<Guid>? EvidenceIds { get; init; }
}
