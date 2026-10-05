using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using A2.Server.Common;
using A2.Server.Engine.Contracts;
using A2.Server.Engine.Repositories;
using A2.Server.Tests.Repositories;
using A2.Server.UserManagement.Models;
using A2.Server.WebApi.Contracts;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace A2.Server.Tests.Controllers;

/// <summary>
/// Integration tests for
/// <see cref="A2.Server.WebApi.Controllers.ScenariosController" /> over real
/// HTTP, against DynamoDB Local.
/// </summary>
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
                            ["DynamoDb:ScenarioTableName"] = "Scenarios",
                            ["DynamoDb:ScenariosByFolderTableName"] =
                                "ScenariosByFolder",
                            ["DynamoDb:ScenariosByTagTableName"] =
                                "ScenariosByTag",
                            ["DynamoDb:ActivityTableName"] = "Activities",
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
        await CreateActivityTableAsync("Activities");

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
                "Scenarios",
                "ScenariosByFolder",
                "ScenariosByTag",
                "Activities",
                "Organizations",
                "OrganizationUsers",
            }
        )
            await _client.DeleteTableAsync(tableName);
        _client.Dispose();
    }

    private async Task CreateActivityTableAsync(string tableName)
    {
        await _client.CreateTableAsync(
            new CreateTableRequest
            {
                TableName = tableName,
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

    // Looks up the caller's OrganizationId directly from DynamoDB so a test can construct the exact
    // key needed to delete an Application row out from under an authenticated client.
    private async Task<Guid> GetOrganizationIdAsync(Guid userId)
    {
        var response = await _client.QueryAsync(
            new QueryRequest
            {
                TableName = "OrganizationUsers",
                IndexName = "UserIdIndex",
                KeyConditionExpression = "UserId = :userId",
                ExpressionAttributeValues = new Dictionary<
                    string,
                    AttributeValue
                >
                {
                    [":userId"] = new(userId.ToString()),
                },
            }
        );
        return Guid.Parse(response.Items[0]["OrganizationId"].S);
    }

    [Fact]
    public async Task Create_WhenValidRequest_RoundTripsTitleThroughGetById()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Checkout completes",
                Description = "Verify a user can complete checkout",
                Folder = "/Checkout",
                Tags = ["smoke"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created =
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.NotNull(created);
        Assert.Equal("Checkout completes", created.Title);
        Assert.Equal("/Checkout", created.Folder);

        // test
        var getResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios/{created.Id}"
        );

        // verify
        var fetched =
            await getResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
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
            new CreateScenarioRequest
            {
                Title = "Checkout completes",
                Description = "Description",
                Folder = null,
                Tags = [""],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created =
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.Equal("", Assert.Single(created!.Tags));

        // test
        var getResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios/{created.Id}"
        );

        // verify
        var fetched =
            await getResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
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
            new CreateScenarioRequest
            {
                Title = "Untitled",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );

        // verify
        var created =
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.Equal("/", created!.Folder);
    }

    [Fact]
    public async Task Create_WhenApplicationDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{Guid.CreateVersion7()}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenFolderChanges_MovesScenarioWithNoIntermediateDuplicateOrGap()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/OldFolder",
                Tags = null,
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var patchResponse = await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/NewFolder",
                Tags = null,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        // test
        var oldFolderResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios?folder={Uri.EscapeDataString("/OldFolder")}"
        );

        // verify
        var oldFolderScenarios =
            await oldFolderResponse.Content.ReadFromJsonAsync<
                List<ScenarioResponse>
            >();
        Assert.Empty(oldFolderScenarios!);

        // test
        var newFolderResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios?folder={Uri.EscapeDataString("/NewFolder")}"
        );

        // verify
        var newFolderScenarios =
            await newFolderResponse.Content.ReadFromJsonAsync<
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
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = [],
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/",
                Tags = ["regression"],
            }
        );
        var listWithTag = await client.GetAsync(
            $"/applications/{appId}/scenarios?tag=regression"
        );

        // verify
        var withTag = await listWithTag.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        Assert.Single(withTag!);

        // test
        await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/",
                Tags = [],
            }
        );
        var listWithoutTag = await client.GetAsync(
            $"/applications/{appId}/scenarios?tag=regression"
        );

        // verify
        var withoutTag = await listWithoutTag.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        Assert.Empty(withoutTag!);
    }

    [Fact]
    public async Task Update_WhenScenarioDoesNotExistInCallersOrganization_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{Guid.CreateVersion7()}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/",
                Tags = null,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenScenarioInDifferentApplication_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId1 = await CreateApplicationAsync(client);
        var appId2 = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId1}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var response = await client.PatchAsJsonAsync(
            $"/applications/{appId2}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/",
                Tags = null,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_WhenScenarioInDifferentApplication_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId1 = await CreateApplicationAsync(client);
        var appId2 = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId1}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var response = await client.GetAsync(
            $"/applications/{appId2}/scenarios/{created.Id}"
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenApplicationDeletedMidRequest_ReturnsConflict()
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // Update() never re-checks the Application's existence itself (only the Scenario's) -- the
        // Application is only re-validated inside TryUpdateAsync's transaction, so deleting it here
        // deterministically exercises that failure without racing the request.
        var organizationId = await GetOrganizationIdAsync(userId);
        await _client.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = "Applications",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId"] = new(organizationId.ToString()),
                    ["Id"] = new(appId.ToString()),
                },
            }
        );

        // test
        var response = await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/",
                Tags = null,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenScenarioDeletedMidRequest_ReturnsNotFound()
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // Update() checks the Scenario exists up front via GetByIdAsync, then relies on
        // TryUpdateAsync's condition expression to catch it being deleted after that -- delete it
        // directly (bypassing the Controller) so the request observes it as still present at the
        // initial check but gone by the time the transact-write's condition runs.
        var organizationId = await GetOrganizationIdAsync(userId);
        await _client.DeleteItemAsync(
            new DeleteItemRequest
            {
                TableName = "Scenarios",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["OrganizationId_ApplicationId"] = new(
                        DynamoDbMapper.ApplicationScopedPartitionKey(
                            organizationId,
                            appId
                        )
                    ),
                    ["Id"] = new(created.Id.ToString()),
                },
            }
        );

        // test
        var response = await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/",
                Tags = null,
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenTagsCountExactlyMax_Succeeds()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/Folder",
                Tags = ["tag1"],
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var response = await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/Folder",
                Tags = ["tag1", "tag2", "tag3", "tag4", "tag5"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenTagsCountExceedMax_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/Folder",
                Tags = ["tag1"],
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var response = await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/Folder",
                Tags = ["tag1", "tag2", "tag3", "tag4", "tag5", "tag6"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenTagsDuplicated_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/Folder",
                Tags = ["tag1"],
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var response = await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/Folder",
                Tags = ["bug", "feature", "bug"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errorResponse =
            await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(errorResponse);
        Assert.Equal(
            "tags must not contain duplicates (case-sensitive).",
            errorResponse.Message
        );
    }

    [Fact]
    public async Task Update_WhenCaseDifferingDuplicates_Succeeds()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/Folder",
                Tags = ["tag1"],
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var response = await client.PatchAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new UpdateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/Folder",
                Tags = ["bug", "Bug", "feature"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated =
            await response.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.NotNull(updated);
        Assert.Equal(3, updated.Tags.Count);
    }

    [Fact]
    public async Task List_WhenBothFolderAndTagGiven_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.GetAsync(
            $"/applications/{appId}/scenarios?folder=/&tag=smoke"
        );

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
            new CreateScenarioRequest
            {
                Title = new string('a', titleLength),
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
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

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = new string('a', descriptionLength),
                Folder = null,
                Tags = null,
            }
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
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = new string('a', folderLength),
                Tags = null,
            }
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenTagsCountExactlyMax_Succeeds()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = ["tag1", "tag2", "tag3", "tag4", "tag5"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenTagsCountExceedMax_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = ["tag1", "tag2", "tag3", "tag4", "tag5", "tag6"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenTagsDuplicated_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = ["bug", "feature", "bug"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errorResponse =
            await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(errorResponse);
        Assert.Equal(
            "tags must not contain duplicates (case-sensitive).",
            errorResponse.Message
        );
    }

    [Fact]
    public async Task Create_WhenTagsWithIdenticalValues_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = ["bug", "bug", "bug", "bug", "bug"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errorResponse =
            await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(errorResponse);
        Assert.Equal(
            "tags must not contain duplicates (case-sensitive).",
            errorResponse.Message
        );
    }

    [Fact]
    public async Task Create_WhenTagsWithDifferentCases_Succeeds()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = ["Bug", "bug", "feature"],
            }
        );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created =
            await response.Content.ReadFromJsonAsync<ScenarioResponse>();
        Assert.NotNull(created);
        Assert.Equal(3, created.Tags.Count);
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
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = [new string('a', tagLength)],
            }
        );

        // verify
        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus == HttpStatusCode.BadRequest)
        {
            var errorResponse =
                await response.Content.ReadFromJsonAsync<ErrorResponse>();
            Assert.NotNull(errorResponse);
            Assert.Equal(
                "each tag must be at most 50 characters.",
                errorResponse.Message
            );
        }
    }

    [Fact]
    public async Task Update_WhenFolderIsMissing_IsRejected()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/Folder",
                Tags = null,
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var response = await client.PatchAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new StringContent(
                """{"title":"Title","description":"Description","tags":null}""",
                Encoding.UTF8,
                "application/json"
            )
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("""{"description":"Description","folder":null,"tags":null}""")] // title missing entirely
    [InlineData(
        """{"title":null,"description":"Description","folder":null,"tags":null}"""
    )] // title explicitly null
    [InlineData(
        """{"title":123,"description":"Description","folder":null,"tags":null}"""
    )] // title wrong type
    [InlineData("""{"title":"Title","folder":null,"tags":null}""")] // description missing entirely
    [InlineData(
        """{"title":"Title","description":null,"folder":null,"tags":null}"""
    )] // description explicitly null
    public async Task Create_WhenRequestHasInvalidShape_ReturnsBadRequest(
        string rawJson
    )
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
                new CreateScenarioRequest
                {
                    Title = "Title",
                    Description = "Description",
                    Folder = null,
                    Tags = null,
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
            .GetAsync(
                $"/applications/{Guid.CreateVersion7()}/scenarios/{Guid.CreateVersion7()}"
            );

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
                $"/applications/{Guid.CreateVersion7()}/scenarios/{Guid.CreateVersion7()}",
                new UpdateScenarioRequest
                {
                    Title = "Title",
                    Description = "Description",
                    Folder = "/",
                    Tags = null,
                }
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Archive_WhenScenarioExists_ReturnsNoContent()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/scenarios/{created.Id}/archive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Archive_WhenAlreadyArchived_ReturnsNoContent()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var firstArchive = await client.PostAsync(
            $"/applications/{appId}/scenarios/{created.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, firstArchive.StatusCode);

        // test
        var secondArchive = await client.PostAsync(
            $"/applications/{appId}/scenarios/{created.Id}/archive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, secondArchive.StatusCode);
    }

    [Fact]
    public async Task Archive_WhenScenarioDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/scenarios/{Guid.CreateVersion7()}/archive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unarchive_WhenScenarioExists_ReturnsNoContent()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // Archive it first
        var archiveResponse = await client.PostAsync(
            $"/applications/{appId}/scenarios/{created.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse.StatusCode);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/scenarios/{created.Id}/unarchive",
            null
        );

        // verify - unarchive returns 204
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // verify - scenario shows up in List again
        var listResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios"
        );
        var scenarios = await listResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        Assert.Contains(created.Id, scenarios!.Select(s => s.Id));

        // verify - scenario does not show up in ListArchived
        var listArchivedResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios/archived"
        );
        var archivedScenarios =
            await listArchivedResponse.Content.ReadFromJsonAsync<
                List<ScenarioResponse>
            >();
        Assert.DoesNotContain(created.Id, archivedScenarios!.Select(s => s.Id));
    }

    [Fact]
    public async Task Unarchive_WhenScenarioDoesNotExist_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.PostAsync(
            $"/applications/{appId}/scenarios/{Guid.CreateVersion7()}/unarchive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_WhenScenarioArchived_ExcludesIt()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse1 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Active Scenario",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var scenario1 = (
            await createResponse1.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var createResponse2 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Archived Scenario",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var scenario2 = (
            await createResponse2.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var archiveResponse = await client.PostAsync(
            $"/applications/{appId}/scenarios/{scenario2.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse.StatusCode);

        // test
        var listResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios"
        );

        // verify
        var scenarios = await listResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        var scenarioIds = scenarios!.Select(s => s.Id).ToList();
        Assert.Contains(scenario1.Id, scenarioIds);
        Assert.DoesNotContain(scenario2.Id, scenarioIds);
    }

    [Fact]
    public async Task List_WhenFilteredByFolderAndScenarioArchived_ExcludesIt()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var folder = "/TestFolder";
        var createResponse1 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Active Scenario",
                Description = "Description",
                Folder = folder,
                Tags = null,
            }
        );
        var scenario1 = (
            await createResponse1.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var createResponse2 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Archived Scenario in Folder",
                Description = "Description",
                Folder = folder,
                Tags = null,
            }
        );
        var scenario2 = (
            await createResponse2.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var archiveResponse = await client.PostAsync(
            $"/applications/{appId}/scenarios/{scenario2.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse.StatusCode);

        // test
        var listResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios?folder={Uri.EscapeDataString(folder)}"
        );

        // verify
        var scenarios = await listResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        var scenarioIds = scenarios!.Select(s => s.Id).ToList();
        Assert.Contains(scenario1.Id, scenarioIds);
        Assert.DoesNotContain(scenario2.Id, scenarioIds);
    }

    [Fact]
    public async Task List_WhenFilteredByTagAndScenarioArchived_ExcludesIt()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var tag = "smoketest";
        var createResponse1 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Active Scenario",
                Description = "Description",
                Folder = null,
                Tags = [tag],
            }
        );
        var scenario1 = (
            await createResponse1.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var createResponse2 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Archived Scenario with Tag",
                Description = "Description",
                Folder = null,
                Tags = [tag],
            }
        );
        var scenario2 = (
            await createResponse2.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var archiveResponse = await client.PostAsync(
            $"/applications/{appId}/scenarios/{scenario2.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse.StatusCode);

        // test
        var listResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios?tag={tag}"
        );

        // verify
        var scenarios = await listResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        var scenarioIds = scenarios!.Select(s => s.Id).ToList();
        Assert.Contains(scenario1.Id, scenarioIds);
        Assert.DoesNotContain(scenario2.Id, scenarioIds);
    }

    [Fact]
    public async Task ListArchived_WhenScenariosArchived_ReturnsOnlyArchivedScenarios()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse1 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Active Scenario",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var scenario1 = (
            await createResponse1.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var createResponse2 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Archived Scenario",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var scenario2 = (
            await createResponse2.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var archiveResponse = await client.PostAsync(
            $"/applications/{appId}/scenarios/{scenario2.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse.StatusCode);

        // test
        var listResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios/archived"
        );

        // verify
        var scenarios = await listResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        var archivedScenario = Assert.Single(scenarios!);
        var archivedScenarioIds = scenarios!.Select(s => s.Id).ToList();
        Assert.Equal(scenario2.Id, archivedScenario.Id);
        Assert.DoesNotContain(scenario1.Id, archivedScenarioIds);
    }

    [Fact]
    public async Task ListArchived_WhenFilteredByFolder_ReturnsOnlyArchivedInFolder()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var folder = "/ArchivedFolder";
        var createResponse1 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Active in Folder",
                Description = "Description",
                Folder = folder,
                Tags = null,
            }
        );
        var scenario1 = (
            await createResponse1.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var createResponse2 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Archived in Folder",
                Description = "Description",
                Folder = folder,
                Tags = null,
            }
        );
        var scenario2 = (
            await createResponse2.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var createResponse3 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Archived in Different Folder",
                Description = "Description",
                Folder = "/OtherFolder",
                Tags = null,
            }
        );
        var scenario3 = (
            await createResponse3.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var archiveResponse2 = await client.PostAsync(
            $"/applications/{appId}/scenarios/{scenario2.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse2.StatusCode);
        var archiveResponse3 = await client.PostAsync(
            $"/applications/{appId}/scenarios/{scenario3.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse3.StatusCode);

        // test
        var listResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios/archived?folder={Uri.EscapeDataString(folder)}"
        );

        // verify
        var scenarios = await listResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        var archivedScenario = Assert.Single(scenarios!);
        Assert.Equal(scenario2.Id, archivedScenario.Id);
        Assert.DoesNotContain(scenario1.Id, scenarios!.Select(s => s.Id));
        Assert.DoesNotContain(scenario3.Id, scenarios!.Select(s => s.Id));
    }

    [Fact]
    public async Task ListArchived_WhenFilteredByTag_ReturnsOnlyArchivedWithTag()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var tag = "regression";
        var createResponse1 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Active with Tag",
                Description = "Description",
                Folder = null,
                Tags = [tag],
            }
        );
        var scenario1 = (
            await createResponse1.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var createResponse2 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Archived with Tag",
                Description = "Description",
                Folder = null,
                Tags = [tag],
            }
        );
        var scenario2 = (
            await createResponse2.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var createResponse3 = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Archived without Tag",
                Description = "Description",
                Folder = null,
                Tags = null,
            }
        );
        var scenario3 = (
            await createResponse3.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var archiveResponse2 = await client.PostAsync(
            $"/applications/{appId}/scenarios/{scenario2.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse2.StatusCode);
        var archiveResponse3 = await client.PostAsync(
            $"/applications/{appId}/scenarios/{scenario3.Id}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, archiveResponse3.StatusCode);

        // test
        var listResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios/archived?tag={tag}"
        );

        // verify
        var scenarios = await listResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        var archivedScenario = Assert.Single(scenarios!);
        Assert.Equal(scenario2.Id, archivedScenario.Id);
        Assert.DoesNotContain(scenario1.Id, scenarios!.Select(s => s.Id));
        Assert.DoesNotContain(scenario3.Id, scenarios!.Select(s => s.Id));
    }

    [Fact]
    public async Task ListArchived_WhenBothFolderAndTagGiven_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.GetAsync(
            $"/applications/{appId}/scenarios/archived?folder=/&tag=smoke"
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Archive_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // setup
        var appId = Guid.CreateVersion7();

        // test
        var response = await _factory
            .CreateClient()
            .PostAsync(
                $"/applications/{appId}/scenarios/{Guid.CreateVersion7()}/archive",
                null
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unarchive_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // setup
        var appId = Guid.CreateVersion7();

        // test
        var response = await _factory
            .CreateClient()
            .PostAsync(
                $"/applications/{appId}/scenarios/{Guid.CreateVersion7()}/unarchive",
                null
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListArchived_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // setup
        var appId = Guid.CreateVersion7();

        // test
        var response = await _factory
            .CreateClient()
            .GetAsync($"/applications/{appId}/scenarios/archived");

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenScenarioExists_ReturnsNoContent()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/TestFolder",
                Tags = ["tag1"],
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test
        var response = await client.DeleteAsync(
            $"/applications/{appId}/scenarios/{created.Id}"
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // verify - scenario is gone
        var getResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // verify - no longer in list
        var listResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios"
        );
        var scenarios = await listResponse.Content.ReadFromJsonAsync<
            List<ScenarioResponse>
        >();
        Assert.DoesNotContain(created.Id, scenarios!.Select(s => s.Id));
    }

    [Fact]
    public async Task Delete_WhenScenarioNotFound_ReturnsNotFound()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test
        var response = await client.DeleteAsync(
            $"/applications/{appId}/scenarios/{Guid.CreateVersion7()}"
        );

        // verify
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenScenarioHasActivities_DeletesScenarioAndAllActivities()
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/TestFolder",
                Tags = ["tag1"],
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // Create an activity via HTTP API
        var activityResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}/activities",
            new CreateActivityRequest
            {
                Description = "Test activity",
                Order = 0,
                PreconditionIds = [],
                EvidenceIds = [],
            }
        );
        Assert.Equal(HttpStatusCode.OK, activityResponse.StatusCode);

        // test
        var response = await client.DeleteAsync(
            $"/applications/{appId}/scenarios/{created.Id}"
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // verify - scenario is gone
        var getResponse = await client.GetAsync(
            $"/applications/{appId}/scenarios/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_WhenScenarioModifiedConcurrently_ReturnsConflict()
    {
        // setup
        var userId = Guid.CreateVersion7();
        await SeedOrganizationMembershipAsync(userId);
        var client = CreateAuthenticatedClient(userId);
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/TestFolder",
                Tags = ["tag1"],
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;
        var organizationId = await GetOrganizationIdAsync(userId);

        // Create an activity to set ActivityCount = 1
        var activityResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios/{created.Id}/activities",
            new CreateActivityRequest
            {
                Description = "Test activity",
                Order = 0,
                PreconditionIds = [],
                EvidenceIds = [],
            }
        );
        Assert.Equal(HttpStatusCode.OK, activityResponse.StatusCode);

        // Create a test-scoped client with IActivityRepository wrapped by ActivityRepositoryRaceConditionWrapper
        // This injects the ActivityCount modification between the scenario read and transaction commit
        var scenarioPartitionKey = $"{organizationId}_{appId}";
        var wrappedClient = _factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    // Replace IActivityRepository with our race condition wrapper
                    services.Replace(
                        ServiceDescriptor.Scoped<IActivityRepository>(sp =>
                        {
                            var realRepository = new DynamoDbActivityRepository(
                                sp.GetRequiredService<IAmazonDynamoDB>(),
                                sp.GetRequiredService<
                                    IOptions<DynamoDbOptions>
                                >()
                            );
                            return new ActivityRepositoryRaceConditionWrapper(
                                realRepository,
                                organizationId,
                                created.Id,
                                RaceConditionMutations.ActivityCountModification(
                                    _client,
                                    "Scenarios",
                                    scenarioPartitionKey,
                                    created.Id
                                )
                            );
                        })
                    );
                })
            )
            .CreateClient();

        // Set auth header on wrapped client
        wrappedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken(userId));

        // test - Delete with the wrapped repository that injects ActivityCount modification
        var response = await wrappedClient.DeleteAsync(
            $"/applications/{appId}/scenarios/{created.Id}"
        );

        // verify
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var errorResponse =
            await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(errorResponse);
    }

    [Fact]
    public async Task Delete_WhenNoAccessToken_ReturnsUnauthorized()
    {
        // setup
        var appId = Guid.CreateVersion7();

        // test
        var response = await _factory
            .CreateClient()
            .DeleteAsync(
                $"/applications/{appId}/scenarios/{Guid.CreateVersion7()}"
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenTagsHasNullItem_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);

        // test - use raw JSON to express null item
        var response = await client.PostAsync(
            $"/applications/{appId}/scenarios",
            new StringContent(
                """{"title":"Title","description":"Desc","tags":[null,"valid"]}""",
                Encoding.UTF8,
                "application/json"
            )
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenTagsHasNullItem_ReturnsBadRequest()
    {
        // setup
        var client = await CreateClientWithMembershipAsync();
        var appId = await CreateApplicationAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/applications/{appId}/scenarios",
            new CreateScenarioRequest
            {
                Title = "Title",
                Description = "Description",
                Folder = "/",
                Tags = ["tag1"],
            }
        );
        var created = (
            await createResponse.Content.ReadFromJsonAsync<ScenarioResponse>()
        )!;

        // test - use raw JSON to express null item in tags array
        var response = await client.PatchAsync(
            $"/applications/{appId}/scenarios/{created.Id}",
            new StringContent(
                """{"title":"Title","description":"Desc","folder":"/","tags":[null,"valid"]}""",
                Encoding.UTF8,
                "application/json"
            )
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
