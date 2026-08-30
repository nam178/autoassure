using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists Preconditions. Storage-agnostic — callers only ever see the Precondition domain model.</summary>
public interface IPreconditionRepository
{
    /// <summary>Creates the Precondition. Returns false if its Application no longer exists.</summary>
    Task<bool> TrySaveAsync(Precondition precondition);

    /// <summary>Updates only Name, ValueSource, ExampleValue, UpdatedByUserId, and UpdatedAt on an
    /// existing Precondition.</summary>
    Task UpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        PreconditionUpdatableFields fields
    );

    Task DeleteAsync(Guid organizationId, Guid id);

    /// <summary>Point lookup by Id, scoped to the Organization.</summary>
    Task<Precondition?> GetByIdAsync(Guid organizationId, Guid id);

    /// <summary>All Preconditions in this Application's library. Ordering: creation order.</summary>
    Task<IReadOnlyList<Precondition>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    );
}
