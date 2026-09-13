namespace A2.Server.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- serialized to the JSON response body, not read in-process
/// <summary>A Run's identity and execution state only -- no Environment, no Scenarios -- exactly what the
/// Application's Runs panel shows for one row of the list.</summary>
public record RunSummaryResponse
{
    public required Guid Id { get; init; }
    public required RunTrigger Trigger { get; init; }
    public required RunStatus Status { get; init; }

    public required int TotalActivityCount { get; init; }
    public required int PassedActivityCount { get; init; }
    public required int FailedActivityCount { get; init; }
    public required int SkippedActivityCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>When the owning worker last proved it was alive. Null while Status is Pending. A Run
    /// stuck on Running with an old LastHeartbeatAt has likely lost its worker -- Status alone does not
    /// tell you that.</summary>
    public DateTimeOffset? LastHeartbeatAt { get; init; }
}
