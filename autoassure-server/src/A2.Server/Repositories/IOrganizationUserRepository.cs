using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>
///     Persists Organization membership rows. Storage-agnostic — callers only ever
///     see the OrganizationUser domain model.
///     A User has at most one membership in any given Organization. Both write
///     methods enforce this: they never create a second row for the same
///     (Organization, User) pair, and never touch a row that does not exist.
/// </summary>
public interface IOrganizationUserRepository
{
    /// <summary>
    ///     Adds a membership linking a User to an Organization. Returns false, and
    ///     changes nothing, when that User already has a membership in that
    ///     Organization.
    /// </summary>
    Task<bool> TryCreateAsync(OrganizationUser membership);

    /// <summary>
    ///     Changes Role, UpdatedByUserId and UpdatedAt on the User's membership in
    ///     the Organization. Returns false, and creates nothing, when that User has
    ///     no membership in that Organization.
    /// </summary>
    Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid userId,
        OrganizationUserUpdatableFields fields
    );

    /// <summary>
    ///     All Organizations this User belongs to. Ordering: by the Organization's
    ///     creation
    ///     time, newest first -- not the order the memberships were created.
    ///
    /// It is guaranteed that the returned organizations are unique (meaning,
    /// no users in an organization has multiple memberships).
    /// </summary>
    Task<IReadOnlyList<OrganizationUser>> ListByUserAsync(Guid userId);
}
