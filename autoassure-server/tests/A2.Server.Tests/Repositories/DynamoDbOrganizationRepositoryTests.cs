using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbOrganizationRepository"/> against DynamoDB Local,
/// covering read/write mapping only.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbOrganizationRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string TableName = "Organizations";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbOrganizationRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbOrganizationRepository(
            _client,
            Options.Create(new DynamoDbOptions { OrganizationTableName = TableName })
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = TableName,
                KeySchema = [new KeySchemaElement("Id", KeyType.HASH)],
                AttributeDefinitions = [new AttributeDefinition("Id", ScalarAttributeType.S)],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    public async Task DisposeAsync()
    {
        await _client.DeleteTableAsync(TableName);
        _client.Dispose();
    }

    [Fact]
    public async Task GetByIdAsync_WhenOrganizationExists_ReturnsIt()
    {
        // setup
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = "Acme Corp",
            IsPersonal = false,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        await _client.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = organization.ToDynamoDbRow() }
        );

        // test
        var result = await _repository.GetByIdAsync(organization.Id);

        // verify
        Assert.Equal(organization, result);
    }

    [Fact]
    public async Task GetByIdAsync_WhenOrganizationIsPersonal_RoundTripsIsPersonal()
    {
        // setup
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = "Personal Org",
            IsPersonal = true,
            CreatedByUserId = Guid.CreateVersion7(),
            UpdatedByUserId = Guid.CreateVersion7(),
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        await _client.PutItemAsync(
            new PutItemRequest { TableName = TableName, Item = organization.ToDynamoDbRow() }
        );

        // test
        var result = await _repository.GetByIdAsync(organization.Id);

        // verify
        Assert.Equal(organization, result);
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ReturnsNull()
    {
        // test
        var result = await _repository.GetByIdAsync(Guid.CreateVersion7());

        // verify
        Assert.Null(result);
    }
}
