using System.Globalization;
using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbRunRepository"/> against DynamoDB Local, covering
/// Create Run, Get Run, List Runs, Start Run, End Run, Update Run Stats, Update Run Heart Beat, the
/// stale in-flight Run query, and Append/List Run Status Update: the transactional create, its
/// Application/Environment existence checks, the TTL retention numbers, the LastEvaluatedKey pagination
/// loop, Get Run's exclusion of status update rows, List Runs' sparse RunHeaderIndex query, the state
/// machine's conditioned writes, the sparse InFlightIndex query's stale-heartbeat-or-passed-deadline
/// logic across its ten shards, the append transaction's two conditions, and the status update log's
/// cursor query across the zero-padding boundary at ten.</summary>
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
                    new AttributeDefinition("InFlightShard", ScalarAttributeType.S),
                    new AttributeDefinition("LastHeartbeatAt", ScalarAttributeType.S),
                ],
                // Mirrors ../../autoassure-infra/dynamodb.tf's RunHeaderIndex and InFlightIndex exactly
                // -- same sparse key shapes and the same projections -- so a test proves what the real
                // indexes actually return rather than what a looser local approximation would.
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
                    new GlobalSecondaryIndex
                    {
                        IndexName = "InFlightIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("InFlightShard", KeyType.HASH),
                            new KeySchemaElement("LastHeartbeatAt", KeyType.RANGE),
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.KEYS_ONLY },
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

    private static RunEnvironmentVariableSnapshot CreateVariableSnapshot(
        string key,
        string value,
        bool isSensitive
    ) =>
        new()
        {
            Key = key,
            Value = value,
            IsSensitive = isSensitive,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = FixedNow,
            UpdatedAt = FixedNow,
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

    private static RunStatusUpdate CreateStatusUpdate(long seq, DateTimeOffset? createdAt = null) =>
        new()
        {
            Seq = seq,
            Kind = RunStatusUpdateKind.AppendActivityResult,
            CreatedAt = createdAt ?? FixedNow,
            ActivityResult = new ActivityResult
            {
                ScenarioId = Guid.CreateVersion7(),
                ActivityId = Guid.CreateVersion7(),
                Status = ActivityResultStatus.Passed,
            },
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
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.NotNull(result);
        Assert.Equal(startedAt, result.StartedAt);
        Assert.Equal(startedAt, result.LastHeartbeatAt);
        Assert.Equal(startedAt + RunExecutionPolicy.MaxRunDuration, result.DeadlineAt);
        Assert.Equal(RunStatus.Running.ToString(), row["Status"].S);
        Assert.Equal(startedAt.ToString("O"), row["StartedAt"].S);
        Assert.Equal(startedAt.ToString("O"), row["LastHeartbeatAt"].S);
        Assert.Equal(
            (startedAt + RunExecutionPolicy.MaxRunDuration).ToString("O"),
            row["DeadlineAt"].S
        );
        Assert.Equal(DynamoDbMapper.RunInFlightShard(runId), row["InFlightShard"].S);
    }

    [Fact]
    public async Task TryStartAsync_WhenSucceeds_OverwritesStoredEnvironmentWithTheMaskedSnapshot()
    {
        // setup -- a Pending Run whose Environment snapshot carries the real (unmasked) value of a
        // sensitive variable, the way TryCreateAsync stores it since Create no longer masks at write
        // time.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var sensitiveVariable = CreateVariableSnapshot(
            "API_KEY",
            "abcdefghijklmnopqrstuvwxyz",
            isSensitive: true
        );
        var plainVariable = CreateVariableSnapshot(
            "BASE_URL",
            "https://staging.example.com",
            isSensitive: false
        );
        var environment = CreateEnvironmentSnapshot(environmentId) with
        {
            Variables = [sensitiveVariable, plainVariable],
        };
        var run = CreateRun(organizationId, applicationId, environmentId) with
        {
            Environment = environment,
        };
        await _repository.TryCreateAsync(run, [CreateScenarioSnapshot()]);
        var startedAt = FixedNow;

        // test
        var result = await _repository.TryStartAsync(
            organizationId,
            applicationId,
            run.Id,
            startedAt,
            environment.Masked()
        );
        var detail = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify -- storage now holds the masked sensitive value and the untouched plain value, proving
        // the Environment attribute was actually overwritten, not just returned masked.
        Assert.NotNull(result);
        var storedSensitive = Assert.Single(
            detail!.Header.Environment.Variables,
            v => v.Key == "API_KEY"
        );
        Assert.Equal(SensitiveValueMasker.Mask(sensitiveVariable.Value), storedSensitive.Value);
        Assert.NotEqual(sensitiveVariable.Value, storedSensitive.Value);
        var storedPlain = Assert.Single(
            detail.Header.Environment.Variables,
            v => v.Key == "BASE_URL"
        );
        Assert.Equal(plainVariable.Value, storedPlain.Value);

        // verify -- a second racing claim still fails and changes nothing, same as without variables.
        var secondClaim = await _repository.TryStartAsync(
            organizationId,
            applicationId,
            run.Id,
            startedAt.AddSeconds(1),
            environment.Masked()
        );
        Assert.Null(secondClaim);
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
        await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow,
            CreateEnvironmentSnapshot(environmentId)
        );
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
            FixedNow.AddMinutes(1),
            CreateEnvironmentSnapshot(environmentId)
        );
        var afterRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.Null(result);
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
        var first = await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow,
            CreateEnvironmentSnapshot(environmentId)
        );
        var second = await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow.AddSeconds(1),
            CreateEnvironmentSnapshot(environmentId)
        );

        // verify
        Assert.NotNull(first);
        Assert.Null(second);
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
        await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow,
            CreateEnvironmentSnapshot(environmentId)
        );
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
            await _repository.TryStartAsync(
                organizationId,
                applicationId,
                runId,
                FixedNow,
                CreateEnvironmentSnapshot(environmentId)
            );
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
        await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );

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
        await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );

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
        await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow,
            CreateEnvironmentSnapshot(environmentId)
        );

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
    public async Task TryUpdateStatsAsync_WhenRunIsNotRunning_FailsAndChangesNothing(
        RunStatus notRunning
    )
    {
        // setup -- reach notRunning by claiming the Run and, unless it must stay Pending, ending it once
        // with that status, so the real test call attempts to update stats on a Run that is not Running.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        if (notRunning != RunStatus.Pending)
        {
            await _repository.TryStartAsync(
                organizationId,
                applicationId,
                runId,
                FixedNow,
                CreateEnvironmentSnapshot(environmentId)
            );
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

    // Claims a fresh pending Run and immediately starts it at startedAt, so its LastHeartbeatAt and
    // DeadlineAt derive from that single instant -- what every stale-query test below manipulates.
    private async Task<Guid> CreateRunningRunAsync(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId,
        DateTimeOffset startedAt
    )
    {
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryStartAsync(
            organizationId,
            applicationId,
            runId,
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );
        return runId;
    }

    // Extracts the 0-9 digit DynamoDbMapper.RunInFlightShard bakes into "Running#<digit>", so a test can
    // ask ListStaleInFlightRunsAsync for exactly the shard a given Run id actually landed on.
    private static int ShardOf(Guid runId) =>
        int.Parse(
            DynamoDbMapper.RunInFlightShard(runId)["Running#".Length..],
            CultureInfo.InvariantCulture
        );

    [Fact]
    public async Task TryHeartbeatAsync_WhenRunIsRunning_MovesLastHeartbeatAt()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            FixedNow
        );

        // test
        var heartbeatAt = FixedNow.AddSeconds(30);
        var result = await _repository.TryHeartbeatAsync(
            organizationId,
            applicationId,
            runId,
            heartbeatAt
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.True(result);
        Assert.Equal(heartbeatAt.ToString("O"), row["LastHeartbeatAt"].S);
    }

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Abandoned)]
    public async Task TryHeartbeatAsync_WhenRunIsNotRunning_FailsAndChangesNothing(
        RunStatus notRunning
    )
    {
        // setup -- reach notRunning by claiming the Run and, unless it must stay Pending, ending it once
        // with that status, so the real test call beats a Run that is not Running.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        if (notRunning != RunStatus.Pending)
        {
            await _repository.TryStartAsync(
                organizationId,
                applicationId,
                runId,
                FixedNow,
                CreateEnvironmentSnapshot(environmentId)
            );
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
        var result = await _repository.TryHeartbeatAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow.AddMinutes(10)
        );
        var afterRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify -- the call changed nothing; LastHeartbeatAt (present or absent) is unchanged.
        Assert.False(result);
        Assert.Equal(
            beforeRow.ContainsKey("LastHeartbeatAt"),
            afterRow.ContainsKey("LastHeartbeatAt")
        );
        if (beforeRow.TryGetValue("LastHeartbeatAt", out var lastHeartbeatAt))
        {
            Assert.Equal(lastHeartbeatAt.S, afterRow["LastHeartbeatAt"].S);
        }
    }

    [Fact]
    public async Task ListStaleInFlightRunsAsync_WhenHeartbeatIsPastCutoff_ReturnsIt()
    {
        // setup -- a Running Run whose heartbeat is older than the sweeper's cutoff, with a DeadlineAt
        // nowhere near passed, so only the heartbeat half of the OR can be what catches it.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            startedAt
        );
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-90);

        // test
        var staleRuns = await _repository.ListStaleInFlightRunsAsync(ShardOf(runId), cutoff);

        // verify
        Assert.Contains(
            staleRuns,
            staleRun =>
                staleRun.Id == runId
                && staleRun.OrganizationId == organizationId
                && staleRun.ApplicationId == applicationId
        );
    }

    [Fact]
    public async Task ListStaleInFlightRunsAsync_WhenHeartbeatIsFreshAndDeadlineNotPassed_DoesNotReturnIt()
    {
        // setup -- a Running Run that just started: fresh heartbeat, DeadlineAt hours away.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            DateTimeOffset.UtcNow
        );
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-90);

        // test
        var staleRuns = await _repository.ListStaleInFlightRunsAsync(ShardOf(runId), cutoff);

        // verify
        Assert.DoesNotContain(staleRuns, staleRun => staleRun.Id == runId);
    }

    [Fact]
    public async Task ListStaleInFlightRunsAsync_WhenHeartbeatIsFreshButDeadlineHasPassed_ReturnsIt()
    {
        // setup -- a Run claimed long enough ago that its fixed DeadlineAt has already passed, then
        // heartbeats just now -- the "stuck in a retry loop, still beating" case the OR exists for. A
        // KeyConditionExpression on LastHeartbeatAt alone would miss this Run entirely.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var startedAt =
            DateTimeOffset.UtcNow - RunExecutionPolicy.MaxRunDuration - TimeSpan.FromHours(1);
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            startedAt
        );
        await _repository.TryHeartbeatAsync(
            organizationId,
            applicationId,
            runId,
            DateTimeOffset.UtcNow
        );
        // A cutoff this recent would not flag the (now fresh) heartbeat as stale on its own.
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-90);

        // test
        var staleRuns = await _repository.ListStaleInFlightRunsAsync(ShardOf(runId), cutoff);

        // verify -- caught only via the DeadlineAt half of the OR.
        Assert.Contains(
            staleRuns,
            staleRun =>
                staleRun.Id == runId
                && staleRun.OrganizationId == organizationId
                && staleRun.ApplicationId == applicationId
        );
    }

    [Fact]
    public async Task ListStaleInFlightRunsAsync_WhenRunHasEnded_NeverReturnsIt()
    {
        // setup -- a Run that, were it still in flight, would be caught by both halves of the OR (stale
        // heartbeat and passed deadline), but has since ended.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var startedAt = DateTimeOffset.UtcNow.AddHours(-5);
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            startedAt
        );
        await _repository.TryEndAsync(
            organizationId,
            applicationId,
            runId,
            RunStatus.Completed,
            null,
            DateTimeOffset.UtcNow
        );
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-90);

        // test
        var staleRuns = await _repository.ListStaleInFlightRunsAsync(ShardOf(runId), cutoff);

        // verify -- ending the Run removed InFlightShard (task 7), which is what drops it out of this
        // query no matter how stale its (now-frozen) heartbeat and deadline still look.
        Assert.DoesNotContain(staleRuns, staleRun => staleRun.Id == runId);
    }

    [Fact]
    public async Task ListStaleInFlightRunsAsync_WhenRunsSpanMultipleShards_EachShardReturnsOnlyItsOwn()
    {
        // setup -- enough Running Runs with stale heartbeats that RunInFlightShard's even spread across
        // ten shards makes it overwhelmingly likely several distinct shards end up populated, proving
        // this Query reads one shard rather than the whole table.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var runIds = new List<Guid>();
        for (var i = 0; i < 40; i++)
        {
            var run = CreateRun(organizationId, applicationId, environmentId);
            await _repository.TryCreateAsync(run, [CreateScenarioSnapshot()]);
            await _repository.TryStartAsync(
                organizationId,
                applicationId,
                run.Id,
                startedAt,
                CreateEnvironmentSnapshot(environmentId)
            );
            runIds.Add(run.Id);
        }
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-90);

        // test -- query every shard independently, the way a sweeper walking 0..9 would.
        var foundByShard = new Dictionary<int, IReadOnlyList<StaleInFlightRun>>();
        for (var shard = 0; shard < RunExecutionPolicy.InFlightShardCount; shard++)
        {
            foundByShard[shard] = await _repository.ListStaleInFlightRunsAsync(shard, cutoff);
        }

        // verify -- every created Run turns up under its own shard and nowhere else, and more than one
        // shard actually held a result.
        foreach (var runId in runIds)
        {
            var ownShard = ShardOf(runId);
            Assert.Contains(foundByShard[ownShard], staleRun => staleRun.Id == runId);
            foreach (
                var otherShard in Enumerable
                    .Range(0, RunExecutionPolicy.InFlightShardCount)
                    .Where(candidate => candidate != ownShard)
            )
            {
                Assert.DoesNotContain(foundByShard[otherShard], staleRun => staleRun.Id == runId);
            }
        }
        var populatedShardCount = foundByShard.Count(entry => entry.Value.Count > 0);
        Assert.True(
            populatedShardCount > 1,
            $"expected the 40 created Run ids to spread across more than one shard, got {populatedShardCount}"
        );
    }

    [Fact]
    public async Task ListStaleInFlightRunsAsync_WhenShardIsOutOfRange_Throws()
    {
        // test & verify
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _repository.ListStaleInFlightRunsAsync(
                RunExecutionPolicy.InFlightShardCount,
                DateTimeOffset.UtcNow
            )
        );
    }

    [Fact]
    public async Task TryAppendStatusUpdateAsync_WhenRunIsRunning_AddsOneRowAndMovesLastSeq()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            FixedNow
        );
        var update = CreateStatusUpdate(1);

        // test
        var result = await _repository.TryAppendStatusUpdateAsync(
            organizationId,
            applicationId,
            runId,
            update,
            expiresAt: 12345
        );
        var headerRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);
        var rawRows = await QueryRawRowsAsync(organizationId, applicationId);

        // verify
        Assert.True(result);
        Assert.Equal("1", headerRow["LastSeq"].N);
        var updateRow = Assert.Single(
            rawRows,
            row => row["RowKey"].S == DynamoDbMapper.RunStatusUpdateRowKey(runId, 1)
        );
        Assert.Equal("12345", updateRow["ExpiresAt"].N);
    }

    [Fact]
    public async Task TryAppendStatusUpdateAsync_WhenSameSeqAppendedTwice_LeavesOneRowAndOneLastSeq()
    {
        // setup -- a retried append (at-least-once dispatch) targets the same Seq
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            FixedNow
        );
        var update = CreateStatusUpdate(1);

        // test
        var first = await _repository.TryAppendStatusUpdateAsync(
            organizationId,
            applicationId,
            runId,
            update,
            expiresAt: null
        );
        var retry = await _repository.TryAppendStatusUpdateAsync(
            organizationId,
            applicationId,
            runId,
            update,
            expiresAt: null
        );
        var headerRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);
        var rawRows = await QueryRawRowsAsync(organizationId, applicationId);

        // verify
        Assert.True(first);
        Assert.False(retry);
        Assert.Equal("1", headerRow["LastSeq"].N);
        var updateRowKey = DynamoDbMapper.RunStatusUpdateRowKey(runId, 1);
        Assert.Single(rawRows, row => row["RowKey"].S == updateRowKey);
    }

    [Fact]
    public async Task TryAppendStatusUpdateAsync_WhenSeqIsNotGreaterThanLastSeq_FailsAndChangesNothing()
    {
        // setup -- LastSeq is already 5
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            FixedNow
        );
        await _repository.TryAppendStatusUpdateAsync(
            organizationId,
            applicationId,
            runId,
            CreateStatusUpdate(5),
            expiresAt: null
        );

        // test -- a lower Seq than the current LastSeq
        var result = await _repository.TryAppendStatusUpdateAsync(
            organizationId,
            applicationId,
            runId,
            CreateStatusUpdate(3),
            expiresAt: null
        );
        var headerRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);
        var rawRows = await QueryRawRowsAsync(organizationId, applicationId);

        // verify -- the header keeps LastSeq 5, and no row was written for the rejected Seq 3, proving
        // the transaction rolled back the Put alongside the failed header condition.
        Assert.False(result);
        Assert.Equal("5", headerRow["LastSeq"].N);
        Assert.DoesNotContain(
            rawRows,
            row => row["RowKey"].S == DynamoDbMapper.RunStatusUpdateRowKey(runId, 3)
        );
    }

    [Fact]
    public async Task TryAppendStatusUpdateAsync_WhenRunHasEnded_FailsAndChangesNothing()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            FixedNow
        );
        await _repository.TryEndAsync(
            organizationId,
            applicationId,
            runId,
            RunStatus.Completed,
            null,
            FixedNow.AddMinutes(5)
        );

        // test
        var result = await _repository.TryAppendStatusUpdateAsync(
            organizationId,
            applicationId,
            runId,
            CreateStatusUpdate(1),
            expiresAt: null
        );
        var headerRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);
        var rawRows = await QueryRawRowsAsync(organizationId, applicationId);

        // verify -- the Run ended without ever taking Seq 1; nothing was written for it.
        Assert.False(result);
        Assert.Equal("0", headerRow["LastSeq"].N);
        Assert.DoesNotContain(
            rawRows,
            row => row["RowKey"].S == DynamoDbMapper.RunStatusUpdateRowKey(runId, 1)
        );
    }

    [Fact]
    public async Task ListStatusUpdatesAsync_WhenReadingAfterCursor_ReturnsOnlyLaterUpdatesInOrder()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            FixedNow
        );
        for (var seq = 1; seq <= 3; seq++)
        {
            await _repository.TryAppendStatusUpdateAsync(
                organizationId,
                applicationId,
                runId,
                CreateStatusUpdate(seq),
                expiresAt: null
            );
        }

        // test
        var updates = await _repository.ListStatusUpdatesAsync(
            organizationId,
            applicationId,
            runId,
            afterSeq: 1,
            limit: 100
        );

        // verify
        Assert.Equal([2L, 3L], updates.Select(update => update.Seq));
    }

    [Fact]
    public async Task ListStatusUpdatesAsync_WhenSequencesCrossTen_ReturnsExactlyTenElevenTwelve()
    {
        // setup -- append Seq 1 through 12, crossing the point where zero-padding starts to matter:
        // an unpadded key would sort "#update#10" before "#update#9", which would corrupt this exact
        // query the moment the log passes nine entries.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreateRunningRunAsync(
            organizationId,
            applicationId,
            environmentId,
            FixedNow
        );
        for (var seq = 1; seq <= 12; seq++)
        {
            await _repository.TryAppendStatusUpdateAsync(
                organizationId,
                applicationId,
                runId,
                CreateStatusUpdate(seq),
                expiresAt: null
            );
        }

        // test -- read from cursor 9
        var updates = await _repository.ListStatusUpdatesAsync(
            organizationId,
            applicationId,
            runId,
            afterSeq: 9,
            limit: 100
        );

        // verify -- exactly 10, 11 and 12, in that order
        Assert.Equal([10L, 11L, 12L], updates.Select(update => update.Seq));
    }

    [Fact]
    public async Task ListStatusUpdatesAsync_WhenAfterSeqIsNegative_Throws()
    {
        // test & verify
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _repository.ListStatusUpdatesAsync(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                afterSeq: -1,
                limit: 10
            )
        );
    }

    [Fact]
    public async Task ListStatusUpdatesAsync_WhenLimitIsNotPositive_Throws()
    {
        // test & verify
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _repository.ListStatusUpdatesAsync(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                afterSeq: 0,
                limit: 0
            )
        );
    }
}
