using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>
/// Persists AutoAssure Organizations. Storage-agnostic — callers only
/// ever see the Organization domain model.
/// </summary>
public interface IOrganizationRepository
{
    /// <summary>Looks up an Organization by Id, or null if none exists.</summary>
    Task<Organization?> GetByIdAsync(Guid organizationId);

    /// <summary>
    /// Looks up multiple Organizations by their Ids. Returns only the
    /// Organizations that exist.
    /// </summary>
    Task<IReadOnlyList<Organization>> GetByIdsAsync(
        IReadOnlyList<Guid> organizationIds
    );

    /// <summary>
    /// Attempts to set the lifecycle state of an Organization to the given state.
    /// Returns true if the Organization exists and was updated, false if it doesn't
    /// exist.
    /// </summary>
    Task<bool> TrySetLifecycleStateAsync(
        Guid organizationId,
        LifecycleState newState
    );
}
