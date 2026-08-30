using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbScenarioRepository"/> against DynamoDB Local,
/// covering read/write mapping correctness only — concurrency/races are covered elsewhere with
/// fakes.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbScenarioRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string ScenarioTableName = "Scenarios";
    private const string ScenariosByFolderTableName = "ScenariosByFolder";
    private const string ScenariosByTagTableName = "ScenariosByTag";
    private const string ApplicationTableName = "Applications";
    private const string PreconditionTableName = "Preconditions";
    private const string EvidenceDefinitionTableName = "EvidenceDefinitions";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbScenarioRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbScenarioRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    ScenarioTableName = ScenarioTableName,
                    ScenariosByFolderTableName = ScenariosByFolderTableName,
                    ScenariosByTagTableName = ScenariosByTagTableName,
                    ApplicationTableName = ApplicationTableName,
                    PreconditionTableName = PreconditionTableName,
                    EvidenceDefinitionTableName = EvidenceDefinitionTableName,
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

        await CreateLibraryTableAsync(PreconditionTableName);
        await CreateLibraryTableAsync(EvidenceDefinitionTableName);

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = ScenarioTableName,
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ApplicationId", ScalarAttributeType.S),
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

        await CreateMappingTableAsync(ScenariosByFolderTableName);
        await CreateMappingTableAsync(ScenariosByTagTableName);
    }

    private async Task CreateLibraryTableAsync(string tableName) =>
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ApplicationId", ScalarAttributeType.S),
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

    private async Task CreateMappingTableAsync(string tableName) =>
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
                KeySchema =
                [
                    new KeySchemaElement("PartitionKey", KeyType.HASH),
                    new KeySchemaElement("ScenarioId", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("PartitionKey", ScalarAttributeType.S),
                    new AttributeDefinition("ScenarioId", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

    public async Task DisposeAsync()
    {
        foreach (
            var tableName in new[]
            {
                ApplicationTableName,
                PreconditionTableName,
                EvidenceDefinitionTableName,
                ScenarioTableName,
                ScenariosByFolderTableName,
                ScenariosByTagTableName,
            }
        )
        {
            await _client.DeleteTableAsync(tableName);
        }
        _client.Dispose();
    }

    private async Task SeedApplicationAsync(Guid organizationId, Guid applicationId) =>
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

    private static Scenario CreateScenario(
        Guid organizationId,
        Guid applicationId,
        string folder = "/",
        IReadOnlyList<string>? tags = null,
        IReadOnlyList<Activity>? activities = null
    )
    {
        var userId = Guid.CreateVersion7();
        return new Scenario
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Title = "Checkout completes",
            Description = "Verify a user can complete checkout",
            Folder = folder,
            Tags = tags ?? [],
            Activities = activities ?? [],
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    [Fact]
    public async Task TrySaveAsync_WhenApplicationExists_RoundTripsThroughGetById()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var scenario = CreateScenario(organizationId, applicationId, "/Checkout", ["smoke"]);

        // test
        var result = await _repository.TrySaveAsync(scenario);
        var fetched = await _repository.GetByIdAsync(organizationId, scenario.Id);

        // verify
        Assert.Equal(ScenarioWriteResult.Success, result);
        Assert.Equivalent(scenario, fetched);
    }

    [Fact]
    public async Task TrySaveAsync_WhenApplicationDoesNotExist_ReturnsApplicationNotFound()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenario = CreateScenario(organizationId, applicationId);

        // test
        var result = await _repository.TrySaveAsync(scenario);

        // verify
        Assert.Equal(ScenarioWriteResult.ApplicationNotFound, result);
    }

    [Fact]
    public async Task TrySaveAsync_WhenReferencedPreconditionDoesNotExist_ReturnsReferenceNotFound()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var activity = new Activity
        {
            Id = Guid.CreateVersion7(),
            Description = "Add item to cart",
            PreconditionIds = [Guid.CreateVersion7()],
        };
        var scenario = CreateScenario(organizationId, applicationId, activities: [activity]);

        // test
        var result = await _repository.TrySaveAsync(scenario);

        // verify
        Assert.Equal(ScenarioWriteResult.ReferenceNotFound, result);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenFolderChanges_MovesScenarioBetweenFolderMappings()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var scenario = CreateScenario(organizationId, applicationId, "/OldFolder");
        await _repository.TrySaveAsync(scenario);

        var updated = scenario with { Folder = "/NewFolder", UpdatedAt = DateTimeOffset.UtcNow };

        // test
        var result = await _repository.TryUpdateAsync(updated, scenario);

        // verify
        Assert.Equal(ScenarioWriteResult.Success, result);
        var oldFolderScenarios = await _repository.ListByFolderAsync(
            organizationId,
            applicationId,
            "/OldFolder"
        );
        Assert.Empty(oldFolderScenarios);
        var newFolderScenarios = await _repository.ListByFolderAsync(
            organizationId,
            applicationId,
            "/NewFolder"
        );
        var found = Assert.Single(newFolderScenarios);
        Assert.Equal(scenario.Id, found.Id);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenApplicationDoesNotExist_ReturnsApplicationNotFound()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenario = CreateScenario(organizationId, applicationId);

        // test
        var result = await _repository.TryUpdateAsync(scenario, scenario);

        // verify
        Assert.Equal(ScenarioWriteResult.ApplicationNotFound, result);
    }

    [Fact]
    public async Task DeleteAsync_WhenScenarioExists_RemovesScenarioAndFolderTagMappings()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var scenario = CreateScenario(organizationId, applicationId, "/Checkout", ["smoke"]);
        await _repository.TrySaveAsync(scenario);

        // test
        await _repository.DeleteAsync(scenario);

        // verify
        Assert.Null(await _repository.GetByIdAsync(organizationId, scenario.Id));
        Assert.Empty(
            await _repository.ListByFolderAsync(organizationId, applicationId, "/Checkout")
        );
        Assert.Empty(await _repository.ListByTagAsync(organizationId, applicationId, "smoke"));
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ReturnsNull()
    {
        // test
        var result = await _repository.GetByIdAsync(Guid.CreateVersion7(), Guid.CreateVersion7());

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenMultipleScenariosExist_ReturnsAllForThatApplication()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var otherApplicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        await SeedApplicationAsync(organizationId, otherApplicationId);
        var scenario1 = CreateScenario(organizationId, applicationId);
        var scenario2 = CreateScenario(organizationId, applicationId);
        var otherScenario = CreateScenario(organizationId, otherApplicationId);
        await _repository.TrySaveAsync(scenario1);
        await _repository.TrySaveAsync(scenario2);
        await _repository.TrySaveAsync(otherScenario);

        // test
        var result = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify
        Assert.Equal(2, result.Count);
        Assert.Contains(result, scenario => scenario.Id == scenario1.Id);
        Assert.Contains(result, scenario => scenario.Id == scenario2.Id);
    }

    [Fact]
    public async Task ListByFolderAsync_WhenScenariosInFolder_ReturnsOnlyMatchingScenarios()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var inFolder = CreateScenario(organizationId, applicationId, "/Checkout");
        var otherFolder = CreateScenario(organizationId, applicationId, "/Other");
        await _repository.TrySaveAsync(inFolder);
        await _repository.TrySaveAsync(otherFolder);

        // test
        var result = await _repository.ListByFolderAsync(
            organizationId,
            applicationId,
            "/Checkout"
        );

        // verify
        var found = Assert.Single(result);
        Assert.Equal(inFolder.Id, found.Id);
    }

    [Fact]
    public async Task ListByTagAsync_WhenScenariosCarryTag_ReturnsOnlyMatchingScenarios()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var tagged = CreateScenario(organizationId, applicationId, tags: ["smoke"]);
        var untagged = CreateScenario(organizationId, applicationId, tags: ["regression"]);
        await _repository.TrySaveAsync(tagged);
        await _repository.TrySaveAsync(untagged);

        // test
        var result = await _repository.ListByTagAsync(organizationId, applicationId, "smoke");

        // verify
        var found = Assert.Single(result);
        Assert.Equal(tagged.Id, found.Id);
    }
}
