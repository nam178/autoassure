using A2.Server.Models;

namespace A2.Server.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string refreshTokenSecretHash);

    Task AddAsync(RefreshToken token);

    /// <summary>Atomically revokes a token, but only if it isn't already revoked. Returns false if it was already revoked (e.g. by a concurrent refresh), so the caller can detect reuse.</summary>
    Task<bool> TryRevokeAsync(string refreshTokenSecretHash, DateTimeOffset revokedAt);
}
