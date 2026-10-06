using A2.Server.Common;
using A2.Server.Engine.Models;

namespace A2.Server.Engine.Repositories;

/// <summary>
/// Persists Scenarios and keeps their folder/tag mappings in sync in the same
/// atomic write.
/// Storage-agnostic — callers only ever see the Scenario domain model. There
/// is no separate public
/// repository for the folder/tag lookup structures backing
/// <see cref="ListByFolderAsync" />/
/// <see cref="ListByTagAsync" /> — they're a private implementation detail,
/// always kept in sync with the
/// Scenario item in one atomic write. <see cref="TryUpdateAsync" /> takes
/// the previous Scenario state because that's the only way the implementation
/// can know which folder/tag
/// mappings must be removed — the caller must GetByIdAsync first and pass the
/// result in.
/// </summary>
public interface IScenarioRepository
{
    /// <summary>
    /// Atomically creates the Scenario and its folder/tag mappings, after
    /// verifying its
    /// Application exists. Returns false when the Application doesn't exist.
    /// </summary>
    Task<bool> TrySaveAsync(Scenario scenario);

    /// <summary>
    /// Atomically updates only Title, Description, Folder, Tags,
    /// UpdatedByUserId, and UpdatedAt on the Scenario, and reconciles its
    /// folder/tag mappings against <paramref name="previousState" />, after
    /// verifying the Scenario and its Application still exist. Takes the full
    /// Scenario (not a narrower fields type) because the mapping diff needs
    /// every field.
    /// </summary>
    Task<ScenarioUpdateResult> TryUpdateAsync(
        Scenario scenario,
        Scenario previousState
    );

    Task<Scenario?> GetByIdAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId
    );

    /// <summary>
    /// Batched point lookup by Id, scoped to the Organization and Application.
    /// Returns only the Scenarios that actually exist and belong to this
    /// Application -- callers MUST check the returned list's ids against
    /// <paramref name="scenarioIds" /> to detect missing or foreign ids. Order
    /// of the returned list does not follow <paramref name="scenarioIds" />.
    /// </summary>
    Task<IReadOnlyList<Scenario>> GetByIdsAsync(
        Guid organizationId,
        Guid applicationId,
        IReadOnlyList<Guid> scenarioIds
    );

    /// <summary>
    /// All Scenarios in this Application, no folder/tag filter. Ordering:
    /// newest first.
    /// </summary>
    Task<IReadOnlyList<Scenario>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    );

    /// <summary>
    /// Scenarios currently in this folder. Strongly consistent. Ordering:
    /// newest first.
    /// </summary>
    Task<IReadOnlyList<Scenario>> ListByFolderAsync(
        Guid organizationId,
        Guid applicationId,
        string folder
    );

    /// <summary>
    /// Scenarios currently carrying this tag. Strongly consistent. Ordering:
    /// newest first.
    /// </summary>
    Task<IReadOnlyList<Scenario>> ListByTagAsync(
        Guid organizationId,
        Guid applicationId,
        string tag
    );

    /// <summary>
    /// Sets the Scenario's LifecycleState to the given value. Returns false if
    /// the Scenario doesn't exist, true on success.
    /// </summary>
    Task<bool> TrySetLifecycleStateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        LifecycleState newState
    );

    /// <summary>
    /// Atomically deletes the Scenario, the given Activities, and its
    /// folder/tag mappings in a single transaction.
    /// </summary>
    Task<ScenarioDeleteResult> TryDeleteAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        IReadOnlyList<Activity> activitiesToDelete
    );
}
