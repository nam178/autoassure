using A2.Server.Common;
using A2.Server.Engine;
using A2.Server.Engine.Models;
using A2.Server.Engine.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>
/// Integration tests for <see cref="DynamoDbScenarioRepository" /> against
/// DynamoDB Local, covering read/write mapping correctness and concurrency/race
/// conditions with real DynamoDB Local interactions and test-scoped activity
/// repository wrappers that inject mutations at specific points.
/// </summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbScenarioRepositoryTests(
    DynamoDbLocalFixture dynamoDbLocalFixture
) : IAsyncLifetime
{
    private const string ScenarioTableName = "Scenarios";
    private const string ScenariosByFolderTableName = "ScenariosByFolder";
    private const string ScenariosByTagTableName = "ScenariosByTag";
    private const string ApplicationTableName = "Applications";
    private const string ActivityTableName = "Activities";
    private const string PreconditionTableName = "Preconditions";
    private const string EvidenceDefinitionTableName = "EvidenceDefinitions";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbScenarioRepository _repository = null!;
    private DynamoDbActivityRepository _activityRepository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _activityRepository = new DynamoDbActivityRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    ActivityTableName = ActivityTableName,
                    ScenarioTableName = ScenarioTableName,
                    PreconditionTableName = PreconditionTableName,
                    EvidenceDefinitionTableName = EvidenceDefinitionTableName,
                }
            )
        );
        _repository = new DynamoDbScenarioRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    ScenarioTableName = ScenarioTableName,
                    ScenariosByFolderTableName = ScenariosByFolderTableName,
                    ScenariosByTagTableName = ScenariosByTagTableName,
                    ApplicationTableName = ApplicationTableName,
                    ActivityTableName = ActivityTableName,
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
                    new AttributeDefinition(
                        "OrganizationId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = ScenarioTableName,
                KeySchema =
                [
                    new KeySchemaElement(
                        "OrganizationId_ApplicationId",
                        KeyType.HASH
                    ),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId_ApplicationId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await CreateMappingTableAsync(
            ScenariosByFolderTableName,
            "OrganizationId_ApplicationId_Folder"
        );
        await CreateMappingTableAsync(
            ScenariosByTagTableName,
            "OrganizationId_ApplicationId_Tag"
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = ActivityTableName,
                KeySchema =
                [
                    new KeySchemaElement(
                        "OrganizationId_ScenarioId",
                        KeyType.HASH
                    ),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId_ScenarioId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    public async Task DisposeAsync()
    {
        foreach (
            var tableName in new[]
            {
                ApplicationTableName,
                ScenarioTableName,
                ScenariosByFolderTableName,
                ScenariosByTagTableName,
                ActivityTableName,
            }
        )
            await _client.DeleteTableAsync(tableName);
        _client.Dispose();
    }

    private async Task CreateMappingTableAsync(
        string tableName,
        string partitionKeyName
    )
    {
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
                KeySchema =
                [
                    new KeySchemaElement(partitionKeyName, KeyType.HASH),
                    new KeySchemaElement("ScenarioId", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        partitionKeyName,
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition(
                        "ScenarioId",
                        ScalarAttributeType.S
                    ),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    private async Task SeedApplicationAsync(
        Guid organizationId,
        Guid applicationId
    )
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

    private static Scenario CreateScenario(
        Guid organizationId,
        Guid applicationId,
        string folder = "/",
        IReadOnlyList<string>? tags = null
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
        var scenario = CreateScenario(
            organizationId,
            applicationId,
            "/Checkout",
            ["smoke"]
        );

        // test
        var result = await _repository.TrySaveAsync(scenario);
        var fetched = await _repository.GetByIdAsync(
            organizationId,
            applicationId,
            scenario.Id
        );

        // verify
        Assert.True(result);
        Assert.Equivalent(scenario, fetched);
    }

    [Fact]
    public async Task TrySaveAsync_WhenApplicationDoesNotExist_ReturnsFalse()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenario = CreateScenario(organizationId, applicationId);

        // test
        var result = await _repository.TrySaveAsync(scenario);

        // verify
        Assert.False(result);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenFolderChanges_MovesScenarioBetweenFolderMappings()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var scenario = CreateScenario(
            organizationId,
            applicationId,
            "/OldFolder"
        );
        await _repository.TrySaveAsync(scenario);

        var updated = scenario with
        {
            Folder = "/NewFolder",
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // test
        var result = await _repository.TryUpdateAsync(updated, scenario);

        // verify
        Assert.Equal(ScenarioUpdateResult.Success, result);
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
        Assert.Equal(ScenarioUpdateResult.ApplicationNotFound, result);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenScenarioDoesNotExist_ReturnsScenarioNotFoundAndDoesNotCreateIt()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var scenario = CreateScenario(organizationId, applicationId);

        // test
        var result = await _repository.TryUpdateAsync(scenario, scenario);
        var fetched = await _repository.GetByIdAsync(
            organizationId,
            applicationId,
            scenario.Id
        );

        // verify
        Assert.Equal(ScenarioUpdateResult.ScenarioNotFound, result);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ReturnsNull()
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
    public async Task GetByIdAsync_WhenScenarioInDifferentApplication_ReturnsNull()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var otherApplicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        await SeedApplicationAsync(organizationId, otherApplicationId);
        var scenario = CreateScenario(organizationId, applicationId);
        await _repository.TrySaveAsync(scenario);

        // test
        var result = await _repository.GetByIdAsync(
            organizationId,
            otherApplicationId,
            scenario.Id
        );

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
        var result = await _repository.ListByApplicationAsync(
            organizationId,
            applicationId
        );

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
        var inFolder = CreateScenario(
            organizationId,
            applicationId,
            "/Checkout"
        );
        var otherFolder = CreateScenario(
            organizationId,
            applicationId,
            "/Other"
        );
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
        var tagged = CreateScenario(
            organizationId,
            applicationId,
            tags: ["smoke"]
        );
        var untagged = CreateScenario(
            organizationId,
            applicationId,
            tags: ["regression"]
        );
        await _repository.TrySaveAsync(tagged);
        await _repository.TrySaveAsync(untagged);

        // test
        var result = await _repository.ListByTagAsync(
            organizationId,
            applicationId,
            "smoke"
        );

        // verify
        var found = Assert.Single(result);
        Assert.Equal(tagged.Id, found.Id);
    }

    [Fact]
    public async Task TrySetLifecycleStateAsync_WhenScenarioExists_ChangesLifecycleState()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var scenario = CreateScenario(organizationId, applicationId);
        await _repository.TrySaveAsync(scenario);

        // test
        var result = await _repository.TrySetLifecycleStateAsync(
            organizationId,
            applicationId,
            scenario.Id,
            LifecycleState.Archived
        );
        var fetched = await _repository.GetByIdAsync(
            organizationId,
            applicationId,
            scenario.Id
        );

        // verify
        Assert.True(result);
        Assert.NotNull(fetched);
        Assert.Equal(LifecycleState.Archived, fetched.LifecycleState);
    }

    [Fact]
    public async Task TrySetLifecycleStateAsync_WhenScenarioNotFound_ReturnsFalse()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();

        // test
        var result = await _repository.TrySetLifecycleStateAsync(
            organizationId,
            applicationId,
            scenarioId,
            LifecycleState.Archived
        );

        // verify
        Assert.False(result);
    }

    [Fact]
    public async Task TryDeleteAsync_WhenScenarioExists_DeletesScenarioAndAllActivitiesAndMappings()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var scenario = CreateScenario(
            organizationId,
            applicationId,
            "/TestFolder",
            ["tag1", "tag2"]
        );
        await _repository.TrySaveAsync(scenario);

        var activity1 = new Activity
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            ScenarioId = scenario.Id,
            Order = 0,
            Description = "Activity 1 description",
            PreconditionIds = [],
            EvidenceIds = [],
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var activity2 = new Activity
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            ScenarioId = scenario.Id,
            Order = 1,
            Description = "Activity 2 description",
            PreconditionIds = [],
            EvidenceIds = [],
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _activityRepository.TrySaveAsync(activity1);
        await _activityRepository.TrySaveAsync(activity2);

        // test
        var result = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenario.Id,
            [activity1, activity2]
        );

        // verify
        Assert.Equal(ScenarioDeleteResult.Success, result);

        var deletedScenario = await _repository.GetByIdAsync(
            organizationId,
            applicationId,
            scenario.Id
        );
        Assert.Null(deletedScenario);

        var deletedActivity1 = await _activityRepository.GetByIdAsync(
            organizationId,
            scenario.Id,
            activity1.Id
        );
        Assert.Null(deletedActivity1);

        var deletedActivity2 = await _activityRepository.GetByIdAsync(
            organizationId,
            scenario.Id,
            activity2.Id
        );
        Assert.Null(deletedActivity2);

        var folderMappings = await _repository.ListByFolderAsync(
            organizationId,
            applicationId,
            "/TestFolder"
        );
        Assert.Empty(folderMappings);

        var tag1Mappings = await _repository.ListByTagAsync(
            organizationId,
            applicationId,
            "tag1"
        );
        Assert.Empty(tag1Mappings);

        var tag2Mappings = await _repository.ListByTagAsync(
            organizationId,
            applicationId,
            "tag2"
        );
        Assert.Empty(tag2Mappings);
    }

    [Fact]
    public async Task TryDeleteAsync_WhenScenarioNotFound_ReturnsScenarioNotFound()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();

        // test
        var result = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenarioId,
            []
        );

        // verify
        Assert.Equal(ScenarioDeleteResult.ScenarioNotFound, result);
    }

    [Fact]
    public async Task TryDeleteAsync_WhenActivityCountChangedConcurrently_ReturnsScenarioModifiedConcurrently()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        // Create application
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

        var scenario = new Scenario
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Title = "Scenario",
            Description = "Description",
            Folder = "/folder",
            Tags = ["tag1"],
            ActivityCount = 0,
            LifecycleState = LifecycleState.Active,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _repository.TrySaveAsync(scenario);

        // Create an activity so ActivityCount = 1
        var activity = new Activity
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            ScenarioId = scenario.Id,
            Order = 0,
            Description = "Activity description",
            PreconditionIds = [],
            EvidenceIds = [],
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _activityRepository.TrySaveAsync(activity);

        // Simulate the activity count changing after the caller listed activities
        await RaceConditionMutations.ActivityCountModification(
            _client,
            ScenarioTableName,
            $"{organizationId}_{applicationId}",
            scenario.Id
        )();

        // test
        var result = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenario.Id,
            [activity]
        );

        // verify
        Assert.Equal(ScenarioDeleteResult.ScenarioModifiedConcurrently, result);

        // verify - Scenario should still exist since delete failed
        var stillExists = await _repository.GetByIdAsync(
            organizationId,
            applicationId,
            scenario.Id
        );
        Assert.NotNull(stillExists);
    }

    [Fact]
    public async Task TryDeleteAsync_WhenFolderMappingDeletedConcurrently_ReturnsScenarioModifiedConcurrently()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        // Create application
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

        var scenario = new Scenario
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Title = "Scenario",
            Description = "Description",
            Folder = "/folder",
            Tags = ["tag1"],
            ActivityCount = 0,
            LifecycleState = LifecycleState.Active,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _repository.TrySaveAsync(scenario);

        // Simulate the folder mapping being deleted after the caller listed activities
        await RaceConditionMutations.FolderMappingDeletion(
            _client,
            ScenariosByFolderTableName,
            organizationId,
            applicationId,
            scenario.Id,
            "/folder"
        )();

        // test
        var result = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenario.Id,
            []
        );

        // verify
        Assert.Equal(ScenarioDeleteResult.ScenarioModifiedConcurrently, result);

        // verify - Scenario should still exist since delete failed
        var stillExists = await _repository.GetByIdAsync(
            organizationId,
            applicationId,
            scenario.Id
        );
        Assert.NotNull(stillExists);
    }

    [Fact]
    public async Task TryDeleteAsync_WhenTagMappingDeletedConcurrently_ReturnsScenarioModifiedConcurrently()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        // Create application
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

        var scenario = new Scenario
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Title = "Scenario",
            Description = "Description",
            Folder = "/folder",
            Tags = ["tag1", "tag2"],
            ActivityCount = 0,
            LifecycleState = LifecycleState.Active,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _repository.TrySaveAsync(scenario);

        // Simulate a tag mapping being deleted after the caller listed activities
        await RaceConditionMutations.TagMappingDeletion(
            _client,
            ScenariosByTagTableName,
            organizationId,
            applicationId,
            scenario.Id,
            "tag1"
        )();

        // test
        var result = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenario.Id,
            []
        );

        // verify
        Assert.Equal(ScenarioDeleteResult.ScenarioModifiedConcurrently, result);

        // verify - Scenario should still exist since delete failed
        var stillExists = await _repository.GetByIdAsync(
            organizationId,
            applicationId,
            scenario.Id
        );
        Assert.NotNull(stillExists);
    }

    [Fact]
    public async Task TryDeleteAsync_WhenOneActivityAddedAndOneRemovedAfterListing_ReturnsScenarioModifiedConcurrently()
    {
        // setup - five activities, listed by the caller
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await SeedApplicationAsync(organizationId, applicationId);
        var scenario = CreateScenario(
            organizationId,
            applicationId,
            "/TestFolder",
            ["tag1"]
        );
        await _repository.TrySaveAsync(scenario);

        var staleActivities = new List<Activity>();
        for (var order = 0; order < 5; order++)
        {
            var activity = CreateActivity(
                organizationId,
                applicationId,
                scenario.Id,
                order
            );
            await _activityRepository.TrySaveAsync(activity);
            staleActivities.Add(activity);
        }

        // setup - after listing, one activity is added and another is removed, so the count is still five
        var addedActivity = CreateActivity(
            organizationId,
            applicationId,
            scenario.Id,
            5
        );
        await _activityRepository.TrySaveAsync(addedActivity);
        await _activityRepository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenario.Id,
            staleActivities[0].Id
        );

        // test - delete with the stale list
        var result = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenario.Id,
            staleActivities
        );

        // verify - nothing is deleted
        Assert.Equal(ScenarioDeleteResult.ScenarioModifiedConcurrently, result);
        var stillExists = await _repository.GetByIdAsync(
            organizationId,
            applicationId,
            scenario.Id
        );
        Assert.NotNull(stillExists);
        var remainingActivities = await _activityRepository.ListByScenarioAsync(
            organizationId,
            scenario.Id
        );
        Assert.Equal(5, remainingActivities.Count);
        Assert.Contains(remainingActivities, a => a.Id == addedActivity.Id);
    }

    private static Activity CreateActivity(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        int order
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            ScenarioId = scenarioId,
            Order = order,
            Description = $"Activity {order} description",
            PreconditionIds = [],
            EvidenceIds = [],
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task TryDeleteAsync_WhenScenarioHasFiveTagsAndNinetyActivities_SucceedsInOneTransaction()
    {
        // setup - Create scenario with max tags and max activities to verify transaction ceiling
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Create application
        await _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = ApplicationTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId"] = new(organizationId.ToString()),
                    ["Id"] = new(applicationId.ToString()),
                    ["Name"] = new("Test App"),
                    ["CreatedByUserId"] = new(userId.ToString()),
                    ["UpdatedByUserId"] = new(userId.ToString()),
                    ["CreatedAt"] = new(now.ToString("O")),
                    ["UpdatedAt"] = new(now.ToString("O")),
                },
            }
        );

        // Create scenario with max tags (5)
        var scenarioId = Guid.CreateVersion7();
        var tags = new List<string> { "tag1", "tag2", "tag3", "tag4", "tag5" };
        var scenario = new Scenario
        {
            Id = scenarioId,
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Title = "Test Scenario",
            Description = "Test",
            Folder = "/",
            Tags = tags,
            ActivityCount = 0,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _repository.TrySaveAsync(scenario);

        // Create max activities for the scenario
        var activities = new List<Activity>();
        for (int i = 0; i < Quota.MaxActivityCountPerScenario; i++)
        {
            var activity = new Activity
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = organizationId,
                ApplicationId = applicationId,
                ScenarioId = scenarioId,
                Description = $"Activity {i}",
                Order = i,
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = now,
                PreconditionIds = [],
                EvidenceIds = [],
            };
            await _activityRepository.TrySaveAsync(activity);
            activities.Add(activity);
        }

        // test - Delete scenario with max tags and max activities
        var result = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenarioId,
            activities
        );

        // verify - Deletion should succeed in a single transaction
        Assert.Equal(ScenarioDeleteResult.Success, result);

        // verify - Scenario should be deleted
        var deleted = await _repository.GetByIdAsync(
            organizationId,
            applicationId,
            scenarioId
        );
        Assert.Null(deleted);
    }
}

/// <summary>
/// Test wrapper for IActivityRepository that injects a race condition mutation during ListByScenarioAsync.
/// The mutation callback is executed after the Scenario read but before the transaction executes,
/// allowing tests to simulate concurrent modifications (ActivityCount changes, folder/tag deletions, etc.).
/// Used for repository tests via direct instantiation and for controller tests via DI customization.
/// </summary>
public sealed class ActivityRepositoryRaceConditionWrapper(
    IActivityRepository inner,
    Guid targetOrganizationId,
    Guid targetScenarioId,
    Func<Task> mutationCallback
) : IActivityRepository
{
    public async Task<IReadOnlyList<Activity>> ListByScenarioAsync(
        Guid organizationId,
        Guid scenarioId
    )
    {
        // Inject race condition mutation if this call matches the configured scenario
        if (
            organizationId == targetOrganizationId
            && scenarioId == targetScenarioId
        )
        {
            await mutationCallback();
        }

        return await inner.ListByScenarioAsync(organizationId, scenarioId);
    }

    // Delegate all other methods to the inner repository
    public Task<Activity?> GetByIdAsync(
        Guid organizationId,
        Guid scenarioId,
        Guid activityId
    ) => inner.GetByIdAsync(organizationId, scenarioId, activityId);

    public Task<ActivitySaveResult> TrySaveAsync(Activity activity) =>
        inner.TrySaveAsync(activity);

    public Task<ActivityUpdateResult> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        Guid activityId,
        ActivityUpdatableFields fields
    ) =>
        inner.TryUpdateAsync(
            organizationId,
            applicationId,
            scenarioId,
            activityId,
            fields
        );

    public Task<bool> TryReorderAsync(
        Guid organizationId,
        Guid scenarioId,
        IReadOnlyList<Guid> orderedActivityIds
    ) => inner.TryReorderAsync(organizationId, scenarioId, orderedActivityIds);

    public Task<bool> TryDeleteAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        Guid activityId
    ) =>
        inner.TryDeleteAsync(
            organizationId,
            applicationId,
            scenarioId,
            activityId
        );
}

// Helper factory methods for creating mutation callbacks
public static class RaceConditionMutations
{
    public static Func<Task> ActivityCountModification(
        IAmazonDynamoDB dynamoDb,
        string scenarioTableName,
        string scenarioPartitionKey,
        Guid scenarioId
    )
    {
        return async () =>
        {
            await dynamoDb.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = scenarioTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId_ApplicationId"] = new(
                            scenarioPartitionKey
                        ),
                        ["Id"] = new(scenarioId.ToString()),
                    },
                    UpdateExpression = "SET ActivityCount = :newCount",
                    ExpressionAttributeValues = new Dictionary<
                        string,
                        AttributeValue
                    >
                    {
                        [":newCount"] = new() { N = "999" },
                    },
                }
            );
        };
    }

    public static Func<Task> FolderMappingDeletion(
        IAmazonDynamoDB dynamoDb,
        string folderTableName,
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        string folder
    )
    {
        return async () =>
        {
            await dynamoDb.DeleteItemAsync(
                new DeleteItemRequest
                {
                    TableName = folderTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId_ApplicationId_Folder"] = new(
                            $"{organizationId}_{applicationId}_{folder}"
                        ),
                        ["ScenarioId"] = new(scenarioId.ToString()),
                    },
                }
            );
        };
    }

    public static Func<Task> TagMappingDeletion(
        IAmazonDynamoDB dynamoDb,
        string tagTableName,
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        string tag
    )
    {
        return async () =>
        {
            await dynamoDb.DeleteItemAsync(
                new DeleteItemRequest
                {
                    TableName = tagTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId_ApplicationId_Tag"] = new(
                            $"{organizationId}_{applicationId}_{tag}"
                        ),
                        ["ScenarioId"] = new(scenarioId.ToString()),
                    },
                }
            );
        };
    }
}
