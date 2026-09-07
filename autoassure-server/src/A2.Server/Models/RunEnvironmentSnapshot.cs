namespace A2.Server.Models;

/// <summary>An Environment copied into a Run at create time, carrying its variables, so a run's record of
/// what it ran against never changes when the Environment is edited later.</summary>
public record RunEnvironmentSnapshot
{
    public required SnapshotSource Source { get; init; }
    public required string Name { get; init; }
    public required EnvironmentClassification Classification { get; init; }
    public required IReadOnlyList<RunEnvironmentVariableSnapshot> Variables { get; init; }

    /// <summary>Copies an Environment as it is right now, together with the already-built snapshots of
    /// its variables. OrganizationId and ApplicationId are left out -- they already live on the Run
    /// header, and a Run never spans two of either.</summary>
    public static RunEnvironmentSnapshot FromEnvironment(
        Environment environment,
        IReadOnlyList<RunEnvironmentVariableSnapshot> variables
    ) =>
        new()
        {
            Source = new SnapshotSource
            {
                Id = environment.Id,
                CreatedByUserId = environment.CreatedByUserId,
                UpdatedByUserId = environment.UpdatedByUserId,
                CreatedAt = environment.CreatedAt,
                UpdatedAt = environment.UpdatedAt,
            },
            Name = environment.Name,
            Classification = environment.Classification,
            Variables = variables,
        };
}
