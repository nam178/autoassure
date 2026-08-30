using A2.Server.Common;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbEnvironmentVariableRepository"/> against DynamoDB
/// Local, covering read/write mapping only.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbEnvironmentVariableRepositoryTests(
    DynamoDbLocalFixture dynamoDbLocalFixture
) : IAsyncLifetime
{
    private const string EnvironmentVariableTableName = "EnvironmentVariables";
    private const string EnvironmentTableName = "Environments";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbEnvironmentVariableRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbEnvironmentVariableRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    EnvironmentVariableTableName = EnvironmentVariableTableName,
                    EnvironmentTableName = EnvironmentTableName,
                }
            )
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = EnvironmentVariableTableName,
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId_EnvironmentId", KeyType.HASH),
                    new KeySchemaElement("Key", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_EnvironmentId", ScalarAttributeType.S),
                    new AttributeDefinition("Key", ScalarAttributeType.S),
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
        await _client.DeleteTableAsync(EnvironmentVariableTableName);
        await _client.DeleteTableAsync(EnvironmentTableName);
        _client.Dispose();
    }

    // Puts a bare Environment row directly (bypassing the Environment repository/mapper) so the
    // EnvironmentVariable repository's cross-entity ConditionCheck finds it.
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

    [Fact]
    public async Task TryUpdateAsync_WhenEnvironmentExists_CreatesRowWithCreatedAndUpdatedFields()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var updatedByUserId = Guid.CreateVersion7();

        // test
        var updated = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            environmentId,
            "API_BASE_URL",
            "https://staging.example.com",
            updatedByUserId
        );
        var result = await _repository.ListByEnvironmentAsync(organizationId, environmentId);

        // verify
        Assert.True(updated);
        var variable = Assert.Single(result);
        Assert.Equal(organizationId, variable.OrganizationId);
        Assert.Equal(environmentId, variable.EnvironmentId);
        Assert.Equal("API_BASE_URL", variable.Key);
        Assert.Equal("https://staging.example.com", variable.Value);
        Assert.Equal(updatedByUserId, variable.CreatedByUserId);
        Assert.Equal(updatedByUserId, variable.UpdatedByUserId);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenVariableAlreadyExists_UpdatesValueButKeepsCreatedFields()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        var originalUserId = Guid.CreateVersion7();
        await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            environmentId,
            "API_BASE_URL",
            "https://staging.example.com",
            originalUserId
        );
        var newUserId = Guid.CreateVersion7();

        // test
        var updated = await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            environmentId,
            "API_BASE_URL",
            "https://staging2.example.com",
            newUserId
        );
        var result = await _repository.ListByEnvironmentAsync(organizationId, environmentId);

        // verify
        Assert.True(updated);
        var variable = Assert.Single(result);
        Assert.Equal("https://staging2.example.com", variable.Value);
        Assert.Equal(originalUserId, variable.CreatedByUserId);
        Assert.Equal(newUserId, variable.UpdatedByUserId);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenEnvironmentDoesNotExist_ReturnsFalse()
    {
        // test
        var updated = await _repository.TryUpdateAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "API_BASE_URL",
            "https://staging.example.com",
            Guid.CreateVersion7()
        );

        // verify
        Assert.False(updated);
    }

    [Fact]
    public async Task DeleteAsync_WhenVariableExists_RemovesRow()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            environmentId,
            "API_BASE_URL",
            "https://staging.example.com",
            Guid.CreateVersion7()
        );

        // test
        await _repository.DeleteAsync(organizationId, environmentId, "API_BASE_URL");
        var result = await _repository.ListByEnvironmentAsync(organizationId, environmentId);

        // verify
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListByEnvironmentAsync_WhenMultipleVariablesExist_ReturnsSortedByKeyScopedToEnvironment()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var applicationId = Guid.CreateVersion7();
        var environmentId = Guid.CreateVersion7();
        var otherEnvironmentId = Guid.CreateVersion7();
        await PutEnvironmentAsync(organizationId, applicationId, environmentId);
        await PutEnvironmentAsync(organizationId, applicationId, otherEnvironmentId);
        var updatedByUserId = Guid.CreateVersion7();

        await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            environmentId,
            "ZOO_KEY",
            "z-value",
            updatedByUserId
        );
        await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            environmentId,
            "API_KEY",
            "a-value",
            updatedByUserId
        );
        await _repository.TryUpdateAsync(
            organizationId,
            applicationId,
            otherEnvironmentId,
            "API_KEY",
            "other-env-value",
            updatedByUserId
        );

        // test
        var result = await _repository.ListByEnvironmentAsync(organizationId, environmentId);

        // verify
        Assert.Equal(["API_KEY", "ZOO_KEY"], result.Select(variable => variable.Key));
        Assert.All(result, variable => Assert.Equal(environmentId, variable.EnvironmentId));
    }
}
