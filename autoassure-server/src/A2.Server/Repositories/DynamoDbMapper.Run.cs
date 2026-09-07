using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

// A Run is split across three row shapes that all share the base table's OrganizationId_ApplicationId
// partition key and a RowKey range key built from the RunId -- see fix_run_design.md section 3:
//   header row:          RowKey = "<runId>"
//   scenario snapshot:   RowKey = "<runId>#scenario#<scenarioId>"
//   status update:       RowKey = "<runId>#update#<seq, zero-padded>"
// This file has one pair of mapping functions per row shape (ToDynamoDbRow / ToRun*), plus the range-key
// builders below and small private helpers for the nested snapshot/result shapes embedded in a row.
public static partial class DynamoDbMapper
{
    /// <summary>Builds a Run header row's range key. This, <see cref="RunScenarioRowKey"/> and
    /// <see cref="RunStatusUpdateRowKey"/> are the only places a runs-table RowKey is ever built --
    /// never construct one by hand anywhere else.</summary>
    public static string RunHeaderRowKey(Guid runId) => runId.ToString();

    /// <summary>Builds a Scenario snapshot row's range key. See <see cref="RunHeaderRowKey"/>.</summary>
    public static string RunScenarioRowKey(Guid runId, Guid scenarioId) =>
        $"{runId}#scenario#{scenarioId}";

    /// <summary>Builds a status update row's range key, zero-padding <paramref name="seq"/> to 12
    /// digits. DynamoDB compares range keys as strings, so an unpadded key would sort "#update#10"
    /// before "#update#9" the moment a Run passes nine updates. This is the one function anywhere in the
    /// codebase that formats a Run's sequence number into a key -- a cursor query's ">" bound is built by
    /// calling this same function with the cursor's sequence number, never by formatting the number
    /// itself.</summary>
    public static string RunStatusUpdateRowKey(Guid runId, long seq) =>
        $"{runId}#update#{seq.ToString("D12", CultureInfo.InvariantCulture)}";

    /// <summary>The status update prefix on its own, with no sequence number. Every
    /// <see cref="RunStatusUpdateRowKey"/> for this run sorts strictly after this string (it is always
    /// a strict prefix of one), while <see cref="RunHeaderRowKey"/> and every
    /// <see cref="RunScenarioRowKey"/> for this run sort strictly before it -- "#scenario#" precedes
    /// "#update#" lexically. Get Run's Query (task 5) uses this as the exclusive upper bound of a
    /// BETWEEN range to read the header and Scenario snapshots without touching a single status update
    /// row, and the status update log's own Query (task 9) reuses it as a begins_with prefix.</summary>
    public static string RunStatusUpdateRowKeyPrefix(Guid runId) => $"{runId}#update#";

    /// <summary>Builds the InFlightIndex shard key for a given shard number (0 through
    /// <see cref="RunExecutionPolicy.InFlightShardCount"/> minus 1) -- the one place "Running#" is ever
    /// formatted, used both when a Run is claimed (<see cref="RunInFlightShard"/>) and when the sweeper
    /// queries a shard for stale Runs.</summary>
    public static string RunInFlightShardKey(int shard) => $"Running#{shard}";

    /// <summary>Derives the InFlightIndex shard key for a Run in progress: a digit 0 through
    /// <see cref="RunExecutionPolicy.InFlightShardCount"/> minus 1, spread deterministically across the
    /// Run's id, so the sweeper's sparse index is many partitions wide instead of one hot one. Sums the
    /// id's bytes rather than using <see cref="Guid.GetHashCode"/>, whose exact result .NET does not
    /// guarantee stable across runtime versions -- this must keep producing the same shard for the same
    /// Run for as long as it stays in flight.</summary>
    public static string RunInFlightShard(Guid runId)
    {
        var sumOfBytes = runId.ToByteArray().Sum(b => b);
        return RunInFlightShardKey(sumOfBytes % RunExecutionPolicy.InFlightShardCount);
    }

    // ----- Header row -----

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
            ["TotalActivityCount"] = new AttributeValue
            {
                N = run.TotalActivityCount.ToString(CultureInfo.InvariantCulture),
            },
            ["PassedActivityCount"] = new AttributeValue
            {
                N = run.PassedActivityCount.ToString(CultureInfo.InvariantCulture),
            },
            ["FailedActivityCount"] = new AttributeValue
            {
                N = run.FailedActivityCount.ToString(CultureInfo.InvariantCulture),
            },
            ["SkippedActivityCount"] = new AttributeValue
            {
                N = run.SkippedActivityCount.ToString(CultureInfo.InvariantCulture),
            },
            ["Environment"] = run.Environment.ToAttributeValue(),
            ["LastSeq"] = new AttributeValue
            {
                N = run.LastSeq.ToString(CultureInfo.InvariantCulture),
            },
            ["CreatedAt"] = new(run.CreatedAt.ToString("O")),
            // Only a header row carries HeaderId. The sparse RunHeaderIndex uses its presence to answer
            // List Runs by reading headers alone, never a Scenario snapshot or status update row.
            ["HeaderId"] = new(run.Id.ToString()),
        };

        if (run.StatusReason is { } statusReason)
        {
            row["StatusReason"] = new(statusReason.ToString());
        }

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

        if (run.DeadlineAt is { } deadlineAt)
        {
            row["DeadlineAt"] = new(deadlineAt.ToString("O"));
        }

        if (run.ExpiresAt is { } expiresAt)
        {
            // DynamoDB's TTL sweep reads this attribute as epoch seconds -- see RunRetentionPolicy for
            // how the value is computed.
            row["ExpiresAt"] = new AttributeValue
            {
                N = expiresAt.ToString(CultureInfo.InvariantCulture),
            };
        }

        // Only an in-flight (Running) row carries InFlightShard. That is what keeps the sparse
        // InFlightIndex small: a Pending, Completed, Cancelled or Abandoned run simply drops out of it.
        if (run.Status == RunStatus.Running)
        {
            row["InFlightShard"] = new(RunInFlightShard(run.Id));
        }

        return row;
    }

    public static Run ToRun(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            ApplicationId = Guid.Parse(row["ApplicationId"].S),
            Trigger = Enum.Parse<RunTrigger>(row["Trigger"].S),
            Status = Enum.Parse<RunStatus>(row["Status"].S),
            StatusReason = row.TryGetValue("StatusReason", out var statusReason)
                ? Enum.Parse<RunStatusReason>(statusReason.S)
                : null,
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
            Environment = row["Environment"].ToRunEnvironmentSnapshot(),
            LastSeq = long.Parse(row["LastSeq"].N, CultureInfo.InvariantCulture),
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
            DeadlineAt = row.TryGetValue("DeadlineAt", out var deadlineAt)
                ? DateTimeOffset.Parse(deadlineAt.S, CultureInfo.InvariantCulture)
                : null,
            ExpiresAt = row.TryGetValue("ExpiresAt", out var expiresAt)
                ? long.Parse(expiresAt.N, CultureInfo.InvariantCulture)
                : null,
        };

    // ----- Header summary (RunHeaderIndex projection) -----

    /// <summary>Builds a <see cref="RunSummary"/> from a <c>RunHeaderIndex</c> query result. Reads only
    /// the attributes that index projects (see the GSI's <c>non_key_attributes</c> in dynamodb.tf) --
    /// unlike <see cref="ToRun"/>, this never touches OrganizationId, ApplicationId, Environment,
    /// LastSeq, TriggeredByUserId, LastHeartbeatAt, DeadlineAt or ExpiresAt, none of which the index
    /// carries.</summary>
    public static RunSummary ToRunSummary(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            Trigger = Enum.Parse<RunTrigger>(row["Trigger"].S),
            Status = Enum.Parse<RunStatus>(row["Status"].S),
            StatusReason = row.TryGetValue("StatusReason", out var statusReason)
                ? Enum.Parse<RunStatusReason>(statusReason.S)
                : null,
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
        };

    // ----- Scenario snapshot row -----

    /// <summary>Builds a Scenario snapshot row. OrganizationId, ApplicationId and RunId are not part of
    /// <see cref="RunScenarioSnapshot"/> itself -- they live on the enclosing Run and are supplied here
    /// by the caller, the same way they are left out of the snapshot model (see its XML doc).
    /// <paramref name="expiresAt"/> must be the same value written on the Run's header and every other
    /// row of it -- see fix_run_design.md section 6.</summary>
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this RunScenarioSnapshot snapshot,
        Guid runId,
        Guid organizationId,
        Guid applicationId,
        long? expiresAt
    )
    {
        var row = new Dictionary<string, AttributeValue>
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
            ["Tags"] = new AttributeValue
            {
                L = snapshot.Tags.Select(tag => new AttributeValue(tag)).ToList(),
            },
            ["Activities"] = new AttributeValue
            {
                L = snapshot.Activities.Select(activity => activity.ToAttributeValue()).ToList(),
            },
        };

        if (expiresAt is { } value)
        {
            row["ExpiresAt"] = new AttributeValue
            {
                N = value.ToString(CultureInfo.InvariantCulture),
            };
        }

        return row;
    }

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

    // ----- Status update row -----

    /// <summary>Builds a status update row. OrganizationId, ApplicationId and RunId are supplied by the
    /// caller for the same reason as <see cref="ToDynamoDbRow(RunScenarioSnapshot,Guid,Guid,Guid,long?)"/>.
    /// <paramref name="expiresAt"/> must be the Run's own ExpiresAt, so the log disappears with the rest
    /// of the Run's rows.</summary>
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(
        this RunStatusUpdate update,
        Guid runId,
        Guid organizationId,
        Guid applicationId,
        long? expiresAt
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
            ["Seq"] = new AttributeValue { N = update.Seq.ToString(CultureInfo.InvariantCulture) },
            ["Kind"] = new(update.Kind.ToString()),
            ["CreatedAt"] = new(update.CreatedAt.ToString("O")),
        };

        if (update.ActivityResult is { } activityResult)
        {
            row["ActivityResult"] = activityResult.ToAttributeValue();
        }

        if (expiresAt is { } value)
        {
            row["ExpiresAt"] = new AttributeValue
            {
                N = value.ToString(CultureInfo.InvariantCulture),
            };
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

    // ----- Nested shapes embedded within a row -----

    private static AttributeValue ToAttributeValue(this SnapshotSource source) =>
        new AttributeValue
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

    private static SnapshotSource ToSnapshotSource(this AttributeValue value) =>
        new()
        {
            Id = Guid.Parse(value.M["Id"].S),
            CreatedByUserId = Guid.Parse(value.M["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(value.M["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(value.M["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(value.M["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };

    private static AttributeValue ToAttributeValue(this RunEnvironmentSnapshot snapshot) =>
        new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["Source"] = snapshot.Source.ToAttributeValue(),
                ["Name"] = new(snapshot.Name),
                ["Classification"] = new(snapshot.Classification.ToString()),
                ["Variables"] = new AttributeValue
                {
                    L = snapshot.Variables.Select(variable => variable.ToAttributeValue()).ToList(),
                },
            },
        };

    private static RunEnvironmentSnapshot ToRunEnvironmentSnapshot(this AttributeValue value) =>
        new()
        {
            Source = value.M["Source"].ToSnapshotSource(),
            Name = value.M["Name"].S,
            Classification = Enum.Parse<EnvironmentClassification>(value.M["Classification"].S),
            Variables = value
                .M["Variables"]
                .L.Select(variable => variable.ToRunEnvironmentVariableSnapshot())
                .ToList(),
        };

    private static AttributeValue ToAttributeValue(this RunEnvironmentVariableSnapshot variable) =>
        new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["Key"] = new(variable.Key),
                ["Value"] = new(variable.Value),
                ["IsSensitive"] = new AttributeValue { BOOL = variable.IsSensitive },
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
            IsSensitive = value.M["IsSensitive"].BOOL ?? false,
            CreatedByUserId = Guid.Parse(value.M["CreatedByUserId"].S),
            UpdatedByUserId = Guid.Parse(value.M["UpdatedByUserId"].S),
            CreatedAt = DateTimeOffset.Parse(value.M["CreatedAt"].S, CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(value.M["UpdatedAt"].S, CultureInfo.InvariantCulture),
        };

    private static AttributeValue ToAttributeValue(this RunPreconditionSnapshot snapshot) =>
        new AttributeValue
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
        new AttributeValue
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
        new AttributeValue
        {
            M = new Dictionary<string, AttributeValue>
            {
                ["Source"] = snapshot.Source.ToAttributeValue(),
                ["Order"] = new AttributeValue
                {
                    N = snapshot.Order.ToString(CultureInfo.InvariantCulture),
                },
                ["Description"] = new(snapshot.Description),
                ["Preconditions"] = new AttributeValue
                {
                    L = snapshot
                        .Preconditions.Select(precondition => precondition.ToAttributeValue())
                        .ToList(),
                },
                ["EvidenceDefinitions"] = new AttributeValue
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
            ["ResolvedPreconditions"] = new AttributeValue
            {
                M = activityResult.ResolvedPreconditions.ToDictionary(
                    kv => kv.Key,
                    kv => new AttributeValue(kv.Value)
                ),
            },
            ["Evidence"] = new AttributeValue
            {
                M = activityResult.Evidence.ToDictionary(
                    kv => kv.Key,
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
                .M.ToDictionary(kv => kv.Key, kv => kv.Value.S),
            Evidence = value.M["Evidence"].M.ToDictionary(kv => kv.Key, kv => kv.Value.S),
            ContinuationReasoning = value.M.TryGetValue("ContinuationReasoning", out var reasoning)
                ? reasoning.S
                : null,
        };
}
