using Google.Apis.Auth;

namespace A2.Server.Services;

/// <summary>
/// Verifies a Google-issued ID token's signature and claims. Use this instead of calling
/// Google's SDK directly so token validation can be faked in tests.
/// </summary>
public interface IGoogleIdTokenValidator
{
    /// <summary>Validates <paramref name="idToken"/> and returns its decoded payload.</summary>
    /// <exception cref="InvalidJwtException">The token's signature, audience, or claims failed validation.</exception>
    Task<GoogleJsonWebSignature.Payload> ValidateAsync(string idToken, string audience);
}
