using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbPreconditionRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> options
) : IPreconditionRepository
{
    private const string IdIndexName = "IdIndex";

    private string TableName => options.Value.PreconditionTableName;
    private string ApplicationTableName => options.Value.ApplicationTableName;

    public async Task<bool> TrySaveAsync(Precondition precondition)
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
                                    ["OrganizationId"] = new(
                                        precondition.OrganizationId.ToString()
                                    ),
                                    ["Id"] = new(precondition.ApplicationId.ToString()),
                                },
                                ConditionExpression = "attribute_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName,
                                Item = precondition.ToDynamoDbRow(),
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

    public Task UpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        PreconditionUpdatableFields fields
    )
    {
        return client.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(organizationId, applicationId)
                    ),
                    ["Id"] = new(id.ToString()),
                },
                UpdateExpression =
                    "SET #name = :name, ValueSource = :valueSource, ExampleValue = :exampleValue, "
                    + "UpdatedByUserId = :updatedByUserId, UpdatedAt = :updatedAt",
                ExpressionAttributeNames = new Dictionary<string, string> { ["#name"] = "Name" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":name"] = new(fields.Name),
                    [":valueSource"] = new(fields.ValueSource.ToString()),
                    [":exampleValue"] = new(fields.ExampleValue),
                    [":updatedByUserId"] = new(fields.UpdatedByUserId.ToString()),
                    [":updatedAt"] = new(fields.UpdatedAt.ToString("O")),
                },
            }
        );
    }

    public async Task DeleteAsync(Guid organizationId, Guid id)
    {
        // The main table's partition key is OrganizationId_ApplicationId, but callers only have the
        // Precondition's Id -- resolve its ApplicationId via the IdIndex GSI first, same as GetByIdAsync.
        var existing = await GetByIdAsync(organizationId, id);
        if (existing is null)
        {
            return;
        }

        await client.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = TableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(
                            organizationId,
                            existing.ApplicationId
                        )
                    ),
                    ["Id"] = new(id.ToString()),
                },
            }
        );
    }

    public async Task<Precondition?> GetByIdAsync(Guid organizationId, Guid id)
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

        return response.Items.Count > 0 ? response.Items[0].ToPrecondition() : null;
    }

    public async Task<IReadOnlyList<Precondition>> ListByApplicationAsync(
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

        return response.Items.Select(item => item.ToPrecondition()).ToList();
    }
}
