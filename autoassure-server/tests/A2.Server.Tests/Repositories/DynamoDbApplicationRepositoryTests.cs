using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbApplicationRepository"/> against DynamoDB Local,
/// covering read/write mapping only.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbApplicationRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string ApplicationTableName = "Applications";
    private const string OrganizationTableName = "Organizations";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbApplicationRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbApplicationRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    ApplicationTableName = ApplicationTableName,
                    OrganizationTableName = OrganizationTableName,
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
                TableName = OrganizationTableName,
                KeySchema = [new KeySchemaElement("Id", KeyType.HASH)],
                AttributeDefinitions = [new AttributeDefinition("Id", ScalarAttributeType.S)],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    public async Task DisposeAsync()
    {
        await _client.DeleteTableAsync(ApplicationTableName);
        await _client.DeleteTableAsync(OrganizationTableName);
        _client.Dispose();
    }

    // Puts a bare Organization row directly (bypassing the Organization repository/mapper) so the
    // Application repository's cross-entity ConditionCheck finds it.
    private Task PutOrganizationAsync(Guid organizationId) =>
        _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = OrganizationTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new(organizationId.ToString()),
                },
            }
        );

    [Fact]
    public async Task TrySaveAsync_WhenOrganizationExists_ReturnsTrueAndPersists()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        await PutOrganizationAsync(organizationId);
        var application = new Application
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Name = "Checkout API",
            Description = "Public checkout service",
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        // test
        var saved = await _repository.TrySaveAsync(application);
        var result = await _repository.GetByIdAsync(organizationId, application.Id);

        // verify
        Assert.True(saved);
        Assert.Equal(application, result);
    }

    [Fact]
    public async Task TrySaveAsync_WhenOrganizationDoesNotExist_ReturnsFalse()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var application = new Application
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Name = "Orphaned App",
            Description = "Belongs to no Organization",
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        // test
        var saved = await _repository.TrySaveAsync(application);

        // verify
        Assert.False(saved);
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
    public async Task ListByOrganizationAsync_WhenMultipleApplicationsExist_ReturnsOnlyThoseScopedToOrganization()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var otherOrganizationId = Guid.CreateVersion7();
        await PutOrganizationAsync(organizationId);
        await PutOrganizationAsync(otherOrganizationId);

        var firstApplication = new Application
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Name = "Checkout API",
            Description = "Public checkout service",
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var secondApplication = new Application
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Name = "Billing API",
            Description = "Internal billing service",
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        };
        var otherOrganizationApplication = new Application
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = otherOrganizationId,
            Name = "Someone Else's API",
            Description = "Belongs to a different tenant",
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
        };
        await _repository.TrySaveAsync(firstApplication);
        await _repository.TrySaveAsync(secondApplication);
        await _repository.TrySaveAsync(otherOrganizationApplication);

        // test
        var result = await _repository.ListByOrganizationAsync(organizationId);

        // verify
        Assert.Equivalent(new[] { firstApplication, secondApplication }, result);
    }
}
