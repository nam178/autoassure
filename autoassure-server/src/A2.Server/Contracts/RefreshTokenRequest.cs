using System.ComponentModel.DataAnnotations;

namespace A2.Server.Contracts;

/// <summary>Requests a new access token using a previously issued refresh token.</summary>
public record RefreshTokenRequest
{
    /// <summary>The raw refresh token secret previously issued to the client, to be exchanged for a new access token.</summary>
    [Required, MaxLength(500)]
    public required string RefreshTokenSecret { get; init; }
}
