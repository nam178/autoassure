using A2.Server.Common;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace A2.Server.Repositories;

public class DynamoDbScenarioRepository(IAmazonDynamoDB client, IOptions<DynamoDbOptions> options)
    : IScenarioRepository
{
    private const string IdIndexName = "IdIndex";

    private string ScenarioTableName => options.Value.ScenarioTableName;
    private string ScenariosByFolderTableName => options.Value.ScenariosByFolderTableName;
    private string ScenariosByTagTableName => options.Value.ScenariosByTagTableName;
    private string ApplicationTableName => options.Value.ApplicationTableName;
    private string PreconditionTableName => options.Value.PreconditionTableName;
    private string EvidenceDefinitionTableName => options.Value.EvidenceDefinitionTableName;

    public async Task<ScenarioWriteResult> TrySaveAsync(Scenario scenario)
    {
        var transactItems = new List<TransactWriteItem>
        {
            ApplicationExistsCheck(scenario.OrganizationId, scenario.ApplicationId),
        };
        transactItems.AddRange(ReferenceExistsChecks(scenario));
        transactItems.Add(PutScenario(scenario));
        transactItems.Add(PutFolderMapping(scenario, scenario.Folder));
        transactItems.AddRange(scenario.Tags.Select(tag => PutTagMapping(scenario, tag)));

        return await TryWriteAsync(transactItems);
    }

    public async Task<ScenarioWriteResult> TryUpdateAsync(Scenario scenario, Scenario previousState)
    {
        var transactItems = new List<TransactWriteItem>
        {
            ApplicationExistsCheck(scenario.OrganizationId, scenario.ApplicationId),
        };
        transactItems.AddRange(ReferenceExistsChecks(scenario));
        // Guards against the Scenario being deleted between the Controller's existence check and this
        // write -- without it, UpdateItem would silently recreate a partial row.
        var scenarioItemIndex = transactItems.Count;
        transactItems.Add(UpdateScenario(scenario));

        // Reconcile the folder mapping: only touch it if the folder actually changed, so an
        // unrelated field update doesn't churn the mapping table.
        if (scenario.Folder != previousState.Folder)
        {
            transactItems.Add(DeleteFolderMapping(previousState, previousState.Folder));
            transactItems.Add(PutFolderMapping(scenario, scenario.Folder));
        }

        // Reconcile tag mappings: remove rows for tags no longer present, add rows for new tags.
        var previousTags = previousState.Tags.ToHashSet();
        var currentTags = scenario.Tags.ToHashSet();
        transactItems.AddRange(
            previousTags.Except(currentTags).Select(tag => DeleteTagMapping(previousState, tag))
        );
        transactItems.AddRange(
            currentTags.Except(previousTags).Select(tag => PutTagMapping(scenario, tag))
        );

        return await TryWriteAsync(transactItems, scenarioItemIndex);
    }

    // Runs the transaction and, on cancellation, distinguishes an Application-missing failure (always
    // transact item 0), a Scenario-missing failure (scenarioItemIndex, only set by TryUpdateAsync),
    // and a Precondition/EvidenceDefinition-missing failure (every other index) -- the three map to
    // different HTTP statuses in the Controller.
    private async Task<ScenarioWriteResult> TryWriteAsync(
        List<TransactWriteItem> transactItems,
        int? scenarioItemIndex = null
    )
    {
        try
        {
            await client.TransactWriteItemsAsync(
                new TransactWriteItemsRequest { TransactItems = transactItems }
            );
            return ScenarioWriteResult.Success;
        }
        catch (TransactionCanceledException ex)
        {
            if (ex.CancellationReasons is not { Count: > 0 } reasons)
            {
                return ScenarioWriteResult.ReferenceNotFound;
            }
            if (reasons[0].Code == "ConditionalCheckFailed")
            {
                return ScenarioWriteResult.ApplicationNotFound;
            }
            if (scenarioItemIndex is { } index && reasons[index].Code == "ConditionalCheckFailed")
            {
                return ScenarioWriteResult.ScenarioNotFound;
            }
            return ScenarioWriteResult.ReferenceNotFound;
        }
    }

    // One ConditionCheck per unique Precondition/EvidenceDefinition referenced across all Activities --
    // deduped, since a DynamoDB transaction rejects two operations targeting the same item.
    private IEnumerable<TransactWriteItem> ReferenceExistsChecks(Scenario scenario)
    {
        var partitionKey = DynamoDbMapper.ApplicationScopedPartitionKey(
            scenario.OrganizationId,
            scenario.ApplicationId
        );

        var preconditionChecks = scenario
            .Activities.SelectMany(activity => activity.PreconditionIds)
            .Distinct()
            .Select(preconditionId => new TransactWriteItem
            {
                ConditionCheck = new ConditionCheck
                {
                    TableName = PreconditionTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId_ApplicationId"] = new(partitionKey),
                        ["Id"] = new(preconditionId.ToString()),
                    },
                    ConditionExpression = "attribute_exists(Id)",
                },
            });

        var evidenceChecks = scenario
            .Activities.SelectMany(activity => activity.EvidenceIds)
            .Distinct()
            .Select(evidenceId => new TransactWriteItem
            {
                ConditionCheck = new ConditionCheck
                {
                    TableName = EvidenceDefinitionTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["OrganizationId_ApplicationId"] = new(partitionKey),
                        ["Id"] = new(evidenceId.ToString()),
                    },
                    ConditionExpression = "attribute_exists(Id)",
                },
            });

        return preconditionChecks.Concat(evidenceChecks);
    }

    private TransactWriteItem ApplicationExistsCheck(Guid organizationId, Guid applicationId) =>
        new()
        {
            ConditionCheck = new ConditionCheck
            {
                TableName = ApplicationTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId"] = new(organizationId.ToString()),
                    ["Id"] = new(applicationId.ToString()),
                },
                ConditionExpression = "attribute_exists(Id)",
            },
        };

    public Task DeleteAsync(Scenario scenario) =>
        client.TransactWriteItemsAsync(
            new TransactWriteItemsRequest
            {
                TransactItems =
                [
                    DeleteScenario(scenario),
                    DeleteFolderMapping(scenario, scenario.Folder),
                    .. scenario.Tags.Select(tag => DeleteTagMapping(scenario, tag)),
                ],
            }
        );

    public async Task<Scenario?> GetByIdAsync(Guid organizationId, Guid id)
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = ScenarioTableName,
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

        return response.Items.Count > 0 ? response.Items[0].ToScenario() : null;
    }

    public async Task<IReadOnlyList<Scenario>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    )
    {
        var response = await client.QueryAsync(
            new QueryRequest
            {
                TableName = ScenarioTableName,
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

        return response.Items.Select(item => item.ToScenario()).ToList();
    }

    public Task<IReadOnlyList<Scenario>> ListByFolderAsync(
        Guid organizationId,
        Guid applicationId,
        string folder
    ) =>
        ListByMappingAsync(
            ScenariosByFolderTableName,
            $"{organizationId}_{applicationId}_{folder}"
        );

    public Task<IReadOnlyList<Scenario>> ListByTagAsync(
        Guid organizationId,
        Guid applicationId,
        string tag
    ) => ListByMappingAsync(ScenariosByTagTableName, $"{organizationId}_{applicationId}_{tag}");

    private async Task<IReadOnlyList<Scenario>> ListByMappingAsync(
        string mappingTableName,
        string partitionKey
    )
    {
        var mappingResponse = await client.QueryAsync(
            new QueryRequest
            {
                TableName = mappingTableName,
                KeyConditionExpression = "PartitionKey = :partitionKey",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":partitionKey"] = new(partitionKey),
                },
                ConsistentRead = true,
            }
        );

        var scenarioIds = mappingResponse.Items.Select(item => item["ScenarioId"].S).ToList();
        if (scenarioIds.Count == 0)
        {
            return [];
        }

        // The Scenarios table's primary key is OrganizationId_ApplicationId + Id, so BatchGetItem
        // needs the full composite key for each row -- both are denormalized onto the mapping row.
        var batchResponse = await client.BatchGetItemAsync(
            new BatchGetItemRequest
            {
                RequestItems = new Dictionary<string, KeysAndAttributes>
                {
                    [ScenarioTableName] = new()
                    {
                        Keys = mappingResponse
                            .Items.Select(item => new Dictionary<string, AttributeValue>
                            {
                                ["OrganizationId_ApplicationId"] = new(
                                    DynamoDbMapper.ApplicationScopedPartitionKey(
                                        Guid.Parse(item["OrganizationId"].S),
                                        Guid.Parse(item["ApplicationId"].S)
                                    )
                                ),
                                ["Id"] = new(item["ScenarioId"].S),
                            })
                            .ToList(),
                        ConsistentRead = true,
                    },
                },
            }
        );

        // BatchGetItem doesn't preserve request order, so re-sort to match the mapping Query's
        // ScenarioId order (creation order) to honor this method's ordering guarantee.
        var scenariosById = batchResponse
            .Responses[ScenarioTableName]
            .ToDictionary(item => item["Id"].S);
        return scenarioIds.Select(scenarioId => scenariosById[scenarioId].ToScenario()).ToList();
    }

    private TransactWriteItem PutScenario(Scenario scenario) =>
        new()
        {
            Put = new Put { TableName = ScenarioTableName, Item = scenario.ToDynamoDbRow() },
        };

    // Used by TryUpdateAsync only -- a partial UpdateItem touching just the user-editable fields,
    // as opposed to PutScenario's full-row write used on create.
    private TransactWriteItem UpdateScenario(Scenario scenario) =>
        new()
        {
            Update = new Update
            {
                TableName = ScenarioTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(
                            scenario.OrganizationId,
                            scenario.ApplicationId
                        )
                    ),
                    ["Id"] = new(scenario.Id.ToString()),
                },
                UpdateExpression =
                    "SET Title = :title, Description = :description, Folder = :folder, "
                    + "Tags = :tags, Activities = :activities, UpdatedByUserId = :updatedByUserId, "
                    + "UpdatedAt = :updatedAt",
                ConditionExpression = "attribute_exists(Id)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":title"] = new(scenario.Title),
                    [":description"] = new(scenario.Description),
                    [":folder"] = new(scenario.Folder),
                    [":tags"] = new AttributeValue
                    {
                        L = scenario.Tags.Select(tag => new AttributeValue(tag)).ToList(),
                    },
                    [":activities"] = new AttributeValue
                    {
                        L = scenario
                            .Activities.Select(activity => activity.ToAttributeValue())
                            .ToList(),
                    },
                    [":updatedByUserId"] = new(scenario.UpdatedByUserId.ToString()),
                    [":updatedAt"] = new(scenario.UpdatedAt.ToString("O")),
                },
            },
        };

    private TransactWriteItem DeleteScenario(Scenario scenario) =>
        new()
        {
            Delete = new Delete
            {
                TableName = ScenarioTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(
                            scenario.OrganizationId,
                            scenario.ApplicationId
                        )
                    ),
                    ["Id"] = new(scenario.Id.ToString()),
                },
            },
        };

    private TransactWriteItem PutFolderMapping(Scenario scenario, string folder) =>
        new()
        {
            Put = new Put
            {
                TableName = ScenariosByFolderTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["PartitionKey"] = new(FolderPartitionKey(scenario, folder)),
                    ["ScenarioId"] = new(scenario.Id.ToString()),
                    ["OrganizationId"] = new(scenario.OrganizationId.ToString()),
                    ["ApplicationId"] = new(scenario.ApplicationId.ToString()),
                    ["Folder"] = new(folder),
                    ["CreatedAt"] = new(DateTimeOffset.UtcNow.ToString("O")),
                },
            },
        };

    private TransactWriteItem DeleteFolderMapping(Scenario scenario, string folder) =>
        new()
        {
            Delete = new Delete
            {
                TableName = ScenariosByFolderTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PartitionKey"] = new(FolderPartitionKey(scenario, folder)),
                    ["ScenarioId"] = new(scenario.Id.ToString()),
                },
            },
        };

    private TransactWriteItem PutTagMapping(Scenario scenario, string tag) =>
        new()
        {
            Put = new Put
            {
                TableName = ScenariosByTagTableName,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["PartitionKey"] = new(TagPartitionKey(scenario, tag)),
                    ["ScenarioId"] = new(scenario.Id.ToString()),
                    ["OrganizationId"] = new(scenario.OrganizationId.ToString()),
                    ["ApplicationId"] = new(scenario.ApplicationId.ToString()),
                    ["Tag"] = new(tag),
                    ["CreatedAt"] = new(DateTimeOffset.UtcNow.ToString("O")),
                },
            },
        };

    private TransactWriteItem DeleteTagMapping(Scenario scenario, string tag) =>
        new()
        {
            Delete = new Delete
            {
                TableName = ScenariosByTagTableName,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["PartitionKey"] = new(TagPartitionKey(scenario, tag)),
                    ["ScenarioId"] = new(scenario.Id.ToString()),
                },
            },
        };

    private static string FolderPartitionKey(Scenario scenario, string folder) =>
        $"{scenario.OrganizationId}_{scenario.ApplicationId}_{folder}";

    private static string TagPartitionKey(Scenario scenario, string tag) =>
        $"{scenario.OrganizationId}_{scenario.ApplicationId}_{tag}";
}
