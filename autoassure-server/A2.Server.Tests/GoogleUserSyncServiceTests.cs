using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using A2.Server.Services;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using TestDynamo;

namespace A2.Server.Tests;

/// <summary>Integration tests for <see cref="GoogleUserSyncService"/> against an in-memory TestDynamo
/// database (no Docker/JVM required), covering how it syncs a <see cref="User"/> from a
/// <see cref="GoogleIdentity"/> through the real <see cref="DynamoDbUserRepository"/>.</summary>
public sealed class GoogleUserSyncServiceTests : IAsyncLifetime
{
    private const string TableName = "Users";

    private AmazonDynamoDBClient _client = null!;
    private GoogleUserSyncService _service = null!;

    public async Task InitializeAsync()
    {
        _client = TestDynamoClient.CreateClient<AmazonDynamoDBClient>();
        _service = new GoogleUserSyncService(
            new DynamoDbUserRepository(
                _client,
                Options.Create(new DynamoDbOptions { UserTableName = TableName })
            )
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = TableName,
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
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SyncAsync_CreatesUser_OnFirstSignIn()
    {
        var identity = new GoogleIdentity(
            "google-1",
            "alice@gmail.com",
            true,
            "Alice",
            "Anderson",
            null
        );

        var user = await _service.SyncAsync(identity);

        Assert.Equal("google-1", user.GoogleUserId);
        Assert.Equal("alice@gmail.com", user.Email);
        Assert.Equal("Alice", user.FirstName);
        Assert.True(user.EmailVerified);
    }

    [Fact]
    public async Task SyncAsync_UpdatesEmail_WhenGoogleAccountEmailChanged()
    {
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
        var updatedUser = await _service.SyncAsync(laterSignIn);

        Assert.Equal(originalUser.Id, updatedUser.Id);
        Assert.Equal("alice.new@gmail.com", updatedUser.Email);
    }
}
