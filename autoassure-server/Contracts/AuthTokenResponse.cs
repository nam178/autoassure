using A2.Server.Models;

namespace A2.Server.Contracts;

public record AuthTokenResponse(string Token, DateTimeOffset ExpiresAt, GoogleIdentity Identity);
