using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using A2.Server.Services;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests;

/// <summary>Integration tests for <see cref="GoogleUserSyncService"/> against DynamoDB Local, covering how
/// it syncs a <see cref="User"/> from a <see cref="GoogleIdentity"/> through the real
/// <see cref="DynamoDbUserRepository"/>, and how it provisions a personal <see cref="Organization"/> on
/// first sign-in.</summary>
[Collection("DynamoDbLocal")]
public sealed class GoogleUserSyncServiceTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string UserTableName = "Users";
    private const string OrganizationTableName = "Organizations";
    private const string OrganizationUserTableName = "OrganizationUsers";

    private AmazonDynamoDBClient _client = null!;
    private GoogleUserSyncService _service = null!;
    private DynamoDbOrganizationRepository _organizationRepository = null!;
    private DynamoDbOrganizationUserRepository _organizationUserRepository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        var options = Options.Create(
            new DynamoDbOptions
            {
                UserTableName = UserTableName,
                OrganizationTableName = OrganizationTableName,
                OrganizationUserTableName = OrganizationUserTableName,
            }
        );
        _organizationRepository = new DynamoDbOrganizationRepository(_client, options);
        _organizationUserRepository = new DynamoDbOrganizationUserRepository(_client, options);
        _service = new GoogleUserSyncService(
            new DynamoDbUserRepository(_client, options),
            _organizationUserRepository,
            new SystemClock()
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

    [Fact]
    public async Task SyncAsync_WhenFirstSignIn_CreatesUser()
    {
        // setup
        var identity = new GoogleIdentity(
            "google-1",
            "alice@gmail.com",
            true,
            "Alice",
            "Anderson",
            null
        );

        // test
        var user = await _service.SyncAsync(identity);

        // verify
        Assert.Equal("google-1", user.GoogleUserId);
        Assert.Equal("alice@gmail.com", user.Email);
        Assert.Equal("Alice", user.FirstName);
        Assert.True(user.EmailVerified);
    }

    [Fact]
    public async Task SyncAsync_WhenGoogleAccountEmailChanged_UpdatesEmail()
    {
        // setup
        var firstSignIn = new GoogleIdentity(
            "google-2",
            "alice@gmail.com",
            true,
            "Alice",
            "Anderson",
            null
        );
        var originalUser = await _service.SyncAsync(firstSignIn);
        var laterSignIn = firstSignIn with { Email = "alice.new@gmail.com" };

        // test
        var updatedUser = await _service.SyncAsync(laterSignIn);

        // verify
        Assert.Equal(originalUser.Id, updatedUser.Id);
        Assert.Equal("alice.new@gmail.com", updatedUser.Email);
    }

    [Fact]
    public async Task SyncAsync_WhenFirstSignIn_CreatesExactlyOnePersonalOrganizationAndOwnerMembership()
    {
        // setup
        var identity = new GoogleIdentity("google-3", "bob@gmail.com", true, "Bob", "Baker", null);

        // test
        var user = await _service.SyncAsync(identity);

        // verify
        var memberships = await _organizationUserRepository.ListByUserAsync(user.Id);
        var membership = Assert.Single(memberships);
        Assert.Equal(OrganizationRole.Owner, membership.Role);
        Assert.Equal(user.Id, membership.CreatedByUserId);

        var organization = await _organizationRepository.GetByIdAsync(membership.OrganizationId);
        Assert.NotNull(organization);
        Assert.True(organization.IsPersonal);
    }

    [Fact]
    public async Task SyncAsync_WhenSignInTwice_DoesNotCreateDuplicatePersonalOrganization()
    {
        // setup
        var identity = new GoogleIdentity(
            "google-4",
            "carol@gmail.com",
            true,
            "Carol",
            "Clark",
            null
        );
        var user = await _service.SyncAsync(identity);

        // test
        await _service.SyncAsync(identity);

        // verify
        var memberships = await _organizationUserRepository.ListByUserAsync(user.Id);
        Assert.Single(memberships);
    }

    [Fact]
    public async Task SyncAsync_WhenUserExistsWithoutPersonalOrganization_BackfillsOne()
    {
        // setup: simulate a User who was created but whose personal Organization creation never
        // completed (e.g. a crash between the two steps).
        var identity = new GoogleIdentity("google-5", "dave@gmail.com", true, "Dave", "Diaz", null);
        var userRepository = new DynamoDbUserRepository(
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
        var orphanedUser = new User
        {
            Id = Guid.CreateVersion7(),
            GoogleUserId = identity.GoogleUserId,
            FirstName = "Dave",
            LastName = "Diaz",
            Email = "dave@gmail.com",
            EmailVerified = true,
        };
        await userRepository.TrySaveAsync(orphanedUser);

        // test
        var user = await _service.SyncAsync(identity);

        // verify
        Assert.Equal(orphanedUser.Id, user.Id);
        var memberships = await _organizationUserRepository.ListByUserAsync(user.Id);
        var membership = Assert.Single(memberships);
        Assert.Equal(OrganizationRole.Owner, membership.Role);
    }
}
