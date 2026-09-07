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
}
