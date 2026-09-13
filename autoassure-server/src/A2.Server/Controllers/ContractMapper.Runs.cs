using A2.Server.Contracts;
using A2.Server.Models;
using ContractActivityResult = A2.Server.Contracts.ActivityResult;
using ContractActivityResultStatus = A2.Server.Contracts.ActivityResultStatus;
using ContractRunStatus = A2.Server.Contracts.RunStatus;
using ContractRunStatusReason = A2.Server.Contracts.RunStatusReason;
using ContractRunStatusUpdateKind = A2.Server.Contracts.RunStatusUpdateKind;
using ContractRunTrigger = A2.Server.Contracts.RunTrigger;
using ModelActivityResult = A2.Server.Models.ActivityResult;
using ModelActivityResultStatus = A2.Server.Models.ActivityResultStatus;
using ModelRunStatus = A2.Server.Models.RunStatus;
using ModelRunStatusReason = A2.Server.Models.RunStatusReason;
using ModelRunStatusUpdateKind = A2.Server.Models.RunStatusUpdateKind;
using ModelRunTrigger = A2.Server.Models.RunTrigger;

namespace A2.Server.Controllers;

/// <summary>Mapping between Run Contracts and domain Models.</summary>
public static partial class ContractMapper
{
    /// <summary>Maps a whole Run to the response Create Run, Get Run and Start Run return. Never carries
    /// the status update log -- see RunResponse's doc.
    ///
    /// <paramref name="maskSensitiveValues"/> defaults to true, masking every sensitive variable's value
    /// (see <see cref="SensitiveValueMasker.Mask"/>) whether or not it is already masked, which is
    /// harmless. Start Run's success path is the only caller that passes false: the winning claim is the
    /// one time real values are ever returned.</summary>
    public static RunResponse ToResponse(this Run run, bool maskSensitiveValues = true) =>
        new()
        {
            Id = run.Id,
            ApplicationId = run.ApplicationId,
            Trigger = run.Trigger.ToContract(),
            Status = run.Status.ToContract(),
            StatusReason = run.StatusReason?.ToContract(),
            TotalActivityCount = run.TotalActivityCount,
            PassedActivityCount = run.PassedActivityCount,
            FailedActivityCount = run.FailedActivityCount,
            SkippedActivityCount = run.SkippedActivityCount,
            Environment = run.Environment.ToResponse(maskSensitiveValues),
            Scenarios = run.Scenarios.Select(s => s.ToResponse()).ToList(),
            LastSeq = run.LastStatusUpdateSequenceNumber,
            TriggeredByUserId = run.TriggeredByUserId,
            CreatedAt = run.CreatedAt,
            StartedAt = run.StartedAt,
            CompletedAt = run.CompletedAt,
            LastHeartbeatAt = run.LastHeartbeatAt,
        };

    public static RunSummaryResponse ToResponse(this RunInfo info) =>
        new()
        {
            Id = info.Id,
            Trigger = info.Trigger.ToContract(),
            Status = info.Status.ToContract(),
            StatusReason = info.StatusReason?.ToContract(),
            TotalActivityCount = info.TotalActivityCount,
            PassedActivityCount = info.PassedActivityCount,
            FailedActivityCount = info.FailedActivityCount,
            SkippedActivityCount = info.SkippedActivityCount,
            CreatedAt = info.CreatedAt,
            StartedAt = info.StartedAt,
            CompletedAt = info.CompletedAt,
            LastHeartbeatAt = info.LastHeartbeatAt,
        };

    public static RunningRunResponse ToResponse(this RunningRun runningRun) =>
        new() { Id = runningRun.Id, StartedAt = runningRun.StartedAt };

    public static RunStatusUpdateResponse ToResponse(this RunStatusUpdate update) =>
        new()
        {
            Seq = update.Seq,
            Kind = update.Kind.ToContract(),
            CreatedAt = update.CreatedAt,
            ActivityResult = update.ActivityResult?.ToResponse(),
        };

    private static SnapshotSourceResponse ToResponse(this RunSnapshotSource source) =>
        new()
        {
            Id = source.Id,
            CreatedByUserId = source.CreatedByUserId,
            UpdatedByUserId = source.UpdatedByUserId,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
        };

    private static RunEnvironmentSnapshotResponse ToResponse(
        this RunEnvironmentSnapshot snapshot,
        bool maskSensitiveValues
    ) =>
        new()
        {
            Source = snapshot.Source.ToResponse(),
            Name = snapshot.Name,
            Classification = snapshot.Classification.ToContract(),
            Variables = snapshot.Variables.Select(v => v.ToResponse(maskSensitiveValues)).ToList(),
        };

    private static RunEnvironmentVariableSnapshotResponse ToResponse(
        this RunEnvironmentVariableSnapshot snapshot,
        bool maskSensitiveValues
    ) =>
        new()
        {
            Key = snapshot.Key,
            Value =
                maskSensitiveValues && snapshot.IsSensitive
                    ? SensitiveValueMasker.Mask(snapshot.Value)
                    : snapshot.Value,
            IsSensitive = snapshot.IsSensitive,
            CreatedByUserId = snapshot.CreatedByUserId,
            UpdatedByUserId = snapshot.UpdatedByUserId,
            CreatedAt = snapshot.CreatedAt,
            UpdatedAt = snapshot.UpdatedAt,
        };

    private static RunScenarioSnapshotResponse ToResponse(this RunScenarioSnapshot snapshot) =>
        new()
        {
            Source = snapshot.Source.ToResponse(),
            Title = snapshot.Title,
            Description = snapshot.Description,
            Folder = snapshot.Folder,
            Tags = snapshot.Tags,
            Activities = snapshot.Activities.Select(a => a.ToResponse()).ToList(),
        };

    private static RunActivitySnapshotResponse ToResponse(this RunActivitySnapshot snapshot) =>
        new()
        {
            Source = snapshot.Source.ToResponse(),
            Order = snapshot.Order,
            Description = snapshot.Description,
            Preconditions = snapshot.Preconditions.Select(p => p.ToResponse()).ToList(),
            EvidenceDefinitions = snapshot.EvidenceDefinitions.Select(e => e.ToResponse()).ToList(),
        };

    private static RunPreconditionSnapshotResponse ToResponse(
        this RunPreconditionSnapshot snapshot
    ) =>
        new()
        {
            Source = snapshot.Source.ToResponse(),
            Name = snapshot.Name,
            ValueSource = snapshot.ValueSource.ToContract(),
            ExampleValue = snapshot.ExampleValue,
        };

    private static RunEvidenceDefinitionSnapshotResponse ToResponse(
        this RunEvidenceDefinitionSnapshot snapshot
    ) =>
        new()
        {
            Source = snapshot.Source.ToResponse(),
            Name = snapshot.Name,
            Description = snapshot.Description,
            ExampleValue = snapshot.ExampleValue,
        };

    public static ContractActivityResult ToResponse(this ModelActivityResult result) =>
        new()
        {
            ScenarioId = result.ScenarioId,
            ActivityId = result.ActivityId,
            Status = result.Status.ToContract(),
            ResolvedPreconditions = result.ResolvedPreconditions,
            Evidence = result.Evidence,
            ContinuationReasoning = result.ContinuationReasoning,
        };

    /// <summary>Maps an appended status update's request body to the domain model, stamping Seq, Kind
    /// and CreatedAt onto it -- the caller supplies only Seq and the ActivityResult
    /// payload.</summary>
    public static RunStatusUpdate ToModel(
        this AppendRunStatusUpdateRequest request,
        DateTimeOffset createdAt
    ) =>
        new()
        {
            Seq = request.Seq,
            Kind = ModelRunStatusUpdateKind.AppendActivityResult,
            CreatedAt = createdAt,
            ActivityResult = request.ActivityResult.ToModel(),
        };

    private static ModelActivityResult ToModel(this ContractActivityResult result) =>
        new()
        {
            ScenarioId = result.ScenarioId,
            ActivityId = result.ActivityId,
            Status = result.Status.ToModel(),
            ResolvedPreconditions = result.ResolvedPreconditions ?? new Dictionary<Guid, string>(),
            Evidence = result.Evidence ?? new Dictionary<Guid, string>(),
            ContinuationReasoning = result.ContinuationReasoning,
        };

    public static ModelRunStatus ToModel(this ContractRunStatus status) =>
        status switch
        {
            ContractRunStatus.Pending => ModelRunStatus.Pending,
            ContractRunStatus.Running => ModelRunStatus.Running,
            ContractRunStatus.Completed => ModelRunStatus.Completed,
            ContractRunStatus.Cancelled => ModelRunStatus.Cancelled,
            ContractRunStatus.Abandoned => ModelRunStatus.Abandoned,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static ContractRunStatus ToContract(this ModelRunStatus status) =>
        status switch
        {
            ModelRunStatus.Pending => ContractRunStatus.Pending,
            ModelRunStatus.Running => ContractRunStatus.Running,
            ModelRunStatus.Completed => ContractRunStatus.Completed,
            ModelRunStatus.Cancelled => ContractRunStatus.Cancelled,
            ModelRunStatus.Abandoned => ContractRunStatus.Abandoned,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    public static ModelRunStatusReason ToModel(this ContractRunStatusReason reason) =>
        reason switch
        {
            ContractRunStatusReason.HeartbeatLost => ModelRunStatusReason.HeartbeatLost,
            ContractRunStatusReason.DeadlineExceeded => ModelRunStatusReason.DeadlineExceeded,
            ContractRunStatusReason.WorkerCrashed => ModelRunStatusReason.WorkerCrashed,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static ContractRunStatusReason ToContract(this ModelRunStatusReason reason) =>
        reason switch
        {
            ModelRunStatusReason.HeartbeatLost => ContractRunStatusReason.HeartbeatLost,
            ModelRunStatusReason.DeadlineExceeded => ContractRunStatusReason.DeadlineExceeded,
            ModelRunStatusReason.WorkerCrashed => ContractRunStatusReason.WorkerCrashed,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static ContractRunTrigger ToContract(this ModelRunTrigger trigger) =>
        trigger switch
        {
            ModelRunTrigger.Manual => ContractRunTrigger.Manual,
            ModelRunTrigger.Scheduled => ContractRunTrigger.Scheduled,
            ModelRunTrigger.Authoring => ContractRunTrigger.Authoring,
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };

    private static ContractRunStatusUpdateKind ToContract(this ModelRunStatusUpdateKind kind) =>
        kind switch
        {
            ModelRunStatusUpdateKind.AppendActivityResult =>
                ContractRunStatusUpdateKind.AppendActivityResult,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static ModelActivityResultStatus ToModel(this ContractActivityResultStatus status) =>
        status switch
        {
            ContractActivityResultStatus.Pending => ModelActivityResultStatus.Pending,
            ContractActivityResultStatus.Running => ModelActivityResultStatus.Running,
            ContractActivityResultStatus.Passed => ModelActivityResultStatus.Passed,
            ContractActivityResultStatus.Failed => ModelActivityResultStatus.Failed,
            ContractActivityResultStatus.Skipped => ModelActivityResultStatus.Skipped,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

    private static ContractActivityResultStatus ToContract(this ModelActivityResultStatus status) =>
        status switch
        {
            ModelActivityResultStatus.Pending => ContractActivityResultStatus.Pending,
            ModelActivityResultStatus.Running => ContractActivityResultStatus.Running,
            ModelActivityResultStatus.Passed => ContractActivityResultStatus.Passed,
            ModelActivityResultStatus.Failed => ContractActivityResultStatus.Failed,
            ModelActivityResultStatus.Skipped => ContractActivityResultStatus.Skipped,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
}
