using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists Activities, scoped to their owning Scenario. Storage-agnostic — callers only ever
/// see the Activity domain model.</summary>
public interface IActivityRepository
{
    /// <summary>Creates the Activity, after verifying its Scenario exists, its Scenario hasn't
    /// reached <see cref="Common.Quota.MaxActivityCountPerScenario"/>, and every referenced
    /// Precondition/EvidenceDefinition exists. Atomically increments the Scenario's Activity
    /// count.</summary>
    Task<ActivitySaveResult> TrySaveAsync(Activity activity);

    /// <summary>Updates only Description, PreconditionIds, EvidenceIds, UpdatedByUserId, and
    /// UpdatedAt on an existing Activity, after verifying the Activity and every newly referenced
    /// Precondition/EvidenceDefinition still exist. <paramref name="applicationId"/> is the
    /// Activity's own ApplicationId (the caller already has it from a prior lookup), used to scope
    /// the Precondition/EvidenceDefinition checks.</summary>
    Task<ActivityUpdateResult> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid scenarioId,
        Guid id,
        ActivityUpdatableFields fields
    );

    /// <summary>Deletes the Activity and atomically decrements its Scenario's Activity count. A
    /// no-op if the Activity no longer exists.</summary>
    Task DeleteAsync(Guid organizationId, Guid applicationId, Guid scenarioId, Guid id);

    /// <summary>Deletes every Activity belonging to the given Scenario. Used when the Scenario
    /// itself is deleted, so no orphaned Activity rows remain.</summary>
    Task DeleteAllByScenarioAsync(Guid organizationId, Guid scenarioId);

    /// <summary>Point lookup by Id, scoped to the Organization.</summary>
    Task<Activity?> GetByIdAsync(Guid organizationId, Guid id);

    /// <summary>All Activities in this Scenario, ordered by <see cref="Activity.Order"/>.</summary>
    Task<IReadOnlyList<Activity>> ListByScenarioAsync(Guid organizationId, Guid scenarioId);

    /// <summary>Atomically reassigns the Order of every Activity in this Scenario to match
    /// <paramref name="orderedActivityIds"/>'s position. Returns false if any id no longer exists.
    /// <paramref name="orderedActivityIds"/> MUST NOT exceed 100 entries.</summary>
    Task<bool> TryReorderAsync(
        Guid organizationId,
        Guid scenarioId,
        IReadOnlyList<Guid> orderedActivityIds
    );
}
