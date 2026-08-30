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
using TestDynamo;

namespace A2.Server.Tests;

/// <summary>Integration tests for <see cref="A2.Server.Controllers.EnvironmentsController"/> over real
/// HTTP, against an in-memory TestDynamo database (no Docker/JVM required).</summary>
public sealed class EnvironmentsControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public EnvironmentsControllerTests(WebApplicationFactory<Program> factory)
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
                            ["DynamoDb:OrganizationTableName"] = "Organizations",
                            ["DynamoDb:OrganizationUserTableName"] = "OrganizationUsers",
                        }
                    )
            );
            builder.ConfigureServices(services =>
            {
                _client = TestDynamoClient.CreateClient<AmazonDynamoDBClient>();
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

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
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

    [Fact]
    public async Task Create_WhenValidRequest_ReturnsEnvironmentWithNoVariablesInList()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>();
        Assert.NotNull(created);
        Assert.Equal("Staging", created.Name);
        Assert.Empty(created.Variables);

        // test
        var listResponse = await client.GetAsync($"/applications/{appId}/environments");

        // verify
        var environments = await listResponse.Content.ReadFromJsonAsync<
            List<EnvironmentResponse>
        >();
        var environment = Assert.Single(environments!);
        Assert.Equal(created.Id, environment.Id);
    }

    [Fact]
    public async Task GetById_WhenVariablesSet_ReturnsEnvironmentWithVariables()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;
        await client.PutAsJsonAsync(
            $"/environments/{created.Id}/variables/API_URL",
            new SetEnvironmentVariableRequest("https://staging.example.com")
        );

        // test
        var getResponse = await client.GetAsync($"/environments/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var environment = await getResponse.Content.ReadFromJsonAsync<EnvironmentResponse>();
        Assert.Equal(created.Id, environment!.Id);
        Assert.Equal("Staging", environment.Name);
        var variable = Assert.Single(environment.Variables);
        Assert.Equal("API_URL", variable.Key);
        Assert.Equal("https://staging.example.com", variable.Value);
    }

    [Fact]
    public async Task GetById_WhenEnvironmentInDifferentOrganization_ReturnsNotFound()
    {
        // setup
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userA);
        await SeedOrganizationMembershipAsync(userB);
        var clientA = CreateAuthenticatedClient(userA);
        var clientB = CreateAuthenticatedClient(userB);
        var appId = await CreateApplicationAsync(clientA);
        var createResponse = await clientA.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;

        // test
        var response = await clientB.GetAsync($"/environments/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenValidRequest_ChangesNameAndClassification()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;

        // test
        var patchResponse = await client.PatchAsJsonAsync(
            $"/environments/{created.Id}",
            new UpdateEnvironmentRequest("Production", EnvironmentClassification.Production)
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var updated = await patchResponse.Content.ReadFromJsonAsync<EnvironmentResponse>();
        Assert.Equal("Production", updated!.Name);
        Assert.Equal(EnvironmentClassification.Production, updated.Classification);
    }

    [Fact]
    public async Task SetVariable_WhenOverwritingExistingKey_DoesNotDisturbOtherVariables()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;

        // test
        await client.PutAsJsonAsync(
            $"/environments/{created.Id}/variables/API_URL",
            new SetEnvironmentVariableRequest("https://staging.example.com")
        );
        await client.PutAsJsonAsync(
            $"/environments/{created.Id}/variables/API_KEY",
            new SetEnvironmentVariableRequest("secret-1")
        );
        await client.PutAsJsonAsync(
            $"/environments/{created.Id}/variables/API_KEY",
            new SetEnvironmentVariableRequest("secret-2")
        );

        // verify
        var getResponse = await client.GetAsync($"/environments/{created.Id}");
        var environment = await getResponse.Content.ReadFromJsonAsync<EnvironmentResponse>();

        Assert.Equal(2, environment!.Variables.Count);
        Assert.Equal(
            "https://staging.example.com",
            environment.Variables.Single(v => v.Key == "API_URL").Value
        );
        Assert.Equal("secret-2", environment.Variables.Single(v => v.Key == "API_KEY").Value);
    }

    [Fact]
    public async Task DeleteVariable_WhenKeyExists_RemovesOnlyThatVariable()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;
        await client.PutAsJsonAsync(
            $"/environments/{created.Id}/variables/API_URL",
            new SetEnvironmentVariableRequest("https://staging.example.com")
        );
        await client.PutAsJsonAsync(
            $"/environments/{created.Id}/variables/API_KEY",
            new SetEnvironmentVariableRequest("secret-1")
        );

        // test
        var deleteResponse = await client.DeleteAsync(
            $"/environments/{created.Id}/variables/API_KEY"
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        var getResponse = await client.GetAsync($"/environments/{created.Id}");
        var environment = await getResponse.Content.ReadFromJsonAsync<EnvironmentResponse>();
        var variable = Assert.Single(environment!.Variables);
        Assert.Equal("API_URL", variable.Key);
    }

    [Fact]
    public async Task Create_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // setup
        var appId = Guid.CreateVersion7();

        // test
        var response = await _factory
            .CreateClient()
            .PostAsJsonAsync(
                $"/applications/{appId}/environments",
                new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenApplicationInDifferentOrganization_ReturnsNotFound()
    {
        // setup
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userA);
        await SeedOrganizationMembershipAsync(userB);
        var clientA = CreateAuthenticatedClient(userA);
        var clientB = CreateAuthenticatedClient(userB);
        var appId = await CreateApplicationAsync(clientA);

        // test
        var createResponseA = await clientA.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponseA.StatusCode);

        // test
        var createResponseB = await clientB.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, createResponseB.StatusCode);

        // test
        var listResponse = await clientB.GetAsync($"/applications/{appId}/environments");

        // verify
        var environments = await listResponse.Content.ReadFromJsonAsync<
            List<EnvironmentResponse>
        >();
        Assert.Empty(environments!);
    }

    [Fact]
    public async Task List_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .GetAsync($"/applications/{Guid.CreateVersion7()}/environments");

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
                $"/environments/{Guid.CreateVersion7()}",
                new UpdateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetVariable_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PutAsJsonAsync(
                $"/environments/{Guid.CreateVersion7()}/variables/API_URL",
                new SetEnvironmentVariableRequest("x")
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteVariable_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .DeleteAsync($"/environments/{Guid.CreateVersion7()}/variables/API_URL");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .GetAsync($"/environments/{Guid.CreateVersion7()}");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(100, HttpStatusCode.OK)]
    [InlineData(101, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    public async Task Create_WhenNameLengthAtBoundary_EnforcesLengthLimit(
        int nameLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest(
                new string('a', nameLength),
                EnvironmentClassification.NonProduction
            )
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(100, HttpStatusCode.OK)]
    [InlineData(101, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    public async Task Update_WhenNameLengthAtBoundary_EnforcesLengthLimit(
        int nameLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;

        // test
        var response = await client.PatchAsJsonAsync(
            $"/environments/{created.Id}",
            new UpdateEnvironmentRequest(
                new string('a', nameLength),
                EnvironmentClassification.NonProduction
            )
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(4000, HttpStatusCode.NoContent)]
    [InlineData(4001, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    public async Task SetVariable_WhenValueLengthAtBoundary_EnforcesLengthLimit(
        int valueLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;

        // test
        var response = await client.PutAsJsonAsync(
            $"/environments/{created.Id}/variables/API_URL",
            new SetEnvironmentVariableRequest(new string('a', valueLength))
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(200, HttpStatusCode.NoContent)]
    [InlineData(201, HttpStatusCode.BadRequest)]
    public async Task SetVariable_WhenKeyLengthAtBoundary_EnforcesLengthLimit(
        int keyLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;

        // test
        var response = await client.PutAsJsonAsync(
            $"/environments/{created.Id}/variables/{new string('k', keyLength)}",
            new SetEnvironmentVariableRequest("x")
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"classification":1}""")] // name missing entirely
    [InlineData("""{"name":null,"classification":1}""")] // name explicitly null
    [InlineData("""{"name":123,"classification":1}""")] // name wrong type
    [InlineData("""{"name":"Staging","classification":99}""")] // classification out of enum range
    public async Task Create_WhenRequestHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/environments",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"classification":1}""")] // name missing entirely
    [InlineData("""{"name":null,"classification":1}""")] // name explicitly null
    [InlineData("""{"name":123,"classification":1}""")] // name wrong type
    [InlineData("""{"name":"Staging","classification":99}""")] // classification out of enum range
    public async Task Update_WhenRequestHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;

        // test
        var response = await client.PatchAsync(
            $"/environments/{created.Id}",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")] // value missing entirely
    [InlineData("""{"value":null}""")] // value explicitly null
    [InlineData("""{"value":123}""")] // value wrong type
    public async Task SetVariable_WhenValueHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/environments",
            new CreateEnvironmentRequest("Staging", EnvironmentClassification.NonProduction)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<EnvironmentResponse>())!;

        // test
        var response = await client.PutAsync(
            $"/environments/{created.Id}/variables/API_URL",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
