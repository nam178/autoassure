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

        return response.IsItemSet ? response.Item.ToRefreshToken() : null;
    }

    public Task SaveAsync(RefreshToken token) =>
        client.PutItemAsync(
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
                },
            }
        );

    public async Task<bool> TryUpdateAsync(string refreshTokenSecretHash, DateTimeOffset revokedAt)
    {
        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = TableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["RefreshTokenSecretHash"] = new(refreshTokenSecretHash),
                    },
                    UpdateExpression = "SET RevokedAt = :revokedAt",
                    ConditionExpression = "attribute_not_exists(RevokedAt)",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":revokedAt"] = new(revokedAt.ToString("O")),
                    },
                }
            );
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }
}
