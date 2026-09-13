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

    /// <summary>Lists the Runs currently Running for this Application. Backed by a separate table, kept
    /// in sync with every Start/End Run write, and read with a strongly consistent read -- unlike
    /// <see cref="ListByApplicationAsync"/>'s index, a Run that just started can never be briefly missing
    /// from this result.
    ///
    /// Returns identifying info only (<see cref="RunningRun"/>), not full Run data. A caller wanting the
    /// full Run makes its own follow-up <see cref="GetByIdAsync"/>.</summary>
    Task<IReadOnlyList<RunningRun>> ListRunningByApplicationAsync(
        Guid organizationId,
        Guid applicationId
    );

    /// <summary>Appends one entry to the Run's status update log and advances the Run's <c>LastSeq</c> to
    /// match, atomically. A retried append changes nothing, and a worker whose Run was cancelled or swept
    /// cannot append to it (see fix_run_design.md section 3).
    ///
    /// The owning worker allocates <see cref="RunStatusUpdate.Seq"/> and supplies it on
    /// <paramref name="update"/>; this repository never invents one, since one owner for life means
    /// nothing else contends for the next number. Sequence numbers are 1-based, which is what lets
    /// <see cref="ListStatusUpdatesAsync"/>'s <c>afterSeq = 0</c> mean "from the start".
    ///
    /// Returns false, and writes nothing, when this Seq was already appended, the Run's <c>LastSeq</c>
    /// has already moved past it, or its <c>Status</c> is not Running.</summary>
    Task<bool> TryAppendStatusUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid runId,
        RunStatusUpdate update
    );

    /// <summary>Returns one page of the Run's status update log, strictly after
    /// <paramref name="afterSeq"/>, in ascending sequence order -- the log only the client folds (see
    /// fix_run_design.md section 7). Never folds, interprets or derives Run state from these entries; it
    /// hands them back exactly as appended.
    ///
    /// Pass <paramref name="afterSeq"/> as 0 to read from the start: sequence numbers are 1-based (see
    /// <see cref="TryAppendStatusUpdateAsync"/>), so 0 excludes nothing real.
    ///
    /// Returns at most <paramref name="limit"/> entries, in one bounded page. Unlike
    /// <see cref="GetByIdAsync"/> and <see cref="ListByApplicationAsync"/>, this deliberately stops at one
    /// page instead of assembling a complete result: the cursor here is a public mechanism the client
    /// drives itself, polling again with the last Seq it holds (see fix_run_design.md section 4).</summary>
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
