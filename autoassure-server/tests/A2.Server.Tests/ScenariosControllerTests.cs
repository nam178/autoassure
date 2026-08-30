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

namespace A2.Server.Tests;

/// <summary>Integration tests for <see cref="A2.Server.Controllers.ScenariosController"/> over real
/// HTTP, against DynamoDB Local.</summary>
[Collection("DynamoDbLocal")]
public sealed class ScenariosControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public ScenariosControllerTests(
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

        await CreateMappingTableAsync("ScenariosByFolder");
        await CreateMappingTableAsync("ScenariosByTag");

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

    private static async Task<Guid> CreatePreconditionAsync(HttpClient client, Guid appId)
    {
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/preconditions",
            new CreatePreconditionRequest(
                "Order ID",
                PreconditionValueSource.SpecificValue,
                "ORD-1"
            )
        );
        var precondition = await response.Content.ReadFromJsonAsync<PreconditionResponse>();
        return precondition!.Id;
    }

    [Fact]
    public async Task Create_WhenValidRequest_RoundTripsTitleAndActivitiesThroughGetById()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var preconditionId = await CreatePreconditionAsync(client, appId);

        // test
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest(
                "Checkout completes",
                "Verify a user can complete checkout",
                "/Checkout",
                ["smoke"],
                [new ActivityRequest("Add item to cart", [preconditionId], [])]
            )
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.NotNull(created);
        Assert.Equal("Checkout completes", created.Title);
        Assert.Equal("/Checkout", created.Folder);
        var activity = Assert.Single(created.Activities);
        Assert.Equal(preconditionId, Assert.Single(activity.PreconditionIds));

        // test
        var getResponse = await client.GetAsync($"/scenarios/{created.Id}");

        // verify
        var fetched = await getResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("Checkout completes", fetched.Title);
    }

    [Fact]
    public async Task Create_WhenTagIsEmptyString_RoundTripsAsEmptyStringInTagsList()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest("Checkout completes", "Description", null, [""], null)
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.Equal("", Assert.Single(created!.Tags));

        // test
        var getResponse = await client.GetAsync($"/scenarios/{created.Id}");

        // verify
        var fetched = await getResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.Equal("", Assert.Single(fetched!.Tags));
    }

    [Fact]
    public async Task Create_WhenFolderNotGiven_DefaultsFolderToRoot()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest("Untitled", "Description", null, null, null)
        );

        // verify
        var created = await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.Equal("/", created!.Folder);
    }

    [Fact]
    public async Task Create_WhenPreconditionIdDoesNotExist_IsRejected()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest(
                "Title",
                "Description",
                null,
                null,
                [new ActivityRequest("Step", [Guid.CreateVersion7()], [])]
            )
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(50, HttpStatusCode.OK)]
    [InlineData(51, HttpStatusCode.BadRequest)]
    public async Task Create_WhenActivityReferenceCountAtBoundary_EnforcesReferenceCountLimit(
        int referenceCount,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var preconditionIds = new List<Guid>();
        for (var i = 0; i < referenceCount; i++)
        {
            preconditionIds.Add(await CreatePreconditionAsync(client, appId));
        }
        var activities = preconditionIds
            .Select(preconditionId => new ActivityRequest("Step", [preconditionId], []))
            .ToList();

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest("Title", "Description", null, null, activities)
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenFolderChanges_MovesScenarioWithNoIntermediateDuplicateOrGap()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest("Title", "Description", "/OldFolder", null, null)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        // test
        var patchResponse = await client.PatchAsJsonAsync(
            $"/scenarios/{created.Id}",
            new UpdateScenarioRequest("Title", "Description", "/NewFolder", null, null)
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        // test
        var oldFolderResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios?folder={Uri.EscapeDataString("/OldFolder")}"
        );

        // verify
        var oldFolderScenarios = await oldFolderResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        Assert.Empty(oldFolderScenarios!);

        // test
        var newFolderResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios?folder={Uri.EscapeDataString("/NewFolder")}"
        );

        // verify
        var newFolderScenarios = await newFolderResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        var scenario = Assert.Single(newFolderScenarios!);
        Assert.Equal(created.Id, scenario.Id);
    }

    [Fact]
    public async Task Update_WhenTagAddedThenRemoved_ReflectsInListByTag()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest("Title", "Description", null, [], null)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        // test
        await client.PatchAsJsonAsync(
            $"/scenarios/{created.Id}",
            new UpdateScenarioRequest("Title", "Description", "/", ["regression"], null)
        );
        var listWithTag = await client.GetAsync($"/applications/{appId}/scenarios?tag=regression");

        // verify
        var withTag = await listWithTag.Content.ReadFromJsonAsync<List<ScenarioResponse>>();
        Assert.Single(withTag!);

        // test
        await client.PatchAsJsonAsync(
            $"/scenarios/{created.Id}",
            new UpdateScenarioRequest("Title", "Description", "/", [], null)
        );
        var listWithoutTag = await client.GetAsync(
            $"/applications/{appId}/scenarios?tag=regression"
        );

        // verify
        var withoutTag = await listWithoutTag.Content.ReadFromJsonAsync<List<ScenarioResponse>>();
        Assert.Empty(withoutTag!);
    }

    [Fact]
    public async Task Delete_WhenScenarioExists_RemovesScenarioAndItIsNoLongerListed()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest("Title", "Description", null, ["tag1"], null)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        // test
        var deleteResponse = await client.DeleteAsync($"/scenarios/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // test
        var getResponse = await client.GetAsync($"/scenarios/{created.Id}");

        // verify
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // test
        var listResponse = await client.GetAsync($"/applications/{appId}/scenarios");

        // verify
        var list = await listResponse.Content.ReadFromJsonAsync<List<ScenarioResponse>>();
        Assert.Empty(list!);
    }

    [Fact]
    public async Task List_WhenBothFolderAndTagGiven_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.GetAsync($"/applications/{appId}/scenarios?folder=/&tag=smoke");

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(200, HttpStatusCode.OK)]
    [InlineData(201, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    public async Task Create_WhenTitleLengthAtBoundary_EnforcesLengthLimit(
        int titleLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest(new string('a', titleLength), "Description", null, null, null)
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(10000, HttpStatusCode.OK)]
    [InlineData(10001, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
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
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest("Title", new string('a', descriptionLength), null, null, null)
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(300, HttpStatusCode.OK)]
    [InlineData(301, HttpStatusCode.BadRequest)]
    public async Task Create_WhenFolderLengthAtBoundary_EnforcesLengthLimit(
        int folderLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest(
                "Title",
                "Description",
                new string('a', folderLength),
                null,
                null
            )
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(20, HttpStatusCode.OK)]
    [InlineData(21, HttpStatusCode.BadRequest)]
    public async Task Create_WhenTagCountAtBoundary_EnforcesTagCountLimit(
        int tagCount,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest(
                "Title",
                "Description",
                null,
                Enumerable.Range(0, tagCount).Select(i => $"tag{i}").ToList(),
                null
            )
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(50, HttpStatusCode.OK)]
    [InlineData(51, HttpStatusCode.BadRequest)]
    public async Task Create_WhenTagLengthAtBoundary_EnforcesTagLengthLimit(
        int tagLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest(
                "Title",
                "Description",
                null,
                [new string('a', tagLength)],
                null
            )
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Theory]
    [InlineData(2000, HttpStatusCode.OK)]
    [InlineData(2001, HttpStatusCode.BadRequest)]
    [InlineData(0, HttpStatusCode.BadRequest)]
    public async Task Create_WhenActivityDescriptionLengthAtBoundary_EnforcesLengthLimit(
        int descriptionLength,
        HttpStatusCode expectedStatus
    )
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest(
                "Title",
                "Description",
                null,
                null,
                [new ActivityRequest(new string('a', descriptionLength), [], [])]
            )
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenFolderIsMissing_IsRejected()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest("Title", "Description", "/Folder", null, null)
        );
        var created = (await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>())!;

        // test
        var response = await client.PatchAsync(
            $"/scenarios/{created.Id}",
            new StringContent(
                """{"title":"Title","description":"Description","tags":null,"activities":null}""",
                Encoding.UTF8,
                "application/json"
            )
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"description":"Description","folder":null,"tags":null,"activities":null}""")] // title missing entirely
    [InlineData(
        """{"title":null,"description":"Description","folder":null,"tags":null,"activities":null}"""
    )] // title explicitly null
    [InlineData(
        """{"title":123,"description":"Description","folder":null,"tags":null,"activities":null}"""
    )] // title wrong type
    [InlineData("""{"title":"Title","folder":null,"tags":null,"activities":null}""")] // description missing entirely
    [InlineData(
        """{"title":"Title","description":null,"folder":null,"tags":null,"activities":null}"""
    )] // description explicitly null
    public async Task Create_WhenRequestHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/scenarios",
            new StringContent(rawJson, Encoding.UTF8, "application/json")
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
                $"/applications/{appId}/scenarios",
                new CreateScenarioRequest("Title", "Description", null, null, null)
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
            .GetAsync($"/applications/{Guid.CreateVersion7()}/scenarios");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // test
        var response = await _factory
            .CreateClient()
            .GetAsync($"/scenarios/{Guid.CreateVersion7()}");

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
                $"/scenarios/{Guid.CreateVersion7()}",
                new UpdateScenarioRequest("Title", "Description", "/", null, null)
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
            .DeleteAsync($"/scenarios/{Guid.CreateVersion7()}");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
