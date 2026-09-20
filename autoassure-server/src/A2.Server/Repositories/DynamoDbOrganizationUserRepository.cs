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

    public Task SaveAsync(OrganizationUser membership)
    {
        return client.PutItemAsync(
            new PutItemRequest
            {
                TableName = TableName,
                Item = membership.ToDynamoDbRow(),
            }
        );
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
}
