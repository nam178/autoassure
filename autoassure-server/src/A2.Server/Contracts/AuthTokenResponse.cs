namespace A2.Server.Contracts;

/// <summary>Returned after a successful Google sign-in: the issued tokens and the signed-in user.</summary>
/// <param name="ExpiresInSeconds">
/// Number of seconds until <paramref name="Token"/> expires, measured from when the response is sent.
/// A relative duration is used instead of an absolute timestamp because the client's clock may be offset
/// from the server's.
/// </param>
public record AuthTokenResponse(
    string Token,
    int ExpiresInSeconds,
    string RefreshTokenSecret,
    UserResponse User
);
