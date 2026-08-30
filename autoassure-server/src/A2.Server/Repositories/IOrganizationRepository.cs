using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists AutoAssure Organizations. Storage-agnostic — callers only ever see the Organization domain model.</summary>
public interface IOrganizationRepository
{
    /// <summary>Looks up an Organization by Id, or null if none exists.</summary>
    Task<Organization?> GetByIdAsync(Guid id);
}
