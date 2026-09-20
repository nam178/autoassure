namespace A2.Server.Repositories;

/// <summary>
///     Outcome of <see cref="IActivityRepository.TrySaveAsync" />: distinguishes
///     which
///     relationship was invalid, since the Scenario-exists check, the Scenario
///     Activity limit, and the
///     Precondition/Evidence-exist checks map to different HTTP statuses in the
///     Controller (404 vs 400
///     vs 400).
/// </summary>
public enum ActivitySaveResult
{
    Success,
    ScenarioNotFound,
    ScenarioActivityLimitReached,
    PreconditionOrEvidenceNotFound,
}
