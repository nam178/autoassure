using System.Globalization;
using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using TestDynamo;

namespace A2.Server.Tests;

/// <summary>Integration tests for <see cref="DynamoDbRefreshTokenRepository"/> against an in-memory
/// TestDynamo database (no Docker/JVM required), covering read/write mapping only — TestDynamo doesn't
/// faithfully emulate conditional-write semantics, so the atomic-revoke race is covered by the fakes in
/// <see cref="AuthTokenServiceTests"/> instead.</summary>
public sealed class DynamoDbRefreshTokenRepositoryTests : IAsyncLifetime
{
    private const string TableName = "RefreshTokens";

    private AmazonDynamoDBClient client = null!;
    private DynamoDbRefreshTokenRepository repository = null!;

    public async Task InitializeAsync()
    {
        client = TestDynamoClient.CreateClient<AmazonDynamoDBClient>();
        repository = new DynamoDbRefreshTokenRepository(
            client,
            Options.Create(new DynamoDbOptions { RefreshTokenTableName = TableName })
        );

        await client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = TableName,
                KeySchema = [new KeySchemaElement("RefreshTokenSecretHash", KeyType.HASH)],
                AttributeDefinitions =
                [
                    new AttributeDefinition("RefreshTokenSecretHash", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    public Task DisposeAsync()
    {
        client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task AddAsync_ThenGetByHashAsync_RoundTripsAllFields()
    {
        var token = new RefreshToken(
            "hash-1",
            "google-user-1",
            "user@example.com",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null
        );

        await repository.AddAsync(token);
        var result = await repository.GetByHashAsync("hash-1");

        Assert.Equal(token, result);
    }

    [Fact]
    public async Task GetByHashAsync_ReturnsNull_WhenNotFound()
    {
        var result = await repository.GetByHashAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task AddAsync_ThenGetByHashAsync_RoundTripsRevokedAt()
    {
        var token = new RefreshToken(
            "hash-2",
            "google-user-2",
            "revoked@example.com",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)
        );

        await client.PutItemAsync(
            new PutItemRequest
            {
                TableName = TableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["RefreshTokenSecretHash"] = new(token.RefreshTokenSecretHash),
                    ["GoogleUserId"] = new(token.GoogleUserId),
                    ["Email"] = new(token.Email),
                    ["ExpiresAt"] = new()
                    {
                        N = token
                            .ExpiresAt.ToUnixTimeSeconds()
                            .ToString(CultureInfo.InvariantCulture),
                    },
                    ["CreatedAt"] = new(token.CreatedAt.ToString("O")),
                    ["RevokedAt"] = new(token.RevokedAt!.Value.ToString("O")),
                },
            }
        );

        var result = await repository.GetByHashAsync("hash-2");

        Assert.Equal(token, result);
    }
}
