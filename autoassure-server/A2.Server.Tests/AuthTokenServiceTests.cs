using System.IdentityModel.Tokens.Jwt;
using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests;

public class AuthTokenServiceTests
{
    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
    {
        public RefreshToken? Stored { get; private set; }
        public string? RevokedHash { get; private set; }

        public Task<RefreshToken?> GetByHashAsync(string refreshTokenSecretHash) =>
            Task.FromResult(
                Stored?.RefreshTokenSecretHash == refreshTokenSecretHash ? Stored : null
            );

        public Task AddAsync(RefreshToken token)
        {
            Stored = token;
            return Task.CompletedTask;
        }

        public Task MarkAsRevoked(string refreshTokenSecretHash, DateTimeOffset revokedAt)
        {
            RevokedHash = refreshTokenSecretHash;
            if (Stored?.RefreshTokenSecretHash == refreshTokenSecretHash)
            {
                Stored = Stored with { RevokedAt = revokedAt };
            }
            return Task.CompletedTask;
        }
    }

    private static AuthTokenService CreateService(
        FakeRefreshTokenRepository repository,
        DateTimeOffset now,
        int expiryDays = 30,
        int jwtExpiryMinutes = 15
    ) =>
        new(
            repository,
            Options.Create(
                new AuthTokenOptions(
                    SigningKey: "this-is-a-test-signing-key-32-bytes-long",
                    Issuer: "test-issuer",
                    Audience: "test-audience",
                    AccessTokenExpiryMinutes: jwtExpiryMinutes,
                    RefreshTokenExpiryDays: expiryDays
                )
            ),
            new FakeClock(now)
        );

    [Fact]
    public async Task IssueAsync_ProducesJwtWithExpectedClaimsAndExpiry()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var service = CreateService(new FakeRefreshTokenRepository(), now, jwtExpiryMinutes: 5);
        var identity = new GoogleIdentity("user-123", "user@example.com", true, "Test User", null);

        var tokens = await service.IssueAsync(identity);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessToken.Value);
        Assert.Equal("user-123", jwt.Subject);
        Assert.Equal(
            "user@example.com",
            jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value
        );
        Assert.Equal("test-issuer", jwt.Issuer);
        Assert.Equal("test-audience", jwt.Audiences.Single());
        Assert.Equal(now.AddMinutes(5), tokens.AccessToken.ExpiresAt);
        Assert.Equal(tokens.AccessToken.ExpiresAt.UtcDateTime, jwt.ValidTo);
    }

    [Fact]
    public async Task RefreshAsync_ReturnsNull_WhenTokenNotFound()
    {
        var service = CreateService(new FakeRefreshTokenRepository(), DateTimeOffset.UtcNow);

        var result = await service.RefreshAsync("unknown-token");

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_ReturnsNull_WhenTokenExpired()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new FakeRefreshTokenRepository();
        var issueService = CreateService(repository, now.AddDays(-31));
        var refreshTokenSecret = await IssueRefreshTokenSecretAsync(issueService);

        var result = await CreateService(repository, now).RefreshAsync(refreshTokenSecret);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public async Task RefreshAsync_HonorsExpiryBoundary(
        int secondsBeforeExpiryCutoff,
        bool expectValid
    )
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new FakeRefreshTokenRepository();
        var issueService = CreateService(
            repository,
            now.AddDays(-30).AddSeconds(secondsBeforeExpiryCutoff),
            expiryDays: 30
        );
        var refreshTokenSecret = await IssueRefreshTokenSecretAsync(issueService);

        var result = await CreateService(repository, now).RefreshAsync(refreshTokenSecret);

        Assert.Equal(expectValid, result is not null);
    }

    [Fact]
    public async Task RefreshAsync_ReturnsNull_WhenTokenRevoked()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new FakeRefreshTokenRepository();
        var service = CreateService(repository, now);
        var refreshTokenSecret = await IssueRefreshTokenSecretAsync(service);
        await repository.MarkAsRevoked(repository.Stored!.RefreshTokenSecretHash, now);

        var result = await service.RefreshAsync(refreshTokenSecret);

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_ReturnsNewTokens_WhenTokenValid()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new FakeRefreshTokenRepository();
        var service = CreateService(repository, now);
        var refreshTokenSecret = await IssueRefreshTokenSecretAsync(service);

        var result = await service.RefreshAsync(refreshTokenSecret);

        Assert.NotNull(result);
        Assert.NotEqual(refreshTokenSecret, result.RefreshTokenSecret);
    }

    [Fact]
    public async Task RefreshAsync_RevokesConsumedToken()
    {
        var now = DateTimeOffset.UtcNow;
        var repository = new FakeRefreshTokenRepository();
        var service = CreateService(repository, now);
        var refreshTokenSecret = await IssueRefreshTokenSecretAsync(service);

        await service.RefreshAsync(refreshTokenSecret);

        Assert.NotNull(repository.RevokedHash);
    }

    private static async Task<string> IssueRefreshTokenSecretAsync(AuthTokenService service)
    {
        var identity = new GoogleIdentity("user-123", "user@example.com", true, "Test User", null);
        var tokens = await service.IssueAsync(identity);
        return tokens.RefreshTokenSecret;
    }
}
