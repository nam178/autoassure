namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>A single-Scenario Try, as returned to the client. Created in Pending status -- nothing
/// here executes anything yet.</summary>
public record TryScenarioResponse(
    Guid Id,
    Guid ScenarioId,
    Guid EnvironmentId,
    RunStatus Status,
    int TotalActivityCount,
    int PassedActivityCount,
    int FailedActivityCount,
    int SkippedActivityCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);
