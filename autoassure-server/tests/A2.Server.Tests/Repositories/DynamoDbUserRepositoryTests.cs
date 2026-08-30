using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbUserRepository"/> against DynamoDB Local,
/// covering read/write mapping only — the concurrent-first-sign-in and concurrent-personal-Organization
/// races are covered elsewhere with fakes.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbUserRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string UserTableName = "Users";
    private const string OrganizationTableName = "Organizations";
    private const string OrganizationUserTableName = "OrganizationUsers";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbUserRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbUserRepository(
            _client,
            Options.Create(
                new DynamoDbOptions
                {
                    UserTableName = UserTableName,
                    OrganizationTableName = OrganizationTableName,
                    OrganizationUserTableName = OrganizationUserTableName,
                }
            )
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = UserTableName,
                KeySchema = [new KeySchemaElement("Id", KeyType.HASH)],
                AttributeDefinitions =
                [
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition("GoogleUserId", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "GoogleUserIdIndex",
                        KeySchema = [new KeySchemaElement("GoogleUserId", KeyType.HASH)],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL },
                    },
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

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = OrganizationUserTableName,
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
        foreach (
            var tableName in new[]
            {
                UserTableName,
                OrganizationTableName,
                OrganizationUserTableName,
            }
        )
        {
            await _client.DeleteTableAsync(tableName);
        }
        _client.Dispose();
    }

    private static User CreateUser(string googleUserId = "google-1") =>
        new()
        {
            Id = Guid.CreateVersion7(),
            GoogleUserId = googleUserId,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            EmailVerified = true,
        };

    [Fact]
    public async Task GetByGoogleUserIdAsync_WhenUserExists_ReturnsUser()
    {
        // setup
        var user = CreateUser();
        await _repository.TrySaveAsync(user);

        // test
        var result = await _repository.GetByGoogleUserIdAsync(user.GoogleUserId);

        // verify
        Assert.Equal(user, result);
    }

    [Fact]
    public async Task GetByGoogleUserIdAsync_WhenNotFound_ReturnsNull()
    {
        // test
        var result = await _repository.GetByGoogleUserIdAsync("does-not-exist");

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task TrySaveAsync_WhenGoogleUserIdIsNew_SavesUserAndReturnsTrue()
    {
        // setup
        var user = CreateUser();

        // test
        var result = await _repository.TrySaveAsync(user);
        var fetched = await _repository.GetByGoogleUserIdAsync(user.GoogleUserId);

        // verify
        Assert.True(result);
        Assert.Equal(user, fetched);
    }

    [Fact]
    public async Task TrySaveAsync_WhenGoogleUserIdWasAlreadySynced_ReturnsFalse()
    {
        // setup
        var user = CreateUser();
        await _repository.TrySaveAsync(user);
        var secondUser = CreateUser(user.GoogleUserId);

        // test
        var result = await _repository.TrySaveAsync(secondUser);

        // verify
        Assert.False(result);
    }

    [Fact]
    public async Task UpdateAsync_WhenUserExists_UpdatesEditableFieldsOnly()
    {
        // setup
        var user = CreateUser();
        await _repository.TrySaveAsync(user);
        var fields = new UserUpdatableFields
        {
            FirstName = "Grace",
            LastName = "Hopper",
            Email = "grace@example.com",
            EmailVerified = false,
        };

        // test
        await _repository.UpdateAsync(user.Id, fields);
        var fetched = await _repository.GetByGoogleUserIdAsync(user.GoogleUserId);

        // verify
        Assert.Equal(
            user with
            {
                FirstName = "Grace",
                LastName = "Hopper",
                Email = "grace@example.com",
                EmailVerified = false,
            },
            fetched
        );
    }

    [Fact]
    public async Task TryCreatePersonalOrganizationAsync_WhenNotYetCreated_SavesOrganizationAndMembershipAndReturnsTrue()
    {
        // setup
        var user = CreateUser();
        await _repository.TrySaveAsync(user);
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = "Ada's Organization",
            IsPersonal = true,
            CreatedByUserId = user.Id,
            UpdatedByUserId = user.Id,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var membership = new OrganizationUser
        {
            OrganizationId = organization.Id,
            UserId = user.Id,
            Role = OrganizationRole.Owner,
            CreatedByUserId = user.Id,
            UpdatedByUserId = user.Id,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };

        // test
        var result = await _repository.TryCreatePersonalOrganizationAsync(organization, membership);

        // verify
        Assert.True(result);
        var organizationItem = await _client.GetItemAsync(
            OrganizationTableName,
            new Dictionary<string, AttributeValue> { ["Id"] = new(organization.Id.ToString()) }
        );
        Assert.Equal(organization, organizationItem.Item.ToOrganization());
        var membershipItem = await _client.GetItemAsync(
            OrganizationUserTableName,
            new Dictionary<string, AttributeValue>
            {
                ["OrganizationId"] = new(organization.Id.ToString()),
                ["UserId"] = new(user.Id.ToString()),
            }
        );
        Assert.Equal(membership, membershipItem.Item.ToOrganizationUser());
    }

    [Fact]
    public async Task TryCreatePersonalOrganizationAsync_WhenAlreadyCreatedForUser_ReturnsFalse()
    {
        // setup
        var user = CreateUser();
        await _repository.TrySaveAsync(user);
        var firstOrganization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = "Ada's Organization",
            IsPersonal = true,
            CreatedByUserId = user.Id,
            UpdatedByUserId = user.Id,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var firstMembership = new OrganizationUser
        {
            OrganizationId = firstOrganization.Id,
            UserId = user.Id,
            Role = OrganizationRole.Owner,
            CreatedByUserId = user.Id,
            UpdatedByUserId = user.Id,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        await _repository.TryCreatePersonalOrganizationAsync(firstOrganization, firstMembership);

        var secondOrganization = firstOrganization with { Id = Guid.CreateVersion7() };
        var secondMembership = firstMembership with { OrganizationId = secondOrganization.Id };

        // test
        var result = await _repository.TryCreatePersonalOrganizationAsync(
            secondOrganization,
            secondMembership
        );

        // verify
        Assert.False(result);
    }
}
