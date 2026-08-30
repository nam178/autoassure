using A2.Server.Common;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;
using Environment = A2.Server.Models.Environment;

namespace A2.Server.Repositories;

public class DynamoDbEnvironmentRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> options
) : IEnvironmentRepository
{
    private const string IdIndexName = "IdIndex";

    private string TableName => options.Value.EnvironmentTableName;
    private string ApplicationTableName => options.Value.ApplicationTableName;

    public async Task<bool> TrySaveAsync(Environment environment)
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
                                TableName = ApplicationTableName,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    ["OrganizationId"] = new(environment.OrganizationId.ToString()),
                                    ["Id"] = new(environment.ApplicationId.ToString()),
                                },
                                ConditionExpression = "attribute_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName,
                                Item = environment.ToDynamoDbRow(),
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

    public async Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        EnvironmentUpdatableFields fields
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
                        ["OrganizationId_ApplicationId"] = new(
                            DynamoDbMapper.ApplicationScopedPartitionKey(
                                organizationId,
                                applicationId
                            )
                        ),
                        ["Id"] = new(id.ToString()),
                    },
                    UpdateExpression =
                        "SET #name = :name, Classification = :classification, "
                        + "UpdatedByUserId = :updatedByUserId, UpdatedAt = :updatedAt",
                    ConditionExpression = "attribute_exists(Id)",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#name"] = "Name",
                    },
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":name"] = new(fields.Name),
                        [":classification"] = new(fields.Classification.ToString()),
                        [":updatedByUserId"] = new(fields.UpdatedByUserId.ToString()),
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

    public async Task<Environment?> GetByIdAsync(Guid organizationId, Guid id)
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                IndexName = IdIndexName,
                KeyConditionExpression = "OrganizationId = :organizationId AND Id = :id",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":organizationId"] = new(organizationId.ToString()),
                    [":id"] = new(id.ToString()),
                },
                Limit = 1,
            }
        );

        return response.Items.Count > 0 ? response.Items[0].ToEnvironment() : null;
    }

    public async Task<IReadOnlyList<Environment>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    )
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                KeyConditionExpression = "OrganizationId_ApplicationId = :partitionKey",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":partitionKey"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                },
                ConsistentRead = true,
            }
        );

        return response.Items.Select(item => item.ToEnvironment()).ToList();
    }
}
