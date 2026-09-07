using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbRunRepository"/> against DynamoDB Local, covering
/// Create Run, Get Run and List Runs: the transactional create, its Application/Environment existence
/// checks, the TTL retention numbers, the LastEvaluatedKey pagination loop, Get Run's exclusion of
/// status update rows, and List Runs' sparse RunHeaderIndex query.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbRunRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string RunTableName = "Runs";
    private const string ApplicationTableName = "Applications";
    private const string EnvironmentTableName = "Environments";

    private static readonly DateTimeOffset FixedNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbRunRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbRunRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    RunTableName = RunTableName,
                    ApplicationTableName = ApplicationTableName,
                    EnvironmentTableName = EnvironmentTableName,
                }
            )
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = RunTableName,
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                    new KeySchemaElement("RowKey", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ApplicationId", ScalarAttributeType.S),
                    new AttributeDefinition("RowKey", ScalarAttributeType.S),
                    new AttributeDefinition("HeaderId", ScalarAttributeType.S),
                ],
                // Mirrors ../../autoassure-infra/dynamodb.tf's RunHeaderIndex exactly -- same sparse
                // key shape and the same INCLUDE projection list -- so a test proves what the real
                // index actually returns rather than what a looser local approximation would.
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "RunHeaderIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                            new KeySchemaElement("HeaderId", KeyType.RANGE),
                        ],
                        Projection = new Projection
                        {
                            ProjectionType = ProjectionType.INCLUDE,
                            NonKeyAttributes =
                            [
                                "Id",
                                "Trigger",
                                "Status",
                                "StatusReason",
                                "TotalActivityCount",
                                "PassedActivityCount",
                                "FailedActivityCount",
                                "SkippedActivityCount",
                                "CreatedAt",
                                "StartedAt",
                                "CompletedAt",
                            ],
                        },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = ApplicationTableName,
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId", KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId", ScalarAttributeType.S),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = EnvironmentTableName,
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ApplicationId", ScalarAttributeType.S),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    public async Task DisposeAsync()
    {
        await _client.DeleteTableAsync(RunTableName);
        await _client.DeleteTableAsync(ApplicationTableName);
        await _client.DeleteTableAsync(EnvironmentTableName);
        _client.Dispose();
    }

    private Task PutApplicationAsync(Guid organizationId, Guid applicationId) =>
        _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = ApplicationTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId"] = new(organizationId.ToString()),
                    ["Id"] = new(applicationId.ToString()),
                },
            }
        );

    private Task PutEnvironmentAsync(Guid organizationId, Guid applicationId, Guid environmentId) =>
        _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = EnvironmentTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    ["Id"] = new(environmentId.ToString()),
                },
            }
        );

    private static SnapshotSource CreateSnapshotSource(Guid id) =>
        new()
        {
            Id = id,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = FixedNow,
            UpdatedAt = FixedNow,
        };

    private static RunEnvironmentSnapshot CreateEnvironmentSnapshot(Guid environmentId) =>
        new()
        {
            Source = CreateSnapshotSource(environmentId),
            Name = "Staging",
            Classification = EnvironmentClassification.NonProduction,
            Variables = [],
        };

    private static Run CreateRun(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId,
        RunTrigger trigger = RunTrigger.Manual,
        DateTimeOffset? createdAt = null
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Trigger = trigger,
            Status = RunStatus.Pending,
            Environment = CreateEnvironmentSnapshot(environmentId),
            CreatedAt = createdAt ?? FixedNow,
        };

    private static RunScenarioSnapshot CreateScenarioSnapshot(string? description = null) =>
        new()
        {
            Source = CreateSnapshotSource(Guid.CreateVersion7()),
            Title = "Checkout completes",
            Description = description ?? "Verify a user can complete checkout",
            Folder = "/",
            Tags = [],
            Activities = [],
        };

    // Reads a Run's raw rows straight from the table, bypassing the repository, so a test can inspect
    // attributes (like ExpiresAt) that the repository's own read side does not surface on every row.
    private async Task<List<Dictionary<string, AttributeValue>>> QueryRawRowsAsync(
        Guid organizationId,
        Guid applicationId
    )
    {
        var response = await _client.QueryAsync(
            new QueryRequest
            {
                TableName = RunTableName,
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
        return response.Items;
    }

    [Fact]
    public async Task TryCreateAsync_WhenApplicationAndEnvironmentExist_CreatesRunReadableWithAllScenarios()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId);
        var scenarios = new[] { CreateScenarioSnapshot("First"), CreateScenarioSnapshot("Second") };

        // test
        var result = await _repository.TryCreateAsync(run, scenarios);
        var detail = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify
        Assert.Equal(RunCreateResult.Success, result);
        Assert.NotNull(detail);
        Assert.Equal(run.Id, detail.Header.Id);
        Assert.Equal(2, detail.Scenarios.Count);
        Assert.Contains(detail.Scenarios, scenario => scenario.Description == "First");
        Assert.Contains(detail.Scenarios, scenario => scenario.Description == "Second");
    }

    [Fact]
    public async Task TryCreateAsync_WhenRunAlreadyExists_ReturnsAlreadyExistsAndChangesNothing()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId);
        var originalScenarios = new[] { CreateScenarioSnapshot("Original") };
        await _repository.TryCreateAsync(run, originalScenarios);

        // test -- a second create for the same Run id, with different Scenario content
        var duplicateScenarios = new[]
        {
            CreateScenarioSnapshot("Retry-1"),
            CreateScenarioSnapshot("Retry-2"),
        };
        var result = await _repository.TryCreateAsync(run, duplicateScenarios);
        var detail = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify
        Assert.Equal(RunCreateResult.AlreadyExists, result);
        Assert.NotNull(detail);
        var found = Assert.Single(detail.Scenarios);
        Assert.Equal("Original", found.Description);
    }

    [Fact]
    public async Task TryCreateAsync_WhenApplicationDoesNotExist_ReturnsApplicationNotFoundAndWritesNothing()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var run = CreateRun(organizationId, applicationId, environmentId);
        var scenarios = new[] { CreateScenarioSnapshot() };

        // test
        var result = await _repository.TryCreateAsync(run, scenarios);
        var detail = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify
        Assert.Equal(RunCreateResult.ApplicationNotFound, result);
        Assert.Null(detail);
    }

    [Fact]
    public async Task TryCreateAsync_WhenEnvironmentDoesNotExist_ReturnsEnvironmentNotFoundAndWritesNothing()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var run = CreateRun(organizationId, applicationId, environmentId);
        var scenarios = new[] { CreateScenarioSnapshot() };

        // test
        var result = await _repository.TryCreateAsync(run, scenarios);
        var detail = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify
        Assert.Equal(RunCreateResult.EnvironmentNotFound, result);
        Assert.Null(detail);
    }

    [Fact]
    public async Task GetByIdAsync_WhenExpiresAtHasPassed_ReturnsNull()
    {
        // setup -- a Manual Run "created" long enough ago that create-time + 3 years has already
        // passed, so its computed ExpiresAt is already in the past.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var longAgo = DateTimeOffset.UtcNow.AddYears(-4);
        var run = CreateRun(
            organizationId,
            applicationId,
            environmentId,
            RunTrigger.Manual,
            longAgo
        );
        await _repository.TryCreateAsync(run, [CreateScenarioSnapshot()]);

        // test
        var detail = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify
        Assert.Null(detail);
    }

    [Fact]
    public async Task TryCreateAsync_WhenAuthoringTrigger_SetsExpiresAtSevenDaysOutOnEveryRow()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var run = CreateRun(
            organizationId,
            applicationId,
            environmentId,
            RunTrigger.Authoring,
            createdAt
        );
        var scenarios = new[] { CreateScenarioSnapshot("A"), CreateScenarioSnapshot("B") };

        // test
        await _repository.TryCreateAsync(run, scenarios);
        var rawRows = await QueryRawRowsAsync(organizationId, applicationId);

        // verify
        var expectedExpiresAt = createdAt.AddDays(7).ToUnixTimeSeconds();
        Assert.Equal(3, rawRows.Count); // header + 2 scenario rows
        Assert.All(rawRows, row => Assert.Equal(expectedExpiresAt.ToString(), row["ExpiresAt"].N));
    }

    [Fact]
    public async Task TryCreateAsync_WhenManualTrigger_SetsExpiresAtThreeYearsOutOnEveryRow()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var run = CreateRun(
            organizationId,
            applicationId,
            environmentId,
            RunTrigger.Manual,
            createdAt
        );
        var scenarios = new[] { CreateScenarioSnapshot("A") };

        // test
        await _repository.TryCreateAsync(run, scenarios);
        var rawRows = await QueryRawRowsAsync(organizationId, applicationId);

        // verify
        var expectedExpiresAt = createdAt.AddDays(3 * 365).ToUnixTimeSeconds();
        Assert.Equal(2, rawRows.Count); // header + 1 scenario row
        Assert.All(rawRows, row => Assert.Equal(expectedExpiresAt.ToString(), row["ExpiresAt"].N));
    }

    [Fact]
    public async Task GetByIdAsync_WhenScenarioRowsSpanQueryPages_ReturnsAllScenariosComplete()
    {
        // setup -- enough Scenario rows, each padded well past DynamoDB's 1 MB single-page Query
        // limit in total, to force GetByIdAsync's LastEvaluatedKey loop to run more than once.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId);
        var padding = new string('x', 25_000);
        var scenarios = Enumerable
            .Range(0, 50)
            .Select(index => CreateScenarioSnapshot($"scenario-{index}-{padding}"))
            .ToList();

        // test
        var createResult = await _repository.TryCreateAsync(run, scenarios);
        var detail = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify -- first, prove a single (non-looping) Query of this data really does get cut off,
        // so the assertion below is actually exercising the LastEvaluatedKey loop and not just
        // happening to fit in one page.
        var singlePage = await _client.QueryAsync(
            new QueryRequest
            {
                TableName = RunTableName,
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
        Assert.NotNull(singlePage.LastEvaluatedKey);
        Assert.NotEmpty(singlePage.LastEvaluatedKey);
        Assert.True(
            singlePage.Items.Count < 51,
            "expected a single Query page to be cut off before all 51 rows (1 header + 50 Scenarios)"
        );

        Assert.Equal(RunCreateResult.Success, createResult);
        Assert.NotNull(detail);
        Assert.Equal(50, detail.Scenarios.Count);
        var expectedIds = scenarios.Select(scenario => scenario.Source.Id).ToHashSet();
        var actualIds = detail.Scenarios.Select(scenario => scenario.Source.Id).ToHashSet();
        Assert.Equal(expectedIds, actualIds);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRunHasStatusUpdateRowWrittenDirectly_ReturnsNoStatusUpdates()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(run, [CreateScenarioSnapshot()]);

        // Writes a status update row by hand, bypassing the repository entirely (Task 5 adds no
        // append method yet), to prove Get Run never returns it even when one exists in the table.
        await _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = RunTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    ["RowKey"] = new(DynamoDbMapper.RunStatusUpdateRowKey(run.Id, 1)),
                },
            }
        );

        // test
        var detail = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify -- only the real Scenario row comes back; the hand-written update row did not blow
        // up the read (it would fail to parse as a Scenario snapshot if it were included) and did not
        // inflate the count.
        Assert.NotNull(detail);
        Assert.Single(detail.Scenarios);
    }

    [Fact]
    public async Task GetByIdAsync_WhenRunDoesNotExist_ReturnsNull()
    {
        // test
        var result = await _repository.GetByIdAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7()
        );

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenRunHasScenarios_ReturnsOneHeaderOnlySummary()
    {
        // setup -- a Run with several Scenario snapshots, so the table holds 4 rows for it (1 header +
        // 3 Scenario rows). Listing must still return exactly one entry, and it must be the header
        // shape, proving the sparse RunHeaderIndex is what answered the query rather than a scan of
        // every row in the partition.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId);
        var scenarios = new[]
        {
            CreateScenarioSnapshot("A"),
            CreateScenarioSnapshot("B"),
            CreateScenarioSnapshot("C"),
        };
        await _repository.TryCreateAsync(run, scenarios);

        // test
        var summaries = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify
        var summary = Assert.Single(summaries);
        Assert.Equal(run.Id, summary.Id);
        Assert.Equal(run.Trigger, summary.Trigger);
        Assert.Equal(run.Status, summary.Status);
        Assert.Equal(run.CreatedAt, summary.CreatedAt);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenRunBelongsToAnotherApplication_DoesNotReturnIt()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var otherApplicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutApplicationAsync(organizationId, otherApplicationId);
        await PutEnvironmentAsync(organizationId, otherApplicationId, environmentId);
        var otherApplicationRun = CreateRun(organizationId, otherApplicationId, environmentId);
        await _repository.TryCreateAsync(otherApplicationRun, [CreateScenarioSnapshot()]);

        // test
        var summaries = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify
        Assert.Empty(summaries);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenTriggerIsAuthoring_NeverReturnsIt()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var manualRun = CreateRun(organizationId, applicationId, environmentId);
        var authoringRun = CreateRun(
            organizationId,
            applicationId,
            environmentId,
            RunTrigger.Authoring
        );
        await _repository.TryCreateAsync(manualRun, [CreateScenarioSnapshot()]);
        await _repository.TryCreateAsync(authoringRun, [CreateScenarioSnapshot()]);

        // test
        var summaries = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify -- only the Manual Run comes back; there is no parameter to include Authoring runs.
        var summary = Assert.Single(summaries);
        Assert.Equal(manualRun.Id, summary.Id);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenMultipleRuns_ReturnsInCreationOrder()
    {
        // setup -- three Runs created in sequence. Guid.CreateVersion7() ids are time-sortable, so
        // ascending id order is creation order.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var firstRun = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(firstRun, [CreateScenarioSnapshot()]);
        var secondRun = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(secondRun, [CreateScenarioSnapshot()]);
        var thirdRun = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(thirdRun, [CreateScenarioSnapshot()]);

        // test
        var summaries = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify
        Assert.Equal(
            [firstRun.Id, secondRun.Id, thirdRun.Id],
            summaries.Select(summary => summary.Id)
        );
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenExpiresAtHasPassed_ExcludesIt()
    {
        // setup -- one Run "created" long enough ago that its computed ExpiresAt has already passed,
        // and one fresh Run in the same Application.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var longAgo = DateTimeOffset.UtcNow.AddYears(-4);
        var expiredRun = CreateRun(
            organizationId,
            applicationId,
            environmentId,
            RunTrigger.Manual,
            longAgo
        );
        var freshRun = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(expiredRun, [CreateScenarioSnapshot()]);
        await _repository.TryCreateAsync(freshRun, [CreateScenarioSnapshot()]);

        // test
        var summaries = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify
        var summary = Assert.Single(summaries);
        Assert.Equal(freshRun.Id, summary.Id);
    }

    // Reads a Run header's raw row straight from the table, bypassing the repository, so a test can
    // check storage-only attributes like InFlightShard that no repository method surfaces on a Run.
    private async Task<Dictionary<string, AttributeValue>> GetRawHeaderRowAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId
    )
    {
        var response = await _client.GetItemAsync(
            new GetItemRequest
            {
                TableName = RunTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    ["RowKey"] = new(DynamoDbMapper.RunHeaderRowKey(runId)),
                },
                ConsistentRead = true,
            }
        );
        return response.Item;
    }

    private async Task<Guid> CreatePendingRunAsync(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId
    )
    {
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(run, [CreateScenarioSnapshot()]);
        return run.Id;
    }

    [Fact]
    public async Task TryStartAsync_WhenRunIsPending_SucceedsAndWritesInFlightShard()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        var startedAt = FixedNow;

        // test
        var result = await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            startedAt
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.True(result);
        Assert.Equal(RunStatus.Running.ToString(), row["Status"].S);
        Assert.Equal(startedAt.ToString("O"), row["StartedAt"].S);
        Assert.Equal(startedAt.ToString("O"), row["LastHeartbeatAt"].S);
        Assert.Equal(
            (startedAt + RunExecutionPolicy.MaxRunDuration).ToString("O"),
            row["DeadlineAt"].S
        );
        Assert.Equal(DynamoDbMapper.RunInFlightShard(runId), row["InFlightShard"].S);
    }

    [Theory]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Abandoned)]
    public async Task TryStartAsync_WhenRunIsNotPending_FailsAndChangesNothing(RunStatus notPending)
    {
        // setup -- claim the Run once to reach Running, then optionally end it, so its Status is
        // notPending before the real test call.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryStartAsync(organizationId, applicationId, runId, FixedNow);
        if (notPending != RunStatus.Running)
        {
            await _repository.TryEndAsync(
                organizationId,
                applicationId,
                runId,
                notPending,
                notPending == RunStatus.Abandoned ? RunStatusReason.WorkerCrashed : null,
                FixedNow
            );
        }
        var beforeRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // test
        var result = await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow.AddMinutes(1)
        );
        var afterRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.False(result);
        Assert.Equal(beforeRow["Status"].S, afterRow["Status"].S);
        Assert.Equal(beforeRow.ContainsKey("StartedAt"), afterRow.ContainsKey("StartedAt"));
    }

    [Fact]
    public async Task TryStartAsync_WhenTwoStartsRaceTheSamePendingRun_ExactlyOneWins()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);

        // test -- two sequential claims stand in for two racing workers; the second one arriving after
        // the first already flipped Status is enough to prove the conditional write, without needing
        // real concurrent threads.
        var first = await _repository.TryStartAsync(organizationId, applicationId, runId, FixedNow);
        var second = await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow.AddSeconds(1)
        );

        // verify
        Assert.True(first);
        Assert.False(second);
    }

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Abandoned)]
    public async Task TryEndAsync_WhenRunIsRunning_SucceedsAndRemovesInFlightShard(
        RunStatus terminalStatus
    )
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryStartAsync(organizationId, applicationId, runId, FixedNow);
        var completedAt = FixedNow.AddMinutes(5);
        RunStatusReason? statusReason =
            terminalStatus == RunStatus.Abandoned ? RunStatusReason.HeartbeatLost : null;

        // test
        var result = await _repository.TryEndAsync(
            organizationId,
            applicationId,
            runId,
            terminalStatus,
            statusReason,
            completedAt
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.True(result);
        Assert.Equal(terminalStatus.ToString(), row["Status"].S);
        Assert.Equal(completedAt.ToString("O"), row["CompletedAt"].S);
        Assert.False(row.ContainsKey("InFlightShard"));
        if (statusReason is { } reason)
        {
            Assert.Equal(reason.ToString(), row["StatusReason"].S);
        }
        else
        {
            Assert.False(row.ContainsKey("StatusReason"));
        }
    }

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Abandoned)]
    public async Task TryEndAsync_WhenRunIsNotRunning_FailsAndChangesNothing(RunStatus notRunning)
    {
        // setup -- reach notRunning by claiming the Run and, unless it must stay Pending, ending it once
        // with that status, so the real test call attempts to end a Run that is not Running.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        if (notRunning != RunStatus.Pending)
        {
            await _repository.TryStartAsync(organizationId, applicationId, runId, FixedNow);
            await _repository.TryEndAsync(
                organizationId,
                applicationId,
                runId,
                notRunning,
                notRunning == RunStatus.Abandoned ? RunStatusReason.WorkerCrashed : null,
                FixedNow.AddMinutes(5)
            );
        }
        var beforeRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // test
        var result = await _repository.TryEndAsync(
            organizationId,
            applicationId,
            runId,
            RunStatus.Completed,
            null,
            FixedNow.AddMinutes(10)
        );
        var afterRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify -- the call changed nothing; the Run keeps whatever Status and CompletedAt it had
        // before this call.
        Assert.False(result);
        Assert.Equal(beforeRow["Status"].S, afterRow["Status"].S);
        Assert.Equal(beforeRow.ContainsKey("CompletedAt"), afterRow.ContainsKey("CompletedAt"));
        if (beforeRow.TryGetValue("CompletedAt", out var completedAt))
        {
            Assert.Equal(completedAt.S, afterRow["CompletedAt"].S);
        }
    }

    [Fact]
    public async Task TryEndAsync_WhenSweeperCallsWithFreshHeartbeat_Fails()
    {
        // setup -- a Running Run whose heartbeat was just set by Start Run
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        var startedAt = FixedNow;
        await _repository.TryStartAsync(organizationId, applicationId, runId, startedAt);

        // test -- the sweeper's cutoff is before the Run's actual heartbeat, so the heartbeat is fresh
        // relative to it and the sweeper must not end the Run
        var staleBeforeCutoff = startedAt.AddSeconds(-90);
        var result = await _repository.TryEndAsync(
            organizationId,
            applicationId,
            runId,
            RunStatus.Abandoned,
            RunStatusReason.HeartbeatLost,
            FixedNow.AddMinutes(5),
            heartbeatCutoff: staleBeforeCutoff
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify -- the Run is still Running; the sweeper's condition rejected the fresh heartbeat
        Assert.False(result);
        Assert.Equal(RunStatus.Running.ToString(), row["Status"].S);
    }

    [Fact]
    public async Task TryEndAsync_WhenSweeperCallsWithStaleHeartbeatCutoff_Succeeds()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        var startedAt = FixedNow;
        await _repository.TryStartAsync(organizationId, applicationId, runId, startedAt);

        // test -- the sweeper's cutoff is after the Run's last heartbeat, so the heartbeat reads as
        // stale and the sweeper may abandon the Run
        var cutoffAfterHeartbeat = startedAt.AddSeconds(90);
        var result = await _repository.TryEndAsync(
            organizationId,
            applicationId,
            runId,
            RunStatus.Abandoned,
            RunStatusReason.HeartbeatLost,
            FixedNow.AddMinutes(5),
            heartbeatCutoff: cutoffAfterHeartbeat
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.True(result);
        Assert.Equal(RunStatus.Abandoned.ToString(), row["Status"].S);
        Assert.Equal(RunStatusReason.HeartbeatLost.ToString(), row["StatusReason"].S);
    }

    [Fact]
    public async Task TryUpdateStatsAsync_WhenRunIsRunning_WritesAbsoluteCounts()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryStartAsync(organizationId, applicationId, runId, FixedNow);

        // test -- a second call with different, still-absolute numbers proves a retry (or a later
        // progress update) simply overwrites rather than accumulating.
        await _repository.TryUpdateStatsAsync(
            organizationId,
            applicationId,
            runId,
            totalActivityCount: 10,
            passedActivityCount: 3,
            failedActivityCount: 1,
            skippedActivityCount: 0
        );
        var result = await _repository.TryUpdateStatsAsync(
            organizationId,
            applicationId,
            runId,
            totalActivityCount: 10,
            passedActivityCount: 7,
            failedActivityCount: 2,
            skippedActivityCount: 1
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.True(result);
        Assert.Equal("10", row["TotalActivityCount"].N);
        Assert.Equal("7", row["PassedActivityCount"].N);
        Assert.Equal("2", row["FailedActivityCount"].N);
        Assert.Equal("1", row["SkippedActivityCount"].N);
    }

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Abandoned)]
    public async Task TryUpdateStatsAsync_WhenRunIsNotRunning_FailsAndChangesNothing(RunStatus notRunning)
    {
        // setup -- reach notRunning by claiming the Run and, unless it must stay Pending, ending it once
        // with that status, so the real test call attempts to update stats on a Run that is not Running.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        if (notRunning != RunStatus.Pending)
        {
            await _repository.TryStartAsync(organizationId, applicationId, runId, FixedNow);
            await _repository.TryEndAsync(
                organizationId,
                applicationId,
                runId,
                notRunning,
                notRunning == RunStatus.Abandoned ? RunStatusReason.WorkerCrashed : null,
                FixedNow.AddMinutes(5)
            );
        }

        // test
        var result = await _repository.TryUpdateStatsAsync(
            organizationId,
            applicationId,
            runId,
            totalActivityCount: 5,
            passedActivityCount: 5,
            failedActivityCount: 0,
            skippedActivityCount: 0
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify -- the call changed nothing; stats remain at zero.
        Assert.False(result);
        Assert.Equal("0", row["TotalActivityCount"].N);
    }
}
