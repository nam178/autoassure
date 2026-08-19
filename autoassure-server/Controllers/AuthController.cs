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
    IAuthTokenService authTokenService
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
            var tokens = await authTokenService.IssueAsync(identity);
            return Ok(
                new AuthTokenResponse(
                    tokens.AccessToken.Value,
                    tokens.AccessToken.ExpiresAt,
                    tokens.RefreshTokenSecret,
                    identity
                )
            );
        }
        catch (Exception ex) when (ex is InvalidJwtException or GoogleTokenExchangeException)
        {
            return Unauthorized(new { error = "Google authorization failed." });
        }
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<RefreshTokenResponse>> Refresh(RefreshTokenRequest request)
    {
        var tokens = await authTokenService.RefreshAsync(request.RefreshTokenSecret);

        if (tokens is null)
        {
            return Unauthorized(new { error = "Refresh token is invalid, expired, or revoked." });
        }

        return Ok(
            new RefreshTokenResponse(
                tokens.AccessToken.Value,
                tokens.AccessToken.ExpiresAt,
                tokens.RefreshTokenSecret
            )
        );
    }
}
