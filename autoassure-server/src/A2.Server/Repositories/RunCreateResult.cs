namespace A2.Server.Repositories;

/// <summary>Outcome of <see cref="IRunRepository.TryCreateAsync" /></summary>
public enum RunCreateResult
{
    Success,
    ApplicationNotFound,
    AlreadyExists,
}