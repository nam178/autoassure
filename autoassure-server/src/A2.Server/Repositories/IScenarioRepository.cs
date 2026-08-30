using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists Scenarios and keeps their folder/tag mappings in sync in the same atomic write.
/// Storage-agnostic — callers only ever see the Scenario domain model. There is no separate public
/// repository for the folder/tag lookup structures backing <see cref="ListByFolderAsync"/>/
/// <see cref="ListByTagAsync"/> — they're a private implementation detail, always kept in sync with the
/// Scenario item in one atomic write. <see cref="TryUpdateAsync"/> and <see cref="DeleteAsync"/> take
/// the previous Scenario state because that's the only way the implementation can know which folder/tag
/// mappings must be removed — the caller must GetByIdAsync first and pass the result in.</summary>
public interface IScenarioRepository
{
    /// <summary>Atomically creates the Scenario and its folder/tag mappings, after verifying its
    /// Application and every referenced Precondition/EvidenceDefinition exist.</summary>
    Task<ScenarioWriteResult> TrySaveAsync(Scenario scenario);

    /// <summary>Atomically updates only Title, Description, Folder, Tags, Activities,
    /// UpdatedByUserId, and UpdatedAt on the Scenario, and reconciles its folder/tag mappings
    /// against <paramref name="previousState"/>, after verifying the Scenario, its Application, and
    /// every referenced Precondition/EvidenceDefinition still exist. Takes the full Scenario (not a
    /// narrower fields type) because both the reference checks and the mapping diff need every
    /// field.</summary>
    Task<ScenarioWriteResult> TryUpdateAsync(Scenario scenario, Scenario previousState);

    /// <summary>Atomically deletes the Scenario and its folder/tag mappings.</summary>
    Task DeleteAsync(Scenario scenario);

    /// <summary>Point lookup by Id, scoped to the Organization.</summary>
    Task<Scenario?> GetByIdAsync(Guid organizationId, Guid id);

    /// <summary>All Scenarios in this Application, no folder/tag filter. Ordering: creation order.</summary>
    Task<IReadOnlyList<Scenario>> ListByApplicationAsync(Guid organizationId, Guid applicationId);

    /// <summary>Scenarios currently in this folder. Strongly consistent. Ordering: creation order.</summary>
    Task<IReadOnlyList<Scenario>> ListByFolderAsync(
        Guid organizationId,
        Guid applicationId,
        string folder
    );

    /// <summary>Scenarios currently carrying this tag. Strongly consistent. Ordering: creation order.</summary>
    Task<IReadOnlyList<Scenario>> ListByTagAsync(
        Guid organizationId,
        Guid applicationId,
        string tag
    );
}
