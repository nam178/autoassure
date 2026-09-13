namespace A2.Server.Models;

public record Run
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required RunTrigger Trigger { get; init; }
    public required RunEnvironmentSnapshot Environment { get; init; }
    public required IReadOnlyList<RunScenarioSnapshot> Scenarios { get; init; }

    /// <summary>Running does not by itself mean the owning worker is still alive -- it may have crashed
    /// or been killed without anything having noticed yet. Treat Running as "not yet terminal," and
    /// check <see cref="LastHeartbeatAt"/> to tell whether it is actually making progress.</summary>
    public required RunStatus Status { get; init; }
    public RunStatusReason? StatusReason { get; init; }
    public int TotalActivityCount { get; init; }
    public int PassedActivityCount { get; init; }
    public int FailedActivityCount { get; init; }
    public int SkippedActivityCount { get; init; }
    public long LastStatusUpdateSequenceNumber { get; init; }

    /// <summary>Who triggered this Run. A person triggers a Manual or Authoring run. Null means a system
    /// timer triggered it.</summary>
    public Guid? TriggeredByUserId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>When the owning worker last proved it was alive. Null while the Run is Pending, since it
    /// has no owner yet.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }
}

public enum RunTrigger
{
    Manual,
    Scheduled,
    Authoring,
}

public enum RunStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Abandoned,
}

public enum RunStatusReason
{
    HeartbeatLost,
    DeadlineExceeded,
    WorkerCrashed,
}
