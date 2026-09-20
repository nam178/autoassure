using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>
///     Request body to reorder a Scenario's Activities. Must contain exactly one
///     entry per
///     Activity currently in the Scenario, as a permutation of their ids.
/// </summary>
public record ReorderActivitiesRequest
{
    [Required]
    [MaxLength(90)]
    public required IReadOnlyList<Guid> OrderedActivityIds { get; init; }
}