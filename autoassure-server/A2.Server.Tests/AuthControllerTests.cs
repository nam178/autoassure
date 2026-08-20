using System.Net;
using System.Net.Http.Json;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Models;
using A2.Server.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace A2.Server.Tests;

public class AuthControllerTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private sealed class FakeGoogleTokenExchangeService(GoogleIdentity? identity)
        : IGoogleTokenExchangeService
    {
        public Task<GoogleIdentity> ExchangeCodeAsync(string code, string codeVerifier) =>
            identity is not null
                ? Task.FromResult(identity)
                : throw new GoogleTokenExchangeException("invalid_grant");
    }

    private sealed class FakeAuthTokenService(IssuedTokens tokens) : IAuthTokenService
    {
        public Task<IssuedTokens> IssueAsync(GoogleIdentity identity) => Task.FromResult(tokens);

        public Task<IssuedTokens?> RefreshAsync(string refreshTokenSecret) =>
            Task.FromResult<IssuedTokens?>(tokens);
    }

    private HttpClient CreateClient(GoogleIdentity? fakeIdentity, IssuedTokens fakeTokens) =>
        factory
            .WithWebHostBuilder(builder =>
                builder
                    .ConfigureAppConfiguration(
                        (_, config) =>
                            config.AddInMemoryCollection(
                                new Dictionary<string, string?>
                                {
                                    ["Auth:SigningKey"] = "test-signing-key-at-least-32-bytes-long",
                                }
                            )
                    )
                    .ConfigureServices(services =>
                    {
                        services.Replace(
                            ServiceDescriptor.Scoped<IGoogleTokenExchangeService>(
                                _ => new FakeGoogleTokenExchangeService(fakeIdentity)
                            )
                        );
                        services.Replace(
                            ServiceDescriptor.Scoped<IAuthTokenService>(
                                _ => new FakeAuthTokenService(fakeTokens)
                            )
                        );
                    })
            )
            .CreateClient();

    [Fact]
    public async Task PostAuthGoogleToken_ReturnsToken_WhenExchangeSucceeds()
    {
        var identity = new GoogleIdentity("user-123", "user@example.com", true, "Test User", null);
        var tokens = new IssuedTokens(
            new AppToken("fake-app-token", DateTimeOffset.UtcNow.AddHours(1)),
            "fake-refresh-token"
        );

        var response = await CreateClient(identity, tokens)
            .PostAsJsonAsync(
                "/auth/google/token",
                new { code = "fake-code", codeVerifier = "fake-verifier" }
            );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.Equal(
            new AuthTokenResponse(
                tokens.AccessToken.Value,
                tokens.AccessToken.ExpiresAt,
                tokens.RefreshTokenSecret,
                identity
            ),
            body
        );
    }

    [Fact]
    public async Task PostAuthGoogleToken_ReturnsUnauthorized_WhenExchangeFails()
    {
        var unusedTokens = new IssuedTokens(
            new AppToken("unused", DateTimeOffset.UtcNow),
            "unused"
        );

        var response = await CreateClient(null, unusedTokens)
            .PostAsJsonAsync(
                "/auth/google/token",
                new { code = "fake-code", codeVerifier = "fake-verifier" }
            );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
