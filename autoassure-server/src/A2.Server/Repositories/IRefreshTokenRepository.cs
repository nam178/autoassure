using A2.Server.Models;

namespace A2.Server.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string refreshTokenSecretHash);

    Task SaveAsync(RefreshToken token);

    /// <summary>
    ///     Atomically updates only RevokedAt, but only if the token exists and isn't
    ///     already
    ///     revoked. Returns false if the token doesn't exist (e.g. expired via TTL) or
    ///     was already
    ///     revoked (e.g. by a concurrent refresh), so the caller can detect reuse.
    /// </summary>
    Task<bool> TryUpdateAsync(
        string refreshTokenSecretHash,
        DateTimeOffset revokedAt
    );
}
