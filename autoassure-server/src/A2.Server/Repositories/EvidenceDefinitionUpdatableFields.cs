namespace A2.Server.Repositories;

/// <summary>The only EvidenceDefinition fields <see cref="IEvidenceDefinitionRepository.TryUpdateAsync"/> is allowed to change.</summary>
public record EvidenceDefinitionUpdatableFields
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ExampleValue { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
