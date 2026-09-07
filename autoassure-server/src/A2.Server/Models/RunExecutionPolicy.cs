namespace A2.Server.Models;

/// <summary>Timing constants for a Run's execution while it is Running, kept in one place next to
/// <see cref="RunRetentionPolicy"/> so the sweeper (task 8) reads the same numbers Start Run (task 7)
/// derives <see cref="Run.DeadlineAt"/> from, rather than each place inventing its own.</summary>
public static class RunExecutionPolicy
{
    /// <summary>How often the owning worker must overwrite <see cref="Run.LastHeartbeatAt"/> while a Run
    /// is Running. Task 7 does not call this directly -- it exists so task 8's heartbeat operation and the
    /// sweeper's staleness check are derived from the same number this policy already fixed, instead of
    /// inventing it again.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>How long a missed heartbeat -- three missed beats -- is tolerated before the sweeper may
    /// treat the owning worker as dead. Not read by task 7; see <see cref="HeartbeatInterval"/>.</summary>
    public static readonly TimeSpan HeartbeatStaleness = TimeSpan.FromSeconds(90);

    /// <summary>The absolute ceiling on how long a single Run may stay Running, set once at Start Run
    /// time into <see cref="Run.DeadlineAt"/>. A heartbeat only proves the owning worker is alive, not that
    /// it is making progress -- a worker stuck retrying the same Activity forever still beats on schedule
    /// -- so the sweeper must also treat a Run whose deadline has passed as dead, even with a fresh
    /// heartbeat.
    ///
    /// fix_run_design.md section 5 requires this absolute deadline but, unlike the 30s/90s heartbeat
    /// numbers above, does not name a duration for it -- it only frames the improvement the heartbeat
    /// brings as detecting a crash "in a minute instead of at the end of a multi-hour ceiling", which
    /// treats the deadline as a coarse backstop rather than a tight budget. Four hours is chosen as a
    /// round number comfortably above what a single Run is expected to take today; like the retention
    /// numbers in <see cref="RunRetentionPolicy"/>, it lives in one place so it can become a
    /// per-organization setting later without hunting through the operation that reads it.</summary>
    public static readonly TimeSpan MaxRunDuration = TimeSpan.FromHours(4);
}
