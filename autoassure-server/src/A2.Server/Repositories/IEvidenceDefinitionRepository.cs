using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists EvidenceDefinitions. Storage-agnostic — callers only ever see the EvidenceDefinition domain model.</summary>
public interface IEvidenceDefinitionRepository
{
    /// <summary>Creates the EvidenceDefinition. Returns false if its Application no longer exists.</summary>
    Task<bool> TrySaveAsync(EvidenceDefinition evidence);

    /// <summary>Updates only Name, Description, ExampleValue, UpdatedByUserId, and UpdatedAt on an
    /// existing EvidenceDefinition. Returns false if the EvidenceDefinition no longer exists.</summary>
    Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        EvidenceDefinitionUpdatableFields fields
    );

    Task DeleteAsync(Guid organizationId, Guid id);

    /// <summary>Point lookup by Id, scoped to the Organization.</summary>
    Task<EvidenceDefinition?> GetByIdAsync(Guid organizationId, Guid id);

    /// <summary>All EvidenceDefinitions in this Application's library. Ordering: creation order.</summary>
    Task<IReadOnlyList<EvidenceDefinition>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    );
}
