namespace A2.Server.Common;

/// <summary>
/// Google OAuth client credentials used to exchange an authorization code for tokens.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global -- bound via IOptions<T> from configuration, not `new`'d directly
public record GoogleAuthOptions
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RedirectUri { get; init; } = "";
}
