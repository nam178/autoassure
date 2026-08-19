using A2.Server.Models;

namespace A2.Server.Repositories;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string refreshTokenSecretHash);

    Task AddAsync(RefreshToken token);

    Task MarkAsRevoked(string refreshTokenSecretHash, DateTimeOffset revokedAt);
}
