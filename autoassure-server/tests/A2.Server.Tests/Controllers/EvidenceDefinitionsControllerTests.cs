using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using A2.Server.Contracts;
using A2.Server.Tests;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace A2.Server.Tests.Controllers;

/// <summary>Integration tests for <see cref="A2.Server.Controllers.EvidenceDefinitionsController"/> over
/// real HTTP, against DynamoDB Local.</summary>
[Collection("DynamoDbLocal")]
public sealed class EvidenceDefinitionsControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public EvidenceDefinitionsControllerTests(
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
                            ["DynamoDb:EvidenceDefinitionTableName"] = "EvidenceDefinitions",
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
                TableName = "EvidenceDefinitions",
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

    public async Task DisposeAsync()
    {
        foreach (
            var tableName in new[]
            {
                "Applications",
                "EvidenceDefinitions",
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

    [Fact]
    public async Task Create_WhenValidRequest_RoundTripsThroughGetUpdateDelete()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/evidence-definitions",
            new CreateEvidenceDefinitionRequest(
                "Order Confirmation ID",
                "The confirmation id shown after checkout",
                "ORD-12345"
            )
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<EvidenceDefinitionResponse>();
        Assert.NotNull(created);
        Assert.Equal("Order Confirmation ID", created.Name);

        // test
        var patchResponse = await client.PatchAsJsonAsync(
            $"/evidence-definitions/{created.Id}",
            new UpdateEvidenceDefinitionRequest("Order ID", "Updated description", "ORD-99999")
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var updated = await patchResponse.Content.ReadFromJsonAsync<EvidenceDefinitionResponse>();
        Assert.Equal("Order ID", updated!.Name);
        Assert.Equal("Updated description", updated.Description);

        // test
        var deleteResponse = await client.DeleteAsync($"/evidence-definitions/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // test
        var listResponse = await client.GetAsync($"/applications/{appId}/evidence-definitions");

        // verify
        var list = await listResponse.Content.ReadFromJsonAsync<List<EvidenceDefinitionResponse>>();
        Assert.Empty(list!);
    }

    [Fact]
    public async Task List_WhenMultipleApplicationsExist_ReturnsOnlyCallersApplicationRows()
    {
        // setup
        var clientA = await CreateClientWithMembershipAsync();
        var clientB = await CreateClientWithMembershipAsync();
        var appIdA = await CreateApplicationAsync(clientA);
        var appIdB = await CreateApplicationAsync(clientB);
        await clientA.PostAsJsonAsync(
            $"/applications/{appIdA}/evidence-definitions",
            new CreateEvidenceDefinitionRequest("A", "", "")
        );
        await clientB.PostAsJsonAsync(
            $"/applications/{appIdB}/evidence-definitions",
            new CreateEvidenceDefinitionRequest("B", "", "")
        );

        // test
        var listResponse = await clientA.GetAsync($"/applications/{appIdA}/evidence-definitions");

        // verify
        var list = await listResponse.Content.ReadFromJsonAsync<List<EvidenceDefinitionResponse>>();
        var evidence = Assert.Single(list!);
        Assert.Equal("A", evidence.Name);
    }

    [Fact]
    public async Task Create_WhenDescriptionAndExampleValueAreEmpty_RoundTripsAsEmptyString()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/evidence-definitions",
            new CreateEvidenceDefinitionRequest("Order Confirmation ID", "", "")
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<EvidenceDefinitionResponse>();
        Assert.Equal("", created!.Description);
        Assert.Equal("", created.ExampleValue);

        // test
        var listResponse = await client.GetAsync($"/applications/{appId}/evidence-definitions");

        // verify
        var list = await listResponse.Content.ReadFromJsonAsync<List<EvidenceDefinitionResponse>>();
        var evidence = Assert.Single(list!);
        Assert.Equal("", evidence.Description);
        Assert.Equal("", evidence.ExampleValue);
    }

    [Fact]
    public async Task Update_WhenDescriptionAndExampleValueSetToEmpty_RoundTripsAsEmptyString()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/evidence-definitions",
            new CreateEvidenceDefinitionRequest(
                "Order Confirmation ID",
                "Non-empty description",
                "ORD-12345"
            )
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<EvidenceDefinitionResponse>()
        )!;

        // test
        var updateResponse = await client.PatchAsJsonAsync(
            $"/evidence-definitions/{created.Id}",
            new UpdateEvidenceDefinitionRequest("Order Confirmation ID", "", "")
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<EvidenceDefinitionResponse>();
        Assert.Equal("", updated!.Description);
        Assert.Equal("", updated.ExampleValue);

        // test
        var listResponse = await client.GetAsync($"/applications/{appId}/evidence-definitions");

        // verify
        var list = await listResponse.Content.ReadFromJsonAsync<List<EvidenceDefinitionResponse>>();
        var evidence = Assert.Single(list!);
        Assert.Equal("", evidence.Description);
        Assert.Equal("", evidence.ExampleValue);
    }

    [Theory]
    [InlineData(201, true)]
    [InlineData(200, false)]
    public async Task Create_WhenNameLengthAtBoundary_EnforcesLengthLimit(
        int nameLength,
        bool expectRejected
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/evidence-definitions",
            new CreateEvidenceDefinitionRequest(new string('a', nameLength), "", "")
        );

        // verify
        Assert.Equal(
            expectRejected ? HttpStatusCode.BadRequest : HttpStatusCode.OK,
            response.StatusCode
        );
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
                $"/applications/{appId}/evidence-definitions",
                new CreateEvidenceDefinitionRequest("X", "", "")
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
            .GetAsync($"/applications/{Guid.CreateVersion7()}/evidence-definitions");

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
                $"/evidence-definitions/{Guid.CreateVersion7()}",
                new UpdateEvidenceDefinitionRequest("X", "", "")
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
            .DeleteAsync($"/evidence-definitions/{Guid.CreateVersion7()}");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(200, HttpStatusCode.OK)]
    [InlineData(201, HttpStatusCode.BadRequest)]
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
            $"/applications/{appId}/evidence-definitions",
            new CreateEvidenceDefinitionRequest("Order Confirmation ID", "", "")
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<EvidenceDefinitionResponse>()
        )!;

        // test
        var response = await client.PatchAsJsonAsync(
            $"/evidence-definitions/{created.Id}",
            new UpdateEvidenceDefinitionRequest(new string('a', nameLength), "", "")
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(500, HttpStatusCode.OK)]
    [InlineData(501, HttpStatusCode.BadRequest)]
    public async Task Create_WhenDescriptionLengthAtBoundary_EnforcesLengthLimit(
        int descriptionLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/evidence-definitions",
            new CreateEvidenceDefinitionRequest("X", new string('a', descriptionLength), "")
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(500, HttpStatusCode.OK)]
    [InlineData(501, HttpStatusCode.BadRequest)]
    public async Task Create_WhenExampleValueLengthAtBoundary_EnforcesLengthLimit(
        int exampleValueLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/evidence-definitions",
            new CreateEvidenceDefinitionRequest("X", "", new string('a', exampleValueLength))
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"description":"","exampleValue":""}""")] // name missing entirely
    [InlineData("""{"name":null,"description":"","exampleValue":""}""")] // name explicitly null
    [InlineData("""{"name":123,"description":"","exampleValue":""}""")] // name wrong type
    public async Task Create_WhenNameHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/evidence-definitions",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"name":"X","exampleValue":""}""")] // description missing entirely
    [InlineData("""{"name":"X","description":null,"exampleValue":""}""")] // description explicitly null
    [InlineData("""{"name":"X","description":123,"exampleValue":""}""")] // description wrong type
    public async Task Create_WhenDescriptionHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/evidence-definitions",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"name":"X","description":""}""")] // exampleValue missing entirely
    [InlineData("""{"name":"X","description":"","exampleValue":null}""")] // exampleValue explicitly null
    [InlineData("""{"name":"X","description":"","exampleValue":123}""")] // exampleValue wrong type
    public async Task Create_WhenExampleValueHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/evidence-definitions",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"description":"","exampleValue":""}""")] // name missing entirely
    [InlineData("""{"name":null,"description":"","exampleValue":""}""")] // name explicitly null
    [InlineData("""{"name":123,"description":"","exampleValue":""}""")] // name wrong type
    public async Task Update_WhenNameHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/evidence-definitions",
            new CreateEvidenceDefinitionRequest("Order Confirmation ID", "", "")
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<EvidenceDefinitionResponse>()
        )!;

        // test
        var response = await client.PatchAsync(
            $"/evidence-definitions/{created.Id}",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
