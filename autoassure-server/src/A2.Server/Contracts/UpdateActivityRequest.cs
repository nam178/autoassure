using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>
///     Request body to edit an existing Activity's
///     Description/PreconditionIds/EvidenceIds.
///     Does not change the Activity's Order -- use the reorder endpoint for that.
/// </summary>
public record UpdateActivityRequest
{
    [Required]
    [MaxLength(2000)]
    public required string Description { get; init; }

    [MaxLength(15)]
    public IReadOnlyList<Guid>? PreconditionIds { get; init; }

    [MaxLength(15)]
    public IReadOnlyList<Guid>? EvidenceIds { get; init; }
}
