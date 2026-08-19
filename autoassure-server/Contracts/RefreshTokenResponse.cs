namespace A2.Server.Contracts;

// ReSharper disable NotAccessedPositionalProperty.Global -- serialized to the JSON response body, not read in-process
public record RefreshTokenResponse(
    string Token,
    DateTimeOffset ExpiresAt,
    string RefreshTokenSecret
);
