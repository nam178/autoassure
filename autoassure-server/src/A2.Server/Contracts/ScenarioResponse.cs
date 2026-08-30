namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>A Scenario, as returned to the client.</summary>
public record ScenarioResponse(
    Guid Id,
    string Title,
    string Description,
    string Folder,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ActivityResponse> Activities
);
