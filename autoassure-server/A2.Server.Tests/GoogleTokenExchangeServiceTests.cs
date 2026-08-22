using System.Net;
using System.Text;
using System.Text.Json;
using A2.Server.Common;
using A2.Server.Services;
using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests;

public class GoogleTokenExchangeServiceTests
{
    private sealed class FakeGoogleOAuthHandler(string validCode, string idToken)
        : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var form = await new StreamReader(
                await request.Content!.ReadAsStreamAsync(cancellationToken)
            ).ReadToEndAsync(cancellationToken);
            var fields = System.Web.HttpUtility.ParseQueryString(form);

            if (
                fields["grant_type"] != "authorization_code"
                || string.IsNullOrEmpty(fields["code_verifier"])
                || string.IsNullOrEmpty(fields["client_id"])
                || string.IsNullOrEmpty(fields["client_secret"])
                || string.IsNullOrEmpty(fields["redirect_uri"])
                || fields["code"] != validCode
            )
            {
                return JsonResponse(
                    HttpStatusCode.BadRequest,
                    new { error = "invalid_grant", error_description = "Bad code" }
                );
            }

            return JsonResponse(
                HttpStatusCode.OK,
                new
                {
                    id_token = idToken,
                    expires_in = 3600,
                    token_type = "Bearer",
                    scope = "openid email",
                }
            );
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body) =>
            new(statusCode)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
    }

    private sealed class FakeGoogleIdTokenValidator(
        string expectedIdToken,
        GoogleJsonWebSignature.Payload payload
    ) : IGoogleIdTokenValidator
    {
        public Task<GoogleJsonWebSignature.Payload> ValidateAsync(
            string idToken,
            string audience
        ) =>
            idToken == expectedIdToken
                ? Task.FromResult(payload)
                : throw new InvalidJwtException("Unexpected id_token.");
    }

    private static GoogleTokenExchangeService CreateService(
        string validCode,
        string idToken,
        GoogleJsonWebSignature.Payload payload
    ) =>
        new(
            new HttpClient(new FakeGoogleOAuthHandler(validCode, idToken)),
            Options.Create(
                new GoogleAuthOptions
                {
                    ClientId = "client",
                    ClientSecret = "secret",
                    RedirectUri = "https://example.com",
                }
            ),
            new FakeGoogleIdTokenValidator(idToken, payload)
        );

    [Fact]
    public async Task ExchangeCodeAsync_ReturnsIdentity_WhenGoogleReturnsValidToken()
    {
        var payload = new GoogleJsonWebSignature.Payload
        {
            Subject = "user-123",
            Email = "user@example.com",
            EmailVerified = true,
            GivenName = "Test",
            FamilyName = "User",
        };
        var service = CreateService("good-code", "fake-id-token", payload);

        var identity = await service.ExchangeCodeAsync("good-code", "verifier");

        Assert.Equal(payload.Subject, identity.GoogleUserId);
        Assert.Equal(payload.Email, identity.Email);
        Assert.True(identity.EmailVerified);
        Assert.Equal(payload.GivenName, identity.FirstName);
        Assert.Equal(payload.FamilyName, identity.LastName);
    }

    [Fact]
    public async Task ExchangeCodeAsync_Throws_WhenGoogleReturnsError()
    {
        var payload = new GoogleJsonWebSignature.Payload { Subject = "unused" };
        var service = CreateService("good-code", "fake-id-token", payload);

        await Assert.ThrowsAsync<GoogleTokenExchangeException>(() =>
            service.ExchangeCodeAsync("bad-code", "verifier")
        );
    }
}
