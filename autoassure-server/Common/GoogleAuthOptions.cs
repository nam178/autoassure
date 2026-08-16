namespace A2.Server.Common;

// ReSharper disable once ClassNeverInstantiated.Global -- bound via IOptions<T> from configuration, not `new`'d directly
public record GoogleAuthOptions(
    string ClientId = "",
    string ClientSecret = "",
    string RedirectUri = ""
);
