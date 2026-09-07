using A2.Server.Common;
using A2.Server.Models;

namespace A2.Server.Repositories;

/// <summary>Persists a Run: the header plus one row per Scenario snapshot, written once at create and
/// never rewritten as a whole. There is no method here that takes a whole Run and stores it again --
/// every later write is a separate, narrowly-conditioned operation (Start Run, End Run, ...) added in
/// later tasks -- and no delete method exists at all: <c>ExpiresAt</c> (DynamoDB TTL) is the only way a
/// Run row ever leaves the table.</summary>
public interface IRunRepository
{
    /// <summary>Atomically creates the Run header and one row per Scenario snapshot in a single
    /// transaction, after verifying the Application and the Environment (<paramref name="run"/>'s own
    /// <c>Environment.Source.Id</c>) both exist. Computes the header's real <c>ExpiresAt</c> from
    /// <paramref name="run"/>'s Trigger and CreatedAt (see <see cref="RunRetentionPolicy"/>) and writes
    /// that same value onto the header and every Scenario row -- any <c>ExpiresAt</c> already set on
    /// <paramref name="run"/> is ignored.
    ///
    /// Returns <see cref="RunCreateResult.AlreadyExists"/>, and writes nothing, when a Run with this Id
    /// already exists. Returns <see cref="RunCreateResult.ApplicationNotFound"/> or
    /// <see cref="RunCreateResult.EnvironmentNotFound"/>, and writes nothing, when that parent does not
    /// exist.
    ///
    /// Throws <see cref="ArgumentException"/> when <paramref name="scenarios"/> has more rows than
    /// <see cref="Quota.MaxScenariosPerRun"/> -- the header, the Application check and the Environment
    /// check already spend 3 of the 100 items DynamoDB's TransactWriteItems allows in one
    /// transaction.
    ///
    /// Named Create rather than Save: a Run header has no update-or-create semantics -- it is written
    /// once and only ever changed through later, narrowly-conditioned operations, and can never be
    /// re-created once it exists.</summary>
    Task<RunCreateResult> TryCreateAsync(Run run, IReadOnlyList<RunScenarioSnapshot> scenarios);

    /// <summary>Returns the Run's header together with its Scenario snapshots -- what the Run *is*,
    /// never its status update log. Returns null when the Run does not exist, when its
    /// <c>ExpiresAt</c> has passed (a row can stay physically queryable for a while after DynamoDB's
    /// TTL sweep is due -- see fix_run_design.md section 6), or when the header exists but carries no
    /// Scenario rows, which reads as expired rather than as a corrupt/partial result.</summary>
    Task<RunDetail?> GetByIdAsync(Guid organizationId, Guid applicationId, Guid id);

    /// <summary>Lists an Application's Runs as headers only, oldest first -- creation order falls out
    /// of the Run id's UUIDv7 range key on the sparse <c>RunHeaderIndex</c> this reads, so no separate
    /// sort is needed. Never reads a Scenario snapshot or status update row: the index holds one entry
    /// per Run no matter how many rows that Run has written, which is what keeps this list cheap (see
    /// fix_run_design.md section 3) -- a <c>FilterExpression</c> over the base table's partition would
    /// read and charge for all of them instead, so this method never issues one against the base table.
    ///
    /// Excludes <see cref="RunTrigger.Authoring"/> runs and expired headers, and takes no parameter to
    /// include either. An Authoring run is polled by its own id and gone within 7 days, and an expired
    /// header is not a Run that still exists (see fix_run_design.md section 6). Nothing lists Runs by
    /// Scenario -- that is out of scope (see fix_run_design.md section 6 and the goal file's "out of
    /// scope" list).</summary>
    Task<IReadOnlyList<RunSummary>> ListByApplicationAsync(Guid organizationId, Guid applicationId);

    /// <summary>Claims a Pending Run for execution: sets <c>Status</c> to Running, stamps
    /// <paramref name="startedAt"/> onto <c>StartedAt</c> and the first <c>LastHeartbeatAt</c>, derives
    /// <c>DeadlineAt</c> from it via <see cref="RunExecutionPolicy.MaxRunDuration"/>, and marks the Run
    /// in-flight so the sweeper's (task 8) index can find it. <c>Status</c> is the only concurrency
    /// control -- conditioning this write on <c>Status = Pending</c> is what lets exactly one of several
    /// racing claims win.
    ///
    /// Returns false, and writes nothing, when the Run's <c>Status</c> is not Pending -- including a
    /// second claim racing an already-successful one, which is what makes duplicate dispatch harmless
    /// (see fix_run_design.md section 5).</summary>
    Task<bool> TryStartAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        DateTimeOffset startedAt
    );

    /// <summary>Moves a Running Run to one of its terminal states, stamping <paramref name="completedAt"/>
    /// onto <c>CompletedAt</c> and, for Abandoned, <paramref name="statusReason"/> onto
    /// <c>StatusReason</c>. Always removes the Run's in-flight marker, which is what drops a finished Run
    /// out of the sweeper's index.
    ///
    /// Serves all three End Run callers named in fix_run_design.md section 5 -- the owning worker
    /// finishing, the user cancelling, and the sweeper abandoning -- rather than one method per caller,
    /// since splitting it would give the same rule two places to drift apart. The sweeper is the only
    /// caller that supplies <paramref name="heartbeatCutoff"/>: when present, the write also requires
    /// <c>LastHeartbeatAt &lt; heartbeatCutoff</c>, so a worker that beat again in the meantime keeps its
    /// Run even though the sweeper read it as stale a moment earlier.
    ///
    /// Returns false, and writes nothing, when the Run's <c>Status</c> is not Running, or when
    /// <paramref name="heartbeatCutoff"/> is supplied and the Run's <c>LastHeartbeatAt</c> is not older
    /// than it.
    ///
    /// Throws <see cref="ArgumentException"/> when <paramref name="terminalStatus"/> is Pending or
    /// Running -- neither is a state this operation can end a Run in -- or when
    /// <paramref name="statusReason"/> is supplied together with a <paramref name="terminalStatus"/> other
    /// than Abandoned, which <see cref="Run.StatusReason"/>'s own doc says never carries one. Callers
    /// avoid both by only ever passing Completed, Cancelled or Abandoned, and a reason only alongside
    /// Abandoned.</summary>
    Task<bool> TryEndAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        RunStatus terminalStatus,
        RunStatusReason? statusReason,
        DateTimeOffset completedAt,
        DateTimeOffset? heartbeatCutoff = null
    );

    /// <summary>Overwrites a Running Run's four activity counts with the absolute values supplied here --
    /// never an increment -- so a retried call does no harm. Its own operation rather than folded into
    /// appending a status update, which is why its counts can lag the log by one write (see
    /// fix_run_design.md section 5); nothing that needs exact progress reads these counts, since that
    /// caller folds the log instead.
    ///
    /// Returns false, and writes nothing, when the Run's <c>Status</c> is not Running.</summary>
    Task<bool> TryUpdateStatsAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        int totalActivityCount,
        int passedActivityCount,
        int failedActivityCount,
        int skippedActivityCount
    );

    /// <summary>Proves the owning worker is still alive by overwriting <c>LastHeartbeatAt</c> with
    /// <paramref name="heartbeatAt"/>. This is the one Run attribute that is never a permanent record --
    /// it is overwritten in place every <see cref="RunExecutionPolicy.HeartbeatInterval"/>, and a beat is
    /// worth nothing once the next one arrives (see fix_run_design.md section 5).
    ///
    /// Returns false, and writes nothing, when the Run's <c>Status</c> is not Running. That is the only
    /// way a cancel or a sweep reaches the worker: there is no signalling channel, so a worker that was
    /// cancelled or already marked Abandoned simply fails its next beat and is expected to stop within
    /// one interval.</summary>
    Task<bool> TryHeartbeatAsync(
        Guid organizationId,
        Guid applicationId,
        Guid id,
        DateTimeOffset heartbeatAt
    );

    /// <summary>Finds in-flight Runs the sweeper should end, in one shard of the sparse in-flight lookup
    /// (0 through <see cref="RunExecutionPolicy.InFlightShardCount"/> minus 1). A Run is stale when its
    /// heartbeat has not arrived since <paramref name="heartbeatCutoff"/>, *or* when its
    /// <c>DeadlineAt</c> has passed even though its heartbeat is still fresh -- a worker stuck in a retry
    /// loop keeps proving it is alive without making progress (see fix_run_design.md section 5). Never
    /// scans: this reads only the shard's own entries.
    ///
    /// Returns identifying info only (<see cref="StaleInFlightRun"/>), not full Run data -- the
    /// underlying lookup is sparse and key-only, so it cannot answer with more than that. A caller
    /// wanting the full Run makes its own follow-up <see cref="GetByIdAsync"/>. A Run that has ended
    /// never appears here, whatever <paramref name="heartbeatCutoff"/> is, because ending a Run removes
    /// it from this lookup (see <see cref="TryEndAsync"/>).
    ///
    /// Throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="shard"/> is outside 0 to
    /// <see cref="RunExecutionPolicy.InFlightShardCount"/> minus 1 -- no Run is ever assigned a shard
    /// outside that range, so a caller passing one made a mistake worth surfacing rather than silently
    /// returning nothing.</summary>
    Task<IReadOnlyList<StaleInFlightRun>> ListStaleInFlightRunsAsync(
        int shard,
        DateTimeOffset heartbeatCutoff
    );
}
