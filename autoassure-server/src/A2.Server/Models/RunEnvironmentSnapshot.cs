namespace A2.Server.Models;

/// <summary>
///     A snapshot of a run's environment variables taken when it created.
/// </summary>
public record RunEnvironmentSnapshot
{
    public required RunSnapshotSource Source { get; init; }
    public required string Name { get; init; }
    public required EnvironmentClassification Classification { get; init; }

    public required IReadOnlyList<RunEnvironmentVariableSnapshot> Variables { get; init; }

    public static RunEnvironmentSnapshot FromEnvironment(
        Environment environment,
        IReadOnlyList<RunEnvironmentVariableSnapshot> variables
    )
    {
        return new RunEnvironmentSnapshot
        {
            Source = new RunSnapshotSource
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

    /// <summary>
    ///     Masks every sensitive variable's value (see
    ///     <see cref="RunEnvironmentVariableSnapshot.Masked" />), leaving
    ///     non-sensitive ones untouched.
    ///     Idempotent: calling it again on the result returns an equivalent snapshot.
    /// </summary>
    public RunEnvironmentSnapshot Masked()
    {
        return this with
        {
            Variables = Variables.Select(v => v.Masked()).ToList(),
        };
    }
}
