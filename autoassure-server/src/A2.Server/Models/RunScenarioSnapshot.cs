namespace A2.Server.Models;

public record RunScenarioSnapshot
{
    public required RunSnapshotSource Source { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Folder { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required IReadOnlyList<RunActivitySnapshot> Activities { get; init; }

    public static RunScenarioSnapshot FromScenario(
        Scenario scenario,
        IReadOnlyList<RunActivitySnapshot> activities
    ) =>
        new()
        {
            Source = new RunSnapshotSource
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
