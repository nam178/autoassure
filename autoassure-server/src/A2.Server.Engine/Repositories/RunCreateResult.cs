namespace A2.Server.Engine.Repositories;

/// <summary>Outcome of <see cref="IRunRepository.TryCreateAsync" /></summary>
public enum RunCreateResult
{
    Success,
    ApplicationNotFound,
    AlreadyExists,
}
