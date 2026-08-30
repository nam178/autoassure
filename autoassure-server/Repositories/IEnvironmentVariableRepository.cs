using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists per-Environment variable rows. Storage-agnostic — callers only ever see the
/// EnvironmentVariable domain model. One row per key-value pair: setting or deleting one variable
/// never reads or rewrites the others.</summary>
public interface IEnvironmentVariableRepository
{
    /// <summary>Updates a single variable's Value (creating the row on first write). Always sets
    /// Value, OrganizationId, EnvironmentId, UpdatedByUserId, UpdatedAt; sets CreatedAt and
    /// CreatedByUserId only if the row doesn't already exist. Returns false if the Environment no
    /// longer exists.</summary>
    Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid environmentId,
        string key,
        string value,
        Guid updatedByUserId
    );

    Task DeleteAsync(Guid organizationId, Guid environmentId, string key);

    /// <summary>All variables for this Environment. Ordering: alphabetically by Key — not the order
    /// variables were set, since variable names have no inherent creation-time ordering.</summary>
    Task<IReadOnlyList<EnvironmentVariable>> ListByEnvironmentAsync(
        Guid organizationId,
        Guid environmentId
    );
}
