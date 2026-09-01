using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>An OAuth 2.0 PKCE authorization code from Google's consent screen, to be exchanged for
/// the user's Google identity.</summary>
public record ExchangeGoogleCodeRequest
{
    /// <summary>The authorization code returned by Google after the user consents.</summary>
    [Required, MaxLength(2000)]
    public required string Code { get; init; }

    /// <summary>The PKCE code verifier the client generated for this authorization request.</summary>
    [Required, MaxLength(200)]
    public required string CodeVerifier { get; init; }
}
