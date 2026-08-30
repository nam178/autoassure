namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>An Environment and its assembled Variables, as returned to the client.</summary>
public record EnvironmentResponse(
    Guid Id,
    string Name,
    EnvironmentClassification Classification,
    IReadOnlyList<EnvironmentVariableResponse> Variables
);
