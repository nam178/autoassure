namespace A2.Server.Models;

// ReSharper disable UnusedAutoPropertyAccessor.Global -- the Runs panel this exists for is Task 10's
// API layer, not built yet; every field here is exercised by DynamoDbRunRepositoryTests in the
// meantime.
/// <summary>A Run's identity and execution state, with no Environment snapshot and no Scenario data --
/// exactly what the Application's Runs panel shows for one row of the list. List Runs answers from a
/// sparse index that never holds anything beyond these fields (see fix_run_design.md section 3), so
/// this is a distinct shape from <see cref="Run"/> rather than a partially filled one: nothing here can
/// be turned into a full <see cref="Run"/> without a separate Get.</summary>
public record RunSummary
{
    public required Guid Id { get; init; }
    public required RunTrigger Trigger { get; init; }
    public required RunStatus Status { get; init; }

    /// <summary>Only set when Status is Abandoned -- see <see cref="Run.StatusReason"/>.</summary>
    public RunStatusReason? StatusReason { get; init; }

    public int TotalActivityCount { get; init; }
    public int PassedActivityCount { get; init; }
    public int FailedActivityCount { get; init; }
    public int SkippedActivityCount { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
