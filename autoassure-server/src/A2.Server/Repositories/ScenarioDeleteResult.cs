namespace A2.Server.Repositories;

/// <summary>
/// Outcome of <see cref="IScenarioRepository.TryDeleteAsync" />: distinguishes
/// whether the Scenario was successfully deleted, not found, or modified concurrently.
/// </summary>
public enum ScenarioDeleteResult
{
    Success,
    ScenarioNotFound,
    ScenarioModifiedConcurrently,
}
