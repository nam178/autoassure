using A2.Server.UserManagement.Models;

namespace A2.Server.UserManagement.Services;

public interface IAuthTokenService
{
    Task<IssuedTokens> IssueAsync(User user);

    Task<IssuedTokens?> RefreshAsync(string refreshTokenSecret);
}
