using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace A2.Server.Common;

/// <summary>Extracts AutoAssure-specific claims from an authenticated request's <see cref="ClaimsPrincipal"/>.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The signed-in User's Id, from the JWT's `sub` claim.</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var sub =
            principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("JWT is missing the 'sub' claim.");
        return Guid.Parse(sub);
    }
}
