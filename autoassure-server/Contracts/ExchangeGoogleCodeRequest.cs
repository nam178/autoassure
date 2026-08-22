namespace A2.Server.Contracts;

/// <summary>An OAuth 2.0 PKCE authorization code from Google's consent screen, to be exchanged for
/// the user's Google identity.</summary>
/// <param name="Code">The authorization code returned by Google after the user consents.</param>
/// <param name="CodeVerifier">The PKCE code verifier the client generated for this authorization request.</param>
public record ExchangeGoogleCodeRequest(string Code, string CodeVerifier);
