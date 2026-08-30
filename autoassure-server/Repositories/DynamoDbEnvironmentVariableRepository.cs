using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbEnvironmentVariableRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> options
) : IEnvironmentVariableRepository
{
    private string TableName => options.Value.EnvironmentVariableTableName;
    private string EnvironmentTableName => options.Value.EnvironmentTableName;

    public async Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId,
        string key,
        string value,
        Guid updatedByUserId
    )
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest
                {
                    TransactItems =
                    [
                        new TransactWriteItem
                        {
                            ConditionCheck = new ConditionCheck
                            {
                                TableName = EnvironmentTableName,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    ["OrganizationId_ApplicationId"] = new(
                                        DynamoDbMapper.ApplicationScopedPartitionKey(
                                            organizationId,
                                            applicationId
                                        )
                                    ),
                                    ["Id"] = new(environmentId.ToString()),
                                },
                                ConditionExpression = "attribute_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            Update = new Update
                            {
                                TableName = TableName,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    ["OrganizationId_EnvironmentId"] = new(
                                        GetPartitionKey(organizationId, environmentId)
                                    ),
                                    ["Key"] = new(key),
                                },
                                UpdateExpression =
                                    "SET #value = :value, OrganizationId = :organizationId, EnvironmentId = :environmentId, "
                                    + "UpdatedByUserId = :updatedByUserId, UpdatedAt = :now, "
                                    + "CreatedAt = if_not_exists(CreatedAt, :now), "
                                    + "CreatedByUserId = if_not_exists(CreatedByUserId, :updatedByUserId)",
                                ExpressionAttributeNames = new Dictionary<string, string>
                                {
                                    ["#value"] = "Value",
                                },
                                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                                {
                                    [":value"] = new(value),
                                    [":organizationId"] = new(organizationId.ToString()),
                                    [":environmentId"] = new(environmentId.ToString()),
                                    [":updatedByUserId"] = new(updatedByUserId.ToString()),
                                    [":now"] = new(now),
                                },
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

    public Task DeleteAsync(Guid organizationId, Guid environmentId, string key) =>
        client.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_EnvironmentId"] = new(
                        GetPartitionKey(organizationId, environmentId)
                    ),
                    ["Key"] = new(key),
                },
            }
        );

    public async Task<IReadOnlyList<EnvironmentVariable>> ListByEnvironmentAsync(
        Guid organizationId,
        Guid environmentId
    )
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                KeyConditionExpression = "OrganizationId_EnvironmentId = :partitionKey",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":partitionKey"] = new(GetPartitionKey(organizationId, environmentId)),
                },
                ConsistentRead = true,
            }
        );

        return response.Items.Select(item => item.ToEnvironmentVariable()).ToList();
    }

    private static string GetPartitionKey(Guid organizationId, Guid environmentId) =>
        $"{organizationId}_{environmentId}";
}
