namespace A2.Server.Models;

/// <summary>An EvidenceDefinition copied whole into a Run at create time, so a run's record of what it
/// captured never changes when the library definition is edited or deleted later.</summary>
public record RunEvidenceDefinitionSnapshot
{
    public required SnapshotSource Source { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ExampleValue { get; init; }

    /// <summary>Copies an EvidenceDefinition as it is right now. OrganizationId and ApplicationId are left
    /// out -- they already live on the Run header, and a Run never spans two of either.</summary>
    public static RunEvidenceDefinitionSnapshot FromEvidenceDefinition(
        EvidenceDefinition evidenceDefinition
    ) =>
        new()
        {
            Source = new SnapshotSource
            {
                Id = evidenceDefinition.Id,
                CreatedByUserId = evidenceDefinition.CreatedByUserId,
                UpdatedByUserId = evidenceDefinition.UpdatedByUserId,
                CreatedAt = evidenceDefinition.CreatedAt,
                UpdatedAt = evidenceDefinition.UpdatedAt,
            },
            Name = evidenceDefinition.Name,
            Description = evidenceDefinition.Description,
            ExampleValue = evidenceDefinition.ExampleValue,
        };
}
