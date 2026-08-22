using A2.Server.Models;

namespace A2.Server.Contracts;

/// <param name="ExpiresInSeconds">
/// Number of seconds until <paramref name="Token"/> expires, measured from when the response is sent.
/// A relative duration is used instead of an absolute timestamp because the client's clock may be offset
/// from the server's.
/// </param>
public record AuthTokenResponse(
    string Token,
    int ExpiresInSeconds,
    string RefreshTokenSecret,
    GoogleIdentity Identity
);
