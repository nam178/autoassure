using A2.Server.Models;

namespace A2.Server.Services;

public interface IGoogleTokenExchangeService
{
    Task<GoogleIdentity> ExchangeCodeAsync(string code, string codeVerifier);
}
