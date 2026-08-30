using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbRunRepository"/> against DynamoDB Local, covering
/// read/write mapping only.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbRunRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string RunTableName = "Runs";
    private const string TryTableName = "Tries";
    private const string ApplicationTableName = "Applications";
    private const string EnvironmentTableName = "Environments";

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
                    TryTableName = TryTableName,
                    ApplicationTableName = ApplicationTableName,
                    EnvironmentTableName = EnvironmentTableName,
                }
            )
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

        await CreateRunTableAsync(RunTableName, "OrganizationId_ApplicationId");
        await CreateRunTableAsync(TryTableName, "OrganizationId_ScenarioId");
    }

    private async Task CreateRunTableAsync(string tableName, string partitionKeyName)
    {
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
                KeySchema =
                [
                    new KeySchemaElement(partitionKeyName, KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(partitionKeyName, ScalarAttributeType.S),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition("OrganizationId", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "IdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("OrganizationId", KeyType.HASH),
                            new KeySchemaElement("Id", KeyType.RANGE),
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    public async Task DisposeAsync()
    {
        await _client.DeleteTableAsync(RunTableName);
        await _client.DeleteTableAsync(TryTableName);
        await _client.DeleteTableAsync(ApplicationTableName);
        await _client.DeleteTableAsync(EnvironmentTableName);
        _client.Dispose();
    }

    private async Task PutApplicationAsync(Guid organizationId, Guid applicationId)
    {
        await _client.PutItemAsync(
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
    }

    private async Task PutEnvironmentAsync(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId
    )
    {
        await _client.PutItemAsync(
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
    }

    private static Run CreateRun(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId,
        RunKind kind,
        Guid? id = null
    ) =>
        new()
        {
            Id = id ?? Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Kind = kind,
            ApplicationId = applicationId,
            ScenarioIds = [Guid.CreateVersion7()],
            EnvironmentId = environmentId,
            Status = RunStatus.Running,
            TotalActivityCount = 3,
            PassedActivityCount = 1,
            FailedActivityCount = 1,
            SkippedActivityCount = 1,
            TriggeredByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero),
            ActivityResults =
            [
                new ActivityResult
                {
                    ScenarioId = Guid.CreateVersion7(),
                    ActivityId = Guid.CreateVersion7(),
                    Status = ActivityResultStatus.Failed,
                    ResolvedPreconditions = new Dictionary<string, string>
                    {
                        ["Order ID"] = "ORD-1",
                    },
                    Evidence = new Dictionary<string, string>
                    {
                        ["Screenshot"] = "s3://evidence/1.png",
                    },
                    ContinuationReasoning = "Retried after transient network failure.",
                },
            ],
        };

    [Fact]
    public async Task TrySaveAsync_WhenKindIsRunAndDependenciesExist_RoundTripsThroughGetAsync()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId, RunKind.Run);

        // test
        var saved = await _repository.TrySaveAsync(run);
        var result = await _repository.GetAsync(organizationId, run.Id, RunKind.Run);

        // verify
        Assert.True(saved);
        Assert.Equivalent(run, result);
    }

    [Fact]
    public async Task TrySaveAsync_WhenKindIsTryAndDependenciesExist_RoundTripsThroughGetAsync()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var run = CreateRun(organizationId, applicationId, environmentId, RunKind.Try);

        // test
        var saved = await _repository.TrySaveAsync(run);
        var result = await _repository.GetAsync(organizationId, run.Id, RunKind.Try);

        // verify
        Assert.True(saved);
        Assert.Equivalent(run, result);
    }

    [Fact]
    public async Task TrySaveAsync_WhenApplicationDoesNotExist_ReturnsFalse()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var run = CreateRun(organizationId, applicationId, environmentId, RunKind.Run);

        // test
        var saved = await _repository.TrySaveAsync(run);

        // verify
        Assert.False(saved);
    }

    [Fact]
    public async Task TrySaveAsync_WhenEnvironmentDoesNotExist_ReturnsFalse()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var run = CreateRun(organizationId, applicationId, environmentId, RunKind.Run);

        // test
        var saved = await _repository.TrySaveAsync(run);

        // verify
        Assert.False(saved);
    }

    [Fact]
    public async Task GetAsync_WhenNotFound_ReturnsNull()
    {
        // setup
        var organizationId = Guid.CreateVersion7();

        // test
        var result = await _repository.GetAsync(organizationId, Guid.CreateVersion7(), RunKind.Run);

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task ListRunsByApplicationAsync_WhenRunsAndTriesExist_ReturnsOnlyRunsScopedToOrganizationAndApplication()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var firstRun = CreateRun(organizationId, applicationId, environmentId, RunKind.Run);
        var secondRun = CreateRun(organizationId, applicationId, environmentId, RunKind.Run);
        await _repository.TrySaveAsync(firstRun);
        await _repository.TrySaveAsync(secondRun);

        // A Try for the same Application must never appear in the Runs panel.
        await _repository.TrySaveAsync(
            CreateRun(organizationId, applicationId, environmentId, RunKind.Try)
        );

        // A Run belonging to a different Organization must never leak into this list.
        var otherOrganizationId = Guid.CreateVersion7();
        var otherApplicationId = Guid.CreateVersion7();
        var otherEnvironmentId = Guid.CreateVersion7();
        await PutApplicationAsync(otherOrganizationId, otherApplicationId);
        await PutEnvironmentAsync(otherOrganizationId, otherApplicationId, otherEnvironmentId);
        await _repository.TrySaveAsync(
            CreateRun(otherOrganizationId, otherApplicationId, otherEnvironmentId, RunKind.Run)
        );

        // test
        var result = await _repository.ListRunsByApplicationAsync(organizationId, applicationId);

        // verify
        Assert.Equivalent(new[] { firstRun, secondRun }, result);
    }

    [Fact]
    public async Task ListRunsByApplicationAsync_WhenNoRunsExist_ReturnsEmpty()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();

        // test
        var result = await _repository.ListRunsByApplicationAsync(organizationId, applicationId);

        // verify
        Assert.Empty(result);
    }
}
