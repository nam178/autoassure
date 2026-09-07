namespace A2.Server.Models;

/// <summary>A Scenario copied into a Run at create time, carrying its Activities in order, so a run's
/// record of what it tested never changes when the Scenario is edited or deleted later.</summary>
public record RunScenarioSnapshot
{
    public required SnapshotSource Source { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Folder { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required IReadOnlyList<RunActivitySnapshot> Activities { get; init; }

    /// <summary>Copies a Scenario as it is right now, together with the already-built snapshots of its
    /// Activities in order. OrganizationId and ApplicationId are left out -- they already live on the Run
    /// header, and a Run never spans two of either. ActivityCount is left out because it is derived and
    /// denormalized on the live Scenario, and can drift from the real count; the Run header's
    /// TotalActivityCount is taken by counting <paramref name="activities"/> instead.</summary>
    public static RunScenarioSnapshot FromScenario(
        Scenario scenario,
        IReadOnlyList<RunActivitySnapshot> activities
    ) =>
        new()
        {
            Source = new SnapshotSource
            {
                Id = scenario.Id,
                CreatedByUserId = scenario.CreatedByUserId,
                UpdatedByUserId = scenario.UpdatedByUserId,
                CreatedAt = scenario.CreatedAt,
                UpdatedAt = scenario.UpdatedAt,
            },
            Title = scenario.Title,
            Description = scenario.Description,
            Folder = scenario.Folder,
            Tags = scenario.Tags,
            Activities = activities,
        };
}
