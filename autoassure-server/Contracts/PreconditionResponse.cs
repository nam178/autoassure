namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>A Precondition library item, as returned to the client.</summary>
public record PreconditionResponse(
    Guid Id,
    string Name,
    PreconditionValueSource ValueSource,
    string ExampleValue
);
