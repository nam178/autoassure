using Google.Apis.Auth;

namespace A2.Server.Services;

/// <summary>Default <see cref="IGoogleIdTokenValidator"/> backed by Google's own JWT validation library.</summary>
public class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    /// <exception cref="InvalidJwtException">The token's signature, audience, or claims failed validation.</exception>
    public Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken, string audience) =>
        GoogleJsonWebSignature.ValidateAsync(
            idToken,
            new GoogleJsonWebSignature.ValidationSettings { Audience = [audience] }
        );
}
