using A2.Server.Models;

namespace A2.Server.Services;

public interface IAuthTokenService
{
    Task<IssuedTokens> IssueAsync(GoogleIdentity identity);

    Task<IssuedTokens?> RefreshAsync(string refreshTokenSecret);
}
