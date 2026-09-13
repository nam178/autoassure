using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbRunRepository"/> against DynamoDB Local, covering
/// Create Run, Get Run, List Runs, List Running Runs, Start Run, End Run, Update Run Stats, Update Run
/// Heart Beat, and Append/List Run Status Update: the transactional create, its Application existence
/// check, the LastEvaluatedKey pagination loop, Get Run's exclusion of status update rows,
/// List Runs' sparse RunHeaderIndex query, List Running Runs' strongly consistent read against the
/// separate RunningRuns table, the state machine's conditioned writes, the append transaction's two
/// conditions, and the status update log's cursor query across the zero-padding boundary at ten.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbRunRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string RunTableName = "Runs";
    private const string RunningRunTableName = "RunningRuns";
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
                    RunningRunTableName = RunningRunTableName,
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
                // Mirrors ../../autoassure-infra/dynamodb.tf's RunHeaderIndex exactly -- same sparse key
                // shape and the same projection -- so a test proves what the real index actually returns
                // rather than what a looser local approximation would.
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
                                "LastHeartbeatAt",
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
                TableName = RunningRunTableName,
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                    new KeySchemaElement("RunId", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ApplicationId", ScalarAttributeType.S),
                    new AttributeDefinition("RunId", ScalarAttributeType.S),
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
        await _client.DeleteTableAsync(RunningRunTableName);
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

    private static RunSnapshotSource CreateSnapshotSource(Guid id) =>
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

    // Defaults to one Scenario, since most tests here only need a Run to have some. A test that cares
    // about Scenario content passes its own.
    private static Run CreateRun(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId,
        RunTrigger trigger = RunTrigger.Manual,
        DateTimeOffset? createdAt = null,
        IReadOnlyList<RunScenarioSnapshot>? scenarios = null
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Trigger = trigger,
            Status = RunStatus.Pending,
            Environment = CreateEnvironmentSnapshot(environmentId),
            Scenarios = scenarios ?? [CreateScenarioSnapshot()],
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
    // attributes that the repository's own read side does not surface on every row.
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
        var result = await _repository.TryCreateAsync(run with { Scenarios = scenarios });
        var stored = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify
        Assert.Equal(RunCreateResult.Success, result);
        Assert.NotNull(stored);
        Assert.Equal(run.Id, stored.Id);
        Assert.Equal(2, stored.Scenarios.Count);
        Assert.Contains(stored.Scenarios, scenario => scenario.Description == "First");
        Assert.Contains(stored.Scenarios, scenario => scenario.Description == "Second");
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
        await _repository.TryCreateAsync(run with { Scenarios = originalScenarios });

        // test -- a second create for the same Run id, with different Scenario content
        var duplicateScenarios = new[]
        {
            CreateScenarioSnapshot("Retry-1"),
            CreateScenarioSnapshot("Retry-2"),
        };
        var result = await _repository.TryCreateAsync(run with { Scenarios = duplicateScenarios });
        var stored = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify
        Assert.Equal(RunCreateResult.AlreadyExists, result);
        Assert.NotNull(stored);
        var found = Assert.Single(stored.Scenarios);
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
        var result = await _repository.TryCreateAsync(run with { Scenarios = scenarios });
        var stored = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify
        Assert.Equal(RunCreateResult.ApplicationNotFound, result);
        Assert.Null(stored);
    }

    [Fact]
    public async Task TryCreateAsync_WhenSuccessful_WritesTheEnvironmentSnapshotOnItsOwnRow()
    {
        // setup -- Environment is not a header attribute: it gets a row of its own, keyed so Get Run's
        // BETWEEN query still picks it up.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId);

        // test
        await _repository.TryCreateAsync(run);
        var rawRows = await QueryRawRowsAsync(organizationId, applicationId);

        // verify
        var headerRow = Assert.Single(
            rawRows,
            row => row["RowKey"].S == DynamoDbMapper.RunHeaderRowKey(run.Id)
        );
        Assert.False(headerRow.ContainsKey("Environment"));

        var environmentRow = Assert.Single(
            rawRows,
            row => row["RowKey"].S == DynamoDbMapper.RunEnvironmentRowKey(run.Id)
        );
        Assert.Equal(environmentId.ToString(), environmentRow["Source"].M["Id"].S);
    }

    [Fact]
    public async Task GetByIdAsync_WhenTheEnvironmentRowIsGone_ThrowsCorruptedDynamoDbRowException()
    {
        // setup -- the Environment row is written in the same transaction as the header and nothing
        // ever deletes it on its own, so a header outliving it can only mean corrupted stored data.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(run);

        // test -- delete the Environment row behind the repository's back, leaving the rest intact.
        await _client.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = RunTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    ["RowKey"] = new(DynamoDbMapper.RunEnvironmentRowKey(run.Id)),
                },
            }
        );

        // verify
        await Assert.ThrowsAsync<CorruptedDynamoDbRowException>(() =>
            _repository.GetByIdAsync(organizationId, applicationId, run.Id)
        );
    }

    [Fact]
    public async Task GetByIdAsync_WhenEveryScenarioRowIsGone_ReturnsRunWithNoScenarios()
    {
        // setup -- a Run having at least one Scenario is a business rule enforced (or not) when the
        // Run is created, not a storage-format guarantee, so the repository doesn't re-validate it here.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(run);

        // test -- delete every Scenario row behind the repository's back, leaving the rest intact.
        foreach (var scenario in run.Scenarios)
        {
            await _client.DeleteItemAsync(
                new DeleteItemRequest
                {
                    TableName = RunTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId_ApplicationId"] = new(
                            DynamoDbMapper.ApplicationScopedPartitionKey(
                                organizationId,
                                applicationId
                            )
                        ),
                        ["RowKey"] = new(
                            DynamoDbMapper.RunScenarioRowKey(run.Id, scenario.Source.Id)
                        ),
                    },
                }
            );
        }

        // verify
        var result = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);
        Assert.NotNull(result);
        Assert.Empty(result.Scenarios);
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
        var createResult = await _repository.TryCreateAsync(run with { Scenarios = scenarios });
        var stored = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

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
        Assert.NotNull(stored);
        Assert.Equal(50, stored.Scenarios.Count);
        var expectedIds = scenarios.Select(scenario => scenario.Source.Id).ToHashSet();
        var actualIds = stored.Scenarios.Select(scenario => scenario.Source.Id).ToHashSet();
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
        await _repository.TryCreateAsync(run);

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
        var stored = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify -- only the real Scenario row comes back; the hand-written update row did not blow
        // up the read (it would fail to parse as a Scenario snapshot if it were included) and did not
        // inflate the count.
        Assert.NotNull(stored);
        Assert.Single(stored.Scenarios);
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
        await _repository.TryCreateAsync(run with { Scenarios = scenarios });

        // test
        var summaries = await _repository.ListByApplicationAsync(
            organizationId,
            applicationId,
            [RunTrigger.Manual, RunTrigger.Scheduled]
        );

        // verify
        var summary = Assert.Single(summaries);
        Assert.Equal(run.Id, summary.Id);
        Assert.Equal(run.Trigger, summary.Trigger);
        Assert.Equal(run.Status, summary.Status);
        Assert.Equal(run.CreatedAt, summary.CreatedAt);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenRunIsRunning_IncludesLastHeartbeatAt()
    {
        // setup -- RunHeaderIndex's projection was widened to carry LastHeartbeatAt so a caller can
        // tell a Running Run apart from one whose worker died without anything having ended it yet;
        // this proves that attribute actually comes back through the index, not just through GetById.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        var startedAt = FixedNow;
        await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );

        // test
        var summaries = await _repository.ListByApplicationAsync(
            organizationId,
            applicationId,
            [RunTrigger.Manual, RunTrigger.Scheduled]
        );

        // verify
        var summary = Assert.Single(summaries);
        Assert.Equal(startedAt, summary.LastHeartbeatAt);
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
        await _repository.TryCreateAsync(
            otherApplicationRun with
            {
                Scenarios = [CreateScenarioSnapshot()],
            }
        );

        // test
        var summaries = await _repository.ListByApplicationAsync(
            organizationId,
            applicationId,
            [RunTrigger.Manual, RunTrigger.Scheduled]
        );

        // verify
        Assert.Empty(summaries);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenTriggersExcludesAuthoring_DoesNotReturnAuthoringRuns()
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
        await _repository.TryCreateAsync(manualRun);
        await _repository.TryCreateAsync(
            authoringRun with
            {
                Scenarios = [CreateScenarioSnapshot()],
            }
        );

        // test
        var summaries = await _repository.ListByApplicationAsync(
            organizationId,
            applicationId,
            [RunTrigger.Manual, RunTrigger.Scheduled]
        );

        // verify
        var summary = Assert.Single(summaries);
        Assert.Equal(manualRun.Id, summary.Id);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenTriggersIncludesAuthoring_ReturnsAuthoringRuns()
    {
        // setup -- proves the trigger filter is a plain allow-list rather than a hardcoded Authoring
        // exclusion: a caller that asks for Authoring gets it back.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var authoringRun = CreateRun(
            organizationId,
            applicationId,
            environmentId,
            RunTrigger.Authoring
        );
        await _repository.TryCreateAsync(
            authoringRun with
            {
                Scenarios = [CreateScenarioSnapshot()],
            }
        );

        // test
        var summaries = await _repository.ListByApplicationAsync(
            organizationId,
            applicationId,
            [RunTrigger.Authoring]
        );

        // verify
        var summary = Assert.Single(summaries);
        Assert.Equal(authoringRun.Id, summary.Id);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenTriggersIsEmpty_Throws()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();

        // test & verify
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repository.ListByApplicationAsync(organizationId, applicationId, [])
        );
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenMultipleRuns_ReturnsNewestFirst()
    {
        // setup -- three Runs created in sequence. Guid.CreateVersion7() ids are time-sortable, so
        // descending id order is newest first.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var firstRun = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(firstRun);
        var secondRun = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(secondRun);
        var thirdRun = CreateRun(organizationId, applicationId, environmentId);
        await _repository.TryCreateAsync(thirdRun);

        // test
        var summaries = await _repository.ListByApplicationAsync(
            organizationId,
            applicationId,
            [RunTrigger.Manual, RunTrigger.Scheduled]
        );

        // verify
        Assert.Equal(
            [thirdRun.Id, secondRun.Id, firstRun.Id],
            summaries.Select(summary => summary.Id)
        );
    }

    [Fact]
    public async Task ListRunningByApplicationAsync_WhenRunIsRunning_ReturnsIt()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var startedAt = FixedNow;
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );

        // test
        var running = await _repository.ListRunningByApplicationAsync(
            organizationId,
            applicationId
        );

        // verify
        var runningRun = Assert.Single(running);
        Assert.Equal(runId, runningRun.Id);
        Assert.Equal(startedAt, runningRun.StartedAt);
    }

    [Fact]
    public async Task ListRunningByApplicationAsync_WhenRunIsPending_DoesNotReturnIt()
    {
        // setup -- a Run that has never started, so it never had a RunningRuns row to begin with.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await CreatePendingRunAsync(organizationId, applicationId, environmentId);

        // test
        var running = await _repository.ListRunningByApplicationAsync(
            organizationId,
            applicationId
        );

        // verify
        Assert.Empty(running);
    }

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Cancelled)]
    [InlineData(RunStatus.Abandoned)]
    public async Task ListRunningByApplicationAsync_WhenRunHasEnded_DoesNotReturnIt(
        RunStatus terminalStatus
    )
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow,
            CreateEnvironmentSnapshot(environmentId)
        );
        await _repository.TryMarkAsEndedAsync(
            organizationId,
            applicationId,
            runId,
            terminalStatus,
            terminalStatus == RunStatus.Abandoned ? RunStatusReason.WorkerCrashed : null,
            FixedNow.AddMinutes(5)
        );

        // test
        var running = await _repository.ListRunningByApplicationAsync(
            organizationId,
            applicationId
        );

        // verify
        Assert.Empty(running);
    }

    [Fact]
    public async Task ListRunningByApplicationAsync_WhenRunBelongsToAnotherApplication_DoesNotReturnIt()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var otherApplicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, otherApplicationId);
        await PutEnvironmentAsync(organizationId, otherApplicationId, environmentId);
        var runId = await CreatePendingRunAsync(organizationId, otherApplicationId, environmentId);
        await _repository.TryMarkAsStartedAsync(
            organizationId,
            otherApplicationId,
            runId,
            FixedNow,
            CreateEnvironmentSnapshot(environmentId)
        );

        // test
        var running = await _repository.ListRunningByApplicationAsync(
            organizationId,
            applicationId
        );

        // verify
        Assert.Empty(running);
    }

    // Reads a Run header's raw row straight from the table, bypassing the repository, so a test can
    // check storage-only attributes that no repository method surfaces on a Run.
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

    // Reads a Run's raw row straight from the RunningRuns table, bypassing the repository, so a test
    // can check for its presence/absence directly -- returns an empty dictionary when no such row
    // exists.
    private async Task<Dictionary<string, AttributeValue>> GetRawRunningRunRowAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId
    )
    {
        var response = await _client.GetItemAsync(
            new GetItemRequest
            {
                TableName = RunningRunTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    ["RunId"] = new(runId.ToString()),
                },
                ConsistentRead = true,
            }
        );
        return response.Item ?? [];
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
        await _repository.TryCreateAsync(run);
        return run.Id;
    }

    [Fact]
    public async Task TryMarkAsStartedAsync_WhenRunIsPending_SucceedsAndWritesRunningRunRow()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        var startedAt = FixedNow;

        // test
        var result = await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);
        var runningRunRow = await GetRawRunningRunRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.NotNull(result);
        Assert.Equal(startedAt, result.StartedAt);
        Assert.Equal(startedAt, result.LastHeartbeatAt);
        Assert.Equal(RunStatus.Running.ToString(), row["Status"].S);
        Assert.Equal(startedAt.ToString("O"), row["StartedAt"].S);
        Assert.Equal(startedAt.ToString("O"), row["LastHeartbeatAt"].S);
        Assert.Equal(startedAt.ToString("O"), runningRunRow["StartedAt"].S);
    }

    [Fact]
    public async Task TryMarkAsStartedAsync_WhenSucceeds_OverwritesStoredEnvironmentWithTheMaskedSnapshot()
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
        await _repository.TryCreateAsync(run);
        var startedAt = FixedNow;

        // test
        var result = await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            run.Id,
            startedAt,
            environment.Masked()
        );
        var stored = await _repository.GetByIdAsync(organizationId, applicationId, run.Id);

        // verify -- storage now holds the masked sensitive value and the untouched plain value, proving
        // the Environment attribute was actually overwritten, not just returned masked.
        Assert.NotNull(result);
        var storedSensitive = Assert.Single(stored!.Environment.Variables, v => v.Key == "API_KEY");
        Assert.Equal(SensitiveValueMasker.Mask(sensitiveVariable.Value), storedSensitive.Value);
        Assert.NotEqual(sensitiveVariable.Value, storedSensitive.Value);
        var storedPlain = Assert.Single(stored.Environment.Variables, v => v.Key == "BASE_URL");
        Assert.Equal(plainVariable.Value, storedPlain.Value);

        // verify -- a second racing claim still fails and changes nothing, same as without variables.
        var secondClaim = await _repository.TryMarkAsStartedAsync(
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
    public async Task TryMarkAsStartedAsync_WhenRunIsNotPending_FailsAndChangesNothing(
        RunStatus notPending
    )
    {
        // setup -- claim the Run once to reach Running, then optionally end it, so its Status is
        // notPending before the real test call.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow,
            CreateEnvironmentSnapshot(environmentId)
        );
        if (notPending != RunStatus.Running)
        {
            await _repository.TryMarkAsEndedAsync(
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
        var result = await _repository.TryMarkAsStartedAsync(
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
    public async Task TryMarkAsStartedAsync_WhenTwoStartsRaceTheSamePendingRun_ExactlyOneWins()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);

        // test -- two sequential claims stand in for two racing workers; the second one arriving after
        // the first already flipped Status is enough to prove the conditional write, without needing
        // real concurrent threads.
        var first = await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow,
            CreateEnvironmentSnapshot(environmentId)
        );
        var second = await _repository.TryMarkAsStartedAsync(
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
    public async Task TryMarkAsEndedAsync_WhenRunIsRunning_SucceedsAndDeletesRunningRunRow(
        RunStatus terminalStatus
    )
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryMarkAsStartedAsync(
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
        var result = await _repository.TryMarkAsEndedAsync(
            organizationId,
            applicationId,
            runId,
            terminalStatus,
            statusReason,
            completedAt
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);
        var runningRunRow = await GetRawRunningRunRowAsync(organizationId, applicationId, runId);

        // verify
        Assert.True(result);
        Assert.Equal(terminalStatus.ToString(), row["Status"].S);
        Assert.Equal(completedAt.ToString("O"), row["CompletedAt"].S);
        Assert.Empty(runningRunRow);
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
    public async Task TryMarkAsEndedAsync_WhenRunIsNotRunning_FailsAndChangesNothing(
        RunStatus notRunning
    )
    {
        // setup -- reach notRunning by claiming the Run and, unless it must stay Pending, ending it once
        // with that status, so the real test call attempts to end a Run that is not Running.
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        if (notRunning != RunStatus.Pending)
        {
            await _repository.TryMarkAsStartedAsync(
                organizationId,
                applicationId,
                runId,
                FixedNow,
                CreateEnvironmentSnapshot(environmentId)
            );
            await _repository.TryMarkAsEndedAsync(
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
        var result = await _repository.TryMarkAsEndedAsync(
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
    public async Task TryMarkAsEndedAsync_WhenHeartbeatCutoffIsBeforeLastHeartbeat_Fails()
    {
        // setup -- a Running Run whose heartbeat was just set by Start Run
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        var startedAt = FixedNow;
        await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );

        // test -- the cutoff is before the Run's actual heartbeat, so the heartbeat is fresh relative to
        // it and this caller must not end the Run
        var staleBeforeCutoff = startedAt.AddSeconds(-90);
        var result = await _repository.TryMarkAsEndedAsync(
            organizationId,
            applicationId,
            runId,
            RunStatus.Abandoned,
            RunStatusReason.HeartbeatLost,
            FixedNow.AddMinutes(5),
            heartbeatCutoff: staleBeforeCutoff
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify -- the Run is still Running; the condition rejected the fresh heartbeat
        Assert.False(result);
        Assert.Equal(RunStatus.Running.ToString(), row["Status"].S);
    }

    [Fact]
    public async Task TryMarkAsEndedAsync_WhenHeartbeatCutoffIsAfterLastHeartbeat_Succeeds()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        var startedAt = FixedNow;
        await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );

        // test -- the cutoff is after the Run's last heartbeat, so the heartbeat reads as stale and this
        // caller may abandon the Run
        var cutoffAfterHeartbeat = startedAt.AddSeconds(90);
        var result = await _repository.TryMarkAsEndedAsync(
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
    public async Task TryUpdateAsync_StatsWhenRunIsRunning_WritesAbsoluteCounts()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            FixedNow,
            CreateEnvironmentSnapshot(environmentId)
        );

        // test -- a second call with different, still-absolute numbers proves a retry (or a later
        // progress update) simply overwrites rather than accumulating.
        await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            runId,
            new RunUpdatableFields
            {
                TotalActivityCount = 10,
                PassedActivityCount = 3,
                FailedActivityCount = 1,
                SkippedActivityCount = 0,
            }
        );
        var result = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            runId,
            new RunUpdatableFields
            {
                TotalActivityCount = 10,
                PassedActivityCount = 7,
                FailedActivityCount = 2,
                SkippedActivityCount = 1,
            }
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
    public async Task TryUpdateAsync_StatsWhenRunIsNotRunning_FailsAndChangesNothing(
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
            await _repository.TryMarkAsStartedAsync(
                organizationId,
                applicationId,
                runId,
                FixedNow,
                CreateEnvironmentSnapshot(environmentId)
            );
            await _repository.TryMarkAsEndedAsync(
                organizationId,
                applicationId,
                runId,
                notRunning,
                notRunning == RunStatus.Abandoned ? RunStatusReason.WorkerCrashed : null,
                FixedNow.AddMinutes(5)
            );
        }

        // test
        var result = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            runId,
            new RunUpdatableFields
            {
                TotalActivityCount = 5,
                PassedActivityCount = 5,
                FailedActivityCount = 0,
                SkippedActivityCount = 0,
            }
        );
        var row = await GetRawHeaderRowAsync(organizationId, applicationId, runId);

        // verify -- the call changed nothing; stats remain at zero.
        Assert.False(result);
        Assert.Equal("0", row["TotalActivityCount"].N);
    }

    // Claims a fresh pending Run and immediately starts it at startedAt, so its LastHeartbeatAt derives
    // from that single instant -- what several tests below manipulate.
    private async Task<Guid> CreateRunningRunAsync(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId,
        DateTimeOffset startedAt
    )
    {
        var runId = await CreatePendingRunAsync(organizationId, applicationId, environmentId);
        await _repository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            startedAt,
            CreateEnvironmentSnapshot(environmentId)
        );
        return runId;
    }

    [Fact]
    public async Task TryUpdateAsync_HeartbeatWhenRunIsRunning_MovesLastHeartbeatAt()
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
        var result = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            runId,
            new RunUpdatableFields { HeartbeatAt = heartbeatAt }
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
    public async Task TryUpdateAsync_HeartbeatWhenRunIsNotRunning_FailsAndChangesNothing(
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
            await _repository.TryMarkAsStartedAsync(
                organizationId,
                applicationId,
                runId,
                FixedNow,
                CreateEnvironmentSnapshot(environmentId)
            );
            await _repository.TryMarkAsEndedAsync(
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
        var result = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            runId,
            new RunUpdatableFields { HeartbeatAt = FixedNow.AddMinutes(10) }
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
            update
        );
        var headerRow = await GetRawHeaderRowAsync(organizationId, applicationId, runId);
        var rawRows = await QueryRawRowsAsync(organizationId, applicationId);

        // verify
        Assert.True(result);
        Assert.Equal("1", headerRow["LastSeq"].N);
        Assert.Single(
            rawRows,
            row => row["RowKey"].S == DynamoDbMapper.RunStatusUpdateRowKey(runId, 1)
        );
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
            update
        );
        var retry = await _repository.TryAppendStatusUpdateAsync(
            organizationId,
            applicationId,
            runId,
            update
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
            CreateStatusUpdate(5)
        );

        // test -- a lower Seq than the current LastSeq
        var result = await _repository.TryAppendStatusUpdateAsync(
            organizationId,
            applicationId,
            runId,
            CreateStatusUpdate(3)
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
        await _repository.TryMarkAsEndedAsync(
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
            CreateStatusUpdate(1)
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
                CreateStatusUpdate(seq)
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
        // an unpadded key would sort "#4000#10" before "#4000#9", which would corrupt this exact
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
                CreateStatusUpdate(seq)
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
