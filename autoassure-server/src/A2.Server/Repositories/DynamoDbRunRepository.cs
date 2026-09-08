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

    public async Task<RunStartResult?> TryStartAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        DateTimeOffset startedAt,
        RunEnvironmentSnapshot maskedEnvironment
    )
    {
        // When a Run is claimed, Then its DeadlineAt is fixed from this same instant so the sweeper
        // (task 8) always compares against the moment the worker actually started, not against a
        // separately-read clock.
        var deadlineAt = startedAt + RunExecutionPolicy.MaxRunDuration;

        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, id),
                    // Overwriting Environment with the caller's already-masked snapshot here, atomically
                    // with the Pending->Running transition, is what wipes the real sensitive values
                    // captured at create time out of storage the instant the Run is claimed -- see this
                    // method's doc on IRunRepository.
                    UpdateExpression =
                        "SET #status = :running, StartedAt = :startedAt, DeadlineAt = :deadlineAt, "
                        + "LastHeartbeatAt = :startedAt, InFlightShard = :shard, "
                        + "Environment = :maskedEnvironment",
                    // Status is the only concurrency control (see fix_run_design.md section 5) -- this
                    // is what lets exactly one of several racing claims win, and it doubles as an
                    // existence check: a Run that does not exist has no Status attribute at all, so the
                    // comparison fails the same way.
                    ConditionExpression = "#status = :pending",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status",
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":running"] = new(RunStatus.Running.ToString()),
                        [":pending"] = new(RunStatus.Pending.ToString()),
                        [":startedAt"] = new(startedAt.ToString("O")),
                        [":deadlineAt"] = new(deadlineAt.ToString("O")),
                        [":shard"] = new(DynamoDbMapper.RunInFlightShard(id)),
                        [":maskedEnvironment"] = maskedEnvironment.ToAttributeValue(),
                    },
                }
            );
            return new RunStartResult
            {
                StartedAt = startedAt,
                LastHeartbeatAt = startedAt,
                DeadlineAt = deadlineAt,
            };
        }
        catch (ConditionalCheckFailedException)
        {
            return null;
        }
    }

    public async Task<bool> TryEndAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        RunStatus terminalStatus,
        RunStatusReason? statusReason,
        DateTimeOffset completedAt,
        DateTimeOffset? heartbeatCutoff = null
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

        if (statusReason is not null && terminalStatus != RunStatus.Abandoned)
        {
            throw new ArgumentException(
                "StatusReason can only be written alongside Abandoned -- Completed and Cancelled are "
                    + "self-explanatory (see Run.StatusReason's doc).",
                nameof(statusReason)
            );
        }

        // When the sweeper calls End Run, Then it adds LastHeartbeatAt < heartbeatCutoff on top of the
        // Status = Running condition every caller shares, so a worker that beat again after the sweeper
        // read it as stale keeps its Run.
        var conditionExpression = "#status = :running";
        var attributeValues = new Dictionary<string, AttributeValue>
        {
            [":terminal"] = new(terminalStatus.ToString()),
            [":completedAt"] = new(completedAt.ToString("O")),
            [":running"] = new(RunStatus.Running.ToString()),
        };
        if (heartbeatCutoff is { } cutoff)
        {
            conditionExpression += " AND LastHeartbeatAt < :cutoff";
            attributeValues[":cutoff"] = new(cutoff.ToString("O"));
        }

        // Removing InFlightShard on every terminal transition is what drops a finished Run out of the
        // sweeper's sparse InFlightIndex, whichever of the three callers ended it.
        var updateExpression =
            "SET #status = :terminal, CompletedAt = :completedAt REMOVE InFlightShard";
        if (statusReason is { } reason)
        {
            updateExpression =
                "SET #status = :terminal, StatusReason = :reason, CompletedAt = :completedAt "
                + "REMOVE InFlightShard";
            attributeValues[":reason"] = new(reason.ToString());
        }

        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, id),
                    UpdateExpression = updateExpression,
                    ConditionExpression = conditionExpression,
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

    public async Task<bool> TryUpdateStatsAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        int totalActivityCount,
        int passedActivityCount,
        int failedActivityCount,
        int skippedActivityCount
    )
    {
        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, id),
                    UpdateExpression =
                        "SET TotalActivityCount = :total, PassedActivityCount = :passed, "
                        + "FailedActivityCount = :failed, SkippedActivityCount = :skipped",
                    ConditionExpression = "#status = :running",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status",
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":total"] = new AttributeValue
                        {
                            N = totalActivityCount.ToString(CultureInfo.InvariantCulture),
                        },
                        [":passed"] = new AttributeValue
                        {
                            N = passedActivityCount.ToString(CultureInfo.InvariantCulture),
                        },
                        [":failed"] = new AttributeValue
                        {
                            N = failedActivityCount.ToString(CultureInfo.InvariantCulture),
                        },
                        [":skipped"] = new AttributeValue
                        {
                            N = skippedActivityCount.ToString(CultureInfo.InvariantCulture),
                        },
                        [":running"] = new(RunStatus.Running.ToString()),
                    },
                }
            );
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<bool> TryHeartbeatAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        DateTimeOffset heartbeatAt
    )
    {
        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, id),
                    UpdateExpression = "SET LastHeartbeatAt = :heartbeatAt",
                    // When the Run was cancelled or already swept, Then Status is no longer Running and
                    // this condition fails -- the worker finds out on its very next beat with no
                    // separate signalling channel (see fix_run_design.md section 5).
                    ConditionExpression = "#status = :running",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status",
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":heartbeatAt"] = new(heartbeatAt.ToString("O")),
                        [":running"] = new(RunStatus.Running.ToString()),
                    },
                }
            );
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<StaleInFlightRun>> ListStaleInFlightRunsAsync(
        int shard,
        DateTimeOffset heartbeatCutoff
    )
    {
        if (shard < 0 || shard >= RunExecutionPolicy.InFlightShardCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shard),
                shard,
                $"Shard must be between 0 and {RunExecutionPolicy.InFlightShardCount - 1}."
            );
        }

        // The InFlightIndex is KEYS_ONLY, so a query against it can only ever return LastHeartbeatAt and
        // the base table's own keys -- never DeadlineAt. A KeyConditionExpression can only narrow this
        // Query by LastHeartbeatAt (the index's range key), so bounding it there would silently drop the
        // "fresh heartbeat, passed deadline" half of the OR the design requires. Reading the whole shard
        // instead, unbounded, is what lets the loop below still catch that case with a follow-up read.
        var rows = await QueryAllPagesAsync(
            new QueryRequest
            {
                TableName = RunTableName,
                IndexName = "InFlightIndex",
                KeyConditionExpression = "InFlightShard = :shard",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":shard"] = new(DynamoDbMapper.RunInFlightShardKey(shard)),
                },
            }
        );

        var now = DateTimeOffset.UtcNow;
        var staleRuns = new List<StaleInFlightRun>();
        foreach (var row in rows)
        {
            var (organizationId, applicationId) = DynamoDbMapper.ParseApplicationScopedPartitionKey(
                row["OrganizationId_ApplicationId"].S
            );
            // Only a header row ever carries InFlightShard, so RowKey here is always the bare RunId --
            // never a Scenario or status update row's suffixed key.
            var id = Guid.Parse(row["RowKey"].S);
            var lastHeartbeatAt = DateTimeOffset.Parse(
                row["LastHeartbeatAt"].S,
                CultureInfo.InvariantCulture
            );

            // When the heartbeat itself is already older than the cutoff, Then this Run is stale without
            // reading anything else -- LastHeartbeatAt is one of the attributes this index projects.
            if (lastHeartbeatAt < heartbeatCutoff)
            {
                staleRuns.Add(
                    new StaleInFlightRun
                    {
                        OrganizationId = organizationId,
                        ApplicationId = applicationId,
                        Id = id,
                    }
                );
                continue;
            }

            // When the heartbeat is fresh, Then only a passed DeadlineAt can still make this Run stale,
            // and DeadlineAt is not projected onto this index -- a consistent read of the header itself
            // is the only way to check it. A Run that ended between the Query above and this read still
            // carries a (now historical) DeadlineAt, but that is harmless: the sweeper's own End Run call
            // is conditioned on Status = Running and simply no-ops for a Run that already ended.
            var headerRow = await client.GetItemAsync(
                new GetItemRequest
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, id),
                    ConsistentRead = true,
                    ProjectionExpression = "DeadlineAt",
                }
            );
            if (
                headerRow.Item.TryGetValue("DeadlineAt", out var deadlineAtValue)
                && DateTimeOffset.Parse(deadlineAtValue.S, CultureInfo.InvariantCulture) < now
            )
            {
                staleRuns.Add(
                    new StaleInFlightRun
                    {
                        OrganizationId = organizationId,
                        ApplicationId = applicationId,
                        Id = id,
                    }
                );
            }
        }

        return staleRuns;
    }

    public async Task<bool> TryAppendStatusUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        RunStatusUpdate update,
        long? expiresAt
    )
    {
        var transactItems = new List<TransactWriteItem>
        {
            new TransactWriteItem
            {
                Put = new Put
                {
                    TableName = RunTableName,
                    Item = update.ToDynamoDbRow(id, organizationId, applicationId, expiresAt),
                    // A retried append (dispatch is at-least-once) targets the same Seq and therefore
                    // the same RowKey, so this is what turns the retry into a no-op instead of a
                    // second row for the same update.
                    ConditionExpression = "attribute_not_exists(RowKey)",
                },
            },
            new TransactWriteItem
            {
                Update = new Update
                {
                    TableName = RunTableName,
                    Key = HeaderKey(organizationId, applicationId, id),
                    UpdateExpression = "SET LastSeq = :seq",
                    // When LastSeq has already reached or passed this Seq, Then this append is a stale
                    // retry and this condition fails alongside the Put above. When Status is not
                    // Running, Then the owning worker was cancelled or swept and cannot append further
                    // -- there is no other signalling channel (see fix_run_design.md section 5).
                    ConditionExpression = "LastSeq < :seq AND #status = :running",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#status"] = "Status",
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":seq"] = new AttributeValue
                        {
                            N = update.Seq.ToString(CultureInfo.InvariantCulture),
                        },
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
        Guid id,
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

        // When afterSeq is already the largest value a seq can ever take, Then no row could possibly
        // sort after it -- return empty rather than overflow computing afterSeq + 1 below.
        if (afterSeq == long.MaxValue)
        {
            return [];
        }

        // BETWEEN this Run's own next-Seq key and its own highest-possible-Seq key stays entirely
        // within this Run's update rows and never crosses into another Run's rows sharing the same
        // partition -- see RunStatusUpdateRowKeyPrefix's doc for why a shared 36-character RunId prefix
        // makes that safe. A bare "RowKey > :cursor" comparator alone would not: DynamoDB allows only
        // one condition on a Query's sort key, so without this upper bound the query would also return
        // every later-sorting row of every OTHER Run in the same Application, since every Run's rows
        // share one partition. Seq is dense (see fix_run_design.md decision 9), so afterSeq + 1 is
        // exactly the smallest Seq an actual row after the cursor could have -- there is no gap a real
        // row could occupy strictly between afterSeq and afterSeq + 1.
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
                    [":low"] = new(DynamoDbMapper.RunStatusUpdateRowKey(id, afterSeq + 1)),
                    [":high"] = new(DynamoDbMapper.RunStatusUpdateRowKey(id, long.MaxValue)),
                },
                ConsistentRead = true,
                ScanIndexForward = true,
                // One bounded page, not the LastEvaluatedKey loop GetByIdAsync and
                // ListByApplicationAsync use -- this cursor is a public, client-driven pagination
                // mechanism (see fix_run_design.md section 4), so a caller wanting more polls again with
                // the last Seq it received rather than this method reading until exhausted.
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
        Guid id
    ) =>
        new()
        {
            ["OrganizationId_ApplicationId"] = new(
                DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
            ),
            ["RowKey"] = new(DynamoDbMapper.RunHeaderRowKey(id)),
        };

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
