using A2.Server.Models;

namespace A2.Server.Repositories;

public interface IRunRepository
{
    /// <summary>Creates a Run.</summary>
    Task<RunCreateResult> TryCreateAsync(Run run);

    /// <exception cref="CorruptedDynamoDbRowException">
    /// The Run's header row exists but its Environment row is missing.
    /// </exception>
    Task<Run?> GetByIdAsync(Guid organizationId, Guid applicationId, Guid runId);

    /// <summary>Lists an Application's Runs, newest first, restricted to the given
    /// <paramref name="triggers"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="triggers"/> is empty.</exception>
    Task<IReadOnlyList<RunInfo>> ListByApplicationAsync(
        Guid organizationId,
        Guid applicationId,
        IReadOnlyCollection<RunTrigger> triggers
    );

    /// <summary>Marks a Pending Run as Running and overwrites its stored Environment snapshot with
    /// <paramref name="environment"/> -- pass this already masked, since this repository persists exactly
    /// the values it's given and never masks secrets itself. Returns null, and changes nothing, when the
    /// Run's Status is not currently Pending.</summary>
    Task<RunStartResult?> TryMarkAsStartedAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        DateTimeOffset startedAt,
        RunEnvironmentSnapshot environment
    );

    /// <exception cref="ArgumentException">
    /// <paramref name="terminalStatus"/> is Pending or Running -- neither is a state this operation can
    /// end a Run in. Callers avoid this by only ever passing Completed, Cancelled or Abandoned.
    /// </exception>
    Task<bool> TryMarkAsEndedAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        RunStatus terminalStatus,
        DateTimeOffset completedAt
    );

    /// <summary>Update one or more properties of a Run</summary>
    /// <exception cref="ArgumentException"><paramref name="fields"/> has every field null.</exception>
    Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        RunUpdatableFields fields
    );

    /// <summary>Lists the Runs currently Running for this Application.</summary>
    Task<IReadOnlyList<RunningRun>> ListRunningByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    );

    /// <summary>Appends one entry to the Run's status update log and advances the Run's <c>LastSeq</c> to
    /// match, atomically.
    /// Returns false, and writes nothing, when this Seq was already appended; when a concurrent append
    /// with a larger Seq has already moved the Run's <c>LastSeq</c> past it, so this one permanently loses
    /// its slot rather than being applied out of order; or when its <c>Status</c> is not Running.</summary>
    Task<bool> TryAppendStatusUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        RunStatusUpdate update
    );

    /// <summary>Returns one page of the Run's status update log, strictly after
    /// <paramref name="afterSeq"/>, in ascending sequence order.
    /// Pass <paramref name="afterSeq"/> as 0 to read from the start.
    /// Returns at most <paramref name="limit"/> entries.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="afterSeq"/> is negative or <paramref name="limit"/> is not positive.
    /// </exception>
    Task<IReadOnlyList<RunStatusUpdate>> ListStatusUpdatesAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        long afterSeq,
        int limit
    );
}