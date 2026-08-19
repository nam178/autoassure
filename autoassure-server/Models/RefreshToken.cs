namespace A2.Server.Models;

public record RefreshToken(
    string RefreshTokenSecretHash,
    string GoogleUserId,
    string Email,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt
);
