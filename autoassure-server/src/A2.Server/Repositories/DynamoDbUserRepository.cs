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
    private string OrganizationTableName => options.Value.OrganizationTableName;
    private string OrganizationUserTableName => options.Value.OrganizationUserTableName;

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

        return response.Items.Count > 0 ? response.Items[0].ToUser() : null;
    }

    public async Task<bool> TrySaveAsync(User user)
    {
        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest
                {
                    TransactItems =
                    [
                        // Locks this Google account's first sign-in. Has no GoogleUserId attribute of
                        // its own, so it never shows up in GoogleUserIdIndex queries -- it only exists to
                        // atomically reject a second, concurrent first sign-in for the same Google account.
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName,
                                Item = new Dictionary<string, AttributeValue>
                                {
                                    ["Id"] = new(
                                        GenerateDidSyncGoogleUserLockId(user.GoogleUserId)
                                    ),
                                },
                                ConditionExpression = "attribute_not_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName,
                                Item = user.ToDynamoDbRow(),
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

    public async Task<bool> TryUpdateAsync(Guid id, UserUpdatableFields fields)
    {
        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = TableName,
                    Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) },
                    UpdateExpression =
                        "SET FirstName = :firstName, LastName = :lastName, Email = :email, "
                        + "EmailVerified = :emailVerified",
                    ConditionExpression = "attribute_exists(Id)",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":firstName"] = new(fields.FirstName),
                        [":lastName"] = new(fields.LastName),
                        [":email"] = new(fields.Email),
                        [":emailVerified"] = new() { BOOL = fields.EmailVerified },
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

    public async Task<bool> TryCreatePersonalOrganizationAsync(
        Organization organization,
        OrganizationUser membership
    )
    {
        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest
                {
                    TransactItems =
                    [
                        // Locks this user's personal Organization creation. Lives in the User table
                        // because neither the Organization table (keyed by a random Organization Id) nor
                        // the OrganizationUser table (keyed by OrganizationId, not UserId) has an
                        // attribute that's deterministic per-user to condition on -- it only exists to
                        // atomically reject a second, concurrent personal-Organization creation for the
                        // same user.
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName,
                                Item = new Dictionary<string, AttributeValue>
                                {
                                    ["Id"] = new(
                                        GenerateDidCreatePersonalOrganizationLockId(
                                            membership.UserId
                                        )
                                    ),
                                },
                                ConditionExpression = "attribute_not_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = OrganizationTableName,
                                Item = organization.ToDynamoDbRow(),
                            },
                        },
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = OrganizationUserTableName,
                                Item = membership.ToDynamoDbRow(),
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

    private static string GenerateDidSyncGoogleUserLockId(string googleUserId) =>
        $"{googleUserId}_DidSyncGoogleUser";

    private static string GenerateDidCreatePersonalOrganizationLockId(Guid userId) =>
        $"{userId}_DidCreatePersonalOrg";
}
