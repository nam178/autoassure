namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>A single step within a Scenario, as returned to the client.</summary>
public record ActivityResponse(
    Guid Id,
    string Description,
    IReadOnlyList<Guid> PreconditionIds,
    IReadOnlyList<Guid> EvidenceIds
);
