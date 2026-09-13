namespace A2.Server.Models;

public record RunActivitySnapshot
{
    public required RunSnapshotSource Source { get; init; }
    public required int Order { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<RunPreconditionSnapshot> Preconditions { get; init; }
    public required IReadOnlyList<RunEvidenceDefinitionSnapshot> EvidenceDefinitions { get; init; }

    public static RunActivitySnapshot FromActivity(
        Activity activity,
        IReadOnlyList<RunPreconditionSnapshot> preconditions,
        IReadOnlyList<RunEvidenceDefinitionSnapshot> evidenceDefinitions
    ) =>
        new()
        {
            Source = new RunSnapshotSource
            {
                Id = activity.Id,
                CreatedByUserId = activity.CreatedByUserId,
                UpdatedByUserId = activity.UpdatedByUserId,
                CreatedAt = activity.CreatedAt,
                UpdatedAt = activity.UpdatedAt,
            },
            Order = activity.Order,
            Description = activity.Description,
            Preconditions = preconditions,
            EvidenceDefinitions = evidenceDefinitions,
        };
}
