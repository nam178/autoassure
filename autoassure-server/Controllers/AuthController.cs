using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Services;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;

namespace A2.Server.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(
    IGoogleTokenExchangeService googleTokenExchangeService,
    ITokenIssuerService tokenService
) : ControllerBase
{
    [HttpPost("google/token")]
    public async Task<ActionResult<AuthTokenResponse>> ExchangeGoogleCode(
        ExchangeGoogleCodeRequest request
    )
    {
        try
        {
            var identity = await googleTokenExchangeService.ExchangeCodeAsync(
                request.Code,
                request.CodeVerifier
            );
            var token = tokenService.IssueToken(identity);
            return Ok(new AuthTokenResponse(token.Value, token.ExpiresAt, identity));
        }
        catch (Exception ex) when (ex is InvalidJwtException or GoogleTokenExchangeException)
        {
            return Unauthorized(new { error = "Google authorization failed." });
        }
    }
}
