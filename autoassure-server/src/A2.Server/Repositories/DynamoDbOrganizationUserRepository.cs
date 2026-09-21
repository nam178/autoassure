using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbOrganizationUserRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> options
) : IOrganizationUserRepository
{
    private const string UserIdIndexName = "UserIdIndex";

    private string TableName => options.Value.OrganizationUserTableName;
    private string OrganizationTableName => options.Value.OrganizationTableName;

    public async Task<bool> TryCreateAsync(OrganizationUser membership)
    {
        try
        {
            await client.PutItemAsync(
                new PutItemRequest
                {
                    TableName = TableName,
                    Item = membership.ToDynamoDbRow(),
                    ConditionExpression =
                        "attribute_not_exists(OrganizationId) AND attribute_not_exists(UserId)",
                }
            );
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid userId,
        OrganizationUserUpdatableFields fields
    )
    {
        try
        {
            await client.UpdateItemAsync(
                new UpdateItemRequest
                {
                    TableName = TableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId"] = new(organizationId.ToString()),
                        ["UserId"] = new(userId.ToString()),
                    },
                    UpdateExpression =
                        "SET #role = :role, UpdatedByUserId = :updatedByUserId, UpdatedAt = :updatedAt",
                    ConditionExpression =
                        "attribute_exists(OrganizationId) AND attribute_exists(UserId)",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#role"] = "Role",
                    },
                    ExpressionAttributeValues = new Dictionary<
                        string,
                        AttributeValue
                    >
                    {
                        [":role"] = new(fields.Role.ToString()),
                        [":updatedByUserId"] = new(
                            fields.UpdatedByUserId.ToString()
                        ),
                        [":updatedAt"] = new(fields.UpdatedAt.ToString("O")),
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

    public async Task<IReadOnlyList<OrganizationUser>> ListByUserAsync(
        Guid userId
    )
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                IndexName = UserIdIndexName,
                KeyConditionExpression = "UserId = :userId",
                ExpressionAttributeValues = new Dictionary<
                    string,
                    AttributeValue
                >
                {
                    [":userId"] = new(userId.ToString()),
                },
                // The index's range key, OrganizationId, is the Organization's own Guid.CreateVersion7()
                // UUID, so descending order is newest first with no separate sort.
                ScanIndexForward = false,
            }
        );

        return response
            .Items.Select(item => item.ToOrganizationUser())
            .ToList();
    }

    public async Task<RemoveMembershipOutcome> TryDeleteAsync(
        Guid organizationId,
        Guid userId,
        OrganizationRole role
    )
    {
        var transactItems = new List<TransactWriteItem>
        {
            new()
            {
                Delete = new Delete
                {
                    TableName = TableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId"] = new(organizationId.ToString()),
                        ["UserId"] = new(userId.ToString()),
                    },
                    ConditionExpression =
                        "attribute_exists(OrganizationId) AND attribute_exists(UserId) AND #role = :role",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#role"] = "Role",
                    },
                    ExpressionAttributeValues = new Dictionary<
                        string,
                        AttributeValue
                    >
                    {
                        [":role"] = new(role.ToString()),
                    },
                },
            },
        };

        if (role == OrganizationRole.Owner)
            transactItems.Add(
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = OrganizationTableName,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["Id"] = new(organizationId.ToString()),
                        },
                        UpdateExpression = "ADD OwnerCount :minusOne",
                        ConditionExpression =
                            "attribute_exists(Id) AND OwnerCount > :one",
                        ExpressionAttributeValues = new Dictionary<
                            string,
                            AttributeValue
                        >
                        {
                            [":minusOne"] = new() { N = "-1" },
                            [":one"] = new() { N = "1" },
                        },
                    },
                }
            );

        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }
            );
            return RemoveMembershipOutcome.Removed;
        }
        catch (TransactionCanceledException ex)
            when (ex.CancellationReasons is { Count: > 0 })
        {
            var reasons = ex.CancellationReasons;
            if (reasons[0].Code == "ConditionalCheckFailed")
                return RemoveMembershipOutcome.NotFound;

            if (
                role == OrganizationRole.Owner
                && reasons.Count > 1
                && reasons[1].Code == "ConditionalCheckFailed"
            )
                return RemoveMembershipOutcome.CannotDeleteLastOwner;

            throw;
        }
    }

    public async Task<OrganizationUser?> GetAsync(
        Guid organizationId,
        Guid userId
    )
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId"] = new(organizationId.ToString()),
                    ["UserId"] = new(userId.ToString()),
                },
                ConsistentRead = true,
            }
        );

        return response.IsItemSet ? response.Item.ToOrganizationUser() : null;
    }
}
