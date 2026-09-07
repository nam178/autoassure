using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using A2.Server.Contracts;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace A2.Server.Tests.Controllers;

/// <summary>Integration tests for <see cref="A2.Server.Controllers.AuthoringRunsController"/> over real
/// HTTP, against DynamoDB Local: the Try button's flat <c>POST /scenarios/{id}/runs</c> route, and that
/// its response carries ApplicationId so a client can build the nested polling URLs even though this
/// create route itself is not nested.</summary>
[Collection("DynamoDbLocal")]
public sealed class AuthoringRunsControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public AuthoringRunsControllerTests(
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
                Item = new Dictionary<string, AttributeValue> { ["Id"] = new(organizationId.ToString()) },
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

    private static async Task<Guid> CreateScenarioAsync(HttpClient client, Guid appId)
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Try this",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var scenario = await response.Content.ReadFromJsonAsync<ScenarioResponse>();
        return scenario!.Id;
    }

    [Fact]
    public async Task Create_WhenValidRequest_ReturnsAuthoringRunCarryingApplicationId()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, appId);
        var scenarioId = await CreateScenarioAsync(client, appId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/runs",
            new CreateAuthoringRunRequest { EnvironmentId = environmentId }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<RunResponse>();
        Assert.Equal(appId, run!.ApplicationId);
        Assert.Equal(RunTrigger.Authoring, run.Trigger);
        Assert.Equal(scenarioId, Assert.Single(run.Scenarios).Source.Id);
    }

    [Fact]
    public async Task Create_ThenGetUsingResponsesApplicationId_BuildsAWorkingPollingUrl()
    {
        // setup -- this route is flat, so the client has no appId of its own; it must come from the
        // response.
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, appId);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var createResponse = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/runs",
            new CreateAuthoringRunRequest { EnvironmentId = environmentId }
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<RunResponse>())!;

        // test
        var getResponse = await client.GetAsync(
            $"/applications/{created.ApplicationId}/runs/{created.Id}"
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task Create_WhenScenarioDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{Guid.CreateVersion7()}/runs",
            new CreateAuthoringRunRequest { EnvironmentId = Guid.CreateVersion7() }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenEnvironmentBelongsToAnotherApplication_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var otherAppId = await CreateApplicationAsync(client);
        var otherEnvironmentId = await CreateEnvironmentAsync(client, otherAppId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/runs",
            new CreateAuthoringRunRequest { EnvironmentId = otherEnvironmentId }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsJsonAsync(
                $"/scenarios/{Guid.CreateVersion7()}/runs",
                new CreateAuthoringRunRequest { EnvironmentId = Guid.CreateVersion7() }
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
