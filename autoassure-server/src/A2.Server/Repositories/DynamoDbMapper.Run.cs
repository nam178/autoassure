using System.Globalization;
using A2.Server.Models;
using Amazon.DynamoDBv2.Model;

namespace A2.Server.Repositories;

public static partial class DynamoDbMapper
{
    public static Dictionary<string, AttributeValue> ToDynamoDbRow(this Run run)
    {
        var row = new Dictionary<string, AttributeValue>
        {
            ["Id"] = new(run.Id.ToString()),
            ["OrganizationId"] = new(run.OrganizationId.ToString()),
            ["Kind"] = new(run.Kind.ToString()),
            ["ApplicationId"] = new(run.ApplicationId.ToString()),
            ["ScenarioIds"] = new AttributeValue
            {
                L = run.ScenarioIds.Select(id => new AttributeValue(id.ToString())).ToList(),
            },
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
            ["TriggeredByUserId"] = new(run.TriggeredByUserId.ToString()),
            ["UpdatedByUserId"] = new(run.UpdatedByUserId.ToString()),
            ["ActivityResults"] = new AttributeValue
            {
                L = run
                    .ActivityResults.Select(activityResult => activityResult.ToAttributeValue())
                    .ToList(),
            },
        };

        if (run.Kind == RunKind.Run)
        {
            row["OrganizationId_ApplicationId"] = new(
                ApplicationScopedPartitionKey(run.OrganizationId, run.ApplicationId)
            );
        }
        else
        {
            row["OrganizationId_ScenarioId"] = new($"{run.OrganizationId}_{run.ScenarioIds[0]}");
        }

        row["EnvironmentId"] = new(run.EnvironmentId.ToString());

        if (run.StartedAt is { } startedAt)
        {
            row["StartedAt"] = new(startedAt.ToString("O"));
        }

        if (run.CompletedAt is { } completedAt)
        {
            row["CompletedAt"] = new(completedAt.ToString("O"));
        }

        return row;
    }

    public static AttributeValue ToAttributeValue(this ActivityResult activityResult)
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

    public static Run ToRun(this Dictionary<string, AttributeValue> row) =>
        new()
        {
            Id = Guid.Parse(row["Id"].S),
            OrganizationId = Guid.Parse(row["OrganizationId"].S),
            Kind = Enum.Parse<RunKind>(row["Kind"].S),
            ApplicationId = Guid.Parse(row["ApplicationId"].S),
            ScenarioIds = row["ScenarioIds"].L.Select(id => Guid.Parse(id.S)).ToList(),
            EnvironmentId = Guid.Parse(row["EnvironmentId"].S),
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
            TriggeredByUserId = Guid.Parse(row["TriggeredByUserId"].S),
            UpdatedByUserId = Guid.Parse(row["UpdatedByUserId"].S),
            StartedAt = row.TryGetValue("StartedAt", out var startedAt)
                ? DateTimeOffset.Parse(startedAt.S, CultureInfo.InvariantCulture)
                : null,
            CompletedAt = row.TryGetValue("CompletedAt", out var completedAt)
                ? DateTimeOffset.Parse(completedAt.S, CultureInfo.InvariantCulture)
                : null,
            ActivityResults = row["ActivityResults"]
                .L.Select(item => item.ToActivityResult())
                .ToList(),
        };

    public static ActivityResult ToActivityResult(this AttributeValue row) =>
        new()
        {
            ScenarioId = Guid.Parse(row.M["ScenarioId"].S),
            ActivityId = Guid.Parse(row.M["ActivityId"].S),
            Status = Enum.Parse<ActivityResultStatus>(row.M["Status"].S),
            ResolvedPreconditions = row.M["ResolvedPreconditions"]
                .M.ToDictionary(kv => kv.Key, kv => kv.Value.S),
            Evidence = row.M["Evidence"].M.ToDictionary(kv => kv.Key, kv => kv.Value.S),
            ContinuationReasoning = row.M.TryGetValue("ContinuationReasoning", out var reasoning)
                ? reasoning.S
                : null,
        };
}
