using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using A2.Server.Contracts;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace A2.Server.Tests.Controllers;

/// <summary>Integration tests for <see cref="A2.Server.Controllers.RunsController"/> over real HTTP,
/// against DynamoDB Local: Create Run (Manual), Get Run, List Runs, Start Run and End Run, plus the
/// nested route's read-after-write fix, snapshot immutability, and the 404/409 rules.</summary>
[Collection("DynamoDbLocal")]
public sealed class RunsControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public RunsControllerTests(
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
                            ["DynamoDb:EnvironmentVariableTableName"] = "EnvironmentVariables",
                            ["DynamoDb:PreconditionTableName"] = "Preconditions",
                            ["DynamoDb:EvidenceDefinitionTableName"] = "EvidenceDefinitions",
                            ["DynamoDb:ScenarioTableName"] = "Scenarios",
                            ["DynamoDb:ScenariosByFolderTableName"] = "ScenariosByFolder",
                            ["DynamoDb:ScenariosByTagTableName"] = "ScenariosByTag",
                            ["DynamoDb:ActivityTableName"] = "Activities",
                            ["DynamoDb:RunTableName"] = "Runs",
                            ["DynamoDb:OrganizationTableName"] = "Organizations",
                            ["DynamoDb:OrganizationUserTableName"] = "OrganizationUsers",
                        }
                    )
            );
            builder.ConfigureServices(services =>
            {
                _client = dynamoDbLocalFixture.CreateClient();
                services.Replace(ServiceDescriptor.Singleton<IAmazonDynamoDB>(_client));
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
                    new AttributeDefinition("OrganizationId", ScalarAttributeType.S),
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
                    new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ApplicationId", ScalarAttributeType.S),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition("OrganizationId", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "IdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("OrganizationId", KeyType.HASH),
                            new KeySchemaElement("Id", KeyType.RANGE),
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL },
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
                    new KeySchemaElement("OrganizationId_EnvironmentId", KeyType.HASH),
                    new KeySchemaElement("Key", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_EnvironmentId", ScalarAttributeType.S),
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
                    new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ApplicationId", ScalarAttributeType.S),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition("OrganizationId", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "IdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("OrganizationId", KeyType.HASH),
                            new KeySchemaElement("Id", KeyType.RANGE),
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await CreateMappingTableAsync("ScenariosByFolder", "OrganizationId_ApplicationId_Folder");
        await CreateMappingTableAsync("ScenariosByTag", "OrganizationId_ApplicationId_Tag");

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "Activities",
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId_ScenarioId", KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ScenarioId", ScalarAttributeType.S),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition("OrganizationId", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "IdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("OrganizationId", KeyType.HASH),
                            new KeySchemaElement("Id", KeyType.RANGE),
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        // Mirrors ../../autoassure-infra/dynamodb.tf's Runs table exactly -- same sparse index shapes --
        // so these tests prove what the real indexes actually return.
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "Runs",
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                    new KeySchemaElement("RowKey", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ApplicationId", ScalarAttributeType.S),
                    new AttributeDefinition("RowKey", ScalarAttributeType.S),
                    new AttributeDefinition("HeaderId", ScalarAttributeType.S),
                    new AttributeDefinition("InFlightShard", ScalarAttributeType.S),
                    new AttributeDefinition("LastHeartbeatAt", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "RunHeaderIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
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
                                "StatusReason",
                                "TotalActivityCount",
                                "PassedActivityCount",
                                "FailedActivityCount",
                                "SkippedActivityCount",
                                "CreatedAt",
                                "StartedAt",
                                "CompletedAt",
                            ],
                        },
                    },
                    new GlobalSecondaryIndex
                    {
                        IndexName = "InFlightIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("InFlightShard", KeyType.HASH),
                            new KeySchemaElement("LastHeartbeatAt", KeyType.RANGE),
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.KEYS_ONLY },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );

        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = "Organizations",
                KeySchema = [new KeySchemaElement("Id", KeyType.HASH)],
                AttributeDefinitions = [new AttributeDefinition("Id", ScalarAttributeType.S)],
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
                    new AttributeDefinition("OrganizationId", ScalarAttributeType.S),
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
                            new KeySchemaElement("OrganizationId", KeyType.RANGE),
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    private async Task CreateLibraryTableAsync(string tableName)
    {
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
                KeySchema =
                [
                    new KeySchemaElement("OrganizationId_ApplicationId", KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("OrganizationId_ApplicationId", ScalarAttributeType.S),
                    new AttributeDefinition("Id", ScalarAttributeType.S),
                    new AttributeDefinition("OrganizationId", ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes =
                [
                    new GlobalSecondaryIndex
                    {
                        IndexName = "IdIndex",
                        KeySchema =
                        [
                            new KeySchemaElement("OrganizationId", KeyType.HASH),
                            new KeySchemaElement("Id", KeyType.RANGE),
                        ],
                        Projection = new Projection { ProjectionType = ProjectionType.ALL },
                    },
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    private async Task CreateMappingTableAsync(string tableName, string partitionKeyName)
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
                    new AttributeDefinition(partitionKeyName, ScalarAttributeType.S),
                    new AttributeDefinition("ScenarioId", ScalarAttributeType.S),
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
                "Organizations",
                "OrganizationUsers",
            }
        )
        {
            await _client.DeleteTableAsync(tableName);
        }
        _client.Dispose();
    }

    private async Task SeedOrganizationMembershipAsync(Guid userId)
    {
        var organizationId = Guid.CreateVersion7();
        await _client.PutItemAsync(
            new PutItemRequest
            {
                TableName = "Organizations",
                Item = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new(organizationId.ToString()),
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
                    ["Role"] = new(Models.OrganizationRole.Owner.ToString()),
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
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()) };
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateAccessToken(userId)
        );
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
        var application = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        return application!.Id;
    }

    private static async Task<Guid> CreateEnvironmentAsync(HttpClient client, Guid appId)
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest
            {
                Name = "Staging",
                Classification = EnvironmentClassification.NonProduction,
            }
        );
        var environment = await response.Content.ReadFromJsonAsync<EnvironmentResponse>();
        return environment!.Id;
    }

    private static async Task<Guid> CreateScenarioAsync(
        HttpClient client,
        Guid appId,
        string title = "Checkout completes"
    )
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = title,
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var scenario = await response.Content.ReadFromJsonAsync<ScenarioResponse>();
        return scenario!.Id;
    }

    private static async Task<Guid> CreateActivityAsync(
        HttpClient client,
        Guid scenarioId,
        IReadOnlyList<Guid>? preconditionIds = null,
        IReadOnlyList<Guid>? evidenceIds = null
    )
    {
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest
            {
                Description = "Click checkout",
                PreconditionIds = preconditionIds,
                EvidenceIds = evidenceIds,
            }
        );
        var activity = await response.Content.ReadFromJsonAsync<ActivityResponse>();
        return activity!.Id;
    }

    // Seeds an Application with one Environment and one Scenario carrying one Activity, ready to create
    // a Run over. Returns (appId, environmentId, scenarioId).
    private static async Task<(
        Guid AppId,
        Guid EnvironmentId,
        Guid ScenarioId
    )> SeedRunnableAppAsync(HttpClient client, string scenarioTitle = "Checkout completes")
    {
        var appId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, appId);
        var scenarioId = await CreateScenarioAsync(client, appId, scenarioTitle);
        await CreateActivityAsync(client, scenarioId);
        return (appId, environmentId, scenarioId);
    }

    private static async Task<RunResponse> CreateRunAsync(
        HttpClient client,
        Guid appId,
        IReadOnlyList<Guid> scenarioIds,
        Guid environmentId
    )
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest { ScenarioIds = scenarioIds, EnvironmentId = environmentId }
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RunResponse>())!;
    }

    [Fact]
    public async Task Create_WhenValidRequest_CreatesPendingRunWithSnapshotAndCountedActivities()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);

        // test
        var run = await CreateRunAsync(client, appId, [scenarioId], environmentId);

        // verify
        Assert.Equal(appId, run.ApplicationId);
        Assert.Equal(RunTrigger.Manual, run.Trigger);
        Assert.Equal(RunStatus.Pending, run.Status);
        Assert.Equal(0, run.LastSeq);
        Assert.Equal(1, run.TotalActivityCount);
        var scenario = Assert.Single(run.Scenarios);
        Assert.Equal("Checkout completes", scenario.Title);
        Assert.Single(scenario.Activities);
    }

    [Fact]
    public async Task CreateThenGet_InSameRequest_IsFoundImmediately()
    {
        // setup -- this is the read-after-write 404 the nested route + ConsistentRead fixes.
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);

        // test
        var getResponse = await client.GetAsync($"/applications/{appId}/runs/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task GetById_WhenRunBelongsToAnotherApplication_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        var otherAppId = await CreateApplicationAsync(client);

        // test
        var response = await client.GetAsync($"/applications/{otherAppId}/runs/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WhenRunDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.GetAsync($"/applications/{appId}/runs/{Guid.CreateVersion7()}");

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditingScenarioAfterRun_DoesNotChangeWhatTheRunReports()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(
            client,
            "Original Title"
        );
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);

        // test -- edit the live Scenario after the Run was created
        var patchResponse = await client.PatchAsJsonAsync(
            $"/scenarios/{scenarioId}",
            new UpdateScenarioRequest
            {
                Title = "Changed After The Run",
                Description = "Changed",
                Folder = "/",
                Tags = null,
            }
        );
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        // verify -- the Run's own snapshot is untouched
        var getResponse = await client.GetAsync($"/applications/{appId}/runs/{created.Id}");
        var fetched = await getResponse.Content.ReadFromJsonAsync<RunResponse>();
        var scenario = Assert.Single(fetched!.Scenarios);
        Assert.Equal("Original Title", scenario.Title);
    }

    [Fact]
    public async Task Create_CopiesReferencedPreconditionsAndEvidenceDefinitionsWhole()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, appId);
        var scenarioId = await CreateScenarioAsync(client, appId);

        var preconditionResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/preconditions",
            new CreatePreconditionRequest
            {
                Name = "Logged in",
                ValueSource = PreconditionValueSource.SpecificValue,
                ExampleValue = "token-123",
            }
        );
        var precondition = (
            await preconditionResponse.Content.ReadFromJsonAsync<PreconditionResponse>()
        )!;

        var evidenceResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/evidence-definitions",
            new CreateEvidenceDefinitionRequest
            {
                Name = "Screenshot",
                Description = "A screenshot of the result",
                ExampleValue = "screenshot.png",
            }
        );
        var evidence = (
            await evidenceResponse.Content.ReadFromJsonAsync<EvidenceDefinitionResponse>()
        )!;

        var activityId = await CreateActivityAsync(
            client,
            scenarioId,
            [precondition.Id],
            [evidence.Id]
        );

        // test
        var run = await CreateRunAsync(client, appId, [scenarioId], environmentId);

        // verify
        var activity = Assert.Single(Assert.Single(run.Scenarios).Activities);
        Assert.Equal(activityId, activity.Source.Id);
        var copiedPrecondition = Assert.Single(activity.Preconditions);
        Assert.Equal("Logged in", copiedPrecondition.Name);
        var copiedEvidence = Assert.Single(activity.EvidenceDefinitions);
        Assert.Equal("Screenshot", copiedEvidence.Name);
    }

    [Fact]
    public async Task List_ReturnsSummariesAndExcludesAuthoringRuns()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var manualRun = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        var authoringResponse = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/runs",
            new CreateAuthoringRunRequest { EnvironmentId = environmentId }
        );
        Assert.Equal(HttpStatusCode.OK, authoringResponse.StatusCode);

        // test
        var listResponse = await client.GetAsync($"/applications/{appId}/runs");

        // verify
        var summaries = await listResponse.Content.ReadFromJsonAsync<List<RunSummaryResponse>>();
        var summary = Assert.Single(summaries!);
        Assert.Equal(manualRun.Id, summary.Id);
    }

    [Fact]
    public async Task Start_WhenPending_Succeeds()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);

        // test
        var startResponse = await client.PostAsync(
            $"/applications/{appId}/runs/{created.Id}/start",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, startResponse.StatusCode);
        var fetched = await (
            await client.GetAsync($"/applications/{appId}/runs/{created.Id}")
        ).Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(RunStatus.Running, fetched!.Status);
    }

    [Fact]
    public async Task Start_WhenAlreadyStarted_ReturnsConflict()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        await client.PostAsync($"/applications/{appId}/runs/{created.Id}/start", null);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/runs/{created.Id}/start",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Start_WhenRunDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/runs/{Guid.CreateVersion7()}/start",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task End_WhenRunning_Succeeds()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        await client.PostAsync($"/applications/{appId}/runs/{created.Id}/start", null);

        // test
        var endResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{created.Id}/end",
            new EndRunRequest { TerminalStatus = RunStatus.Completed, StatusReason = null }
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, endResponse.StatusCode);
        var fetched = await (
            await client.GetAsync($"/applications/{appId}/runs/{created.Id}")
        ).Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(RunStatus.Completed, fetched!.Status);
    }

    [Fact]
    public async Task End_WhenCalledTwice_ReturnsConflictTheSecondTime()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        await client.PostAsync($"/applications/{appId}/runs/{created.Id}/start", null);
        var endRequest = new EndRunRequest
        {
            TerminalStatus = RunStatus.Completed,
            StatusReason = null,
        };
        var firstEnd = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{created.Id}/end",
            endRequest
        );
        Assert.Equal(HttpStatusCode.NoContent, firstEnd.StatusCode);

        // test
        var secondEnd = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{created.Id}/end",
            endRequest
        );

        // verify
        Assert.Equal(HttpStatusCode.Conflict, secondEnd.StatusCode);
    }

    [Fact]
    public async Task End_WhenNeverStarted_ReturnsConflict()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{created.Id}/end",
            new EndRunRequest { TerminalStatus = RunStatus.Completed, StatusReason = null }
        );

        // verify
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Running)]
    public async Task End_WhenTerminalStatusIsNotTerminal_ReturnsBadRequest(RunStatus notTerminal)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        await client.PostAsync($"/applications/{appId}/runs/{created.Id}/start", null);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{created.Id}/end",
            new EndRunRequest { TerminalStatus = notTerminal, StatusReason = null }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task End_WhenStatusReasonSetForNonAbandoned_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        await client.PostAsync($"/applications/{appId}/runs/{created.Id}/start", null);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{created.Id}/end",
            new EndRunRequest
            {
                TerminalStatus = RunStatus.Completed,
                StatusReason = RunStatusReason.WorkerCrashed,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRun_WhenRunHasStatusUpdates_ReturnsNoStatusUpdateData()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        await client.PostAsync($"/applications/{appId}/runs/{created.Id}/start", null);
        var activityId = created.Scenarios[0].Activities[0].Source.Id;
        for (var seq = 1; seq <= 2; seq++)
        {
            var appendResponse = await client.PostAsJsonAsync(
                $"/applications/{appId}/runs/{created.Id}/status-updates",
                new AppendRunStatusUpdateRequest
                {
                    Seq = seq,
                    ActivityResult = new ActivityResult
                    {
                        ScenarioId = scenarioId,
                        ActivityId = activityId,
                        Status = ActivityResultStatus.Passed,
                    },
                }
            );
            Assert.Equal(HttpStatusCode.OK, appendResponse.StatusCode);
        }

        // test
        var getResponse = await client.GetAsync($"/applications/{appId}/runs/{created.Id}");

        // verify -- LastSeq reflects the appended updates, but the response's own top-level shape
        // carries nothing from the log itself: no per-update field exists on it at all.
        var raw = await getResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(raw);
        var topLevelPropertyNames = document
            .RootElement.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet();
        Assert.Equal(
            new HashSet<string>
            {
                "id",
                "applicationId",
                "trigger",
                "status",
                "statusReason",
                "totalActivityCount",
                "passedActivityCount",
                "failedActivityCount",
                "skippedActivityCount",
                "environment",
                "scenarios",
                "lastSeq",
                "triggeredByUserId",
                "createdAt",
                "startedAt",
                "completedAt",
            },
            topLevelPropertyNames
        );
        var fetched = await getResponse.Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(2, fetched!.LastSeq);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(1, HttpStatusCode.OK)]
    public async Task Create_WhenScenarioIdsCountAtLowerBoundary_EnforcesMinCount(
        int scenarioCount,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var scenarioIds = scenarioCount == 0 ? [] : new List<Guid> { scenarioId };

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest { ScenarioIds = scenarioIds, EnvironmentId = environmentId }
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenScenarioIdsCountAtUpperBoundary_Succeeds()
    {
        // setup -- Quota.MaxScenariosPerRun is 97; every id must reference a real Scenario belonging to
        // this Application for the request to pass validation and actually succeed.
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, appId);
        var scenarioIds = new List<Guid>();
        for (var i = 0; i < 97; i++)
        {
            scenarioIds.Add(await CreateScenarioAsync(client, appId, $"Scenario {i}"));
        }

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest { ScenarioIds = scenarioIds, EnvironmentId = environmentId }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenScenarioIdsCountExceedsUpperBoundary_ReturnsBadRequest()
    {
        // setup -- 98 exceeds Quota.MaxScenariosPerRun (97), so [MaxLength] rejects this before any id is
        // looked up -- the ids need not reference real Scenarios.
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, appId);
        var scenarioIds = Enumerable.Range(0, 98).Select(_ => Guid.CreateVersion7()).ToList();

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest { ScenarioIds = scenarioIds, EnvironmentId = environmentId }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenScenarioIdsHasDuplicate_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest
            {
                ScenarioIds = [scenarioId, scenarioId],
                EnvironmentId = environmentId,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenScenarioIdBelongsToAnotherApplication_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, _) = await SeedRunnableAppAsync(client);
        var otherAppId = await CreateApplicationAsync(client);
        var otherScenarioId = await CreateScenarioAsync(client, otherAppId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest { ScenarioIds = [otherScenarioId], EnvironmentId = environmentId }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenEnvironmentBelongsToAnotherApplication_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, _, scenarioId) = await SeedRunnableAppAsync(client);
        var otherAppId = await CreateApplicationAsync(client);
        var otherEnvironmentId = await CreateEnvironmentAsync(client, otherAppId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest { ScenarioIds = [scenarioId], EnvironmentId = otherEnvironmentId }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenApplicationDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{Guid.CreateVersion7()}/runs",
            new CreateRunRequest
            {
                ScenarioIds = [Guid.CreateVersion7()],
                EnvironmentId = Guid.CreateVersion7(),
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"environmentId":"11111111-1111-1111-1111-111111111111"}""")] // scenarioIds missing
    [InlineData("""{"scenarioIds":["11111111-1111-1111-1111-111111111111"]}""")] // environmentId missing
    public async Task Create_WhenRequestHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/runs",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStats_WhenCountIsNegative_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        await client.PostAsync($"/applications/{appId}/runs/{created.Id}/start", null);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{created.Id}/stats",
            new UpdateRunStatsRequest
            {
                TotalActivityCount = -1,
                PassedActivityCount = 0,
                FailedActivityCount = 0,
                SkippedActivityCount = 0,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStats_WhenRunning_Succeeds()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        await client.PostAsync($"/applications/{appId}/runs/{created.Id}/start", null);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs/{created.Id}/stats",
            new UpdateRunStatsRequest
            {
                TotalActivityCount = 1,
                PassedActivityCount = 1,
                FailedActivityCount = 0,
                SkippedActivityCount = 0,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var fetched = await (
            await client.GetAsync($"/applications/{appId}/runs/{created.Id}")
        ).Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(1, fetched!.PassedActivityCount);
    }

    [Fact]
    public async Task Heartbeat_WhenRunning_Succeeds()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);
        await client.PostAsync($"/applications/{appId}/runs/{created.Id}/start", null);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/runs/{created.Id}/heartbeat",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_WhenNotRunning_ReturnsConflict()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var (appId, environmentId, scenarioId) = await SeedRunnableAppAsync(client);
        var created = await CreateRunAsync(client, appId, [scenarioId], environmentId);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/runs/{created.Id}/heartbeat",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsJsonAsync(
                $"/applications/{Guid.CreateVersion7()}/runs",
                new CreateRunRequest
                {
                    ScenarioIds = [Guid.CreateVersion7()],
                    EnvironmentId = Guid.CreateVersion7(),
                }
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .GetAsync($"/applications/{Guid.CreateVersion7()}/runs/{Guid.CreateVersion7()}");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .GetAsync($"/applications/{Guid.CreateVersion7()}/runs");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Start_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsync(
                $"/applications/{Guid.CreateVersion7()}/runs/{Guid.CreateVersion7()}/start",
                null
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task End_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsJsonAsync(
                $"/applications/{Guid.CreateVersion7()}/runs/{Guid.CreateVersion7()}/end",
                new EndRunRequest { TerminalStatus = RunStatus.Completed, StatusReason = null }
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Heartbeat_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsync(
                $"/applications/{Guid.CreateVersion7()}/runs/{Guid.CreateVersion7()}/heartbeat",
                null
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStats_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsJsonAsync(
                $"/applications/{Guid.CreateVersion7()}/runs/{Guid.CreateVersion7()}/stats",
                new UpdateRunStatsRequest
                {
                    TotalActivityCount = 0,
                    PassedActivityCount = 0,
                    FailedActivityCount = 0,
                    SkippedActivityCount = 0,
                }
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
