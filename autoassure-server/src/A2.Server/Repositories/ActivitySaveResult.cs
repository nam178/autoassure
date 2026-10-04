namespace A2.Server.Repositories;

/// <summary>
/// Outcome of <see cref="IActivityRepository.TrySaveAsync" />
/// </summary>
public enum ActivitySaveResult
{
    Success,
    ScenarioNotFound,
    ScenarioNotActive,
    ScenarioActivityLimitReached,
    PreconditionOrEvidenceNotFound,
}
