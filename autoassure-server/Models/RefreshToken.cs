namespace A2.Server.Models;

public record RefreshToken(
    string RefreshTokenSecretHash,
    string UserId,
    string Email,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt
);
