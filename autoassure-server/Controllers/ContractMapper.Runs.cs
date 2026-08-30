using A2.Server.Contracts;
using ContractRunStatus = A2.Server.Contracts.RunStatus;
using ModelRunStatus = A2.Server.Models.RunStatus;
using Run = A2.Server.Models.Run;

namespace A2.Server.Controllers;

/// <summary>Mapping between Run Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    public static RunResponse ToResponse(this Run run) =>
        new(
            run.Id,
            run.ScenarioIds,
            run.EnvironmentId,
            run.Status.ToContract(),
            run.TotalActivityCount,
            run.PassedActivityCount,
            run.FailedActivityCount,
            run.SkippedActivityCount,
            run.StartedAt,
            run.CompletedAt
        );

    /// <summary>Maps a single-Scenario Run to the response returned by the Try-a-Scenario endpoints.</summary>
    public static TryScenarioResponse ToTryResponse(this Run run) =>
        new(
            run.Id,
            run.ScenarioIds[0],
            run.EnvironmentId,
            run.Status.ToContract(),
            run.TotalActivityCount,
            run.PassedActivityCount,
            run.FailedActivityCount,
            run.SkippedActivityCount,
            run.StartedAt,
            run.CompletedAt
        );

    public static ContractRunStatus ToContract(this ModelRunStatus status) =>
        status switch
        {
            ModelRunStatus.Pending => ContractRunStatus.Pending,
            ModelRunStatus.Running => ContractRunStatus.Running,
            ModelRunStatus.Completed => ContractRunStatus.Completed,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
}
