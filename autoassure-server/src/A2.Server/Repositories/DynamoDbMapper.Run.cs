using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    // 1000/2000/3000/4000 fix the sort order of a Run's row kinds explicitly, rather than leaving it to
    // however their names happen to compare alphabetically. The gaps of 1000 leave room to insert a new
    // row kind later without renumbering the ones above.
    public static string RunHeaderRowKey(Guid runId) => $"{runId}#1000";

    public static string RunEnvironmentRowKey(Guid runId) => $"{runId}#2000";

    public static string RunScenarioRowKey(Guid runId, Guid scenarioId) =>
        $"{runId}#3000#{scenarioId}";

    public static string RunStatusUpdateRowKey(Guid runId, long seq) =>
        $"{runId}#4000#{seq.ToString("D12", CultureInfo.InvariantCulture)}";

    public static string RunStatusUpdateRowKeyPrefix(Guid runId) => $"{runId}#4000#";

    public static Dictionary<string, AttributeValue> ToDynamoDbRow(this Run run)
    {
        var row = new Dictionary<string, AttributeValue>
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(run.OrganizationId, run.ApplicationId)
            ),
            ["RowKey"] = new(RunHeaderRowKey(run.Id)),
            ["Id"] = new(run.Id.ToString()),
            ["OrganizationId"] = new(run.OrganizationId.ToString()),
            ["ApplicationId"] = new(run.ApplicationId.ToString()),
            ["Trigger"] = new(run.Trigger.ToString()),
            ["Status"] = new(run.Status.ToString()),
            ["TotalActivityCount"] = new()
            {
                N = run.TotalActivityCount.ToString(CultureInfo.InvariantCulture),
            },
            ["PassedActivityCount"] = new()
            {
                N = run.PassedActivityCount.ToString(CultureInfo.InvariantCulture),
            },
            ["FailedActivityCount"] = new()
            {
                N = run.FailedActivityCount.ToString(CultureInfo.InvariantCulture),
            },
            ["SkippedActivityCount"] = new()
            {
                N = run.SkippedActivityCount.ToString(CultureInfo.InvariantCulture),
            },
            ["LastSeq"] = new()
            {
                N = run.LastStatusUpdateSequenceNumber.ToString(CultureInfo.InvariantCulture),
            },
            ["CreatedAt"] = new(run.CreatedAt.ToString("O")),
            // Only a header row carries HeaderId. The sparse RunHeaderIndex uses its presence to answer
            // List Runs by reading headers alone, never a Scenario snapshot or status update row.
            ["HeaderId"] = new(run.Id.ToString()),
        };

        if (run.TriggeredByUserId is { } triggeredByUserId)
        {
            row["TriggeredByUserId"] = new(triggeredByUserId.ToString());
        }

        if (run.StartedAt is { } startedAt)
        {
            row["StartedAt"] = new(startedAt.ToString("O"));
        }

        if (run.CompletedAt is { } completedAt)
        {
            row["CompletedAt"] = new(completedAt.ToString("O"));
        }

        if (run.LastHeartbeatAt is { } lastHeartbeatAt)
        {
            row["LastHeartbeatAt"] = new(lastHeartbeatAt.ToString("O"));
        }

        return row;
    }

    public static Run ToRun(
        this Dictionary<string, AttributeValue> row,
        RunEnvironmentSnapshot environment,
        IReadOnlyList<RunScenarioSnapshot> scenarios
    ) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            Environment = environment,
            Scenarios = scenarios,
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            ApplicationId = Guid.Parse(row["ApplicationId"].S),
            Trigger = Enum.Parse<RunTrigger>(row["Trigger"].S),
            Status = Enum.Parse<RunStatus>(row["Status"].S),
            TotalActivityCount = int.Parse(
                row["TotalActivityCount"].N,
                CultureInfo.InvariantCulture
            ),
            PassedActivityCount = int.Parse(
                row["PassedActivityCount"].N,
                CultureInfo.InvariantCulture
            ),
            FailedActivityCount = int.Parse(
                row["FailedActivityCount"].N,
                CultureInfo.InvariantCulture
            ),
            SkippedActivityCount = int.Parse(
                row["SkippedActivityCount"].N,
                CultureInfo.InvariantCulture
            ),
            LastStatusUpdateSequenceNumber = long.Parse(
                row["LastSeq"].N,
                CultureInfo.InvariantCulture
            ),
            TriggeredByUserId = row.TryGetValue("TriggeredByUserId", out var triggeredByUserId)
                ? Guid.Parse(triggeredByUserId.S)
                : null,
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            StartedAt = row.TryGetValue("StartedAt", out var startedAt)
                ? DateTimeOffset.Parse(startedAt.S, CultureInfo.InvariantCulture)
                : null,
            CompletedAt = row.TryGetValue("CompletedAt", out var completedAt)
                ? DateTimeOffset.Parse(completedAt.S, CultureInfo.InvariantCulture)
                : null,
            LastHeartbeatAt = row.TryGetValue("LastHeartbeatAt", out var lastHeartbeatAt)
                ? DateTimeOffset.Parse(lastHeartbeatAt.S, CultureInfo.InvariantCulture)
                : null,
        };

    // ----- Header summary (RunHeaderIndex projection) -----

    /// <summary>Builds a <see cref="RunInfo"/> from a <c>RunHeaderIndex</c> query result. Reads only
    /// the attributes that index projects (see the GSI's <c>non_key_attributes</c> in dynamodb.tf) --
    /// unlike <see cref="ToRun"/>, this never touches OrganizationId, ApplicationId, LastSeq or
    /// TriggeredByUserId, none of which the index carries. The Environment snapshot is not a header-row
    /// attribute at all -- it lives on its own row -- so it was never something this index could have
    /// projected either.</summary>
    public static RunInfo ToRunInfo(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            Trigger = Enum.Parse<RunTrigger>(row["Trigger"].S),
            Status = Enum.Parse<RunStatus>(row["Status"].S),
            TotalActivityCount = int.Parse(
                row["TotalActivityCount"].N,
                CultureInfo.InvariantCulture
            ),
            PassedActivityCount = int.Parse(
                row["PassedActivityCount"].N,
                CultureInfo.InvariantCulture
            ),
            FailedActivityCount = int.Parse(
                row["FailedActivityCount"].N,
                CultureInfo.InvariantCulture
            ),
            SkippedActivityCount = int.Parse(
                row["SkippedActivityCount"].N,
                CultureInfo.InvariantCulture
            ),
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            StartedAt = row.TryGetValue("StartedAt", out var startedAt)
                ? DateTimeOffset.Parse(startedAt.S, CultureInfo.InvariantCulture)
                : null,
            CompletedAt = row.TryGetValue("CompletedAt", out var completedAt)
                ? DateTimeOffset.Parse(completedAt.S, CultureInfo.InvariantCulture)
                : null,
            LastHeartbeatAt = row.TryGetValue("LastHeartbeatAt", out var lastHeartbeatAt)
                ? DateTimeOffset.Parse(lastHeartbeatAt.S, CultureInfo.InvariantCulture)
                : null,
        };

    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this RunningRun runningRun,
        Guid organizationId,
        Guid applicationId
    ) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(organizationId, applicationId)
            ),
            ["RunId"] = new(runningRun.Id.ToString()),
            ["StartedAt"] = new(runningRun.StartedAt.ToString("O")),
        };

    public static RunningRun ToRunningRun(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["RunId"].S),
            StartedAt = DateTimeOffset.Parse(row["StartedAt"].S, CultureInfo.InvariantCulture),
        };

    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this RunEnvironmentSnapshot snapshot,
        Guid runId,
        Guid organizationId,
        Guid applicationId
    ) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(organizationId, applicationId)
            ),
            ["RowKey"] = new(RunEnvironmentRowKey(runId)),
            ["RunId"] = new(runId.ToString()),
            ["OrganizationId"] = new(organizationId.ToString()),
            ["ApplicationId"] = new(applicationId.ToString()),
            ["Source"] = snapshot.Source.ToAttributeValue(),
            ["Name"] = new(snapshot.Name),
            ["Classification"] = new(snapshot.Classification.ToString()),
            ["Variables"] = snapshot.ToVariablesAttributeValue(),
        };

    public static RunEnvironmentSnapshot ToRunEnvironmentSnapshot(
        this Dictionary<string, AttributeValue> row
    ) =>
        new()
        {
            Source = row["Source"].ToSnapshotSource(),
            Name = row["Name"].S,
            Classification = Enum.Parse<EnvironmentClassification>(row["Classification"].S),
            Variables = row["Variables"]
                .L.Select(variable => variable.ToRunEnvironmentVariableSnapshot())
                .ToList(),
        };

    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this RunScenarioSnapshot snapshot,
        Guid runId,
        Guid organizationId,
        Guid applicationId
    ) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(organizationId, applicationId)
            ),
            ["RowKey"] = new(RunScenarioRowKey(runId, snapshot.Source.Id)),
            ["RunId"] = new(runId.ToString()),
            ["OrganizationId"] = new(organizationId.ToString()),
            ["ApplicationId"] = new(applicationId.ToString()),
            ["ScenarioId"] = new(snapshot.Source.Id.ToString()),
            ["Source"] = snapshot.Source.ToAttributeValue(),
            ["Title"] = new(snapshot.Title),
            ["Description"] = new(snapshot.Description),
            ["Folder"] = new(snapshot.Folder),
            ["Tags"] = new() { L = snapshot.Tags.Select(tag => new AttributeValue(tag)).ToList() },
            ["Activities"] = new()
            {
                L = snapshot.Activities.Select(activity => activity.ToAttributeValue()).ToList(),
            },
        };

    public static RunScenarioSnapshot ToRunScenarioSnapshot(
        this Dictionary<string, AttributeValue> row
    ) =>
        new()
        {
            Source = row["Source"].ToSnapshotSource(),
            Title = row["Title"].S,
            Description = row["Description"].S,
            Folder = row["Folder"].S,
            Tags = row["Tags"].L.Select(tag => tag.S).ToList(),
            Activities = row["Activities"]
                .L.Select(activity => activity.ToRunActivitySnapshot())
                .ToList(),
        };

    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this RunStatusUpdate update,
        Guid runId,
        Guid organizationId,
        Guid applicationId
    )
    {
        var row = new Dictionary<string, AttributeValue>
        {
            ["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(organizationId, applicationId)
            ),
            ["RowKey"] = new(RunStatusUpdateRowKey(runId, update.Seq)),
            ["RunId"] = new(runId.ToString()),
            ["OrganizationId"] = new(organizationId.ToString()),
            ["ApplicationId"] = new(applicationId.ToString()),
            ["Seq"] = new() { N = update.Seq.ToString(CultureInfo.InvariantCulture) },
            ["Kind"] = new(update.Kind.ToString()),
            ["CreatedAt"] = new(update.CreatedAt.ToString("O")),
        };

        if (update.ActivityResult is { } activityResult)
        {
            row["ActivityResult"] = activityResult.ToAttributeValue();
        }

        return row;
    }

    public static RunStatusUpdate ToRunStatusUpdate(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Seq = long.Parse(row["Seq"].N, CultureInfo.InvariantCulture),
            Kind = Enum.Parse<RunStatusUpdateKind>(row["Kind"].S),
            CreatedAt = DateTimeOffset.Parse(row["CreatedAt"].S, CultureInfo.InvariantCulture),
            ActivityResult = row.TryGetValue("ActivityResult", out var activityResult)
                ? activityResult.ToActivityResult()
                : null,
        };

    private static AttributeValue ToAttributeValue(this RunSnapshotSource source) =>
        new()
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["Id"] = new(source.Id.ToString()),
                ["CreatedByUserId"] = new(source.CreatedByUserId.ToString()),
                ["UpdatedByUserId"] = new(source.UpdatedByUserId.ToString()),
                ["CreatedAt"] = new(source.CreatedAt.ToString("O")),
                ["UpdatedAt"] = new(source.UpdatedAt.ToString("O")),
            },
        };

    private static RunSnapshotSource ToSnapshotSource(this AttributeValue value) =>
        new()
        {
            Id = Guid.Parse(value.M["Id"].S),
            CreatedByUserId = Guid.Parse(value.M["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(value.M["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(value.M["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(value.M["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };

    internal static AttributeValue ToVariablesAttributeValue(
        this RunEnvironmentSnapshot snapshot
    ) => new() { L = snapshot.Variables.Select(variable => variable.ToAttributeValue()).ToList() };

    private static AttributeValue ToAttributeValue(this RunEnvironmentVariableSnapshot variable) =>
        new()
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["Key"] = new(variable.Key),
                ["Value"] = new(variable.Value),
                ["IsSensitive"] = new() { BOOL = variable.IsSensitive },
                ["CreatedByUserId"] = new(variable.CreatedByUserId.ToString()),
                ["UpdatedByUserId"] = new(variable.UpdatedByUserId.ToString()),
                ["CreatedAt"] = new(variable.CreatedAt.ToString("O")),
                ["UpdatedAt"] = new(variable.UpdatedAt.ToString("O")),
            },
        };

    private static RunEnvironmentVariableSnapshot ToRunEnvironmentVariableSnapshot(
        this AttributeValue value
    ) =>
        new()
        {
            Key = value.M["Key"].S,
            Value = value.M["Value"].S,
            IsSensitive = value.M["IsSensitive"].RequireBool("IsSensitive"),
            CreatedByUserId = Guid.Parse(value.M["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(value.M["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(value.M["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(value.M["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };

    private static AttributeValue ToAttributeValue(this RunPreconditionSnapshot snapshot) =>
        new()
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["Source"] = snapshot.Source.ToAttributeValue(),
                ["Name"] = new(snapshot.Name),
                ["ValueSource"] = new(snapshot.ValueSource.ToString()),
                ["ExampleValue"] = new(snapshot.ExampleValue),
            },
        };

    private static RunPreconditionSnapshot ToRunPreconditionSnapshot(this AttributeValue value) =>
        new()
        {
            Source = value.M["Source"].ToSnapshotSource(),
            Name = value.M["Name"].S,
            ValueSource = Enum.Parse<PreconditionValueSource>(value.M["ValueSource"].S),
            ExampleValue = value.M["ExampleValue"].S,
        };

    private static AttributeValue ToAttributeValue(this RunEvidenceDefinitionSnapshot snapshot) =>
        new()
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["Source"] = snapshot.Source.ToAttributeValue(),
                ["Name"] = new(snapshot.Name),
                ["Description"] = new(snapshot.Description),
                ["ExampleValue"] = new(snapshot.ExampleValue),
            },
        };

    private static RunEvidenceDefinitionSnapshot ToRunEvidenceDefinitionSnapshot(
        this AttributeValue value
    ) =>
        new()
        {
            Source = value.M["Source"].ToSnapshotSource(),
            Name = value.M["Name"].S,
            Description = value.M["Description"].S,
            ExampleValue = value.M["ExampleValue"].S,
        };

    private static AttributeValue ToAttributeValue(this RunActivitySnapshot snapshot) =>
        new()
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["Source"] = snapshot.Source.ToAttributeValue(),
                ["Order"] = new() { N = snapshot.Order.ToString(CultureInfo.InvariantCulture) },
                ["Description"] = new(snapshot.Description),
                ["Preconditions"] = new()
                {
                    L = snapshot
                        .Preconditions.Select(precondition => precondition.ToAttributeValue())
                        .ToList(),
                },
                ["EvidenceDefinitions"] = new()
                {
                    L = snapshot
                        .EvidenceDefinitions.Select(evidenceDefinition =>
                            evidenceDefinition.ToAttributeValue()
                        )
                        .ToList(),
                },
            },
        };

    private static RunActivitySnapshot ToRunActivitySnapshot(this AttributeValue value) =>
        new()
        {
            Source = value.M["Source"].ToSnapshotSource(),
            Order = int.Parse(value.M["Order"].N, CultureInfo.InvariantCulture),
            Description = value.M["Description"].S,
            Preconditions = value
                .M["Preconditions"]
                .L.Select(precondition => precondition.ToRunPreconditionSnapshot())
                .ToList(),
            EvidenceDefinitions = value
                .M["EvidenceDefinitions"]
                .L.Select(evidenceDefinition =>
                    evidenceDefinition.ToRunEvidenceDefinitionSnapshot()
                )
                .ToList(),
        };

    private static AttributeValue ToAttributeValue(this ActivityResult activityResult)
    {
        var map = new Dictionary<string, AttributeValue>
        {
            ["ScenarioId"] = new(activityResult.ScenarioId.ToString()),
            ["ActivityId"] = new(activityResult.ActivityId.ToString()),
            ["Status"] = new(activityResult.Status.ToString()),
            ["ResolvedPreconditions"] = new()
            {
                M = activityResult.ResolvedPreconditions.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => new AttributeValue(kv.Value)
                ),
            },
            ["Evidence"] = new()
            {
                M = activityResult.Evidence.ToDictionary(
                    kv => kv.Key.ToString(),
                    kv => new AttributeValue(kv.Value)
                ),
            },
        };

        if (activityResult.ContinuationReasoning is { } continuationReasoning)
        {
            map["ContinuationReasoning"] = new(continuationReasoning);
        }

        return new AttributeValue { M = map };
    }

    private static ActivityResult ToActivityResult(this AttributeValue value) =>
        new()
        {
            ScenarioId = Guid.Parse(value.M["ScenarioId"].S),
            ActivityId = Guid.Parse(value.M["ActivityId"].S),
            Status = Enum.Parse<ActivityResultStatus>(value.M["Status"].S),
            ResolvedPreconditions = value
                .M["ResolvedPreconditions"]
                .M.ToDictionary(kv => Guid.Parse(kv.Key), kv => kv.Value.S),
            Evidence = value
                .M["Evidence"]
                .M.ToDictionary(kv => Guid.Parse(kv.Key), kv => kv.Value.S),
            ContinuationReasoning = value.M.TryGetValue("ContinuationReasoning", out var reasoning)
                ? reasoning.S
                : null,
        };
}
