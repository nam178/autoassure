using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Models;
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
            var user = await googleUserSyncService.SyncAsync(identity);
            var tokens = await authTokenService.IssueAsync(user);
            return Ok(
                new AuthTokenResponse(
                    tokens.AccessToken.Value,
                    ExpiresInSeconds(tokens.AccessToken.ExpiresAt),
                    tokens.RefreshTokenSecret,
                    ToUserResponse(user)
                )
            );
        }
        catch (Exception ex) when (ex is InvalidJwtException or GoogleTokenExchangeException)
        {
            return Unauthorized(new { error = "Google authorization failed." });
        }
    }

    private static UserResponse ToUserResponse(User user) =>
        new(user.Id, user.FirstName, user.LastName, user.Email, user.EmailVerified);

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
                ExpiresInSeconds(tokens.AccessToken.ExpiresAt),
                tokens.RefreshTokenSecret
            )
        );
    }

    private int ExpiresInSeconds(DateTimeOffset expiresAt) =>
        (int)Math.Round((expiresAt - clock.UtcNow).TotalSeconds);
}
