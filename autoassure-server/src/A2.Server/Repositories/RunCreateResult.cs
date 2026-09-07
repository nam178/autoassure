namespace A2.Server.Repositories;

/// <summary>Outcome of <see cref="IRunRepository.TryCreateAsync"/>: distinguishes which condition
/// failed, since a missing Application, a missing Environment, and an already-existing Run id map to
/// different HTTP statuses in the Controller.</summary>
public enum RunCreateResult
{
    Success,
    ApplicationNotFound,
    EnvironmentNotFound,
    AlreadyExists,
}
