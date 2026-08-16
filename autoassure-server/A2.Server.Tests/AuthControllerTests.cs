using System.Net;
using System.Net.Http.Json;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Models;
using A2.Server.Services;
using Microsoft.AspNetCore.Mvc.Testing;
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

    private sealed class FakeTokenService(AppToken token) : ITokenIssuerService
    {
        public AppToken IssueToken(GoogleIdentity identity) => token;
    }

    private HttpClient CreateClient(GoogleIdentity? fakeIdentity, AppToken fakeToken) =>
        factory
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.Replace(
                        ServiceDescriptor.Scoped<IGoogleTokenExchangeService>(
                            _ => new FakeGoogleTokenExchangeService(fakeIdentity)
                        )
                    );
                    services.Replace(
                        ServiceDescriptor.Scoped<ITokenIssuerService>(_ => new FakeTokenService(
                            fakeToken
                        ))
                    );
                })
            )
            .CreateClient();

    [Fact]
    public async Task PostAuthGoogleToken_ReturnsToken_WhenExchangeSucceeds()
    {
        var identity = new GoogleIdentity("user-123", "user@example.com", true, "Test User", null);
        var token = new AppToken("fake-app-token", DateTimeOffset.UtcNow.AddHours(1));

        var response = await CreateClient(identity, token)
            .PostAsJsonAsync(
                "/auth/google/token",
                new { code = "fake-code", codeVerifier = "fake-verifier" }
            );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.Equal(new AuthTokenResponse(token.Value, token.ExpiresAt, identity), body);
    }

    [Fact]
    public async Task PostAuthGoogleToken_ReturnsUnauthorized_WhenExchangeFails()
    {
        var response = await CreateClient(null, new AppToken("unused", DateTimeOffset.UtcNow))
            .PostAsJsonAsync(
                "/auth/google/token",
                new { code = "fake-code", codeVerifier = "fake-verifier" }
            );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
