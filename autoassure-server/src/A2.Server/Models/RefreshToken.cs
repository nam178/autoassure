namespace A2.Server.Models;

/// <summary>A long-lived credential that lets a User obtain a new AppToken without signing in
/// again, until it expires or is revoked.</summary>
public record RefreshToken(
    string RefreshTokenSecretHash,
    Guid UserId,
    string Email,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt
);
