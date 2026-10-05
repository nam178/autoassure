namespace A2.Server.UserManagement.Models;

/// <summary>
/// A bearer token issued to a client, along with when it stops being
/// valid.
/// </summary>
public record AppToken(string Value, DateTimeOffset ExpiresAt);
