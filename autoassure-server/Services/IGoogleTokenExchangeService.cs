using A2.Server.Common;
using A2.Server.Models;

namespace A2.Server.Services;

/// <summary>Exchanges a Google OAuth authorization code for the signed-in user's identity.</summary>
public interface IGoogleTokenExchangeService
{
    /// <summary>
    /// Exchanges <paramref name="code"/> and <paramref name="codeVerifier"/> for the caller's Google identity.
    /// </summary>
    /// <exception cref="GoogleTokenExchangeException">Google rejected the code or returned no ID token.</exception>
    /// <exception cref="Google.Apis.Auth.InvalidJwtException">The returned ID token failed validation.</exception>
    Task<GoogleIdentity> ExchangeCodeAsync(string code, string codeVerifier);
}
