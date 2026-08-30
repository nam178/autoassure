using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Environment = A2.Server.Models.Environment;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbEnvironmentRepository"/> against DynamoDB Local,
/// covering read/write mapping only.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbEnvironmentRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string EnvironmentTableName = "Environments";
    private const string ApplicationTableName = "Applications";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbEnvironmentRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbEnvironmentRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    EnvironmentTableName = EnvironmentTableName,
                    ApplicationTableName = ApplicationTableName,
                }
            )
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
    }

    public async Task DisposeAsync()
    {
        await _client.DeleteTableAsync(EnvironmentTableName);
        await _client.DeleteTableAsync(ApplicationTableName);
        _client.Dispose();
    }

    // Puts a bare Application row directly (bypassing the Application repository/mapper) so the
    // Environment repository's cross-entity ConditionCheck finds it.
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

    [Fact]
    public async Task TrySaveAsync_WhenApplicationExists_ReturnsTrueAndPersists()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var environment = new Environment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Name = "Staging",
            Classification = EnvironmentClassification.NonProduction,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        // test
        var saved = await _repository.TrySaveAsync(environment);
        var result = await _repository.GetByIdAsync(organizationId, environment.Id);

        // verify
        Assert.True(saved);
        Assert.Equal(environment, result);
    }

    [Fact]
    public async Task TrySaveAsync_WhenApplicationDoesNotExist_ReturnsFalse()
    {
        // setup
        var environment = new Environment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = Guid.CreateVersion7(),
            ApplicationId = Guid.CreateVersion7(),
            Name = "Orphaned Environment",
            Classification = EnvironmentClassification.NonProduction,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        // test
        var saved = await _repository.TrySaveAsync(environment);

        // verify
        Assert.False(saved);
    }

    [Fact]
    public async Task UpdateAsync_WhenEnvironmentExists_UpdatesNameClassificationAndAuditFields()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var environment = new Environment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Name = "Staging",
            Classification = EnvironmentClassification.NonProduction,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        await _repository.TrySaveAsync(environment);
        var updatedByUserId = Guid.CreateVersion7();
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        // test
        await _repository.UpdateAsync(
            organizationId,
            applicationId,
            environment.Id,
            new EnvironmentUpdatableFields
            {
                Name = "Production",
                Classification = EnvironmentClassification.Production,
                UpdatedByUserId = updatedByUserId,
                UpdatedAt = updatedAt,
            }
        );
        var result = await _repository.GetByIdAsync(organizationId, environment.Id);

        // verify
        Assert.Equal(
            environment with
            {
                Name = "Production",
                Classification = EnvironmentClassification.Production,
                UpdatedByUserId = updatedByUserId,
                UpdatedAt = updatedAt,
            },
            result
        );
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
    public async Task ListByApplicationAsync_WhenMultipleEnvironmentsExist_ReturnsOnlyThoseScopedToApplication()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var otherApplicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutApplicationAsync(organizationId, otherApplicationId);

        var firstEnvironment = new Environment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Name = "Staging",
            Classification = EnvironmentClassification.NonProduction,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var secondEnvironment = new Environment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Name = "Production",
            Classification = EnvironmentClassification.Production,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        };
        var otherApplicationEnvironment = new Environment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = otherApplicationId,
            Name = "Dev",
            Classification = EnvironmentClassification.NonProduction,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
        };
        await _repository.TrySaveAsync(firstEnvironment);
        await _repository.TrySaveAsync(secondEnvironment);
        await _repository.TrySaveAsync(otherApplicationEnvironment);

        // test
        var result = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify
        Assert.Equivalent(new[] { firstEnvironment, secondEnvironment }, result);
    }
}
