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

/// <summary>Integration tests for <see cref="A2.Server.Controllers.ActivitiesController"/> over real
/// HTTP, against DynamoDB Local.</summary>
[Collection("DynamoDbLocal")]
public sealed class ActivitiesControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public ActivitiesControllerTests(
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
                            ["DynamoDb:PreconditionTableName"] = "Preconditions",
                            ["DynamoDb:EvidenceDefinitionTableName"] = "EvidenceDefinitions",
                            ["DynamoDb:ScenarioTableName"] = "Scenarios",
                            ["DynamoDb:ScenariosByFolderTableName"] = "ScenariosByFolder",
                            ["DynamoDb:ScenariosByTagTableName"] = "ScenariosByTag",
                            ["DynamoDb:ActivityTableName"] = "Activities",
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
                "Preconditions",
                "EvidenceDefinitions",
                "Scenarios",
                "ScenariosByFolder",
                "ScenariosByTag",
                "Activities",
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

    private static async Task<Guid> CreateScenarioAsync(HttpClient client, Guid appId)
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var scenario = await response.Content.ReadFromJsonAsync<ScenarioResponse>();
        return scenario!.Id;
    }

    private static async Task<Guid> CreatePreconditionAsync(HttpClient client, Guid appId)
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/preconditions",
            new CreatePreconditionRequest
            {
                Name = "Order ID",
                ValueSource = PreconditionValueSource.SpecificValue,
                ExampleValue = "ORD-1",
            }
        );
        var precondition = await response.Content.ReadFromJsonAsync<PreconditionResponse>();
        return precondition!.Id;
    }

    [Fact]
    public async Task Create_WhenValidRequest_RoundTripsThroughGetUpdateDelete()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var preconditionId = await CreatePreconditionAsync(client, appId);

        // test
        var createResponse = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest
            {
                Description = "Add item to cart",
                PreconditionIds = [preconditionId],
                EvidenceIds = [],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ActivityResponse>();
        Assert.NotNull(created);
        Assert.Equal("Add item to cart", created.Description);
        Assert.Equal(0, created.Order);
        Assert.Equal(preconditionId, Assert.Single(created.PreconditionIds));

        // test
        var patchResponse = await client.PatchAsJsonAsync(
            $"/activities/{created.Id}",
            new UpdateActivityRequest
            {
                Description = "Updated step",
                PreconditionIds = [],
                EvidenceIds = [],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var updated = await patchResponse.Content.ReadFromJsonAsync<ActivityResponse>();
        Assert.Equal("Updated step", updated!.Description);
        Assert.Empty(updated.PreconditionIds);

        // test
        var deleteResponse = await client.DeleteAsync($"/activities/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // test
        var listResponse = await client.GetAsync($"/scenarios/{scenarioId}/activities");

        // verify
        var list = await listResponse.Content.ReadFromJsonAsync<List<ActivityResponse>>();
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Create_WhenPreconditionIdDoesNotExist_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest
            {
                Description = "Step",
                PreconditionIds = [Guid.CreateVersion7()],
                EvidenceIds = [],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenScenarioDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{Guid.CreateVersion7()}/activities",
            new CreateActivityRequest { Description = "Step" }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenScenarioAlreadyHasMaxActivities_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        for (var i = 0; i < 90; i++)
        {
            await client.PostAsJsonAsync(
                $"/scenarios/{scenarioId}/activities",
                new CreateActivityRequest { Description = "Step" }
            );
        }

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = "One too many" }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_WhenMultipleActivitiesExist_ReturnsOrderedByCreationOrder()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = "First" }
        );
        await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = "Second" }
        );

        // test
        var response = await client.GetAsync($"/scenarios/{scenarioId}/activities");

        // verify
        var list = await response.Content.ReadFromJsonAsync<List<ActivityResponse>>();
        Assert.Equal(["First", "Second"], list!.Select(a => a.Description));
        Assert.Equal([0, 1], list!.Select(a => a.Order));
    }

    [Fact]
    public async Task Update_WhenActivityDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();

        // test
        var response = await client.PatchAsJsonAsync(
            $"/activities/{Guid.CreateVersion7()}",
            new UpdateActivityRequest { Description = "X" }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenPreconditionIdDoesNotExist_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var createResponse = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = "Step" }
        );
        var created = await createResponse.Content.ReadFromJsonAsync<ActivityResponse>();

        // test
        var response = await client.PatchAsJsonAsync(
            $"/activities/{created!.Id}",
            new UpdateActivityRequest
            {
                Description = "Updated step",
                PreconditionIds = [Guid.CreateVersion7()],
                EvidenceIds = [],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenActivityDoesNotExist_ReturnsNoContent()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();

        // test
        var response = await client.DeleteAsync($"/activities/{Guid.CreateVersion7()}");

        // verify
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Reorder_WhenValidPermutation_UpdatesOrder()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var firstResponse = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = "First" }
        );
        var first = (await firstResponse.Content.ReadFromJsonAsync<ActivityResponse>())!;
        var secondResponse = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = "Second" }
        );
        var second = (await secondResponse.Content.ReadFromJsonAsync<ActivityResponse>())!;

        // test
        var response = await client.PatchAsJsonAsync(
            $"/scenarios/{scenarioId}/activities/order",
            new ReorderActivitiesRequest { OrderedActivityIds = [second.Id, first.Id] }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reordered = await response.Content.ReadFromJsonAsync<List<ActivityResponse>>();
        Assert.Equal([second.Id, first.Id], reordered!.Select(a => a.Id));
    }

    [Fact]
    public async Task Reorder_WhenNotAPermutation_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = "First" }
        );

        // test
        var response = await client.PatchAsJsonAsync(
            $"/scenarios/{scenarioId}/activities/order",
            new ReorderActivitiesRequest { OrderedActivityIds = [Guid.CreateVersion7()] }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reorder_WhenScenarioDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();

        // test
        var response = await client.PatchAsJsonAsync(
            $"/scenarios/{Guid.CreateVersion7()}/activities/order",
            new ReorderActivitiesRequest { OrderedActivityIds = [] }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(2000, HttpStatusCode.OK)]
    [InlineData(2001, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    public async Task Create_WhenDescriptionLengthAtBoundary_EnforcesLengthLimit(
        int descriptionLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = new string('a', descriptionLength) }
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(15, HttpStatusCode.OK)]
    [InlineData(16, HttpStatusCode.BadRequest)]
    public async Task Create_WhenPreconditionIdCountAtBoundary_EnforcesCountLimit(
        int preconditionCount,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);
        var preconditionIds = new List<Guid>();
        for (var i = 0; i < preconditionCount; i++)
        {
            preconditionIds.Add(await CreatePreconditionAsync(client, appId));
        }

        // test
        var response = await client.PostAsJsonAsync(
            $"/scenarios/{scenarioId}/activities",
            new CreateActivityRequest { Description = "Step", PreconditionIds = preconditionIds }
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"preconditionIds":null,"evidenceIds":null}""")] // description missing entirely
    [InlineData("""{"description":null,"preconditionIds":null,"evidenceIds":null}""")] // description explicitly null
    [InlineData("""{"description":123,"preconditionIds":null,"evidenceIds":null}""")] // description wrong type
    public async Task Create_WhenRequestHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var scenarioId = await CreateScenarioAsync(client, appId);

        // test
        var response = await client.PostAsync(
            $"/scenarios/{scenarioId}/activities",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
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
                $"/scenarios/{Guid.CreateVersion7()}/activities",
                new CreateActivityRequest { Description = "Step" }
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
            .GetAsync($"/scenarios/{Guid.CreateVersion7()}/activities");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PatchAsJsonAsync(
                $"/activities/{Guid.CreateVersion7()}",
                new UpdateActivityRequest { Description = "Step" }
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .DeleteAsync($"/activities/{Guid.CreateVersion7()}");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reorder_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PatchAsJsonAsync(
                $"/scenarios/{Guid.CreateVersion7()}/activities/order",
                new ReorderActivitiesRequest { OrderedActivityIds = [] }
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
