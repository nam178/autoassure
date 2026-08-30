using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbOrganizationUserRepository"/> against DynamoDB
/// Local, covering read/write mapping only.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbOrganizationUserRepositoryTests(
    DynamoDbLocalFixture dynamoDbLocalFixture
) : IAsyncLifetime
{
    private const string TableName = "OrganizationUsers";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbOrganizationUserRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbOrganizationUserRepository(
            _client,
            Options.Create(new DynamoDbOptions { OrganizationUserTableName = TableName })
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = TableName,
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId", KeyType.HASH),
                    new KeySchemaElement("UserId", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId", ScalarAttributeType.S),
                    new AttributeDefinition("UserId", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "UserIdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("UserId", KeyType.HASH),
                            new KeySchemaElement("OrganizationId", KeyType.RANGE),
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
        _client.Dispose();
    }

    private static OrganizationUser CreateMembership(
        Guid organizationId,
        Guid userId,
        OrganizationRole role = OrganizationRole.Owner
    ) =>
        new()
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public async Task SaveAsync_WhenMembershipHasAllFields_RoundTripsThroughListByUser()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var membership = CreateMembership(organizationId, userId);

        // test
        await _repository.SaveAsync(membership);
        var result = await _repository.ListByUserAsync(userId);

        // verify
        Assert.Equal([membership], result);
    }

    [Fact]
    public async Task SaveAsync_WhenMembershipIsMemberRole_RoundTripsRole()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var membership = CreateMembership(organizationId, userId, OrganizationRole.Member);

        // test
        await _repository.SaveAsync(membership);
        var result = await _repository.ListByUserAsync(userId);

        // verify
        Assert.Equal([membership], result);
    }

    [Fact]
    public async Task ListByUserAsync_WhenUserBelongsToMultipleOrganizations_ReturnsAllMemberships()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var membership1 = CreateMembership(Guid.CreateVersion7(), userId);
        var membership2 = CreateMembership(Guid.CreateVersion7(), userId);
        await _repository.SaveAsync(membership1);
        await _repository.SaveAsync(membership2);

        // test
        var result = await _repository.ListByUserAsync(userId);

        // verify
        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.OrganizationId == membership1.OrganizationId);
        Assert.Contains(result, item => item.OrganizationId == membership2.OrganizationId);
    }

    [Fact]
    public async Task ListByUserAsync_WhenUserHasNoMemberships_ReturnsEmpty()
    {
        // test
        var result = await _repository.ListByUserAsync(Guid.CreateVersion7());

        // verify
        Assert.Empty(result);
    }

    [Fact]
    public async Task ListByUserAsync_WhenAnotherUserHasMembershipInSameOrganization_ExcludesIt()
    {
        // setup
        var organizationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var otherUserId = Guid.CreateVersion7();
        var membership = CreateMembership(organizationId, userId);
        var otherMembership = CreateMembership(organizationId, otherUserId);
        await _repository.SaveAsync(membership);
        await _repository.SaveAsync(otherMembership);

        // test
        var result = await _repository.ListByUserAsync(userId);

        // verify
        Assert.Equal([membership], result);
    }
}
