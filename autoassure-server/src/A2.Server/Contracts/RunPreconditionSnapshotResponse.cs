namespace A2.Server.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- serialized to the JSON response body, not read in-process
/// <summary>A Precondition as it was when a Run was created, as returned to the client.</summary>
public record RunPreconditionSnapshotResponse
{
    public required SnapshotSourceResponse Source { get; init; }
    public required string Name { get; init; }
    public required PreconditionValueSource ValueSource { get; init; }
    public required string ExampleValue { get; init; }
}
