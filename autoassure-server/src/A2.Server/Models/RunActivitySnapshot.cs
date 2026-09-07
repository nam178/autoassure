namespace A2.Server.Models;

/// <summary>An Activity copied into a Run at create time, carrying its own Preconditions and
/// EvidenceDefinitions copied whole rather than by id, so a run's record of what it checked and captured
/// never changes when the library is edited later.</summary>
public record RunActivitySnapshot
{
    public required SnapshotSource Source { get; init; }
    public required int Order { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<RunPreconditionSnapshot> Preconditions { get; init; }
    public required IReadOnlyList<RunEvidenceDefinitionSnapshot> EvidenceDefinitions { get; init; }

    /// <summary>Copies an Activity as it is right now, together with the already-built snapshots of the
    /// Preconditions and EvidenceDefinitions it currently references. OrganizationId, ApplicationId and
    /// ScenarioId are left out -- they already live on the Run header and on the enclosing scenario
    /// snapshot, and a Run never spans two of any of them. PreconditionIds and EvidenceIds are left out in
    /// favor of the copied-whole lists, so a deleted or edited library row can never orphan the
    /// run.</summary>
    public static RunActivitySnapshot FromActivity(
        Activity activity,
        IReadOnlyList<RunPreconditionSnapshot> preconditions,
        IReadOnlyList<RunEvidenceDefinitionSnapshot> evidenceDefinitions
    ) =>
        new()
        {
            Source = new SnapshotSource
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
