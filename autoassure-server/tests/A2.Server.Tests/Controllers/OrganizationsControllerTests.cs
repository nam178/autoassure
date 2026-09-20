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

namespace A2.Server.Tests.Controllers;

/// <summary>
///     Integration tests for
///     <see cref="A2.Server.Controllers.OrganizationsController" /> over real
///     HTTP, against DynamoDB Local.
/// </summary>
[Collection("DynamoDbLocal")]
public sealed class OrganizationsControllerTests
    : IClassFixture<WebApplicationFactory<Program>>,
        IAsyncLifetime
{
    private const string SigningKey = "test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "autoassure-server";
    private const string Audience = "autoassure-web";

    private readonly WebApplicationFactory<Program> _factory;
    private AmazonDynamoDBClient _client = null!;

    public OrganizationsControllerTests(
        WebApplicationFactory<Program> factory,
        DynamoDbLocalFixture dynamoDbLocalFixture
    )
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Auth:SigningKey"] = SigningKey,
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
        // Force the factory (and its ConfigureServices callback) to run now
        _ = _factory.Server;

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
            var tableName in new[] { "Organizations", "OrganizationUsers" }
        )
            await _client.DeleteTableAsync(tableName);
        _client.Dispose();
    }

    private async Task<Guid> SeedOrganizationWithUserAsync(
        Guid userId,
        bool isPersonal,
        OrganizationRole role
    )
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
                    ["IsPersonal"] = new() { BOOL = isPersonal },
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
                    ["Role"] = new(role.ToString()),
                    ["CreatedByUserId"] = new(userId.ToString()),
                    ["UpdatedByUserId"] = new(userId.ToString()),
                    ["CreatedAt"] = new(DateTimeOffset.UtcNow.ToString("O")),
                    ["UpdatedAt"] = new(DateTimeOffset.UtcNow.ToString("O")),
                },
            }
        );

        return organizationId;
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

    [Fact]
    public async Task ArchiveOrganization_WhenOwner_Returns204()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var organizationId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Owner
        );
        var client = CreateAuthenticatedClient(userId);

        // test
        var response = await client.PostAsync(
            $"/organizations/{organizationId}/archive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify the organization is now archived
        var getResponse = await client.GetAsync("/organizations");
        var organizations = await getResponse.Content.ReadFromJsonAsync<
            List<OrganizationResponse>
        >();
        Assert.NotNull(organizations);
        Assert.Empty(
            organizations); // Should not appear in active organizations
    }

    [Fact]
    public async Task ArchiveOrganization_WhenMember_Returns403()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var organizationId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Member
        );
        var client = CreateAuthenticatedClient(userId);

        // test
        var response = await client.PostAsync(
            $"/organizations/{organizationId}/archive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Contains(
            "owner",
            error.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task ArchiveOrganization_WhenPersonal_Returns400()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var organizationId = await SeedOrganizationWithUserAsync(
            userId,
            true,
            OrganizationRole.Owner
        );
        var client = CreateAuthenticatedClient(userId);

        // test
        var response = await client.PostAsync(
            $"/organizations/{organizationId}/archive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Contains(
            "Personal",
            error.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task ArchiveOrganization_WhenCalledTwice_BothReturn204()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var organizationId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Owner
        );
        var client = CreateAuthenticatedClient(userId);

        // test - first archive
        var response1 = await client.PostAsync(
            $"/organizations/{organizationId}/archive",
            null
        );
        Assert.Equal(HttpStatusCode.NoContent, response1.StatusCode);

        // test - second archive (idempotent, allowed by [AllowArchivedOrganization])
        var response2 = await client.PostAsync(
            $"/organizations/{organizationId}/archive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent,
            response2.StatusCode); // Idempotent returns 204
    }

    [Fact]
    public async Task UnarchiveOrganization_WhenOwner_Returns204()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var organizationId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Owner
        );
        var client = CreateAuthenticatedClient(userId);

        // First archive it
        await client.PostAsync(
            $"/organizations/{organizationId}/archive",
            null
        );

        // test - unarchive
        var response = await client.PostAsync(
            $"/organizations/{organizationId}/unarchive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify it's back in active organizations
        var getResponse = await client.GetAsync("/organizations");
        var organizations = await getResponse.Content.ReadFromJsonAsync<
            List<OrganizationResponse>
        >();
        Assert.NotNull(organizations);
        Assert.Single(organizations);
        Assert.Equal(organizationId, organizations[0].Id);
    }

    [Fact]
    public async Task UnarchiveOrganization_WhenMember_Returns403()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var organizationId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Member
        );
        var client = CreateAuthenticatedClient(userId);

        // Manually archive it (without going through the controller)
        await _client.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = "Organizations",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new(organizationId.ToString()),
                },
                UpdateExpression = "SET LifecycleState = :newState",
                ExpressionAttributeValues = new Dictionary<
                    string,
                    AttributeValue
                >
                {
                    [":newState"] = new(LifecycleState.Archived.ToString()),
                },
            }
        );

        // test
        var response = await client.PostAsync(
            $"/organizations/{organizationId}/unarchive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Contains(
            "owner",
            error.Message,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task ListOrganizations_ReturnsOnlyActiveOrganizations()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var activeId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Owner
        );
        var archivedId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Owner
        );
        var client = CreateAuthenticatedClient(userId);

        // Archive one organization
        await _client.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = "Organizations",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new(archivedId.ToString()),
                },
                UpdateExpression = "SET LifecycleState = :newState",
                ExpressionAttributeValues = new Dictionary<
                    string,
                    AttributeValue
                >
                {
                    [":newState"] = new(LifecycleState.Archived.ToString()),
                },
            }
        );

        // test
        var response = await client.GetAsync("/organizations");

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organizations = await response.Content.ReadFromJsonAsync<
            List<OrganizationResponse>
        >();
        Assert.NotNull(organizations);
        Assert.Single(organizations);
        Assert.Equal(activeId, organizations[0].Id);
    }

    [Fact]
    public async Task
        ListArchivedOrganizations_ReturnsOnlyArchivedOrganizations()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var activeId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Owner
        );
        var archivedId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Owner
        );
        var client = CreateAuthenticatedClient(userId);

        // Archive one organization
        await _client.UpdateItemAsync(
            new UpdateItemRequest
            {
                TableName = "Organizations",
                Key = new Dictionary<string, AttributeValue>
                {
                    ["Id"] = new(archivedId.ToString()),
                },
                UpdateExpression = "SET LifecycleState = :newState",
                ExpressionAttributeValues = new Dictionary<
                    string,
                    AttributeValue
                >
                {
                    [":newState"] = new(LifecycleState.Archived.ToString()),
                },
            }
        );

        // test
        var response = await client.GetAsync("/organizations/archived");

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organizations = await response.Content.ReadFromJsonAsync<
            List<OrganizationResponse>
        >();
        Assert.NotNull(organizations);
        Assert.Single(organizations);
        Assert.Equal(archivedId, organizations[0].Id);
    }

    [Fact]
    public async Task
        UnarchiveOrganization_ProvesThatAllowArchivedOrganizationAttributeIsWired()
    {
        // setup
        var userId = Guid.CreateVersion7();
        var organizationId = await SeedOrganizationWithUserAsync(
            userId,
            false,
            OrganizationRole.Owner
        );
        var client = CreateAuthenticatedClient(userId);

        // Archive the organization
        await client.PostAsync(
            $"/organizations/{organizationId}/archive",
            null
        );

        // Verify it's archived and filtered from GET requests
        var getResponse = await client.GetAsync("/organizations");
        var organizations = await getResponse.Content.ReadFromJsonAsync<
            List<OrganizationResponse>
        >();
        Assert.NotNull(organizations);
        Assert.Empty(
            organizations); // Should be filtered out by RequireActiveOrganizationFilter

        // test - unarchive should still work despite the filter (proves [AllowArchivedOrganization] is wired)
        var response = await client.PostAsync(
            $"/organizations/{organizationId}/unarchive",
            null
        );

        // verify
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}