namespace A2.Server.Engine.Repositories;

/// <summary>
/// Outcome of <see cref="IActivityRepository.TryUpdateAsync" />: distinguishes
/// which
/// relationship was invalid, since the Activity-exists check and the
/// Precondition/Evidence-exist
/// checks map to different HTTP statuses in the Controller (404 vs 400).
/// </summary>
public enum ActivityUpdateResult
{
    Success,
    ActivityNotFound,
    PreconditionOrEvidenceNotFound,
}
