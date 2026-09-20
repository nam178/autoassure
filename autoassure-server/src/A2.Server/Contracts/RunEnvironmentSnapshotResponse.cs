namespace A2.Server.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- serialized to the JSON response body, not read in-process
/// <summary>
///     The Environment a Run ran against, as it was when the Run was created, as
///     returned to the
///     client.
/// </summary>
public record RunEnvironmentSnapshotResponse
{
    public required SnapshotSourceResponse Source { get; init; }
    public required string Name { get; init; }
    public required EnvironmentClassification Classification { get; init; }

    public required IReadOnlyList<RunEnvironmentVariableSnapshotResponse> Variables { get; init; }
}
