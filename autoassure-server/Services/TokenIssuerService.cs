using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using A2.Server.Common;
using A2.Server.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace A2.Server.Services;

public class TokenIssuerService(IOptions<TokenIssuerServiceOptions> options) : ITokenIssuerService
{
    public AppToken IssueToken(GoogleIdentity identity)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(options.Value.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, identity.GoogleUserId),
            new Claim(JwtRegisteredClaimNames.Email, identity.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey)),
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: options.Value.Issuer,
            audience: options.Value.Audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials
        );

        return new AppToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
