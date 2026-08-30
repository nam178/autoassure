namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>A Run over one or more Scenarios, as returned to the client. Created in Pending status --
/// nothing here executes anything yet.</summary>
public record RunResponse(
    Guid Id,
    IReadOnlyList<Guid> ScenarioIds,
    Guid EnvironmentId,
    RunStatus Status,
    int TotalActivityCount,
    int PassedActivityCount,
    int FailedActivityCount,
    int SkippedActivityCount,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt
);
