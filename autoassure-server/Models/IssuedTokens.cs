namespace A2.Server.Models;

/// <summary>The pair of tokens handed to a user after a successful sign-in: a short-lived access
/// token and the secret for a longer-lived refresh token.</summary>
public record IssuedTokens(AppToken AccessToken, string RefreshTokenSecret);
