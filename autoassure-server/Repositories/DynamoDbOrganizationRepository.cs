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

    public async Task<Organization?> GetByIdAsync(Guid id)
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue> { ["Id"] = new(id.ToString()) },
                ConsistentRead = true,
            }
        );

        return response.IsItemSet ? response.Item.ToOrganization() : null;
    }
}
