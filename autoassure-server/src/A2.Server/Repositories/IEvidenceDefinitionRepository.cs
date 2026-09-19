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
        Guid evidenceDefinitionId,
        EvidenceDefinitionUpdatableFields fields
    );

    /// <summary>Point lookup by Id, scoped to the Organization.</summary>
    Task<EvidenceDefinition?> GetByIdAsync(Guid organizationId, Guid evidenceDefinitionId);

    /// <summary>All EvidenceDefinitions in this Application's library. Ordering: newest first.</summary>
    Task<IReadOnlyList<EvidenceDefinition>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    );
}
