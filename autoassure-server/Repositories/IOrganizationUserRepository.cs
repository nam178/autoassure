using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists Organization membership rows. Storage-agnostic — callers only ever see the
/// OrganizationUser domain model.</summary>
public interface IOrganizationUserRepository
{
    /// <summary>Creates a new membership linking a User to an Organization.</summary>
    Task SaveAsync(OrganizationUser membership);

    /// <summary>All Organizations this User belongs to. Ordering: by the Organization's creation
    /// time, not the order the memberships were created.</summary>
    Task<IReadOnlyList<OrganizationUser>> ListByUserAsync(Guid userId);
}
