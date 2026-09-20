namespace A2.Server.Contracts;

/// <summary>A single step within a Scenario, as returned to the client.</summary>
public record ActivityResponse
{
    public required Guid Id { get; init; }
    public required Guid ScenarioId { get; init; }
    public required string Description { get; init; }
    public required int Order { get; init; }
    public required IReadOnlyList<Guid> PreconditionIds { get; init; }
    public required IReadOnlyList<Guid> EvidenceIds { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}