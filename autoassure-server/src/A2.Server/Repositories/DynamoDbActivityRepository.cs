using System.Globalization;
using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbActivityRepository(IAmazonDynamoDB client, IOptions<DynamoDbOptions> options)
    : IActivityRepository
{
    private const string IdIndexName = "IdIndex";

    private string TableName => options.Value.ActivityTableName;
    private string ScenarioTableName => options.Value.ScenarioTableName;
    private string PreconditionTableName => options.Value.PreconditionTableName;
    private string EvidenceDefinitionTableName => options.Value.EvidenceDefinitionTableName;

    public async Task<ActivitySaveResult> TrySaveAsync(Activity activity)
    {
        // The Scenario-exists check and the Activity count limit are combined into one Update on
        // the Scenario item (rather than a separate ConditionCheck), since a transaction rejects two
        // operations on the same item.
        var transactItems = new List<TransactWriteItem>
        {
            IncrementScenarioActivityCount(
                activity.OrganizationId,
                activity.ApplicationId,
                activity.ScenarioId
            ),
        };
        transactItems.AddRange(
            PreconditionAndEvidenceExistsCheck(
                activity.OrganizationId,
                activity.ApplicationId,
                activity.PreconditionIds,
                activity.EvidenceIds
            )
        );
        transactItems.Add(
            new TransactWriteItem
            {
                Put = new Put { TableName = TableName, Item = activity.ToDynamoDbRow() },
            }
        );

        // When TransactWriteItemsAsync is cancelled due to the Scenario Update (index 0) failing,
        // Then a follow-up read distinguishes ScenarioNotFound from ScenarioActivityLimitReached,
        // since the combined condition can't say which clause failed. When it's cancelled due to a
        // Precondition/Evidence ConditionCheck (index > 0) failing, Then return
        // PreconditionOrEvidenceNotFound; otherwise rethrow, since the cancellation isn't caused by
        // either of those checks.
        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }
            );
            return ActivitySaveResult.Success;
        }
        catch (TransactionCanceledException ex) when (ex.CancellationReasons is { Count: > 0 })
        {
            var reasons = ex.CancellationReasons;
            if (reasons[0].Code == "ConditionalCheckFailed")
            {
                var scenarioExists = await ScenarioExistsAsync(
                    activity.OrganizationId,
                    activity.ApplicationId,
                    activity.ScenarioId
                );
                return scenarioExists
                    ? ActivitySaveResult.ScenarioActivityLimitReached
                    : ActivitySaveResult.ScenarioNotFound;
            }

            if (reasons.Skip(1).Any(reason => reason.Code == "ConditionalCheckFailed"))
            {
                return ActivitySaveResult.PreconditionOrEvidenceNotFound;
            }

            throw;
        }
    }

    public async Task<ActivityUpdateResult> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        Guid activityId,
        ActivityUpdatableFields fields
    )
    {
        var transactItems = new List<TransactWriteItem>
        {
            UpdateActivity(organizationId, scenarioId, activityId, fields),
        };
        transactItems.AddRange(
            PreconditionAndEvidenceExistsCheck(
                organizationId,
                applicationId,
                fields.PreconditionIds,
                fields.EvidenceIds
            )
        );

        // When TransactWriteItemsAsync is cancelled due to the Activity ConditionCheck (index 0)
        // failing, Then return ActivityNotFound. When it's cancelled due to a Precondition/Evidence
        // ConditionCheck (index > 0) failing, Then return PreconditionOrEvidenceNotFound; otherwise
        // rethrow, since the cancellation isn't caused by either reference check.
        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }
            );
            return ActivityUpdateResult.Success;
        }
        catch (TransactionCanceledException ex) when (ex.CancellationReasons is { Count: > 0 })
        {
            var reasons = ex.CancellationReasons;
            if (reasons[0].Code == "ConditionalCheckFailed")
            {
                return ActivityUpdateResult.ActivityNotFound;
            }

            if (reasons.Skip(1).Any(reason => reason.Code == "ConditionalCheckFailed"))
            {
                return ActivityUpdateResult.PreconditionOrEvidenceNotFound;
            }

            throw;
        }
    }

    public async Task<Activity?> GetByIdAsync(Guid organizationId, Guid activityId)
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                IndexName = IdIndexName,
                KeyConditionExpression = "OrganizationId = :organizationId AND Id = :id",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":organizationId"] = new(organizationId.ToString()),
                    [":id"] = new(activityId.ToString()),
                },
                Limit = 1,
            }
        );

        return response.Items.Count > 0 ? response.Items[0].ToActivity() : null;
    }

    public async Task<IReadOnlyList<Activity>> ListByScenarioAsync(
        Guid organizationId,
        Guid scenarioId
    )
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                KeyConditionExpression = "OrganizationId_ScenarioId = :partitionKey",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":partitionKey"] = new(
                        DynamoDbMapper.ScenarioScopedPartitionKey(organizationId, scenarioId)
                    ),
                },
                ConsistentRead = true,
            }
        );

        // Order isn't the sort key, so results need an in-memory sort -- fine at the capped ≤90
        // Activities per Scenario.
        return response
            .Items.Select(item => item.ToActivity())
            .OrderBy(activity => activity.Order)
            .ToList();
    }

    public async Task<bool> TryReorderAsync(
        Guid organizationId,
        Guid scenarioId,
        IReadOnlyList<Guid> orderedActivityIds
    )
    {
        var partitionKey = DynamoDbMapper.ScenarioScopedPartitionKey(organizationId, scenarioId);
        var transactItems = new List<TransactWriteItem>();
        for (var index = 0; index < orderedActivityIds.Count; index++)
        {
            transactItems.Add(
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = TableName,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["OrganizationId_ScenarioId"] = new(partitionKey),
                            ["Id"] = new(orderedActivityIds[index].ToString()),
                        },
                        UpdateExpression = "SET #order = :order",
                        ConditionExpression = "attribute_exists(Id)",
                        ExpressionAttributeNames = new Dictionary<string, string>
                        {
                            ["#order"] = "Order",
                        },
                        ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                        {
                            [":order"] = new() { N = index.ToString(CultureInfo.InvariantCulture) },
                        },
                    },
                }
            );
        }

        // When TransactWriteItemsAsync is cancelled due to an Activity ConditionCheck failing, Then
        // return ActivityNotFound; otherwise rethrow, since the cancellation isn't caused by a
        // missing Activity.
        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }
            );
            return true;
        }
        catch (TransactionCanceledException ex)
            when (ex.CancellationReasons is { Count: > 0 } reasons
                && reasons.Any(reason => reason.Code == "ConditionalCheckFailed")
            )
        {
            return false;
        }
    }

    // Only used to disambiguate why IncrementScenarioActivityCount's combined condition failed --
    // not part of the transaction itself.
    private async Task<bool> ScenarioExistsAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId
    )
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = ScenarioTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    ["Id"] = new(scenarioId.ToString()),
                },
                ConsistentRead = true,
            }
        );
        return response.IsItemSet;
    }

    private TransactWriteItem IncrementScenarioActivityCount(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId
    ) =>
        new()
        {
            Update = new Update
            {
                TableName = ScenarioTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    ["Id"] = new(scenarioId.ToString()),
                },
                UpdateExpression = "ADD ActivityCount :one",
                // Scenarios written before ActivityCount existed have no such attribute -- Then
                // treat it as 0 (matching ToScenario's read-side default) instead of failing the
                // numeric comparison DynamoDB would otherwise reject against a missing attribute.
                ConditionExpression =
                    "attribute_exists(Id) AND (attribute_not_exists(ActivityCount) OR ActivityCount < :max)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":one"] = new() { N = "1" },
                    [":max"] = new()
                    {
                        N = Quota.MaxActivityCountPerScenario.ToString(
                            CultureInfo.InvariantCulture
                        ),
                    },
                },
            },
        };

    private TransactWriteItem UpdateActivity(
        Guid organizationId,
        Guid scenarioId,
        Guid activityId,
        ActivityUpdatableFields fields
    ) =>
        new()
        {
            Update = new Update
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ScenarioId"] = new(
                        DynamoDbMapper.ScenarioScopedPartitionKey(organizationId, scenarioId)
                    ),
                    ["Id"] = new(activityId.ToString()),
                },
                UpdateExpression =
                    "SET Description = :description, PreconditionIds = :preconditionIds, "
                    + "EvidenceIds = :evidenceIds, UpdatedByUserId = :updatedByUserId, "
                    + "UpdatedAt = :updatedAt",
                ConditionExpression = "attribute_exists(Id)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":description"] = new(fields.Description),
                    [":preconditionIds"] = new()
                    {
                        L = fields
                            .PreconditionIds.Select(pid => new AttributeValue(pid.ToString()))
                            .ToList(),
                    },
                    [":evidenceIds"] = new()
                    {
                        L = fields
                            .EvidenceIds.Select(eid => new AttributeValue(eid.ToString()))
                            .ToList(),
                    },
                    [":updatedByUserId"] = new(fields.UpdatedByUserId.ToString()),
                    [":updatedAt"] = new(fields.UpdatedAt.ToString("O")),
                },
            },
        };

    // One ConditionCheck per unique Precondition/EvidenceDefinition referenced by the Activity --
    // deduped, since a DynamoDB transaction rejects two operations targeting the same item.
    private IEnumerable<TransactWriteItem> PreconditionAndEvidenceExistsCheck(
        Guid organizationId,
        Guid applicationId,
        IReadOnlyList<Guid> preconditionIds,
        IReadOnlyList<Guid> evidenceIds
    )
    {
        var partitionKey = DynamoDbMapper.ApplicationScopedPartitionKey(
            organizationId,
            applicationId
        );

        var preconditionChecks = preconditionIds
            .Distinct()
            .Select(preconditionId => new TransactWriteItem
            {
                ConditionCheck = new ConditionCheck
                {
                    TableName = PreconditionTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId_ApplicationId"] = new(partitionKey),
                        ["Id"] = new(preconditionId.ToString()),
                    },
                    ConditionExpression = "attribute_exists(Id)",
                },
            });

        var evidenceChecks = evidenceIds
            .Distinct()
            .Select(evidenceId => new TransactWriteItem
            {
                ConditionCheck = new ConditionCheck
                {
                    TableName = EvidenceDefinitionTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId_ApplicationId"] = new(partitionKey),
                        ["Id"] = new(evidenceId.ToString()),
                    },
                    ConditionExpression = "attribute_exists(Id)",
                },
            });

        return preconditionChecks.Concat(evidenceChecks);
    }
}
