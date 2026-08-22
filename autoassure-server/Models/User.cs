namespace A2.Server.Models;

/// <summary>An AutoAssure user account, provisioned from a Google sign-in.</summary>
public record User
{
    public required string Id { get; init; }
    public required string GoogleUserId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required bool EmailVerified { get; init; }
}
