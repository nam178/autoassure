using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using A2.Server.Contracts;
using A2.Server.Models;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ActivityResult = A2.Server.Contracts.ActivityResult;
using ActivityResultStatus = A2.Server.Contracts.ActivityResultStatus;
using EnvironmentClassification = A2.Server.Contracts.EnvironmentClassification;
using RunStatus = A2.Server.Contracts.RunStatus;

namespace A2.Server.Tests.Controllers;

/// <summary>
///     Integration tests for
///     <see cref="A2.Server.Controllers.RunStatusUpdatesController" /> over
///     real HTTP, against DynamoDB Local: Append Run Status Update, List Run
///     Status Updates, and the full
///     create -> start -> append -> poll -> end round trip a client actually
///     drives.
/// </summary>
[Collection("DynamoDbLocal")]
public sealed class RunStatusUpdatesControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public RunStatusUpdatesControllerTests(
        WebApplicationFactory<Program> factory,
        DynamoDbLocalFixture dynamoDbLocalFixture
    )
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Auth:SigningKey"] = SigningKey,
                            ["DynamoDb:ApplicationTableName"] = "Applications",
                            ["DynamoDb:EnvironmentTableName"] = "Environments",
                            ["DynamoDb:EnvironmentVariableTableName"] =
                                "EnvironmentVariables",
                            ["DynamoDb:PreconditionTableName"] =
                                "Preconditions",
                            ["DynamoDb:EvidenceDefinitionTableName"] =
                                "EvidenceDefinitions",
                            ["DynamoDb:ScenarioTableName"] = "Scenarios",
                            ["DynamoDb:ScenariosByFolderTableName"] =
                                "ScenariosByFolder",
                            ["DynamoDb:ScenariosByTagTableName"] =
                                "ScenariosByTag",
                            ["DynamoDb:ActivityTableName"] = "Activities",
                            ["DynamoDb:RunTableName"] = "Runs",
                            ["DynamoDb:RunningRunTableName"] = "RunningRuns",
                            ["DynamoDb:OrganizationTableName"] =
                                "Organizations",
                            ["DynamoDb:OrganizationUserTableName"] =
                                "OrganizationUsers",
                        }
                    )
            );
            builder.ConfigureServices(services =>
            {
                _client = dynamoDbLocalFixture.CreateClient();
                services.Replace(
                    ServiceDescriptor.Singleton<IAmazonDynamoDB>(_client)
                );
            });
        });
    }

    public async Task InitializeAsync()
    {
        _ = _factory.Server;

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "Applications",
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId", KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "Environments",
                KeySchema =
                [
                    new KeySchemaElement(
                        "OrganizationId_ApplicationId",
                        KeyType.HASH
                    ),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId_ApplicationId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition(
                        "OrganizationId",
                        ScalarAttributeType.S
                    ),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "IdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement(
                                "OrganizationId",
                                KeyType.HASH
                            ),
                            new KeySchemaElement("Id", KeyType.RANGE),
                        ],
                        Projection = new Projection
                        {
                            ProjectionType = ProjectionType.ALL,
                        },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "EnvironmentVariables",
                KeySchema =
                [
                    new KeySchemaElement(
                        "OrganizationId_EnvironmentId",
                        KeyType.HASH
                    ),
                    new KeySchemaElement("Key", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId_EnvironmentId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("Key", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await CreateLibraryTableAsync("Preconditions");
        await CreateLibraryTableAsync("EvidenceDefinitions");

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "Scenarios",
                KeySchema =
                [
                    new KeySchemaElement(
                        "OrganizationId_ApplicationId",
                        KeyType.HASH
                    ),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId_ApplicationId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition(
                        "OrganizationId",
                        ScalarAttributeType.S
                    ),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "IdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement(
                                "OrganizationId",
                                KeyType.HASH
                            ),
                            new KeySchemaElement("Id", KeyType.RANGE),
                        ],
                        Projection = new Projection
                        {
                            ProjectionType = ProjectionType.ALL,
                        },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await CreateMappingTableAsync(
            "ScenariosByFolder",
            "OrganizationId_ApplicationId_Folder"
        );
        await CreateMappingTableAsync(
            "ScenariosByTag",
            "OrganizationId_ApplicationId_Tag"
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "Activities",
                KeySchema =
                [
                    new KeySchemaElement(
                        "OrganizationId_ScenarioId",
                        KeyType.HASH
                    ),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId_ScenarioId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition(
                        "OrganizationId",
                        ScalarAttributeType.S
                    ),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "IdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement(
                                "OrganizationId",
                                KeyType.HASH
                            ),
                            new KeySchemaElement("Id", KeyType.RANGE),
                        ],
                        Projection = new Projection
                        {
                            ProjectionType = ProjectionType.ALL,
                        },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "Runs",
                KeySchema =
                [
                    new KeySchemaElement(
                        "OrganizationId_ApplicationId",
                        KeyType.HASH
                    ),
                    new KeySchemaElement("RowKey", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId_ApplicationId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("RowKey", ScalarAttributeType.S),
                    new AttributeDefinition("HeaderId", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "RunHeaderIndex",
                        KeySchema =
                        [
                            new KeySchemaElement(
                                "OrganizationId_ApplicationId",
                                KeyType.HASH
                            ),
                            new KeySchemaElement("HeaderId", KeyType.RANGE),
                        ],
                        Projection = new Projection
                        {
                            ProjectionType = ProjectionType.INCLUDE,
                            NonKeyAttributes =
                            [
                                "Id",
                                "Trigger",
                                "Status",
                                "TotalActivityCount",
                                "PassedActivityCount",
                                "FailedActivityCount",
                                "SkippedActivityCount",
                                "CreatedAt",
                                "StartedAt",
                                "CompletedAt",
                                "LastHeartbeatAt",
                            ],
                        },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "RunningRuns",
                KeySchema =
                [
                    new KeySchemaElement(
                        "OrganizationId_ApplicationId",
                        KeyType.HASH
                    ),
                    new KeySchemaElement("RunId", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId_ApplicationId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("RunId", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "Organizations",
                KeySchema = [new KeySchemaElement("Id", KeyType.HASH)],
                AttributeDefinitions =
                [
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "OrganizationUsers",
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId", KeyType.HASH),
                    new KeySchemaElement("UserId", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("UserId", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "UserIdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("UserId", KeyType.HASH),
                            new KeySchemaElement(
                                "OrganizationId",
                                KeyType.RANGE
                            ),
                        ],
                        Projection = new Projection
                        {
                            ProjectionType = ProjectionType.ALL,
                        },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    public async Task DisposeAsync()
    {
        foreach (
            var tableName in new[]
            {
                "Applications",
                "Environments",
                "EnvironmentVariables",
                "Preconditions",
                "EvidenceDefinitions",
                "Scenarios",
                "ScenariosByFolder",
                "ScenariosByTag",
                "Activities",
                "Runs",
                "RunningRuns",
                "Organizations",
                "OrganizationUsers",
            }
        )
            await _client.DeleteTableAsync(tableName);
        _client.Dispose();
    }

    private async Task CreateLibraryTableAsync(string tableName)
    {
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
                KeySchema =
                [
                    new KeySchemaElement(
                        "OrganizationId_ApplicationId",
                        KeyType.HASH
                    ),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        "OrganizationId_ApplicationId",
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition(
                        "OrganizationId",
                        ScalarAttributeType.S
                    ),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "IdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement(
                                "OrganizationId",
                                KeyType.HASH
                            ),
                            new KeySchemaElement("Id", KeyType.RANGE),
                        ],
                        Projection = new Projection
                        {
                            ProjectionType = ProjectionType.ALL,
                        },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    private async Task CreateMappingTableAsync(
        string tableName,
        string partitionKeyName
    )
    {
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
                KeySchema =
                [
                    new KeySchemaElement(partitionKeyName, KeyType.HASH),
                    new KeySchemaElement("ScenarioId", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(
                        partitionKeyName,
                        ScalarAttributeType.S
                    ),
                    new AttributeDefinition(
                        "ScenarioId",
                        ScalarAttributeType.S
                    ),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    private async Task SeedOrganizationMembershipAsync(Guid userId)
    {
        var organizationId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        await _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = "Organizations",
                Item = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new(organizationId.ToString()),
                    ["Name"] = new("Test Organization"),
                    ["IsPersonal"] = new() { BOOL = true },
                    ["CreatedByUserId"] = new(userId.ToString()),
                    ["UpdatedByUserId"] = new(userId.ToString()),
                    ["CreatedAt"] = new(now.ToString("O")),
                    ["UpdatedAt"] = new(now.ToString("O")),
                    ["LifecycleState"] = new(LifecycleState.Active.ToString()),
                },
            }
        );
        await _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = "OrganizationUsers",
                Item = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId"] = new(organizationId.ToString()),
                    ["UserId"] = new(userId.ToString()),
                    ["Role"] = new(OrganizationRole.Owner.ToString()),
                    ["CreatedByUserId"] = new(userId.ToString()),
                    ["UpdatedByUserId"] = new(userId.ToString()),
                    ["CreatedAt"] = new(DateTimeOffset.UtcNow.ToString("O")),
                    ["UpdatedAt"] = new(DateTimeOffset.UtcNow.ToString("O")),
                },
            }
        );
    }

    private static string CreateAccessToken(Guid userId)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256
        );
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
        };
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken(userId));
        return client;
    }

    private async Task<HttpClient> CreateClientWithMembershipAsync()
    {
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        return CreateAuthenticatedClient(userId);
    }

    private static async Task<Guid> CreateApplicationAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/applications",
            new CreateApplicationRequest { Name = "Test App", Description = "" }
        );
        var application =
            await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        return application!.Id;
    }

    private static async Task<Guid> CreateEnvironmentAsync(
        HttpClient client,
        Guid appId
    )
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest
            {
                Name = "Staging",
                Classification = EnvironmentClassification.NonProduction,
            }
        );
        var environment =
            await response.Content.ReadFromJsonAsync<EnvironmentResponse>();
        return environment!.Id;
    }

    private static async Task<Guid> CreateScenarioAsync(
        HttpClient client,
        Guid appId
    )
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Checkout completes",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var scenario =
            await response.Content.ReadFromJsonAsync<ScenarioResponse>();
        return scenario!.Id;
    }

    private static async Task<Guid> CreateActivityAsync(
        HttpClient client,
        Guid scenarioId
    )
    {
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = "Click checkout" }
        );
        var activity =
            await response.Content.ReadFromJsonAsync<ActivityResponse>();
        return activity!.Id;
    }

    // Seeds an Application with one Environment and one Scenario carrying two Activities, creates a Run
    // over it, and starts it. Returns (appId, runId, scenarioId, activityIds).
    private static async Task<(
        Guid AppId,
        Guid RunId,
        Guid ScenarioId,
        List<Guid> ActivityIds
    )> SeedRunningRunAsync(HttpClient client)
    {
        var appId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, appId);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var activityIds = new List<Guid>
        {
            await CreateActivityAsync(client, scenarioId),
            await CreateActivityAsync(client, scenarioId),
        };

        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest
            {
                ScenarioIds = [scenarioId],
                EnvironmentId = environmentId,
            }
        );
        var run = (
            await createResponse.Content.ReadFromJsonAsync<RunResponse>()
        )!;

        var startResponse = await client.PostAsync(
            $"/applications/{appId}/runs/{run.Id}/start",
            null
        );
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        return (appId, run.Id, scenarioId, activityIds);
    }

    [Fact]
    public async Task Append_WhenRunning_AddsOneUpdateThatListComesBackWith()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, runId, scenarioId, activityIds) = await SeedRunningRunAsync(
            client
        );

        // test
        var appendResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{runId}/status-updates",
            new AppendRunStatusUpdateRequest
            {
                Seq = 1,
                ActivityResult = new ActivityResult
                {
                    ScenarioId = scenarioId,
                    ActivityId = activityIds[0],
                    Status = ActivityResultStatus.Passed,
                },
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, appendResponse.StatusCode);
        var appended =
            await appendResponse.Content.ReadFromJsonAsync<RunStatusUpdateResponse>();
        Assert.Equal(1, appended!.Seq);

        var listResponse = await client.GetAsync(
            $"/applications/{appId}/runs/{runId}/status-updates?after=0"
        );
        var updates = await listResponse.Content.ReadFromJsonAsync<
            List<RunStatusUpdateResponse>
        >();
        var update = Assert.Single(updates!);
        Assert.Equal(activityIds[0], update.ActivityResult!.ActivityId);
        Assert.Equal(ActivityResultStatus.Passed, update.ActivityResult.Status);
    }

    [Fact]
    public async Task Append_WhenSeqAlreadyUsed_ReturnsConflictAndLeavesOneRow()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, runId, scenarioId, activityIds) = await SeedRunningRunAsync(
            client
        );
        var request = new AppendRunStatusUpdateRequest
        {
            Seq = 1,
            ActivityResult = new ActivityResult
            {
                ScenarioId = scenarioId,
                ActivityId = activityIds[0],
                Status = ActivityResultStatus.Passed,
            },
        };
        await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{runId}/status-updates",
            request
        );

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{runId}/status-updates",
            request
        );

        // verify
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var listResponse = await client.GetAsync(
            $"/applications/{appId}/runs/{runId}/status-updates?after=0"
        );
        var updates = await listResponse.Content.ReadFromJsonAsync<
            List<RunStatusUpdateResponse>
        >();
        Assert.Single(updates!);
    }

    [Theory]
    [InlineData(ActivityResultStatus.Pending, HttpStatusCode.BadRequest)]
    [InlineData(ActivityResultStatus.Running, HttpStatusCode.BadRequest)]
    [InlineData(ActivityResultStatus.Passed, HttpStatusCode.OK)]
    [InlineData(ActivityResultStatus.Failed, HttpStatusCode.OK)]
    [InlineData(ActivityResultStatus.Skipped, HttpStatusCode.OK)]
    public async Task Append_WhenActivityResultStatusIsPendingOrRunning_ReturnsBadRequest(
        ActivityResultStatus status,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, runId, scenarioId, activityIds) = await SeedRunningRunAsync(
            client
        );

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{runId}/status-updates",
            new AppendRunStatusUpdateRequest
            {
                Seq = 1,
                ActivityResult = new ActivityResult
                {
                    ScenarioId = scenarioId,
                    ActivityId = activityIds[0],
                    Status = status,
                },
            }
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(
        """{"activityResult":{"scenarioId":"11111111-1111-1111-1111-111111111111","activityId":"11111111-1111-1111-1111-111111111111","status":2}}"""
    )] // seq missing
    [InlineData("""{"seq":1}""")] // activityResult missing
    [InlineData(
        """{"seq":1,"activityResult":{"scenarioId":"11111111-1111-1111-1111-111111111111","activityId":"11111111-1111-1111-1111-111111111111","status":99}}"""
    )] // status out of enum range
    public async Task Append_WhenRequestHasInvalidShape_ReturnsBadRequest(
        string rawJson
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/runs/{Guid.CreateVersion7()}/status-updates",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Append_WhenRunDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{Guid.CreateVersion7()}/status-updates",
            new AppendRunStatusUpdateRequest
            {
                Seq = 1,
                ActivityResult = new ActivityResult
                {
                    ScenarioId = Guid.CreateVersion7(),
                    ActivityId = Guid.CreateVersion7(),
                    Status = ActivityResultStatus.Passed,
                },
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(1, HttpStatusCode.OK)]
    public async Task Append_WhenSeqAtLowerBoundary_EnforcesMinimum(
        long seq,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, runId, scenarioId, activityIds) = await SeedRunningRunAsync(
            client
        );

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{runId}/status-updates",
            new AppendRunStatusUpdateRequest
            {
                Seq = seq,
                ActivityResult = new ActivityResult
                {
                    ScenarioId = scenarioId,
                    ActivityId = activityIds[0],
                    Status = ActivityResultStatus.Passed,
                },
            }
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(2000, HttpStatusCode.OK)]
    [InlineData(2001, HttpStatusCode.BadRequest)]
    public async Task Append_WhenContinuationReasoningLengthAtBoundary_EnforcesLengthLimit(
        int continuationReasoningLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, runId, scenarioId, activityIds) = await SeedRunningRunAsync(
            client
        );

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{runId}/status-updates",
            new AppendRunStatusUpdateRequest
            {
                Seq = 1,
                ActivityResult = new ActivityResult
                {
                    ScenarioId = scenarioId,
                    ActivityId = activityIds[0],
                    Status = ActivityResultStatus.Passed,
                    ContinuationReasoning = new string(
                        'a',
                        continuationReasoningLength
                    ),
                },
            }
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task List_WhenAfterCursorGiven_ReturnsOnlyLaterUpdatesInOrder()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, runId, scenarioId, activityIds) = await SeedRunningRunAsync(
            client
        );
        for (var seq = 1; seq <= 3; seq++)
            await client.PostAsJsonAsync(
                $"/applications/{appId}/runs/{runId}/status-updates",
                new AppendRunStatusUpdateRequest
                {
                    Seq = seq,
                    ActivityResult = new ActivityResult
                    {
                        ScenarioId = scenarioId,
                        ActivityId = activityIds[0],
                        Status = ActivityResultStatus.Passed,
                    },
                }
            );

        // test
        var response = await client.GetAsync(
            $"/applications/{appId}/runs/{runId}/status-updates?after=1"
        );

        // verify
        var updates = await response.Content.ReadFromJsonAsync<
            List<RunStatusUpdateResponse>
        >();
        Assert.Equal([2L, 3L], updates!.Select(u => u.Seq));
    }

    [Fact]
    public async Task List_WhenAfterIsNegative_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.GetAsync(
            $"/applications/{appId}/runs/{Guid.CreateVersion7()}/status-updates?after=-1"
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullRoundTrip_CreateStartAppendPollEnd_ClientFoldsTheLogItAppended()
    {
        // setup -- this walks the exact flow fix_run_design.md section 7 describes: Create, keep the id;
        // List Run Status Updates from 0 and fold; poll again from the last sequence held; End Run.
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, appId);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var activity1 = await CreateActivityAsync(client, scenarioId);
        var activity2 = await CreateActivityAsync(client, scenarioId);

        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest
            {
                ScenarioIds = [scenarioId],
                EnvironmentId = environmentId,
            }
        );
        var run = (
            await createResponse.Content.ReadFromJsonAsync<RunResponse>()
        )!;

        var startResponse = await client.PostAsync(
            $"/applications/{appId}/runs/{run.Id}/start",
            null
        );
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

        // The client folds the log as it goes: a Dictionary keyed by ActivityId standing in for the
        // fold, which every real client implements its own way.
        var folded = new Dictionary<Guid, ActivityResultStatus>();

        var firstAppend = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{run.Id}/status-updates",
            new AppendRunStatusUpdateRequest
            {
                Seq = 1,
                ActivityResult = new ActivityResult
                {
                    ScenarioId = scenarioId,
                    ActivityId = activity1,
                    Status = ActivityResultStatus.Passed,
                },
            }
        );
        Assert.Equal(HttpStatusCode.OK, firstAppend.StatusCode);

        // First poll, from 0.
        var firstPollResponse = await client.GetAsync(
            $"/applications/{appId}/runs/{run.Id}/status-updates?after=0"
        );
        var firstPoll = await firstPollResponse.Content.ReadFromJsonAsync<
            List<RunStatusUpdateResponse>
        >();
        foreach (var update in firstPoll!)
            folded[update.ActivityResult!.ActivityId] = update
                .ActivityResult
                .Status;
        var lastSeqHeld = firstPoll.Max(u => u.Seq);

        var secondAppend = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{run.Id}/status-updates",
            new AppendRunStatusUpdateRequest
            {
                Seq = 2,
                ActivityResult = new ActivityResult
                {
                    ScenarioId = scenarioId,
                    ActivityId = activity2,
                    Status = ActivityResultStatus.Failed,
                },
            }
        );
        Assert.Equal(HttpStatusCode.OK, secondAppend.StatusCode);

        // Second poll, from the last sequence the client already holds -- proving it gets only what is
        // new.
        var secondPollResponse = await client.GetAsync(
            $"/applications/{appId}/runs/{run.Id}/status-updates?after={lastSeqHeld}"
        );
        var secondPoll = await secondPollResponse.Content.ReadFromJsonAsync<
            List<RunStatusUpdateResponse>
        >();
        var onlyNewUpdate = Assert.Single(secondPoll!);
        Assert.Equal(2, onlyNewUpdate.Seq);
        foreach (var update in secondPoll!)
            folded[update.ActivityResult!.ActivityId] = update
                .ActivityResult
                .Status;

        var statsResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{run.Id}/stats",
            new UpdateRunStatsRequest
            {
                TotalActivityCount = 2,
                PassedActivityCount = 1,
                FailedActivityCount = 1,
                SkippedActivityCount = 0,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, statsResponse.StatusCode);

        var endResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{run.Id}/end",
            new EndRunRequest { TerminalStatus = RunStatus.Completed }
        );
        Assert.Equal(HttpStatusCode.NoContent, endResponse.StatusCode);

        // verify -- the client's folded view matches exactly what it appended, and the finished Run's
        // own header agrees.
        Assert.Equal(
            new Dictionary<Guid, ActivityResultStatus>
            {
                [activity1] = ActivityResultStatus.Passed,
                [activity2] = ActivityResultStatus.Failed,
            },
            folded
        );

        var finalGet = await client.GetAsync(
            $"/applications/{appId}/runs/{run.Id}"
        );
        var final = await finalGet.Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(RunStatus.Completed, final!.Status);
        Assert.Equal(2, final.LastSeq);
        Assert.Equal(1, final.PassedActivityCount);
        Assert.Equal(1, final.FailedActivityCount);
    }

    [Fact]
    public async Task Append_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsJsonAsync(
                $"/applications/{Guid.CreateVersion7()}/runs/{Guid.CreateVersion7()}/status-updates",
                new AppendRunStatusUpdateRequest
                {
                    Seq = 1,
                    ActivityResult = new ActivityResult
                    {
                        ScenarioId = Guid.CreateVersion7(),
                        ActivityId = Guid.CreateVersion7(),
                        Status = ActivityResultStatus.Passed,
                    },
                }
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .GetAsync(
                $"/applications/{Guid.CreateVersion7()}/runs/{Guid.CreateVersion7()}/status-updates"
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
