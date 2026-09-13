namespace A2.Server.Models;

public record RunInfo
{
    public required Guid Id { get; init; }
    public required RunTrigger Trigger { get; init; }

    /// <summary>Running does not by itself mean the owning worker is still alive -- see
    /// <see cref="Run.Status"/>.</summary>
    public required RunStatus Status { get; init; }

    public int TotalActivityCount { get; init; }
    public int PassedActivityCount { get; init; }
    public int FailedActivityCount { get; init; }
    public int SkippedActivityCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>When the owning worker last proved it was alive -- see <see cref="Run.LastHeartbeatAt"/>.
    /// Null while the Run is Pending, since it has no owner yet.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }
}
