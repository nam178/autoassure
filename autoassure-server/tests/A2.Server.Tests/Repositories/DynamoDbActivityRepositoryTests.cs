using System.Globalization;
using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>
///     Integration tests for <see cref="DynamoDbActivityRepository" /> against
///     DynamoDB Local,
///     covering read/write mapping correctness only — concurrency/races are
///     covered elsewhere with
///     fakes.
/// </summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbActivityRepositoryTests(
    DynamoDbLocalFixture dynamoDbLocalFixture
) : IAsyncLifetime
{
    private const string ActivityTableName = "Activities";
    private const string ScenarioTableName = "Scenarios";
    private const string PreconditionTableName = "Preconditions";
    private const string EvidenceDefinitionTableName = "EvidenceDefinitions";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbActivityRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbActivityRepository(
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

        await CreateLibraryTableAsync(PreconditionTableName);
        await CreateLibraryTableAsync(EvidenceDefinitionTableName);

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
                ScenarioTableName,
                PreconditionTableName,
                EvidenceDefinitionTableName,
                ActivityTableName,
            }
        )
            await _client.DeleteTableAsync(tableName);
        _client.Dispose();
    }

    private async Task CreateLibraryTableAsync(string tableName)
    {
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
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
    }

    private async Task SeedScenarioAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId
    )
    {
        await _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = ScenarioTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        $"{organizationId}_{applicationId}"
                    ),
                    ["Id"] = new(scenarioId.ToString()),
                },
            }
        );
    }

    private async Task SeedPreconditionAsync(
        Guid organizationId,
        Guid applicationId,
        Guid preconditionId
    )
    {
        await _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = PreconditionTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        $"{organizationId}_{applicationId}"
                    ),
                    ["Id"] = new(preconditionId.ToString()),
                },
            }
        );
    }

    private static Activity CreateActivity(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        int order = 0,
        IReadOnlyList<Guid>? preconditionIds = null,
        Guid? id = null
    )
    {
        return new Activity
        {
            Id = id ?? Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            ScenarioId = scenarioId,
            Description = "Add item to cart",
            Order = order,
            PreconditionIds = preconditionIds ?? [],
            EvidenceIds = [],
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    [Fact]
    public async Task TrySaveAsync_WhenScenarioExists_RoundTripsThroughGetById()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioId
        );

        // test
        var result = await _repository.TrySaveAsync(activity);
        var fetched = await _repository.GetByIdAsync(
            organizationId,
            scenarioId,
            activity.Id
        );

        // verify
        Assert.Equal(ActivitySaveResult.Success, result);
        Assert.Equivalent(activity, fetched);
    }

    [Fact]
    public async Task TrySaveAsync_WhenScenarioDoesNotExist_ReturnsScenarioNotFound()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioId
        );

        // test
        var result = await _repository.TrySaveAsync(activity);

        // verify
        Assert.Equal(ActivitySaveResult.ScenarioNotFound, result);
    }

    [Fact]
    public async Task TrySaveAsync_WhenReferencedPreconditionDoesNotExist_ReturnsReferenceNotFound()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioId,
            preconditionIds: [Guid.CreateVersion7()]
        );

        // test
        var result = await _repository.TrySaveAsync(activity);

        // verify
        Assert.Equal(ActivitySaveResult.PreconditionOrEvidenceNotFound, result);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenCalled_UpdatesOnlyAllowedFields()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioId
        );
        await _repository.TrySaveAsync(activity);
        var preconditionId = Guid.CreateVersion7();
        await SeedPreconditionAsync(
            organizationId,
            applicationId,
            preconditionId
        );

        var updatedByUserId = Guid.CreateVersion7();
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var fields = new ActivityUpdatableFields
        {
            Description = "Updated description",
            PreconditionIds = [preconditionId],
            EvidenceIds = [],
            UpdatedByUserId = updatedByUserId,
            UpdatedAt = updatedAt,
        };

        // test
        var result = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            scenarioId,
            activity.Id,
            fields
        );
        var fetched = await _repository.GetByIdAsync(
            organizationId,
            scenarioId,
            activity.Id
        );

        // verify
        Assert.Equal(ActivityUpdateResult.Success, result);
        Assert.Equivalent(
            activity with
            {
                Description = fields.Description,
                PreconditionIds = fields.PreconditionIds,
                EvidenceIds = fields.EvidenceIds,
                UpdatedByUserId = fields.UpdatedByUserId,
                UpdatedAt = fields.UpdatedAt,
            },
            fetched
        );
    }

    [Fact]
    public async Task TryUpdateAsync_WhenActivityDoesNotExist_ReturnsActivityNotFound()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        // test
        var result = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            scenarioId,
            id,
            new ActivityUpdatableFields
            {
                Description = "Updated description",
                UpdatedByUserId = Guid.CreateVersion7(),
                UpdatedAt = new DateTimeOffset(
                    2026,
                    2,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero
                ),
            }
        );

        // verify
        Assert.Equal(ActivityUpdateResult.ActivityNotFound, result);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenScenarioIdDoesNotMatchActivity_ReturnsActivityNotFound()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var actualScenarioId = Guid.CreateVersion7();
        var otherScenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(
            organizationId,
            applicationId,
            actualScenarioId
        );
        await SeedScenarioAsync(organizationId, applicationId, otherScenarioId);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            actualScenarioId
        );
        await _repository.TrySaveAsync(activity);

        // test
        // The Update's ConditionCheck runs against the wrong partition key (Id exists, but not
        // under otherScenarioId's partition) -- exercising the catch block's index-0
        // ConditionalCheckFailed branch.
        var result = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            otherScenarioId,
            activity.Id,
            new ActivityUpdatableFields
            {
                Description = "Updated description",
                UpdatedByUserId = Guid.CreateVersion7(),
                UpdatedAt = new DateTimeOffset(
                    2026,
                    2,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero
                ),
            }
        );

        // verify
        Assert.Equal(ActivityUpdateResult.ActivityNotFound, result);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenReferencedPreconditionDoesNotExist_ReturnsReferenceNotFound()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioId
        );
        await _repository.TrySaveAsync(activity);

        // test
        var result = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            scenarioId,
            activity.Id,
            new ActivityUpdatableFields
            {
                Description = "Updated description",
                PreconditionIds = [Guid.CreateVersion7()],
                UpdatedByUserId = Guid.CreateVersion7(),
                UpdatedAt = new DateTimeOffset(
                    2026,
                    2,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero
                ),
            }
        );

        // verify
        Assert.Equal(
            ActivityUpdateResult.PreconditionOrEvidenceNotFound,
            result
        );
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
    public async Task GetByIdAsync_WhenActivityInDifferentScenario_ReturnsNull()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioIdA = Guid.CreateVersion7();
        var scenarioIdB = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioIdA);
        await SeedScenarioAsync(organizationId, applicationId, scenarioIdB);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioIdA
        );
        await _repository.TrySaveAsync(activity);

        // test
        var result = await _repository.GetByIdAsync(
            organizationId,
            scenarioIdB,
            activity.Id
        );

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task ListByScenarioAsync_WhenMultipleActivitiesExist_ReturnsAllOrderedByOrder()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var second = CreateActivity(
            organizationId,
            applicationId,
            scenarioId,
            1
        );
        var first = CreateActivity(organizationId, applicationId, scenarioId);
        await _repository.TrySaveAsync(second);
        await _repository.TrySaveAsync(first);

        // test
        var result = await _repository.ListByScenarioAsync(
            organizationId,
            scenarioId
        );

        // verify
        Assert.Equal([first.Id, second.Id], result.Select(a => a.Id));
    }

    [Fact]
    public async Task ListByScenarioAsync_WhenNoActivitiesExist_ReturnsEmpty()
    {
        // test
        var result = await _repository.ListByScenarioAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7()
        );

        // verify
        Assert.Empty(result);
    }

    [Fact]
    public async Task TryReorderAsync_WhenValidPermutation_UpdatesOrder()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var first = CreateActivity(organizationId, applicationId, scenarioId);
        var second = CreateActivity(
            organizationId,
            applicationId,
            scenarioId,
            1
        );
        await _repository.TrySaveAsync(first);
        await _repository.TrySaveAsync(second);

        // test
        var result = await _repository.TryReorderAsync(
            organizationId,
            scenarioId,
            [second.Id, first.Id]
        );
        var reordered = await _repository.ListByScenarioAsync(
            organizationId,
            scenarioId
        );

        // verify
        Assert.True(result);
        Assert.Equal([second.Id, first.Id], reordered.Select(a => a.Id));
    }

    [Fact]
    public async Task TryReorderAsync_WhenActivityIdDoesNotExist_ReturnsFalse()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioId
        );
        await _repository.TrySaveAsync(activity);

        // test
        var result = await _repository.TryReorderAsync(
            organizationId,
            scenarioId,
            [activity.Id, Guid.CreateVersion7()]
        );

        // verify
        Assert.False(result);
    }

    [Fact]
    public async Task TryDeleteAsync_WhenActivityExists_RemovesItAndDecrementsCount()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioId
        );
        await _repository.TrySaveAsync(activity);

        // test
        var result = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenarioId,
            activity.Id
        );
        var remaining = await _repository.ListByScenarioAsync(
            organizationId,
            scenarioId
        );
        var activityCount = await GetScenarioActivityCountAsync(
            organizationId,
            applicationId,
            scenarioId
        );

        // verify
        Assert.True(result);
        Assert.Empty(remaining);
        Assert.Equal(0, activityCount);
    }

    [Fact]
    public async Task TryDeleteAsync_WhenCalledTwice_SecondReturnsFalseAndCountUnchanged()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioId
        );
        await _repository.TrySaveAsync(activity);

        // test
        var firstDelete = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenarioId,
            activity.Id
        );
        var secondDelete = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenarioId,
            activity.Id
        );
        var activityCount = await GetScenarioActivityCountAsync(
            organizationId,
            applicationId,
            scenarioId
        );

        // verify
        Assert.True(firstDelete);
        Assert.False(secondDelete);
        Assert.Equal(0, activityCount);
    }

    [Fact]
    public async Task TryDeleteAsync_WhenMultipleActivitiesExist_LeavesOtherOrderValuesIntact()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var first = CreateActivity(organizationId, applicationId, scenarioId);
        var second = CreateActivity(
            organizationId,
            applicationId,
            scenarioId,
            1
        );
        var third = CreateActivity(
            organizationId,
            applicationId,
            scenarioId,
            2
        );
        await _repository.TrySaveAsync(first);
        await _repository.TrySaveAsync(second);
        await _repository.TrySaveAsync(third);

        // test: delete the middle one
        var result = await _repository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenarioId,
            second.Id
        );
        var remaining = await _repository.ListByScenarioAsync(
            organizationId,
            scenarioId
        );

        // verify: order values should be 0 and 2 (gap is expected, not renumbered)
        Assert.True(result);
        Assert.Equal(2, remaining.Count);
        Assert.Equal([0, 2], remaining.Select(a => a.Order));
        Assert.Equal([first.Id, third.Id], remaining.Select(a => a.Id));
    }

    [Fact]
    public async Task TryDeleteAsync_WhenScenarioHasNoActivityCountAttribute_ThrowsToPreventNegativeCount()
    {
        // setup: Scenario with no ActivityCount attribute (legacy row)
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var scenarioId = Guid.CreateVersion7();
        await SeedScenarioAsync(organizationId, applicationId, scenarioId);
        var activity = CreateActivity(
            organizationId,
            applicationId,
            scenarioId
        );
        await _repository.TrySaveAsync(activity);

        // Delete the ActivityCount attribute to simulate a legacy Scenario
        await _client.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = ScenarioTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        $"{organizationId}_{applicationId}"
                    ),
                    ["Id"] = new(scenarioId.ToString()),
                },
                UpdateExpression = "REMOVE ActivityCount",
            }
        );

        // test & verify: deletion should throw because scenario has no ActivityCount
        // (the condition check fails, which is a broken invariant)
        await Assert.ThrowsAsync<TransactionCanceledException>(() =>
            _repository.TryDeleteAsync(
                organizationId,
                applicationId,
                scenarioId,
                activity.Id
            )
        );

        // verify: ActivityCount attribute should still not exist (count did not become -1)
        var activityCount = await GetScenarioActivityCountAsync(
            organizationId,
            applicationId,
            scenarioId
        );
        Assert.Equal(0, activityCount);
    }

    private async Task<int> GetScenarioActivityCountAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId
    )
    {
        var response = await _client.GetItemAsync(
            new GetItemRequest
            {
                TableName = ScenarioTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        $"{organizationId}_{applicationId}"
                    ),
                    ["Id"] = new(scenarioId.ToString()),
                },
                ConsistentRead = true,
            }
        );

        if (!response.IsItemSet)
            throw new InvalidOperationException("Scenario not found");

        return response.Item.TryGetValue("ActivityCount", out var count)
            ? int.Parse(count.N, CultureInfo.InvariantCulture)
            : 0;
    }
}
