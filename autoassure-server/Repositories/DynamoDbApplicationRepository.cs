using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbApplicationRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> options
) : IApplicationRepository
{
    private string TableName => options.Value.ApplicationTableName;
    private string OrganizationTableName => options.Value.OrganizationTableName;

    public async Task<bool> TrySaveAsync(Application application)
    {
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
                                TableName = OrganizationTableName,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    ["Id"] = new(application.OrganizationId.ToString()),
                                },
                                ConditionExpression = "attribute_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName,
                                Item = application.ToDynamoDbRow(),
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

    public async Task<Application?> GetByIdAsync(Guid organizationId, Guid id)
    {
        var response = await client.GetItemAsync(
            new GetItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId"] = new(organizationId.ToString()),
                    ["Id"] = new(id.ToString()),
                },
                ConsistentRead = true,
            }
        );

        return response.IsItemSet ? response.Item.ToApplication() : null;
    }

    public async Task<IReadOnlyList<Application>> ListByOrganizationAsync(Guid organizationId)
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                KeyConditionExpression = "OrganizationId = :organizationId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":organizationId"] = new(organizationId.ToString()),
                },
                ConsistentRead = true,
            }
        );

        return response.Items.Select(item => item.ToApplication()).ToList();
    }
}
