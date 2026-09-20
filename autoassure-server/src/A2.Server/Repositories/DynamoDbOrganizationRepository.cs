using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbOrganizationRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> options
) : IOrganizationRepository
{
    private string TableName => options.Value.OrganizationTableName;

    public async Task<Organization?> GetByIdAsync(Guid organizationId)
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new(organizationId.ToString()),
                },
                ConsistentRead = true,
            }
        );

        return response.IsItemSet ? response.Item.ToOrganization() : null;
    }

    public async Task<IReadOnlyList<Organization>> GetByIdsAsync(
        IReadOnlyList<Guid> organizationIds
    )
    {
        if (organizationIds.Count == 0) return [];

        var requestItems = new Dictionary<string, KeysAndAttributes>
        {
            [TableName] = new()
            {
                Keys = organizationIds
                    .Select(id => new Dictionary<string, AttributeValue>
                    {
                        ["Id"] = new(id.ToString()),
                    })
                    .ToList(),
                ConsistentRead = true,
            },
        };

        var response = await client.BatchGetItemAsync(
            new BatchGetItemRequest { RequestItems = requestItems }
        );

        return response
            .Responses[TableName]
            .Select(item => item.ToOrganization())
            .ToList();
    }

    public async Task<bool> TrySetLifecycleStateAsync(
        Guid organizationId,
        LifecycleState newState
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
                        ["Id"] = new(organizationId.ToString()),
                    },
                    UpdateExpression = "SET LifecycleState = :newState",
                    ExpressionAttributeValues = new Dictionary<
                        string,
                        AttributeValue
                    >
                    {
                        [":newState"] = new(newState.ToString()),
                    },
                    // Condition: the row must exist
                    ConditionExpression = "attribute_exists(Id)",
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