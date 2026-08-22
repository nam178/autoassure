using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbUserRepository(IAmazonDynamoDB client, IOptions<DynamoDbOptions> options)
    : IUserRepository
{
    private const string GoogleUserIdIndexName = "GoogleUserIdIndex";

    private string TableName => options.Value.UserTableName;

    public async Task<User?> GetByGoogleUserIdAsync(string googleUserId)
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                IndexName = GoogleUserIdIndexName,
                KeyConditionExpression = "GoogleUserId = :googleUserId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":googleUserId"] = new(googleUserId),
                },
                Limit = 1,
            }
        );

        return response.Items.Count > 0 ? ToUser(response.Items[0]) : null;
    }

    public async Task<bool> TryCreateAsync(User user)
    {
        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest
                {
                    TransactItems =
                    [
                        // Claims the GoogleUserId for this user's Id. Has no GoogleUserId attribute of
                        // its own, so it never shows up in GoogleUserIdIndex queries -- it only exists to
                        // atomically reject a second, concurrent first sign-in for the same Google account.
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName,
                                Item = new Dictionary<string, AttributeValue>
                                {
                                    ["Id"] = new(GoogleUserIdLockId(user.GoogleUserId)),
                                },
                                ConditionExpression = "attribute_not_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName,
                                Item = ToItem(user),
                                ConditionExpression = "attribute_not_exists(Id)",
                            },
                        },
                    ],
                }
            );
            return true;
        }
        catch (TransactionCanceledException)
        {
            return false;
        }
    }

    public Task UpdateAsync(User user) =>
        client.PutItemAsync(new PutItemRequest { TableName = TableName, Item = ToItem(user) });

    private static string GoogleUserIdLockId(string googleUserId) => $"GoogleUserId#{googleUserId}";

    private static Dictionary<string, AttributeValue> ToItem(User user) =>
        new()
        {
            ["Id"] = new(user.Id),
            ["GoogleUserId"] = new(user.GoogleUserId),
            ["FirstName"] = new(user.FirstName),
            ["LastName"] = new(user.LastName),
            ["Email"] = new(user.Email),
            ["EmailVerified"] = new() { BOOL = user.EmailVerified },
        };

    private static User ToUser(Dictionary<string, AttributeValue> item) =>
        new()
        {
            Id = item["Id"].S,
            GoogleUserId = item["GoogleUserId"].S,
            FirstName = item["FirstName"].S,
            LastName = item["LastName"].S,
            Email = item["Email"].S,
            EmailVerified = item["EmailVerified"].BOOL ?? false,
        };
}
