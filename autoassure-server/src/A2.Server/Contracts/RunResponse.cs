namespace A2.Server.Contracts;

/// <summary>
///     A Run's identity, execution state and what it ran, as returned to the
///     client. Never carries
///     the status update log -- LastSeq and Status are what tell a client whether
///     it is worth polling List
///     Run Status Updates and when to stop. ApplicationId is included even under
///     the nested
///     <c>/applications/{applicationId}/runs/{runId}</c> route because the
///     authoring create route
///     (<c>POST /scenarios/{scenarioId}/runs</c>) is flat and returns this same
///     shape -- without it, a client
///     following an authoring Run would have no way to build its polling URLs.
/// </summary>
public record RunResponse
{
    public required Guid Id { get; init; }
    public required Guid ApplicationId { get; init; }
    public required RunTrigger Trigger { get; init; }
    public required RunStatus Status { get; init; }

    public required int TotalActivityCount { get; init; }
    public required int PassedActivityCount { get; init; }
    public required int FailedActivityCount { get; init; }
    public required int SkippedActivityCount { get; init; }

    public required RunEnvironmentSnapshotResponse Environment { get; init; }

    /// <summary>One snapshot per Scenario the Run ran, in no particular order.</summary>
    public required IReadOnlyList<RunScenarioSnapshotResponse> Scenarios { get; init; }

    /// <summary>
    ///     The highest sequence number appended to this Run's status update log so
    ///     far. A client
    ///     that already holds up to this sequence has nothing new to poll for.
    /// </summary>
    public required long LastSeq { get; init; }

    /// <summary>
    ///     Who triggered this Run. Null for a Scheduled Run -- a system timer has
    ///     no user id.
    /// </summary>
    public Guid? TriggeredByUserId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    ///     When the owning worker last proved it was alive. Null while Status is
    ///     Pending. A Run
    ///     stuck on Running with an old LastHeartbeatAt has likely lost its worker --
    ///     Status alone does not
    ///     tell you that.
    /// </summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }
}
