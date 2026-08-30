using System.Net;
using System.Net.Http.Json;
using System.Text;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Models;
using A2.Server.Services;
using A2.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace A2.Server.Tests.Controllers;

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

    private sealed class FakeAuthTokenService(IssuedTokens? tokens) : IAuthTokenService
    {
        public Task<IssuedTokens> IssueAsync(User user) => Task.FromResult(tokens!);

        public Task<IssuedTokens?> RefreshAsync(string refreshTokenSecret) =>
            Task.FromResult(tokens);
    }

    private sealed class FakeGoogleUserSyncService : IGoogleUserSyncService
    {
        public static readonly Guid FixedUserId = Guid.CreateVersion7();

        public Task<User> SyncAsync(GoogleIdentity googleIdentity) =>
            Task.FromResult(
                new User
                {
                    Id = FixedUserId,
                    GoogleUserId = googleIdentity.GoogleUserId,
                    FirstName = googleIdentity.FirstName ?? "",
                    LastName = googleIdentity.LastName ?? "",
                    Email = googleIdentity.Email,
                    EmailVerified = googleIdentity.EmailVerified,
                }
            );
    }

    private HttpClient CreateClient(GoogleIdentity? fakeIdentity, IssuedTokens? fakeTokens) =>
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
                            ServiceDescriptor.Scoped<IGoogleUserSyncService>(
                                _ => new FakeGoogleUserSyncService()
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
    public async Task PostAuthGoogleToken_WhenExchangeSucceeds_ReturnsToken()
    {
        // setup
        var identity = new GoogleIdentity(
            "user-123",
            "user@example.com",
            true,
            "Test",
            "User",
            null
        );
        var tokens = new IssuedTokens(
            new AppToken("fake-app-token", DateTimeOffset.UtcNow.AddHours(1)),
            "fake-refresh-token"
        );

        // test
        var response = await CreateClient(identity, tokens)
            .PostAsJsonAsync(
                "/auth/google/token",
                new { code = "fake-code", codeVerifier = "fake-verifier" }
            );

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(body);
        Assert.Equal(tokens.AccessToken.Value, body.Token);
        Assert.Equal(tokens.RefreshTokenSecret, body.RefreshTokenSecret);
        Assert.Equal(FakeGoogleUserSyncService.FixedUserId, body.User.Id);
        Assert.InRange(body.ExpiresInSeconds, 3595, 3600);
    }

    [Fact]
    public async Task PostAuthGoogleToken_WhenExchangeFails_ReturnsUnauthorized()
    {
        // setup
        var unusedTokens = new IssuedTokens(
            new AppToken("unused", DateTimeOffset.UtcNow),
            "unused"
        );

        // test
        var response = await CreateClient(null, unusedTokens)
            .PostAsJsonAsync(
                "/auth/google/token",
                new { code = "fake-code", codeVerifier = "fake-verifier" }
            );

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("Google authorization failed.", error!.Message);
    }

    [Fact]
    public async Task PostAuthRefresh_WhenRefreshSucceeds_ReturnsNewTokens()
    {
        // setup
        var identity = new GoogleIdentity(
            "user-123",
            "user@example.com",
            true,
            "Test",
            "User",
            null
        );
        var tokens = new IssuedTokens(
            new AppToken("fake-app-token", DateTimeOffset.UtcNow.AddHours(1)),
            "fake-refresh-token"
        );

        // test
        var response = await CreateClient(identity, tokens)
            .PostAsJsonAsync("/auth/refresh", new { refreshTokenSecret = "fake-refresh-token" });

        // verify
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>();
        Assert.NotNull(body);
        Assert.Equal(tokens.AccessToken.Value, body.Token);
        Assert.Equal(tokens.RefreshTokenSecret, body.RefreshTokenSecret);
        Assert.InRange(body.ExpiresInSeconds, 3595, 3600);
    }

    [Fact]
    public async Task PostAuthRefresh_WhenTokenInvalid_ReturnsUnauthorized()
    {
        // test
        var response = await CreateClient(null, null)
            .PostAsJsonAsync("/auth/refresh", new { refreshTokenSecret = "unknown-token" });

        // verify
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal("Refresh token is invalid, expired, or revoked.", error!.Message);
    }

    [Theory]
    [InlineData("""{"codeVerifier":"v"}""")] // code missing entirely
    [InlineData("""{"code":null,"codeVerifier":"v"}""")] // code explicitly null
    [InlineData("""{"code":123,"codeVerifier":"v"}""")] // code wrong type
    public async Task PostAuthGoogleToken_WhenCodeHasInvalidShape_ReturnsBadRequest(string rawJson)
    {
        // test
        var response = await CreateClient(null, null)
            .PostAsync(
                "/auth/google/token",
                new StringContent(rawJson, Encoding.UTF8, "application/json")
            );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")] // refreshTokenSecret missing entirely
    [InlineData("""{"refreshTokenSecret":null}""")] // refreshTokenSecret explicitly null
    [InlineData("""{"refreshTokenSecret":123}""")] // refreshTokenSecret wrong type
    public async Task PostAuthRefresh_WhenRefreshTokenSecretHasInvalidShape_ReturnsBadRequest(
        string rawJson
    )
    {
        // test
        var response = await CreateClient(null, null)
            .PostAsync(
                "/auth/refresh",
                new StringContent(rawJson, Encoding.UTF8, "application/json")
            );

        // verify
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
