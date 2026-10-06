using A2.Server.Engine.Models;

namespace A2.Server.Engine.Repositories;

/// <summary>
/// Persists Preconditions. Storage-agnostic — callers only ever see the
/// Precondition domain model.
/// </summary>
public interface IPreconditionRepository
{
    /// <summary>
    /// Creates the Precondition. Returns false if its Application no longer
    /// exists.
    /// </summary>
    Task<bool> TrySaveAsync(Precondition precondition);

    /// <summary>
    /// Updates only Name, ValueSource, ExampleValue, UpdatedByUserId, and
    /// UpdatedAt on an existing Precondition. Returns false if the Precondition
    /// no longer exists.
    /// </summary>
    Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid preconditionId,
        PreconditionUpdatableFields fields
    );

    Task<Precondition?> GetByIdAsync(
        Guid organizationId,
        Guid applicationId,
        Guid preconditionId
    );

    /// <summary>
    /// All Preconditions in this Application's library. Ordering: newest
    /// first.
    /// </summary>
    Task<IReadOnlyList<Precondition>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    );
}
