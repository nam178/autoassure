using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>
///     The only OrganizationUser fields
///     <see cref="IOrganizationUserRepository.TryUpdateAsync" /> is allowed to
///     change.
/// </summary>
public record OrganizationUserUpdatableFields
{
    public required OrganizationRole Role { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
