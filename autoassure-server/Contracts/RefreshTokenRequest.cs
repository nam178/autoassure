namespace A2.Server.Contracts;

/// <param name="RefreshTokenSecret">The raw refresh token secret previously issued to the client, to be exchanged for a new access token.</param>
public record RefreshTokenRequest(string RefreshTokenSecret);
