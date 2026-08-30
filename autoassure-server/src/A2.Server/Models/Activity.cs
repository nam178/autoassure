namespace A2.Server.Models;

/// <summary>A single step within a Scenario, embedded and ordered. Its Id is stable within the
/// Scenario so a Run's ActivityResult can reference it.</summary>
public record Activity
{
    public required Guid Id { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<Guid> PreconditionIds { get; init; } = [];
    public IReadOnlyList<Guid> EvidenceIds { get; init; } = [];
}
