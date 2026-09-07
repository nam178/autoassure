namespace A2.Server.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- serialized to the JSON response body, not read in-process
/// <summary>An Activity as it was when a Run was created, together with the Preconditions and
/// EvidenceDefinitions it referenced at that moment, as returned to the client.</summary>
public record RunActivitySnapshotResponse
{
    public required SnapshotSourceResponse Source { get; init; }
    public required int Order { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<RunPreconditionSnapshotResponse> Preconditions { get; init; }
    public required IReadOnlyList<RunEvidenceDefinitionSnapshotResponse> EvidenceDefinitions { get; init; }
}
