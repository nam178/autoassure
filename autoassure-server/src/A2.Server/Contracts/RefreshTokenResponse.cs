namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
/// <summary>Returned after a successful token refresh: the newly issued tokens.</summary>
/// <param name="ExpiresInSeconds">
/// Number of seconds until <paramref name="Token"/> expires, measured from when the response is sent.
/// A relative duration is used instead of an absolute timestamp because the client's clock may be offset
/// from the server's.
/// </param>
public record RefreshTokenResponse(string Token, int ExpiresInSeconds, string RefreshTokenSecret);
