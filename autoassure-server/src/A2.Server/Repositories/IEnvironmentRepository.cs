using Environment = A2.Server.Models.Environment;

namespace A2.Server.Repositories;

/// <summary>Persists Environments. Storage-agnostic — callers only ever see the Environment domain model.</summary>
public interface IEnvironmentRepository
{
    /// <summary>Creates the Environment. Returns false if its Application no longer exists.</summary>
    Task<bool> TrySaveAsync(Environment environment);

    /// <summary>Updates only Name, Classification, UpdatedByUserId, and UpdatedAt on an existing
    /// Environment. Returns false if the Environment no longer exists.</summary>
    Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        EnvironmentUpdatableFields fields
    );

    /// <summary>Point lookup by Id, scoped to the Organization.</summary>
    Task<Environment?> GetByIdAsync(Guid organizationId, Guid id);

    /// <summary>All Environments belonging to this Application. Ordering: creation order.</summary>
    Task<IReadOnlyList<Environment>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    );
}
