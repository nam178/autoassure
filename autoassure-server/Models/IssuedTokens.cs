namespace A2.Server.Models;

public record IssuedTokens(AppToken AccessToken, string RefreshTokenSecret);
