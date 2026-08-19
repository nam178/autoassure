namespace A2.Server.Common;

/// <summary>
/// Configuration for the app's own auth tokens, issued by <c>AuthTokenService</c> after a user
/// signs in (see <c>Auth</c> in appsettings).
/// </summary>
/// <param name="SigningKey">Key used to sign and validate access tokens (JWTs).</param>
/// <param name="Issuer">Issuer claim (`iss`) on issued access tokens, and the value required to accept one.</param>
/// <param name="Audience">Audience claim (`aud`) on issued access tokens, and the value required to accept one.</param>
/// <param name="AccessTokenExpiryMinutes">
/// How long an access token stays valid. Short-lived: once expired, the client must call
/// the refresh endpoint to get a new one rather than logging in again.
/// </param>
/// <param name="RefreshTokenExpiryDays">
/// How long a refresh token stays valid after being issued. Long-lived: once expired, the
/// user must log in again to get a new one.
/// </param>
// ReSharper disable once ClassNeverInstantiated.Global -- bound via IOptions<T> from configuration, not `new`'d directly
public record AuthTokenOptions(
    string SigningKey = "",
    string Issuer = "",
    string Audience = "",
    int AccessTokenExpiryMinutes = 15,
    int RefreshTokenExpiryDays = 30
);
