using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbPreconditionRepository"/> against DynamoDB Local,
/// covering read/write mapping only.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbPreconditionRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string TableName = "Preconditions";
    private const string ApplicationTableName = "Applications";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbPreconditionRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbPreconditionRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    PreconditionTableName = TableName,
                    ApplicationTableName = ApplicationTableName,
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
                TableName = TableName,
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
    }

    public async Task DisposeAsync()
    {
        await _client.DeleteTableAsync(TableName);
        await _client.DeleteTableAsync(ApplicationTableName);
        _client.Dispose();
    }

    // Inserts a bare Application row so TrySaveAsync's ConditionCheck (attribute_exists(Id)) passes.
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

    private static Precondition CreatePrecondition(
        Guid organizationId,
        Guid applicationId,
        Guid? id = null
    ) =>
        new()
        {
            Id = id ?? Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Name = "Order Confirmation ID",
            ValueSource = PreconditionValueSource.PriorActivity,
            ExampleValue = "ORD-12345",
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public async Task TrySaveAsync_WhenApplicationExists_RoundTripsThroughGetById()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var precondition = CreatePrecondition(organizationId, applicationId);

        // test
        var saved = await _repository.TrySaveAsync(precondition);
        var result = await _repository.GetByIdAsync(organizationId, precondition.Id);

        // verify
        Assert.True(saved);
        Assert.Equal(precondition, result);
    }

    [Fact]
    public async Task TrySaveAsync_WhenApplicationDoesNotExist_ReturnsFalse()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var precondition = CreatePrecondition(organizationId, applicationId);

        // test
        var saved = await _repository.TrySaveAsync(precondition);

        // verify
        Assert.False(saved);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenCalled_UpdatesOnlyAllowedFields()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var precondition = CreatePrecondition(organizationId, applicationId);
        await _repository.TrySaveAsync(precondition);

        var updatedByUserId = Guid.CreateVersion7();
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var fields = new PreconditionUpdatableFields
        {
            Name = "Updated Name",
            ValueSource = PreconditionValueSource.SpecificValue,
            ExampleValue = "updated-value",
            UpdatedByUserId = updatedByUserId,
            UpdatedAt = updatedAt,
        };

        // test
        var succeeded = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            precondition.Id,
            fields
        );
        var result = await _repository.GetByIdAsync(organizationId, precondition.Id);

        // verify
        Assert.True(succeeded);
        Assert.Equal(
            precondition with
            {
                Name = fields.Name,
                ValueSource = fields.ValueSource,
                ExampleValue = fields.ExampleValue,
                UpdatedByUserId = fields.UpdatedByUserId,
                UpdatedAt = fields.UpdatedAt,
            },
            result
        );
    }

    [Fact]
    public async Task TryUpdateAsync_WhenPreconditionDoesNotExist_ReturnsFalseAndDoesNotCreateIt()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        // test
        var succeeded = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            id,
            new PreconditionUpdatableFields
            {
                Name = "Updated Name",
                ValueSource = PreconditionValueSource.SpecificValue,
                ExampleValue = "updated-value",
                UpdatedByUserId = Guid.CreateVersion7(),
                UpdatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );
        var result = await _repository.GetByIdAsync(organizationId, id);

        // verify
        Assert.False(succeeded);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenExists_RemovesPrecondition()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var precondition = CreatePrecondition(organizationId, applicationId);
        await _repository.TrySaveAsync(precondition);

        // test
        await _repository.DeleteAsync(organizationId, precondition.Id);
        var result = await _repository.GetByIdAsync(organizationId, precondition.Id);

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenNotFound_DoesNothing()
    {
        // setup
        var organizationId = Guid.CreateVersion7();

        // test
        var exception = await Record.ExceptionAsync(() =>
            _repository.DeleteAsync(organizationId, Guid.CreateVersion7())
        );

        // verify
        Assert.Null(exception);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ReturnsNull()
    {
        // setup
        var organizationId = Guid.CreateVersion7();

        // test
        var result = await _repository.GetByIdAsync(organizationId, Guid.CreateVersion7());

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenMultiplePreconditionsExist_ReturnsAllScopedToOrganizationAndApplication()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var first = CreatePrecondition(organizationId, applicationId);
        var second = CreatePrecondition(organizationId, applicationId);
        await _repository.TrySaveAsync(first);
        await _repository.TrySaveAsync(second);

        // Belongs to a different Organization -- must never appear in the first Organization's list.
        var otherOrganizationId = Guid.CreateVersion7();
        var otherApplicationId = Guid.CreateVersion7();
        await PutApplicationAsync(otherOrganizationId, otherApplicationId);
        await _repository.TrySaveAsync(CreatePrecondition(otherOrganizationId, otherApplicationId));

        // test
        var result = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify
        Assert.Equivalent(new[] { first, second }, result);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenNoPreconditionsExist_ReturnsEmpty()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();

        // test
        var result = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify
        Assert.Empty(result);
    }
}
