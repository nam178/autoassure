using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbRunRepository(IAmazonDynamoDB client, IOptions<DynamoDbOptions> options)
    : IRunRepository
{
    private const string IdIndexName = "IdIndex";

    private string RunsTableName => options.Value.RunTableName;
    private string TriesTableName => options.Value.TryTableName;
    private string ApplicationTableName => options.Value.ApplicationTableName;
    private string EnvironmentTableName => options.Value.EnvironmentTableName;

    public async Task<bool> TrySaveAsync(Run run)
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
                                    ["OrganizationId"] = new(run.OrganizationId.ToString()),
                                    ["Id"] = new(run.ApplicationId.ToString()),
                                },
                                ConditionExpression = "attribute_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            ConditionCheck = new ConditionCheck
                            {
                                TableName = EnvironmentTableName,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    ["OrganizationId_ApplicationId"] = new(
                                        DynamoDbMapper.ApplicationScopedPartitionKey(
                                            run.OrganizationId,
                                            run.ApplicationId
                                        )
                                    ),
                                    ["Id"] = new(run.EnvironmentId.ToString()),
                                },
                                ConditionExpression = "attribute_exists(Id)",
                            },
                        },
                        new TransactWriteItem
                        {
                            Put = new Put
                            {
                                TableName = TableName(run.Kind),
                                Item = run.ToDynamoDbRow(),
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

    public async Task<Run?> GetAsync(Guid organizationId, Guid id, RunKind kind)
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = TableName(kind),
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

        return response.Items.Count > 0 ? response.Items[0].ToRun() : null;
    }

    public async Task<IReadOnlyList<Run>> ListRunsByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    )
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = RunsTableName,
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

        return response.Items.Select(item => item.ToRun()).ToList();
    }

    private string TableName(RunKind kind) => kind == RunKind.Try ? TriesTableName : RunsTableName;
}
