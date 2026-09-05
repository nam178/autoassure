namespace A2.Server.Repositories;

/// <summary>Outcome of a Scenario field update: distinguishes which relationship was invalid, since
/// the Application-exists check and the Scenario-exists check map to different HTTP statuses in the
/// Controller.</summary>
public enum ScenarioUpdateResult
{
    Success,
    ApplicationNotFound,
    ScenarioNotFound,
}
