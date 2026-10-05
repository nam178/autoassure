namespace A2.Server.Engine.Repositories;

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
