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
    /// <summary>Assembles a RunDetail (header plus Scenario snapshots) into the full response returned
    /// by Create Run, Get Run and Start Run. Never carries the status update log -- see RunResponse's
    /// doc.
    ///
    /// <paramref name="maskSensitiveValues"/> defaults to true, masking every sensitive variable's value
    /// (see <see cref="SensitiveValueMasker.Mask"/>) regardless of whether storage currently holds the
    /// real value or an already-masked one -- masking an already-masked value is harmless. The only
    /// caller that passes false is Start Run's success path: the winning claim gets the real values back
    /// exactly once, from the pre-claim read it already made before <c>TryStartAsync</c> overwrote
    /// storage with the masked snapshot.</summary>
    public static RunResponse ToResponse(this RunDetail detail, bool maskSensitiveValues = true) =>
        new()
        {
            Id = detail.Header.Id,
            ApplicationId = detail.Header.ApplicationId,
            Trigger = detail.Header.Trigger.ToContract(),
            Status = detail.Header.Status.ToContract(),
            StatusReason = detail.Header.StatusReason?.ToContract(),
            TotalActivityCount = detail.Header.TotalActivityCount,
            PassedActivityCount = detail.Header.PassedActivityCount,
            FailedActivityCount = detail.Header.FailedActivityCount,
            SkippedActivityCount = detail.Header.SkippedActivityCount,
            Environment = detail.Header.Environment.ToResponse(maskSensitiveValues),
            Scenarios = detail.Scenarios.Select(s => s.ToResponse()).ToList(),
            LastSeq = detail.Header.LastSeq,
            TriggeredByUserId = detail.Header.TriggeredByUserId,
            CreatedAt = detail.Header.CreatedAt,
            StartedAt = detail.Header.StartedAt,
            CompletedAt = detail.Header.CompletedAt,
        };

    public static RunSummaryResponse ToResponse(this RunSummary summary) =>
        new()
        {
            Id = summary.Id,
            Trigger = summary.Trigger.ToContract(),
            Status = summary.Status.ToContract(),
            StatusReason = summary.StatusReason?.ToContract(),
            TotalActivityCount = summary.TotalActivityCount,
            PassedActivityCount = summary.PassedActivityCount,
            FailedActivityCount = summary.FailedActivityCount,
            SkippedActivityCount = summary.SkippedActivityCount,
            CreatedAt = summary.CreatedAt,
            StartedAt = summary.StartedAt,
            CompletedAt = summary.CompletedAt,
        };

    public static RunStatusUpdateResponse ToResponse(this RunStatusUpdate update) =>
        new()
        {
            Seq = update.Seq,
            Kind = update.Kind.ToContract(),
            CreatedAt = update.CreatedAt,
            ActivityResult = update.ActivityResult?.ToResponse(),
        };

    private static SnapshotSourceResponse ToResponse(this SnapshotSource source) =>
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
            ResolvedPreconditions =
                result.ResolvedPreconditions ?? new Dictionary<string, string>(),
            Evidence = result.Evidence ?? new Dictionary<string, string>(),
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
