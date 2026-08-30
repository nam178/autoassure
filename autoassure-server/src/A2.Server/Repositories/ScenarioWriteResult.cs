namespace A2.Server.Repositories;

/// <summary>Outcome of a Scenario Create/Update: distinguishes which relationship was invalid, since
/// the Application-exists check, the Scenario-exists check, and the Precondition/Evidence-exist
/// checks map to different HTTP statuses in the Controller (404 vs 400).</summary>
public enum ScenarioWriteResult
{
    Success,
    ApplicationNotFound,
    ScenarioNotFound,
    ReferenceNotFound,
}
