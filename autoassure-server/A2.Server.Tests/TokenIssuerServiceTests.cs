using System.IdentityModel.Tokens.Jwt;
using A2.Server.Common;
using A2.Server.Models;
using A2.Server.Services;
using Microsoft.Extensions.Options;

namespace A2.Server.Tests;

public class TokenServiceTests
{
    [Fact]
    public void IssueToken_ProducesJwtWithExpectedClaimsAndExpiry()
    {
        var options = new TokenIssuerServiceOptions(
            SigningKey: "this-is-a-test-signing-key-32-bytes-long",
            Issuer: "test-issuer",
            Audience: "test-audience",
            ExpiryMinutes: 5
        );
        var service = new TokenIssuerService(Options.Create(options));
        var identity = new GoogleIdentity("user-123", "user@example.com", true, "Test User", null);

        var token = service.IssueToken(identity);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);
        Assert.Equal("user-123", jwt.Subject);
        Assert.Equal(
            "user@example.com",
            jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value
        );
        Assert.Equal("test-issuer", jwt.Issuer);
        Assert.Equal("test-audience", jwt.Audiences.Single());
        Assert.Equal(token.ExpiresAt.UtcDateTime, jwt.ValidTo, TimeSpan.FromSeconds(1));
    }
}
