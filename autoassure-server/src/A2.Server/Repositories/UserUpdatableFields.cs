namespace A2.Server.Repositories;

/// <summary>The only User fields <see cref="IUserRepository.TryUpdateAsync"/> is allowed to change.</summary>
public record UserUpdatableFields
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public required bool EmailVerified { get; init; }
}
