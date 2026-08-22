namespace A2.Server.Contracts;

/// <summary>Requests a new access token using a previously issued refresh token.</summary>
/// <param name="RefreshTokenSecret">The raw refresh token secret previously issued to the client, to be exchanged for a new access token.</param>
public record RefreshTokenRequest(string RefreshTokenSecret);
