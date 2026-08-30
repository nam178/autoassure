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

/// <summary>Integration tests for <see cref="A2.Server.Controllers.TriesController"/> and
/// <see cref="A2.Server.Controllers.RunsController"/> over real HTTP, against DynamoDB Local. Covers only
/// creation -- execution is stubbed for this steel thread.</summary>
[Collection("DynamoDbLocal")]
public sealed class TriesAndRunsControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public TriesAndRunsControllerTests(
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
                            ["DynamoDb:ScenarioTableName"] = "Scenarios",
                            ["DynamoDb:ScenariosByFolderTableName"] = "ScenariosByFolder",
                            ["DynamoDb:ScenariosByTagTableName"] = "ScenariosByTag",
                            ["DynamoDb:RunTableName"] = "Runs",
                            ["DynamoDb:TryTableName"] = "Tries",
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

        await CreateMappingTableAsync("ScenariosByFolder");
        await CreateMappingTableAsync("ScenariosByTag");

        await CreateRunTableAsync("Runs", "OrganizationId_ApplicationId");
        await CreateRunTableAsync("Tries", "OrganizationId_ScenarioId");

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

    private async Task CreateMappingTableAsync(string tableName)
    {
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
                KeySchema =
                [
                    new KeySchemaElement("PartitionKey", KeyType.HASH),
                    new KeySchemaElement("ScenarioId", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition("PartitionKey", ScalarAttributeType.S),
                    new AttributeDefinition("ScenarioId", ScalarAttributeType.S),
                ],
                BillingMode = BillingMode.PAY_PER_REQUEST,
            }
        );
    }

    private async Task CreateRunTableAsync(string tableName, string partitionKeyName)
    {
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
                KeySchema =
                [
                    new KeySchemaElement(partitionKeyName, KeyType.HASH),
                    new KeySchemaElement("Id", KeyType.RANGE),
                ],
                AttributeDefinitions =
                [
                    new AttributeDefinition(partitionKeyName, ScalarAttributeType.S),
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

    public async Task DisposeAsync()
    {
        foreach (
            var tableName in new[]
            {
                "Applications",
                "Scenarios",
                "Environments",
                "EnvironmentVariables",
                "ScenariosByFolder",
                "ScenariosByTag",
                "Runs",
                "Tries",
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
            new CreateApplicationRequest("Test App", "")
        );
        var application = await response.Content.ReadFromJsonAsync<ApplicationResponse>();
        return application!.Id;
    }

    private static async Task<Guid> CreateScenarioAsync(HttpClient client, Guid appId)
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest("Title", "Description", null, null, null)
        );
        var scenario = await response.Content.ReadFromJsonAsync<ScenarioResponse>();
        return scenario!.Id;
    }

    private static async Task<Guid> CreateEnvironmentAsync(HttpClient client, Guid appId)
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var environment = await response.Content.ReadFromJsonAsync<EnvironmentResponse>();
        return environment!.Id;
    }

    [Fact]
    public async Task PostTry_WhenScenarioExists_CreatesPendingTryInTriesTableNotRuns()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var environmentId = await CreateEnvironmentAsync(client, appId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/try",
            new CreateTryRequest(environmentId)
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TryScenarioResponse>();
        Assert.NotNull(created);
        Assert.Equal(scenarioId, created.ScenarioId);
        Assert.Equal(environmentId, created.EnvironmentId);
        Assert.Equal(RunStatus.Pending, created.Status);

        var triesScanResponse = await _client.ScanAsync(new ScanRequest { TableName = "Tries" });
        var triesItem = Assert.Single(triesScanResponse.Items);
        Assert.Equal(created.Id.ToString(), triesItem["Id"].S);

        var runsScanResponse = await _client.ScanAsync(new ScanRequest { TableName = "Runs" });
        Assert.Empty(runsScanResponse.Items);

        // test
        var getResponse = await client.GetAsync($"/tries/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task PostRun_WhenScenarioExists_CreatesPendingRunInRunsTableNotTries()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var environmentId = await CreateEnvironmentAsync(client, appId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest([scenarioId], environmentId)
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RunResponse>();
        Assert.NotNull(created);
        Assert.Equal(RunStatus.Pending, created.Status);
        Assert.Equal(environmentId, created.EnvironmentId);

        var triesScanResponse = await _client.ScanAsync(new ScanRequest { TableName = "Tries" });
        Assert.Empty(triesScanResponse.Items);

        // test
        var getResponse = await client.GetAsync($"/runs/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task ListRuns_WhenTriesAndRunsExist_NeverReturnsTries()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var environmentId = await CreateEnvironmentAsync(client, appId);
        await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/try",
            new CreateTryRequest(environmentId)
        );
        await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest([scenarioId], environmentId)
        );

        // test
        var listResponse = await client.GetAsync($"/applications/{appId}/runs");
        var runs = await listResponse.Content.ReadFromJsonAsync<List<RunResponse>>();

        // verify
        Assert.Single(runs!);
    }

    [Fact]
    public async Task PostRun_WhenApplicationDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{Guid.CreateVersion7()}/runs",
            new CreateRunRequest([Guid.CreateVersion7()], Guid.CreateVersion7())
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostTry_WhenScenarioDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{Guid.CreateVersion7()}/try",
            new CreateTryRequest(Guid.CreateVersion7())
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostTry_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsync($"/scenarios/{Guid.CreateVersion7()}/try", null);

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTryById_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory.CreateClient().GetAsync($"/tries/{Guid.CreateVersion7()}");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRun_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsJsonAsync(
                $"/applications/{Guid.CreateVersion7()}/runs",
                new CreateRunRequest([Guid.CreateVersion7()], Guid.CreateVersion7())
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListRuns_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .GetAsync($"/applications/{Guid.CreateVersion7()}/runs");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRunById_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory.CreateClient().GetAsync($"/runs/{Guid.CreateVersion7()}");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRun_WhenScenarioIdsIsEmpty_IsRejected()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest([], Guid.CreateVersion7())
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostRun_WhenEnvironmentDoesNotExist_IsRejected()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest([scenarioId], Guid.CreateVersion7())
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostRun_WhenEnvironmentBelongsToDifferentApplication_IsRejected()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var otherAppId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, otherAppId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/runs",
            new CreateRunRequest([scenarioId], environmentId)
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTry_WhenEnvironmentDoesNotExist_IsRejected()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/try",
            new CreateTryRequest(Guid.CreateVersion7())
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTry_WhenEnvironmentBelongsToDifferentApplication_IsRejected()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var otherAppId = await CreateApplicationAsync(client);
        var environmentId = await CreateEnvironmentAsync(client, otherAppId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/try",
            new CreateTryRequest(environmentId)
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{}""")] // environmentId missing entirely
    [InlineData("""{"environmentId":null}""")] // environmentId explicitly null
    [InlineData("""{"environmentId":123}""")] // environmentId wrong type
    public async Task PostTry_WhenRequestHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);

        // test
        var response = await client.PostAsync(
            $"/scenarios/{scenarioId}/try",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"environmentId":"11111111-1111-1111-1111-111111111111"}""")] // scenarioIds missing entirely
    [InlineData("""{"scenarioIds":null,"environmentId":"11111111-1111-1111-1111-111111111111"}""")] // scenarioIds explicitly null
    [InlineData(
        """{"scenarioIds":"not-an-array","environmentId":"11111111-1111-1111-1111-111111111111"}"""
    )] // scenarioIds wrong type
    [InlineData("""{"scenarioIds":["11111111-1111-1111-1111-111111111111"],"environmentId":123}""")] // environmentId wrong type
    public async Task PostRun_WhenRequestHasInvalidShape_ReturnsBadRequest(string rawJson)
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
}
