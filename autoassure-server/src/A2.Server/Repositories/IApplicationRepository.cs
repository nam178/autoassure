using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists Applications. Storage-agnostic — callers only ever see the Application domain model.</summary>
public interface IApplicationRepository
{
    /// <summary>Creates the Application. Returns false if its Organization no longer exists.</summary>
    Task<bool> TrySaveAsync(Application application);

    /// <summary>Point lookup by Id, scoped to the Organization.</summary>
    Task<Application?> GetByIdAsync(Guid organizationId, Guid id);

    /// <summary>All Applications owned by this Organization. Ordering: creation order.</summary>
    Task<IReadOnlyList<Application>> ListByOrganizationAsync(Guid organizationId);
}
