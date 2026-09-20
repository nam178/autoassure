namespace A2.Server.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- serialized to the JSON response body, not read in-process
/// <summary>
///     An EvidenceDefinition as it was when a Run was created, as returned to
///     the client.
/// </summary>
public record RunEvidenceDefinitionSnapshotResponse
{
    public required SnapshotSourceResponse Source { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string ExampleValue { get; init; }
}
