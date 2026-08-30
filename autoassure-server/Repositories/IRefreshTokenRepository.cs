using A2.Server.Models;

namespace A2.Server.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string refreshTokenSecretHash);

    Task SaveAsync(RefreshToken token);

    /// <summary>Atomically updates only RevokedAt, but only if it isn't already set. Returns false if it was already revoked (e.g. by a concurrent refresh), so the caller can detect reuse.</summary>
    Task<bool> TryUpdateAsync(string refreshTokenSecretHash, DateTimeOffset revokedAt);
}
