namespace A2.Server.Common;

/// <summary>Configuration for signing and validating the app's own JWTs (see <c>Jwt</c> in appsettings).</summary>
public record TokenIssuerServiceOptions(
    string SigningKey = "",
    string Issuer = "",
    string Audience = "",
    int ExpiryMinutes = 60
);
