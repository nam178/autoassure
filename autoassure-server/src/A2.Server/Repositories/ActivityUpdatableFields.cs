namespace A2.Server.Repositories;

/// <summary>The only Activity fields <see cref="IActivityRepository.TryUpdateAsync"/> is allowed to change.</summary>
public record ActivityUpdatableFields
{
    public required string Description { get; init; }
    public IReadOnlyList<Guid> PreconditionIds { get; init; } = [];
    public IReadOnlyList<Guid> EvidenceIds { get; init; } = [];
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
