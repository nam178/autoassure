namespace A2.Server.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- serialized to the JSON response body, not read in-process
/// <summary>
///     A Scenario as it was when a Run was created, together with its Activities
///     in order, as
///     returned to the client. Never changes when the live Scenario is edited or
///     deleted afterward.
/// </summary>
public record RunScenarioSnapshotResponse
{
    public required SnapshotSourceResponse Source { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Folder { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }

    public required IReadOnlyList<RunActivitySnapshotResponse> Activities { get; init; }
}
