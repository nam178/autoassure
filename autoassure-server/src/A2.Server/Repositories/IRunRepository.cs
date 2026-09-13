using A2.Server.Common;
using A2.Server.Models;

namespace A2.Server.Repositories;

public interface IRunRepository
{
    /// <summary>Creates a Run.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="run"/> holds more than <see cref="Quota.MaxScenariosPerRun"/> Scenarios.
    /// </exception>
    Task<RunCreateResult> TryCreateAsync(Run run);

    Task<Run?> GetByIdAsync(Guid organizationId, Guid applicationId, Guid runId);

    /// <summary>Lists an Application's Runs, newest first</summary>
    Task<IReadOnlyList<RunInfo>> ListByApplicationAsync(Guid organizationId, Guid applicationId);

    Task<RunStartResult?> TryMarkAsStartedAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        DateTimeOffset startedAt,
        RunEnvironmentSnapshot maskedEnvironment
    );

    /// <exception cref="ArgumentException">
    /// <paramref name="terminalStatus"/> is Pending or Running -- neither is a state this operation can
    /// end a Run in -- or <paramref name="statusReason"/> is supplied together with a
    /// <paramref name="terminalStatus"/> other than Abandoned, which <see cref="Run.StatusReason"/>'s own
    /// doc says never carries one. Callers avoid both by only ever passing Completed, Cancelled or
    /// Abandoned, and a reason only alongside Abandoned.
    /// </exception>
    Task<bool> TryMarkAsEndedAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        RunStatus terminalStatus,
        RunStatusReason? statusReason,
        DateTimeOffset completedAt,
        DateTimeOffset? heartbeatCutoff = null
    );

    /// <summary>Update one or more properties of a Run</summary>
    /// <exception cref="ArgumentException"><paramref name="fields"/> has every field null.</exception>
    Task<bool> TryUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
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
    /// <paramref name="expiresAt"/> must be the Run's own <c>ExpiresAt</c>, as
    /// <see cref="GetByIdAsync"/> already returned it, so the entry expires with the rest of the Run (see
    /// fix_run_design.md section 6). The caller supplies it because the owning worker already holds the
    /// Run for its lifetime, and re-reading it on every append would add a read to the hottest write path
    /// here for a value the caller already has.
    ///
    /// Returns false, and writes nothing, when this Seq was already appended, the Run's <c>LastSeq</c>
    /// has already moved past it, or its <c>Status</c> is not Running.</summary>
    Task<bool> TryAppendStatusUpdateAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        RunStatusUpdate update,
        DateTimeOffset? expiresAt
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
        Guid id,
        long afterSeq,
        int limit
    );
}
