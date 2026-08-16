using A2.Server.Models;

namespace A2.Server.Services;

public interface ITokenIssuerService
{
    AppToken IssueToken(GoogleIdentity identity);
}
