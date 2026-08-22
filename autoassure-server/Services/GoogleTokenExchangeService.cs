using System.Text.Json.Serialization;
using A2.Server.Common;
using A2.Server.Models;
using Microsoft.Extensions.Options;

namespace A2.Server.Services;

public class GoogleTokenExchangeService(
    HttpClient httpClient,
    IOptions<GoogleAuthOptions> options,
    IGoogleIdTokenValidator idTokenValidator
) : IGoogleTokenExchangeService
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private sealed record GoogleTokenEndpointResponse(
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription
    );

    public async Task<GoogleIdentity> ExchangeCodeAsync(string code, string codeVerifier)
    {
        var response = await httpClient.PostAsync(
            TokenEndpoint,
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["code_verifier"] = codeVerifier,
                    ["client_id"] = options.Value.ClientId,
                    ["client_secret"] = options.Value.ClientSecret,
                    ["redirect_uri"] = options.Value.RedirectUri,
                }
            )
        );

        var body = await response.Content.ReadFromJsonAsync<GoogleTokenEndpointResponse>();

        if (!response.IsSuccessStatusCode || body?.IdToken is null)
        {
            throw new GoogleTokenExchangeException(
                body?.ErrorDescription ?? body?.Error ?? "Google token exchange failed."
            );
        }

        var payload = await idTokenValidator.ValidateAsync(body.IdToken, options.Value.ClientId);

        return new GoogleIdentity(
            payload.Subject,
            payload.Email,
            payload.EmailVerified,
            payload.Name,
            payload.HostedDomain
        );
    }
}
