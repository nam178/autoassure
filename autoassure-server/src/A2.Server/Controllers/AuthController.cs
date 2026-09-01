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
    IGoogleUserSyncService googleUserSyncService,
    IAuthTokenService authTokenService,
    IClock clock
) : ControllerBase
{
    /// <response code="401">The Google authorization code or PKCE verifier is invalid or expired.</response>
    [HttpPost("google/token", Name = "ExchangeGoogleCode")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
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
            var user = await googleUserSyncService.SyncAsync(identity);
            var tokens = await authTokenService.IssueAsync(user);
            return Ok(
                new AuthTokenResponse(
                    tokens.AccessToken.Value,
                    ExpiresInSeconds(tokens.AccessToken.ExpiresAt),
                    tokens.RefreshTokenSecret,
                    user.ToResponse()
                )
            );
        }
        catch (Exception ex) when (ex is InvalidJwtException or GoogleTokenExchangeException)
        {
            return Unauthorized(new ErrorResponse("Google authorization failed."));
        }
    }

    /// <response code="401">The refresh token is invalid, expired, or revoked.</response>
    [HttpPost("refresh", Name = "RefreshToken")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RefreshTokenResponse>> Refresh(RefreshTokenRequest request)
    {
        var tokens = await authTokenService.RefreshAsync(request.RefreshTokenSecret);

        if (tokens is null)
        {
            return Unauthorized(
                new ErrorResponse("Refresh token is invalid, expired, or revoked.")
            );
        }

        return Ok(
            new RefreshTokenResponse(
                tokens.AccessToken.Value,
                ExpiresInSeconds(tokens.AccessToken.ExpiresAt),
                tokens.RefreshTokenSecret
            )
        );
    }

    private int ExpiresInSeconds(DateTimeOffset expiresAt) =>
        (int)Math.Round((expiresAt - clock.UtcNow).TotalSeconds);
}
