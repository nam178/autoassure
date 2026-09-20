namespace A2.Server.Models;

/// <summary>
///     A Precondition copied whole into a Run at create time, so a run's record of
///     what it checked
///     never changes when the library definition is edited or deleted later.
/// </summary>
public record RunPreconditionSnapshot
{
    public required RunSnapshotSource Source { get; init; }
    public required string Name { get; init; }
    public required PreconditionValueSource ValueSource { get; init; }
    public required string ExampleValue { get; init; }

    /// <summary>
    ///     Copies a Precondition as it is right now. OrganizationId and ApplicationId
    ///     are left out --
    ///     they already live on the Run, and a Run never spans two of either.
    /// </summary>
    public static RunPreconditionSnapshot FromPrecondition(
        Precondition precondition
    )
    {
        return new RunPreconditionSnapshot
        {
            Source = new RunSnapshotSource
            {
                Id = precondition.Id,
                CreatedByUserId = precondition.CreatedByUserId,
                UpdatedByUserId = precondition.UpdatedByUserId,
                CreatedAt = precondition.CreatedAt,
                UpdatedAt = precondition.UpdatedAt,
            },
            Name = precondition.Name,
            ValueSource = precondition.ValueSource,
            ExampleValue = precondition.ExampleValue,
        };
    }
}