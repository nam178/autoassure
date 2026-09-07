namespace A2.Server.Models;

/// <summary>A single ordered step within a Scenario, referencing Preconditions/EvidenceDefinitions
/// from its Application's library. Its Id is stable so other records can reference it.</summary>
public record Activity
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required Guid ScenarioId { get; init; }
    public required string Description { get; init; }
    public required int Order { get; init; }
    public IReadOnlyList<Guid> PreconditionIds { get; init; } = [];
    public IReadOnlyList<Guid> EvidenceIds { get; init; } = [];
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
