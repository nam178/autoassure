namespace A2.Server.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- serialized to the JSON response body, not read in-process
/// <summary>A Run's identity and execution state only -- no Environment, no Scenarios -- exactly what the
/// Application's Runs panel shows for one row of the list. Authoring Runs never appear here; fetch one by
/// id instead.</summary>
public record RunSummaryResponse
{
    public required Guid Id { get; init; }
    public required RunTrigger Trigger { get; init; }
    public required RunStatus Status { get; init; }

    /// <summary>Only set when Status is Abandoned.</summary>
    public RunStatusReason? StatusReason { get; init; }

    public required int TotalActivityCount { get; init; }
    public required int PassedActivityCount { get; init; }
    public required int FailedActivityCount { get; init; }
    public required int SkippedActivityCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
