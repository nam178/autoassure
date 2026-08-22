using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace A2.Server.Services;

public class AuthTokenService(
    IRefreshTokenRepository repository,
    IOptions<AuthTokenOptions> authTokenOptions,
    IClock clock
) : IAuthTokenService
{
    public async Task<IssuedTokens> IssueAsync(GoogleIdentity identity)
    {
        var accessToken = GenerateNewAccessToken(identity);
        var refreshTokenSecret = await CreateRefreshTokenSecretAsync(
            identity.GoogleUserId,
            identity.Email
        );

        return new IssuedTokens(accessToken, refreshTokenSecret);
    }

    public async Task<IssuedTokens?> RefreshAsync(string refreshTokenSecret)
    {
        var refreshTokenSecretHash = Hash(refreshTokenSecret);
        var stored = await repository.GetByHashAsync(refreshTokenSecretHash);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt < clock.UtcNow)
        {
            return null;
        }

        // Atomically claim the token so two concurrent refreshes of the same secret can't both
        // succeed. If we lose the race, someone else already revoked/used it — treat as invalid.
        var claimed = await repository.TryRevokeAsync(refreshTokenSecretHash, clock.UtcNow);
        if (!claimed)
        {
            return null;
        }

        var identity = new GoogleIdentity(stored.GoogleUserId, stored.Email, false, null, null);
        var accessToken = GenerateNewAccessToken(identity);
        var newRefreshTokenSecret = await CreateRefreshTokenSecretAsync(
            stored.GoogleUserId,
            stored.Email
        );

        return new IssuedTokens(accessToken, newRefreshTokenSecret);
    }

    private AppToken GenerateNewAccessToken(GoogleIdentity identity)
    {
        var expiresAt = clock.UtcNow.AddMinutes(authTokenOptions.Value.AccessTokenExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, identity.GoogleUserId),
            new Claim(JwtRegisteredClaimNames.Email, identity.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authTokenOptions.Value.SigningKey)),
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: authTokenOptions.Value.Issuer,
            audience: authTokenOptions.Value.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials
        );

        return new AppToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private async Task<string> CreateRefreshTokenSecretAsync(string googleUserId, string email)
    {
        var secret = Convert
            .ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var now = clock.UtcNow;

        await repository.AddAsync(
            new RefreshToken(
                Hash(secret),
                googleUserId,
                email,
                now.AddDays(authTokenOptions.Value.RefreshTokenExpiryDays),
                now,
                null
            )
        );

        return secret;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
