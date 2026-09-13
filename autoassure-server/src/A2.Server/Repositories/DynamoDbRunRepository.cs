using System.Globalization;
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
    private string RunningRunTableName => options.Value.RunningRunTableName;
    private string ApplicationTableName => options.Value.ApplicationTableName;

    public async Task<RunCreateResult> TryCreateAsync(Run run)
    {
        var transactItems = new List<TransactWriteItem>
        {
            // Notes:
            // No need conditional check for  environments, and scenario here because a run can exist despite that
            // environments and scenario are deleted. That's the whole point of taking their snapshots.
            ApplicationExistsCheck(run.OrganizationId, run.ApplicationId),
            new()
            {
                Put = new Put
                {
                    TableName = RunTableName,
                    Item = run.ToDynamoDbRow(),
                    ConditionExpression = "attribute_not_exists(RowKey)",
                },
            },
            new()
            {
                Put = new Put
                {
                    TableName = RunTableName,
                    Item = run.Environment.ToDynamoDbRow(
                        run.Id,
                        run.OrganizationId,
                        run.ApplicationId
                    ),
                },
            },
        };
        transactItems.AddRange(
            run.Scenarios.Select(scenario => new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = RunTableName,
                    Item = scenario.ToDynamoDbRow(run.Id, run.OrganizationId, run.ApplicationId),
                },
            })
        );

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
                return RunCreateResult.AlreadyExists;
            }

            throw;
        }
    }

    public async Task<Run?> GetByIdAsync(Guid organizationId, Guid applicationId, Guid runId)
    {
        var headerRowKey = DynamoDbMapper.RunHeaderRowKey(runId);
        var environmentRowKey = DynamoDbMapper.RunEnvironmentRowKey(runId);
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
                    // Guaranteed, not assumed: RunHeaderRowKey/RunEnvironmentRowKey/RunScenarioRowKey
                    // carry the 1000/2000/3000 sort-key prefixes, all below RunStatusUpdateRowKey's
                    // 4000 -- see those methods' doc.
                    [":low"] = new(headerRowKey),
                    [":high"] = new(DynamoDbMapper.RunStatusUpdateRowKeyPrefix(runId)),
                },
                ConsistentRead = true,
            }
        );

        var headerRow = rows.Find(row => row["RowKey"].S == headerRowKey);
        if (headerRow is null)
        {
            return null;
        }

        var environmentRow = rows.Find(row => row["RowKey"].S == environmentRowKey);

        if (environmentRow is null)
        {
            // The Environment row is written in the same transaction as the header and nothing ever
            // deletes it on its own -- a header with no Environment row means the stored data is
            // corrupted, not merely absent.
            throw new CorruptedDynamoDbRowException(
                $"Run '{runId}' has a header row but no Environment row."
            );
        }

        var scenarios = rows.Where(row =>
                row["RowKey"].S != headerRowKey && row["RowKey"].S != environmentRowKey
            )
            .Select(row => row.ToRunScenarioSnapshot())
            .ToList();

        return headerRow.ToRun(environmentRow.ToRunEnvironmentSnapshot(), scenarios);
    }

    public async Task<IReadOnlyList<RunInfo>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId,
        IReadOnlyCollection<RunTrigger> triggers
    )
    {
        if (triggers.Count == 0)
        {
            throw new ArgumentException("triggers cannot be empty.", nameof(triggers));
        }

        var expressionAttributeValues = new Dictionary<string, AttributeValue>
        {
            [":partitionKey"] = new(
                DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
            ),
        };
        var triggerPlaceholders = triggers
            .Select((trigger, index) =>
                {
                    var placeholder = $":trigger{index}";
                    expressionAttributeValues[placeholder] = new AttributeValue(trigger.ToString());
                    return placeholder;
                }
            )
            .ToList();

        var rows = await QueryAllPagesAsync(
            new QueryRequest
            {
                TableName = RunTableName,
                IndexName = "RunHeaderIndex",
                KeyConditionExpression = "OrganizationId_ApplicationId = :partitionKey",
                FilterExpression = $"#trigger IN ({string.Join(", ", triggerPlaceholders)})",
                ExpressionAttributeNames = new Dictionary<string, string>
                {
                    ["#trigger"] = "Trigger",
                },
                ExpressionAttributeValues = expressionAttributeValues,
                // The index's range key, HeaderId, is the Run's own UUIDv7 id, so descending order is
                // newest-first with no separate sort.
                ScanIndexForward = false,
            }
        );

        return rows.Select(row => row.ToRunInfo()).ToList();
    }

    public async Task<RunStartResult?> TryMarkAsStartedAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        DateTimeOffset startedAt,
        RunEnvironmentSnapshot environment
    )
    {
        var transactItems = new List<TransactWriteItem>
        {
            new()
            {
                Update = new Update
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, runId),
                    UpdateExpression =
                        "SET #status = :running, StartedAt = :startedAt, LastHeartbeatAt = :startedAt",
                    ConditionExpression = "#status = :pending",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status",
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":running"] = new(nameof(RunStatus.Running)),
                        [":pending"] = new(nameof(RunStatus.Pending)),
                        [":startedAt"] = new(startedAt.ToString("O")),
                    },
                },
            },
            new()
            {
                Update = new Update
                {
                    TableName = RunTableName,
                    Key = EnvironmentRowKey(organizationId, applicationId, runId),
                    UpdateExpression = "SET Variables = :variables",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":variables"] = environment.ToVariablesAttributeValue(),
                    },
                },
            },
            new()
            {
                Put = new Put
                {
                    TableName = RunningRunTableName,
                    Item = new RunningRun { Id = runId, StartedAt = startedAt }.ToDynamoDbRow(
                        organizationId,
                        applicationId
                    ),
                },
            },
        };

        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }
            );
            return new RunStartResult { StartedAt = startedAt, LastHeartbeatAt = startedAt };
        }
        catch (TransactionCanceledException ex)
            when (ex.CancellationReasons is { Count: > 0 } reasons
                  && reasons[0].Code == "ConditionalCheckFailed"
                 )
        {
            return null;
        }
    }

    public async Task<bool> TryMarkAsEndedAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        RunStatus terminalStatus,
        DateTimeOffset completedAt
    )
    {
        if (
            terminalStatus != RunStatus.Completed
            && terminalStatus != RunStatus.Cancelled
            && terminalStatus != RunStatus.Abandoned
        )
        {
            throw new ArgumentException(
                $"{terminalStatus} is not a terminal state End Run can write -- only Completed, "
                + "Cancelled or Abandoned.",
                nameof(terminalStatus)
            );
        }

        var conditionExpression = "#status = :running";
        var attributeValues = new Dictionary<string, AttributeValue>
        {
            [":terminal"] = new(terminalStatus.ToString()),
            [":completedAt"] = new(completedAt.ToString("O")),
            [":running"] = new(RunStatus.Running.ToString()),
        };

        var updateExpression = "SET #status = :terminal, CompletedAt = :completedAt";

        // Two items, one transaction: the header item carries every condition above, and deleting the
        // RunningRuns row rides along with no condition of its own -- if the header's condition fails,
        // the whole transaction cancels and the row is left in place. Deleting it in the same
        // transaction, rather than as a separate write, is what keeps it from ever drifting out of sync
        // with the header's own Status, whichever of the three callers ended the Run.
        var transactItems = new List<TransactWriteItem>
        {
            new()
            {
                Update = new Update
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, runId),
                    UpdateExpression = updateExpression,
                    ConditionExpression = conditionExpression,
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status",
                    },
                    ExpressionAttributeValues = attributeValues,
                },
            },
            new()
            {
                Delete = new Delete
                {
                    TableName = RunningRunTableName,
                    Key = RunningRunKey(organizationId, applicationId, runId),
                },
            },
        };

        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }
            );
            return true;
        }
        catch (TransactionCanceledException ex)
            when (ex.CancellationReasons is { Count: > 0 } reasons
                  && reasons[0].Code == "ConditionalCheckFailed"
                 )
        {
            return false;
        }
    }

    public async Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        RunUpdatableFields fields
    )
    {
        var setClauses = new List<string>();
        var attributeValues = new Dictionary<string, AttributeValue>
        {
            [":running"] = new(RunStatus.Running.ToString()),
        };

        if (fields.HeartbeatAt is { } heartbeatAt)
        {
            setClauses.Add("LastHeartbeatAt = :heartbeatAt");
            attributeValues[":heartbeatAt"] = new(heartbeatAt.ToString("O"));
        }

        if (fields.TotalActivityCount is { } total)
        {
            setClauses.Add("TotalActivityCount = :total");
            attributeValues[":total"] = new AttributeValue
            {
                N = total.ToString(CultureInfo.InvariantCulture),
            };
        }

        if (fields.PassedActivityCount is { } passed)
        {
            setClauses.Add("PassedActivityCount = :passed");
            attributeValues[":passed"] = new AttributeValue
            {
                N = passed.ToString(CultureInfo.InvariantCulture),
            };
        }

        if (fields.FailedActivityCount is { } failed)
        {
            setClauses.Add("FailedActivityCount = :failed");
            attributeValues[":failed"] = new AttributeValue
            {
                N = failed.ToString(CultureInfo.InvariantCulture),
            };
        }

        if (fields.SkippedActivityCount is { } skipped)
        {
            setClauses.Add("SkippedActivityCount = :skipped");
            attributeValues[":skipped"] = new AttributeValue
            {
                N = skipped.ToString(CultureInfo.InvariantCulture),
            };
        }

        if (setClauses.Count == 0)
        {
            throw new ArgumentException(
                "At least one field on RunUpdatableFields must be set.",
                nameof(fields)
            );
        }

        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, runId),
                    UpdateExpression = "SET " + string.Join(", ", setClauses),
                    // When the Run was cancelled or already swept, Then Status is no longer Running and
                    // this condition fails -- a heartbeating worker finds out on its very next beat with
                    // no separate signalling channel (see fix_run_design.md section 5).
                    ConditionExpression = "#status = :running",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status",
                    },
                    ExpressionAttributeValues = attributeValues,
                }
            );
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<RunningRun>> ListRunningByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    )
    {
        // A strongly consistent read against the RunningRuns table itself, not a GSI -- GSIs never
        // support ConsistentRead, so a Run that just started in the same transaction that put it here
        // could otherwise be briefly missing from this result.
        var rows = await QueryAllPagesAsync(
            new QueryRequest
            {
                TableName = RunningRunTableName,
                KeyConditionExpression = "OrganizationId_ApplicationId = :partitionKey",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":partitionKey"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                },
                ConsistentRead = true,
            }
        );

        return rows.Select(row => row.ToRunningRun()).ToList();
    }

    public async Task<bool> TryAppendStatusUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        RunStatusUpdate update
    )
    {
        var transactItems = new List<TransactWriteItem>
        {
            new()
            {
                Put = new Put
                {
                    TableName = RunTableName,
                    Item = update.ToDynamoDbRow(runId, organizationId, applicationId),
                    ConditionExpression = "attribute_not_exists(RowKey)",
                },
            },
            new()
            {
                Update = new Update
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, runId),
                    UpdateExpression = "SET LastSeq = :seq",
                    ConditionExpression = "LastSeq < :seq AND #status = :running",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status",
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":seq"] = new() { N = update.Seq.ToString(CultureInfo.InvariantCulture) },
                        [":running"] = new(RunStatus.Running.ToString()),
                    },
                },
            },
        };

        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }
            );
            return true;
        }
        catch (TransactionCanceledException ex)
            when (ex.CancellationReasons?.Any(reason => reason.Code == "ConditionalCheckFailed")
                  == true
                 )
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<RunStatusUpdate>> ListStatusUpdatesAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        long afterSeq,
        int limit
    )
    {
        if (afterSeq < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(afterSeq),
                afterSeq,
                "afterSeq cannot be negative."
            );
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "limit must be positive.");
        }

        if (afterSeq == long.MaxValue)
        {
            return [];
        }

        var response = await client.QueryAsync(
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
                    [":low"] = new(DynamoDbMapper.RunStatusUpdateRowKey(runId, afterSeq + 1)),
                    [":high"] = new(DynamoDbMapper.RunStatusUpdateRowKey(runId, long.MaxValue)),
                },
                ConsistentRead = true,
                ScanIndexForward = true,
                Limit = limit,
            }
        );

        return response.Items.Select(row => row.ToRunStatusUpdate()).ToList();
    }

    // Builds a header row's primary key -- the shared Key every Start/End/Update Stats UpdateItem
    // targets, since none of them ever touch a Scenario snapshot or status update row.
    private static Dictionary<string, AttributeValue> HeaderKey(
        Guid organizationId,
        Guid applicationId,
        Guid runId
    ) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
            ),
            ["RowKey"] = new(DynamoDbMapper.RunHeaderRowKey(runId)),
        };

    // Builds a Run's Environment snapshot row's primary key -- the Key TryMarkAsStartedAsync's masked
    // Variables
    // overwrite targets. Mirrors HeaderKey.
    private static Dictionary<string, AttributeValue> EnvironmentRowKey(
        Guid organizationId,
        Guid applicationId,
        Guid runId
    ) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
            ),
            ["RowKey"] = new(DynamoDbMapper.RunEnvironmentRowKey(runId)),
        };

    // Builds a RunningRuns row's primary key -- the Key TryMarkAsEndedAsync's Delete targets. Mirrors
    // HeaderKey, but against RunningRunTableName's own key shape (OrganizationId_ApplicationId + RunId),
    // not the Runs table's RowKey.
    private static Dictionary<string, AttributeValue> RunningRunKey(
        Guid organizationId,
        Guid applicationId,
        Guid runId
    ) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
            ),
            ["RunId"] = new(runId.ToString()),
        };

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