using System.Globalization;
using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests.Repositories;

/// <summary>Integration tests for <see cref="DynamoDbRefreshTokenRepository"/> against DynamoDB Local,
/// covering read/write mapping only — the atomic-revoke race is covered by the fakes in
/// <c>AuthTokenServiceTests</c> (A2.Server.UnitTests) instead.</summary>
[Collection("DynamoDbLocal")]
public sealed class DynamoDbRefreshTokenRepositoryTests(DynamoDbLocalFixture dynamoDbLocalFixture)
    : IAsyncLifetime
{
    private const string TableName = "RefreshTokens";

    private AmazonDynamoDBClient _client = null!;
    private DynamoDbRefreshTokenRepository _repository = null!;

    public async Task InitializeAsync()
    {
        _client = dynamoDbLocalFixture.CreateClient();
        _repository = new DynamoDbRefreshTokenRepository(
            _client,
            Options.Create(new DynamoDbOptions { RefreshTokenTableName = TableName })
        );

        await _client.CreateTableAsync(
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

    public async Task DisposeAsync()
    {
        await _client.DeleteTableAsync(TableName);
        _client.Dispose();
    }

    [Fact]
    public async Task SaveAsync_WhenTokenHasAllFields_RoundTripsThroughGetByHash()
    {
        // setup
        var token = new RefreshToken(
            "hash-1",
            Guid.CreateVersion7(),
            "user@example.com",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null
        );

        // test
        await _repository.SaveAsync(token);
        var result = await _repository.GetByHashAsync("hash-1");

        // verify
        Assert.Equal(token, result);
    }

    [Fact]
    public async Task GetByHashAsync_WhenNotFound_ReturnsNull()
    {
        // test
        var result = await _repository.GetByHashAsync("does-not-exist");

        // verify
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByHashAsync_WhenStoredTokenIsRevoked_RoundTripsRevokedAt()
    {
        // setup
        var token = new RefreshToken(
            "hash-2",
            Guid.CreateVersion7(),
            "revoked@example.com",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)
        );

        await _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = TableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["RefreshTokenSecretHash"] = new(token.RefreshTokenSecretHash),
                    ["UserId"] = new(token.UserId.ToString()),
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

        // test
        var result = await _repository.GetByHashAsync("hash-2");

        // verify
        Assert.Equal(token, result);
    }

    [Fact]
    public async Task TryUpdateAsync_WhenTokenIsNotYetRevoked_SetsRevokedAtAndReturnsTrue()
    {
        // setup
        var token = new RefreshToken(
            "hash-3",
            Guid.CreateVersion7(),
            "user@example.com",
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null
        );
        await _repository.SaveAsync(token);
        var revokedAt = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

        // test
        var result = await _repository.TryUpdateAsync("hash-3", revokedAt);
        var fetched = await _repository.GetByHashAsync("hash-3");

        // verify
        Assert.True(result);
        Assert.Equal(token with { RevokedAt = revokedAt }, fetched);
    }
}
