using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbRunRepository(IAmazonDynamoDB client, IOptions<DynamoDbOptions> options)
    : IRunRepository
{
    private string RunTableName => options.Value.RunTableName;
    private string ApplicationTableName => options.Value.ApplicationTableName;
    private string EnvironmentTableName => options.Value.EnvironmentTableName;

    public async Task<RunCreateResult> TryCreateAsync(
        Run run,
        IReadOnlyList<RunScenarioSnapshot> scenarios
    )
    {
        // DynamoDB's TransactWriteItems caps a transaction at 100 items and 4 MB. The Application
        // check, the Environment check and the header Put already spend 3 of those items, leaving
        // Quota.MaxScenariosPerRun for Scenario rows -- see its doc for the exact number.
        if (scenarios.Count > Quota.MaxScenariosPerRun)
        {
            throw new ArgumentException(
                $"A Run cannot hold more than {Quota.MaxScenariosPerRun} Scenario snapshots -- "
                    + "DynamoDB's TransactWriteItems caps a single transaction at 100 items.",
                nameof(scenarios)
            );
        }

        // Every row of this Run -- the header and every Scenario snapshot -- carries the same
        // ExpiresAt, computed once here rather than trusted from the caller, so the 7-day/3-year
        // retention numbers stay in the one place RunRetentionPolicy puts them.
        var expiresAt = RunRetentionPolicy.ExpiresAt(run.Trigger, run.CreatedAt);
        var effectiveRun = run with { ExpiresAt = expiresAt };

        var transactItems = new List<TransactWriteItem>
        {
            ApplicationExistsCheck(run.OrganizationId, run.ApplicationId),
            EnvironmentExistsCheck(
                run.OrganizationId,
                run.ApplicationId,
                run.Environment.Source.Id
            ),
            new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = RunTableName,
                    Item = effectiveRun.ToDynamoDbRow(),
                    // A create can never overwrite -- this is the only condition that guards the
                    // header, and since the whole transaction is atomic, it also protects every
                    // Scenario row below from being written alongside a duplicate header.
                    ConditionExpression = "attribute_not_exists(RowKey)",
                },
            },
        };
        transactItems.AddRange(
            scenarios.Select(scenario => new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = RunTableName,
                    Item = scenario.ToDynamoDbRow(
                        run.Id,
                        run.OrganizationId,
                        run.ApplicationId,
                        expiresAt
                    ),
                },
            })
        );

        // When TransactWriteItemsAsync is cancelled due to the Application check (index 0) failing,
        // Then return ApplicationNotFound. When it's the Environment check (index 1), Then return
        // EnvironmentNotFound. When it's the header Put (index 2), Then a Run with this Id already
        // exists -- return AlreadyExists. Any other cancellation reason (throttling, conflict, etc.)
        // is unexpected and MUST propagate instead of being reported as a business result.
        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }
            );
            return RunCreateResult.Success;
        }
        catch (TransactionCanceledException ex)
            when (ex.CancellationReasons is { Count: >= 3 } reasons)
        {
            if (reasons[0].Code == "ConditionalCheckFailed")
            {
                return RunCreateResult.ApplicationNotFound;
            }

            if (reasons[1].Code == "ConditionalCheckFailed")
            {
                return RunCreateResult.EnvironmentNotFound;
            }

            if (reasons[2].Code == "ConditionalCheckFailed")
            {
                return RunCreateResult.AlreadyExists;
            }

            throw;
        }
    }

    public async Task<RunDetail?> GetByIdAsync(Guid organizationId, Guid applicationId, Guid id)
    {
        var headerRowKey = DynamoDbMapper.RunHeaderRowKey(id);

        // BETWEEN the header key and the status-update prefix reads the header and every Scenario
        // snapshot row in one Query, while never touching a status update row -- see
        // RunStatusUpdateRowKeyPrefix's doc for why that boundary is safe to rely on.
        var rows = await QueryAllPagesAsync(
            new QueryRequest
            {
                TableName = RunTableName,
                KeyConditionExpression =
                    "OrganizationId_ApplicationId = :partitionKey AND RowKey BETWEEN :low AND :high",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":partitionKey"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    [":low"] = new(headerRowKey),
                    [":high"] = new(DynamoDbMapper.RunStatusUpdateRowKeyPrefix(id)),
                },
                ConsistentRead = true,
            }
        );

        var headerRow = rows.Find(row => row["RowKey"].S == headerRowKey);
        if (headerRow is null)
        {
            return null;
        }

        var header = headerRow.ToRun();
        if (IsExpired(header.ExpiresAt))
        {
            return null;
        }

        var scenarios = rows.Where(row => row["RowKey"].S != headerRowKey)
            .Select(row => row.ToRunScenarioSnapshot())
            .ToList();

        // When the header survives but no Scenario row does -- because they expired first, or a data
        // anomaly -- Then treat the Run as not found rather than returning a partial result. See
        // fix_run_design.md section 6.
        if (scenarios.Count == 0)
        {
            return null;
        }

        return new RunDetail { Header = header, Scenarios = scenarios };
    }

    public async Task<IReadOnlyList<RunSummary>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    )
    {
        // The sparse RunHeaderIndex holds exactly one entry per Run -- only header rows carry
        // HeaderId -- so this Query can never read a Scenario snapshot or status update row, unlike a
        // FilterExpression over the base table's partition, which fix_run_design.md forbids here
        // because it would read and charge for every row the split exists to stop paying for.
        // Excluding Authoring runs with a FilterExpression on Trigger alone is still cheap: it filters
        // header-only projections, not full rows.
        var rows = await QueryAllPagesAsync(
            new QueryRequest
            {
                TableName = RunTableName,
                IndexName = "RunHeaderIndex",
                KeyConditionExpression = "OrganizationId_ApplicationId = :partitionKey",
                FilterExpression = "#trigger <> :authoring",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#trigger"] = "Trigger",
                },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":partitionKey"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    [":authoring"] = new(RunTrigger.Authoring.ToString()),
                },
                // The index's range key, HeaderId, is the Run's own UUIDv7 id, so ascending order (the
                // default, set explicitly here) is creation order with no separate sort.
                ScanIndexForward = true,
            }
        );

        // RunHeaderIndex does not project ExpiresAt, so there is no expiry attribute to read here. It
        // does project Trigger and CreatedAt, and TryCreateAsync always derives a header's ExpiresAt
        // from exactly those two values via RunRetentionPolicy, so recomputing it here reproduces the
        // stored value without needing it projected.
        return rows.Select(row => row.ToRunSummary())
            .Where(summary =>
                !IsExpired(RunRetentionPolicy.ExpiresAt(summary.Trigger, summary.CreatedAt))
            )
            .ToList();
    }

    private static bool IsExpired(long? expiresAt) =>
        expiresAt is { } value && value <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private TransactWriteItem ApplicationExistsCheck(Guid organizationId, Guid applicationId) =>
        new()
        {
            ConditionCheck = new ConditionCheck
            {
                TableName = ApplicationTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId"] = new(organizationId.ToString()),
                    ["Id"] = new(applicationId.ToString()),
                },
                ConditionExpression = "attribute_exists(Id)",
            },
        };

    private TransactWriteItem EnvironmentExistsCheck(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId
    ) =>
        new()
        {
            ConditionCheck = new ConditionCheck
            {
                TableName = EnvironmentTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    ["Id"] = new(environmentId.ToString()),
                },
                ConditionExpression = "attribute_exists(Id)",
            },
        };

    // DynamoDB caps a single Query response at 1 MB, so a Run whose header and Scenario snapshots
    // together exceed that must be read across multiple pages. Then loop on LastEvaluatedKey until
    // DynamoDB reports none remain, rather than assuming the first page is everything -- nothing else
    // in this codebase does that yet (see fix_run_design.md section 3).
    private async Task<List<Dictionary<string, AttributeValue>>> QueryAllPagesAsync(
        QueryRequest request
    )
    {
        var rows = new List<Dictionary<string, AttributeValue>>();
        Dictionary<string, AttributeValue>? lastEvaluatedKey = null;
        do
        {
            request.ExclusiveStartKey = lastEvaluatedKey;
            var response = await client.QueryAsync(request);
            rows.AddRange(response.Items);
            lastEvaluatedKey = response.LastEvaluatedKey is { Count: > 0 } key ? key : null;
        } while (lastEvaluatedKey is not null);

        return rows;
    }
}
