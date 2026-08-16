namespace A2.Server.Models;

public record AppToken(string Value, DateTimeOffset ExpiresAt);
