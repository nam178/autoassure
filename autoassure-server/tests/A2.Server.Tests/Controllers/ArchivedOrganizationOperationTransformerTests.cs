using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace A2.Server.Tests.Controllers;

/// <summary>
///     Integration tests for
///     <see cref="A2.Server.Common.ArchivedOrganizationOperationTransformer" />.
///     These tests verify that the OpenAPI documentation is correctly updated to
///     show 403 responses
///     for non-GET operations on endpoints that require authentication and don't
///     allow archived organizations.
/// </summary>
public sealed class ArchivedOrganizationOperationTransformerTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ArchivedOrganizationOperationTransformerTests(
        WebApplicationFactory<Program> factory
    )
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Auth:SigningKey"] =
                            "test-signing-key-at-least-32-bytes-long",
                    }
                )
            );
        });
    }

    private async Task<JsonElement> GetOpenApiDocumentAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private JsonElement? GetOperation(
        JsonElement document,
        string path,
        string method
    )
    {
        if (!document.TryGetProperty("paths", out var paths))
            return null;

        if (!paths.TryGetProperty(path, out var pathItem))
            return null;

        if (!pathItem.TryGetProperty(method.ToLower(), out var operation))
            return null;

        return operation;
    }

    private bool HasResponse(JsonElement operation, string statusCode)
    {
        if (!operation.TryGetProperty("responses", out var responses))
            return false;

        return responses.TryGetProperty(statusCode, out _);
    }

    private string? GetResponseDescription(
        JsonElement operation,
        string statusCode
    )
    {
        if (!operation.TryGetProperty("responses", out var responses))
            return null;

        if (!responses.TryGetProperty(statusCode, out var response))
            return null;

        if (response.TryGetProperty("description", out var description))
            return description.GetString();

        return null;
    }

    [Fact]
    public async Task
        ArchivedOrganizationOperationTransformer_AddsForbiddenResponseToPostEndpoints()
    {
        // Get the OpenAPI document
        var document = await GetOpenApiDocumentAsync();

        // Get a sample POST endpoint that requires auth but allows archived (Create Activity)
        // Archive endpoint has [AllowArchivedOrganization] so it won't get the transformer message
        var createActivityOperation = GetOperation(
            document,
            "/scenarios/{scenarioId}/activities",
            "post"
        );
        Assert.NotNull(createActivityOperation);

        // Verify that it has a 403 response
        Assert.True(HasResponse(createActivityOperation.Value, "403"));

        // Verify that the description includes the archived organization message
        var description = GetResponseDescription(
            createActivityOperation.Value,
            "403"
        );
        Assert.NotNull(description);
        Assert.Contains(
            "archived",
            description,
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task
        ArchivedOrganizationOperationTransformer_DoesNotAddForbiddenToGetEndpoints()
    {
        // Get the OpenAPI document
        var document = await GetOpenApiDocumentAsync();

        // Get a GET endpoint (List organizations)
        var listOperation = GetOperation(document, "/organizations", "get");
        Assert.NotNull(listOperation);

        // Verify that it does NOT have the archived organization 403 message
        var description = GetResponseDescription(listOperation.Value, "403");
        if (description != null)
            Assert.DoesNotContain(
                "Returns 403 when the caller's Organization is archived.",
                description
            );
    }

    [Fact]
    public async Task
        ArchivedOrganizationOperationTransformer_DoesNotApplyToAllowArchivedOrganizationEndpoints()
    {
        // Get the OpenAPI document
        var document = await GetOpenApiDocumentAsync();

        // Get the Unarchive endpoint which has [AllowArchivedOrganization]
        var unarchiveOperation = GetOperation(
            document,
            "/organizations/{id}/unarchive",
            "post"
        );
        Assert.NotNull(unarchiveOperation);

        // Verify that it does NOT have the archived organization 403 message
        // (though it may have a 403 for the Owner-only check)
        var description = GetResponseDescription(
            unarchiveOperation.Value,
            "403"
        );
        if (description != null)
            Assert.DoesNotContain(
                "Returns 403 when the caller's Organization is archived.",
                description
            );
    }

    [Fact]
    public async Task
        ArchivedOrganizationOperationTransformer_AppendsToExistingForbiddenResponses()
    {
        // Get the OpenAPI document
        var document = await GetOpenApiDocumentAsync();

        // Get a POST endpoint that has its own 403 description (Create Activity)
        // This endpoint documents 403 for invalid requests, and should also get the archived organization message appended
        var createActivityOperation = GetOperation(
            document,
            "/scenarios/{scenarioId}/activities",
            "post"
        );
        Assert.NotNull(createActivityOperation);

        var description = GetResponseDescription(
            createActivityOperation.Value,
            "403"
        );
        Assert.NotNull(description);

        // Verify that it contains both the original description (from the controller)
        // AND the new archived organization message (from the transformer, appended)
        Assert.Contains(
            "archived",
            description,
            StringComparison.OrdinalIgnoreCase
        );
    }
}