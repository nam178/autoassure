namespace A2.Server.Common;

/// <summary>
/// Configuration for the app's own auth tokens, issued by <c>AuthTokenService</c> after a user
/// signs in (see <c>Auth</c> in appsettings).
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global -- bound via IOptions<T> from configuration, not `new`'d directly
public record AuthTokenOptions
{
    /// <summary>Key used to sign and validate access tokens (JWTs).</summary>
    public string SigningKey { get; init; } = "";

    /// <summary>Issuer claim (`iss`) on issued access tokens, and the value required to accept one.</summary>
    public string Issuer { get; init; } = "";

    /// <summary>Audience claim (`aud`) on issued access tokens, and the value required to accept one.</summary>
    public string Audience { get; init; } = "";

    /// <summary>
    /// How long an access token stays valid. Short-lived: once expired, the client must call
    /// the refresh endpoint to get a new one rather than logging in again.
    /// </summary>
    public int AccessTokenExpiryMinutes { get; init; } = 15;

    /// <summary>
    /// How long a refresh token stays valid after being issued. Long-lived: once expired, the
    /// user must log in again to get a new one.
    /// </summary>
    public int RefreshTokenExpiryDays { get; init; } = 30;
}
