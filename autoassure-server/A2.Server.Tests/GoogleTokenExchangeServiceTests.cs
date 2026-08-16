using System.Net;
using System.Text;
using System.Text.Json;
using A2.Server.Common;
using A2.Server.Services;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests;

public class GoogleTokenExchangeServiceTests
{
    private sealed class StubHandler(HttpStatusCode statusCode, object body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(body),
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
    }

    [Fact]
    public async Task ExchangeCodeAsync_Throws_WhenGoogleReturnsError()
    {
        var handler = new StubHandler(
            HttpStatusCode.BadRequest,
            new { error = "invalid_grant", error_description = "Bad code" }
        );
        var httpClient = new HttpClient(handler);
        var options = Options.Create(
            new GoogleAuthOptions(
                ClientId: "client",
                ClientSecret: "secret",
                RedirectUri: "https://example.com"
            )
        );
        var service = new GoogleTokenExchangeService(httpClient, options);

        await Assert.ThrowsAsync<GoogleTokenExchangeException>(() =>
            service.ExchangeCodeAsync("bad-code", "verifier")
        );
    }
}
