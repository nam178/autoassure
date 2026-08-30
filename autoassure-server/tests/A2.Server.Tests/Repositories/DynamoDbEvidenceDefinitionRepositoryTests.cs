using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbEvidenceDefinitionRepository"/> against DynamoDB
/// Local, covering read/write mapping only.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbEvidenceDefinitionRepositoryTests(
    DynamoDbLocalFixture dynamoDbLocalFixture
) : IAsyncLifetime
{
    private const string TableName = "EvidenceDefinitions";
    private const string ApplicationTableName = "Applications";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbEvidenceDefinitionRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbEvidenceDefinitionRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    EvidenceDefinitionTableName = TableName,
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

    // Puts an Application row directly so TrySaveAsync's ConditionCheck against the Applications table
    // succeeds without depending on IApplicationRepository.
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

    private static EvidenceDefinition CreateEvidenceDefinition(
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
            Description = "The confirmation ID returned after placing an order.",
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
        var evidence = CreateEvidenceDefinition(organizationId, applicationId);

        // test
        var saved = await _repository.TrySaveAsync(evidence);
        var result = await _repository.GetByIdAsync(organizationId, evidence.Id);

        // verify
        Assert.True(saved);
        Assert.Equal(evidence, result);
    }

    [Fact]
    public async Task TrySaveAsync_WhenApplicationDoesNotExist_ReturnsFalse()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var evidence = CreateEvidenceDefinition(organizationId, applicationId);

        // test
        var saved = await _repository.TrySaveAsync(evidence);

        // verify
        Assert.False(saved);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenCalled_UpdatesOnlyUpdatableFields()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var evidence = CreateEvidenceDefinition(organizationId, applicationId);
        await _repository.TrySaveAsync(evidence);

        var updatedByUserId = Guid.CreateVersion7();
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var fields = new EvidenceDefinitionUpdatableFields
        {
            Name = "Updated Name",
            Description = "Updated description.",
            ExampleValue = "ORD-99999",
            UpdatedByUserId = updatedByUserId,
            UpdatedAt = updatedAt,
        };

        // test
        var succeeded = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            evidence.Id,
            fields
        );
        var result = await _repository.GetByIdAsync(organizationId, evidence.Id);

        // verify
        Assert.True(succeeded);
        Assert.Equal(
            evidence with
            {
                Name = fields.Name,
                Description = fields.Description,
                ExampleValue = fields.ExampleValue,
                UpdatedByUserId = fields.UpdatedByUserId,
                UpdatedAt = fields.UpdatedAt,
            },
            result
        );
    }

    [Fact]
    public async Task TryUpdateAsync_WhenEvidenceDefinitionDoesNotExist_ReturnsFalseAndDoesNotCreateIt()
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
            new EvidenceDefinitionUpdatableFields
            {
                Name = "Updated Name",
                Description = "Updated description.",
                ExampleValue = "ORD-99999",
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
    public async Task DeleteAsync_WhenEvidenceDefinitionExists_RemovesIt()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var evidence = CreateEvidenceDefinition(organizationId, applicationId);
        await _repository.TrySaveAsync(evidence);

        // test
        await _repository.DeleteAsync(organizationId, evidence.Id);
        var result = await _repository.GetByIdAsync(organizationId, evidence.Id);

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_WhenEvidenceDefinitionDoesNotExist_DoesNothing()
    {
        // setup
        var organizationId = Guid.CreateVersion7();

        // test / verify — no exception expected.
        await _repository.DeleteAsync(organizationId, Guid.CreateVersion7());
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
    public async Task GetByIdAsync_WhenScopedToDifferentOrganization_ReturnsNull()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var otherOrganizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        var evidence = CreateEvidenceDefinition(organizationId, applicationId);
        await _repository.TrySaveAsync(evidence);

        // test
        var result = await _repository.GetByIdAsync(otherOrganizationId, evidence.Id);

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenMultipleEvidenceDefinitionsExist_ReturnsOnlyThoseForApplication()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var otherApplicationId = Guid.CreateVersion7();
        await PutApplicationAsync(organizationId, applicationId);
        await PutApplicationAsync(organizationId, otherApplicationId);

        var evidence1 = CreateEvidenceDefinition(organizationId, applicationId);
        var evidence2 = CreateEvidenceDefinition(organizationId, applicationId);
        var otherEvidence = CreateEvidenceDefinition(organizationId, otherApplicationId);
        await _repository.TrySaveAsync(evidence1);
        await _repository.TrySaveAsync(evidence2);
        await _repository.TrySaveAsync(otherEvidence);

        // test
        var result = await _repository.ListByApplicationAsync(organizationId, applicationId);

        // verify
        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Id == evidence1.Id);
        Assert.Contains(result, item => item.Id == evidence2.Id);
    }

    [Fact]
    public async Task ListByApplicationAsync_WhenNoEvidenceDefinitionsExist_ReturnsEmpty()
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
