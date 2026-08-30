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
using TestDynamo;

namespace A2.Server.Tests;

/// <summary>Integration tests for <see cref="A2.Server.Controllers.ApplicationsController"/> over real
/// HTTP, against an in-memory TestDynamo database (no Docker/JVM required). Each Organization used by a
/// test is pre-seeded with an <see cref="OrganizationUser"/> membership so <c>ICallerOrganizationService</c>
/// can resolve the caller's Organization from the request's JWT.</summary>
public sealed class ApplicationsControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public ApplicationsControllerTests(WebApplicationFactory<Program> factory)
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
        // Force the factory (and its ConfigureServices callback, which creates _client) to run now,
        // rather than lazily on the first request, so the tables below can be created against it.
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

    private async Task SeedOrganizationMembershipAsync(Guid userId, bool seedOrganization = true)
    {
        var organizationId = Guid.CreateVersion7();
        if (seedOrganization)
        {
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
        }

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

    [Fact]
    public async Task Create_WhenValidRequest_RoundTripsThroughGetById()
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);

        // test
        var createResponse = await client.PostAsJsonAsync(
            "/applications",
            new CreateApplicationRequest("Checkout Service", "Handles order checkout")
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.NotNull(created);
        Assert.Equal("Checkout Service", created.Name);

        // test
        var getResponse = await client.GetAsync($"/applications/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal(created, fetched);
    }

    [Fact]
    public async Task Create_WhenDescriptionIsEmpty_RoundTripsAsEmptyString()
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);

        // test
        var createResponse = await client.PostAsJsonAsync(
            "/applications",
            new CreateApplicationRequest("Checkout Service", "")
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal("", created!.Description);

        // test
        var getResponse = await client.GetAsync($"/applications/{created.Id}");

        // verify
        var fetched = await getResponse.Content.ReadFromJsonAsync<ApplicationResponse>();
        Assert.Equal("", fetched!.Description);
    }

    [Fact]
    public async Task Create_WhenOrganizationDoesNotExist_ReturnsNotFound()
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId, seedOrganization: false);
        var client = CreateAuthenticatedClient(userId);

        // test
        var response = await client.PostAsJsonAsync(
            "/applications",
            new CreateApplicationRequest("App", "")
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_WhenMultipleOrganizationsExist_ReturnsOnlyCallersOrganizationApplications()
    {
        // setup
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userA);
        await SeedOrganizationMembershipAsync(userB);
        var clientA = CreateAuthenticatedClient(userA);
        var clientB = CreateAuthenticatedClient(userB);
        await clientA.PostAsJsonAsync("/applications", new CreateApplicationRequest("App A", ""));
        await clientB.PostAsJsonAsync("/applications", new CreateApplicationRequest("App B", ""));

        // test
        var listResponse = await clientA.GetAsync("/applications");
        var applications = await listResponse.Content.ReadFromJsonAsync<
            List<ApplicationResponse>
        >();

        // verify
        Assert.NotNull(applications);
        var application = Assert.Single(applications);
        Assert.Equal("App A", application.Name);
    }

    [Fact]
    public async Task GetById_WhenApplicationInDifferentOrganization_ReturnsNotFound()
    {
        // setup
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userA);
        await SeedOrganizationMembershipAsync(userB);
        var clientA = CreateAuthenticatedClient(userA);
        var clientB = CreateAuthenticatedClient(userB);
        var createResponse = await clientA.PostAsJsonAsync(
            "/applications",
            new CreateApplicationRequest("App A", "")
        );
        var created = await createResponse.Content.ReadFromJsonAsync<ApplicationResponse>();

        // test
        var getResponse = await clientB.GetAsync($"/applications/{created!.Id}");

        // verify
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task List_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory.CreateClient().GetAsync("/applications");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .PostAsJsonAsync("/applications", new CreateApplicationRequest("App", ""));

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
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);

        // test
        var response = await client.PostAsJsonAsync(
            "/applications",
            new CreateApplicationRequest(new string('a', nameLength), "")
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(1000, HttpStatusCode.OK)]
    [InlineData(1001, HttpStatusCode.BadRequest)]
    public async Task Create_WhenDescriptionLengthAtBoundary_EnforcesLengthLimit(
        int descriptionLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);

        // test
        var response = await client.PostAsJsonAsync(
            "/applications",
            new CreateApplicationRequest("App", new string('a', descriptionLength))
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"description":"x"}""")] // name missing entirely
    [InlineData("""{"name":null,"description":"x"}""")] // name explicitly null
    [InlineData("""{"name":123,"description":"x"}""")] // name wrong type
    public async Task Create_WhenNameHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);

        // test
        var response = await client.PostAsync(
            "/applications",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"name":"App"}""")] // description missing entirely
    [InlineData("""{"name":"App","description":null}""")] // description explicitly null
    [InlineData("""{"name":"App","description":123}""")] // description wrong type
    public async Task Create_WhenDescriptionHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);

        // test
        var response = await client.PostAsync(
            "/applications",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
