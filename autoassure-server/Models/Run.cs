namespace A2.Server.Models;

/// <summary>A single execution of one or more Scenarios -- either a one-off Try (Kind=Try, exactly
/// one Scenario) or a full Run (Kind=Run, one or more Scenarios). Created in Pending status; how it
/// transitions is future work -- nothing here actually executes anything yet.</summary>
public record Run
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required RunKind Kind { get; init; }
    public required Guid ApplicationId { get; init; }
    public required IReadOnlyList<Guid> ScenarioIds { get; init; }
    public required Guid EnvironmentId { get; init; }
    public required RunStatus Status { get; init; }
    public int TotalActivityCount { get; init; }
    public int PassedActivityCount { get; init; }
    public int FailedActivityCount { get; init; }
    public int SkippedActivityCount { get; init; }
    public required Guid TriggeredByUserId { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public IReadOnlyList<ActivityResult> ActivityResults { get; init; } = [];
}

/// <summary>Whether a Run record is a one-off Try or a full Run.</summary>
public enum RunKind
{
    Try,
    Run,
}

/// <summary>A Run's lifecycle state only -- carries no pass/fail judgment; see the activity counts for that.</summary>
public enum RunStatus
{
    Pending,
    Running,
    Completed,
}
