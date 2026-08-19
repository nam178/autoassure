using System.Globalization;
using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbRefreshTokenRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> options
) : IRefreshTokenRepository
{
    private string TableName => options.Value.RefreshTokenTableName;

    public async Task<RefreshToken?> GetByHashAsync(string refreshTokenSecretHash)
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["RefreshTokenSecretHash"] = new(refreshTokenSecretHash),
                },
                ConsistentRead = true,
            }
        );

        return response.IsItemSet ? ToRefreshToken(response.Item) : null;
    }

    public Task AddAsync(RefreshToken token) =>
        client.PutItemAsync(
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
                },
            }
        );

    public Task MarkAsRevoked(string refreshTokenSecretHash, DateTimeOffset revokedAt) =>
        client.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["RefreshTokenSecretHash"] = new(refreshTokenSecretHash),
                },
                UpdateExpression = "SET RevokedAt = :revokedAt",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":revokedAt"] = new(revokedAt.ToString("O")),
                },
            }
        );

    private static RefreshToken ToRefreshToken(Dictionary<string, AttributeValue> item) =>
        new(
            item["RefreshTokenSecretHash"].S,
            item["GoogleUserId"].S,
            item["Email"].S,
            DateTimeOffset.FromUnixTimeSeconds(
                long.Parse(item["ExpiresAt"].N, CultureInfo.InvariantCulture)
            ),
            DateTimeOffset.Parse(item["CreatedAt"].S, CultureInfo.InvariantCulture),
            item.TryGetValue("RevokedAt", out var revokedAt)
                ? DateTimeOffset.Parse(revokedAt.S, CultureInfo.InvariantCulture)
                : null
        );
}
