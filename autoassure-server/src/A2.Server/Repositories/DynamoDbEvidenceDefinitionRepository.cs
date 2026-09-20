using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbEvidenceDefinitionRepository(
    IAmazonDynamoDB client,
    IOptions<DynamoDbOptions> options
) : IEvidenceDefinitionRepository
{
    private const string IdIndexName = "IdIndex";

    private string TableName => options.Value.EvidenceDefinitionTableName;
    private string ApplicationTableName => options.Value.ApplicationTableName;

    public async Task<bool> TrySaveAsync(EvidenceDefinition evidence)
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
                                        evidence.OrganizationId.ToString()
                                    ),
                                    ["Id"] = new(
                                        evidence.ApplicationId.ToString()
                                    ),
                                },
                                ConditionExpression = "attribute_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName,
                                Item = evidence.ToDynamoDbRow(),
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
        Guid evidenceDefinitionId,
        EvidenceDefinitionUpdatableFields fields
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
                        ["Id"] = new(evidenceDefinitionId.ToString()),
                    },
                    UpdateExpression =
                        "SET #name = :name, Description = :description, ExampleValue = :exampleValue, "
                        + "UpdatedByUserId = :updatedByUserId, UpdatedAt = :updatedAt",
                    ConditionExpression = "attribute_exists(Id)",
                    ExpressionAttributeNames = new Dictionary<string, string>
                    {
                        ["#name"] = "Name",
                    },
                    ExpressionAttributeValues = new Dictionary<
                        string,
                        AttributeValue
                    >
                    {
                        [":name"] = new(fields.Name),
                        [":description"] = new(fields.Description),
                        [":exampleValue"] = new(fields.ExampleValue),
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

    public async Task<EvidenceDefinition?> GetByIdAsync(
        Guid organizationId,
        Guid evidenceDefinitionId
    )
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                IndexName = IdIndexName,
                KeyConditionExpression =
                    "OrganizationId = :organizationId AND Id = :id",
                ExpressionAttributeValues = new Dictionary<
                    string,
                    AttributeValue
                >
                {
                    [":organizationId"] = new(organizationId.ToString()),
                    [":id"] = new(evidenceDefinitionId.ToString()),
                },
                Limit = 1,
            }
        );

        return response.Items.Count > 0
            ? response.Items[0].ToEvidenceDefinition()
            : null;
    }

    public async Task<IReadOnlyList<EvidenceDefinition>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    )
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName,
                KeyConditionExpression =
                    "OrganizationId_ApplicationId = :partitionKey",
                ExpressionAttributeValues = new Dictionary<
                    string,
                    AttributeValue
                >
                {
                    [":partitionKey"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(
                            organizationId,
                            applicationId
                        )
                    ),
                },
                ConsistentRead = true,
                // Id is the table's range key and a Guid.CreateVersion7() UUID, so descending order is
                // newest first with no separate sort.
                ScanIndexForward = false,
            }
        );

        return response
            .Items.Select(item => item.ToEvidenceDefinition())
            .ToList();
    }
}