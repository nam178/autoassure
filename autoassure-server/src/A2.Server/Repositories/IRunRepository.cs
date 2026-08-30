using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists Tries and Runs. Storage-agnostic — callers only ever see the Run domain model;
/// the implementation picks the physical table based on <see cref="Run.Kind"/>.</summary>
public interface IRunRepository
{
    /// <summary>Creates or updates a Try or a Run, per <c>run.Kind</c>. Returns false if its
    /// Application or Environment no longer exists.</summary>
    Task<bool> TrySaveAsync(Run run);

    /// <summary>Point lookup by Id and Kind, scoped to the Organization.</summary>
    Task<Run?> GetAsync(Guid organizationId, Guid id, RunKind kind);

    /// <summary>The Runs panel for an Application; never returns Tries. Ordering: creation order (the
    /// order Runs were triggered).</summary>
    Task<IReadOnlyList<Run>> ListRunsByApplicationAsync(Guid organizationId, Guid applicationId);
}
