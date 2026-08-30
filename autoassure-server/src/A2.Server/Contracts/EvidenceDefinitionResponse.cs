namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>An EvidenceDefinition library item, as returned to the client.</summary>
public record EvidenceDefinitionResponse(
    Guid Id,
    string Name,
    string Description,
    string ExampleValue
);
