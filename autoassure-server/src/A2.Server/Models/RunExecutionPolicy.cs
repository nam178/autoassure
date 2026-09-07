namespace A2.Server.Models;

/// <summary>Timing constants that govern a Run while it is Running, kept in one place next to
/// <see cref="RunRetentionPolicy"/> so every part of the system that reasons about a Run's liveness derives
/// its numbers from the same source instead of each inventing its own.</summary>
public static class RunExecutionPolicy
{
    /// <summary>How often a running worker must prove it is still alive by overwriting
    /// <see cref="Run.LastHeartbeatAt"/>.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>How long a worker can go quiet -- three missed heartbeats -- before it is considered
    /// dead.</summary>
    public static readonly TimeSpan HeartbeatStaleness = TimeSpan.FromSeconds(90);

    /// <summary>The outer time limit on a single Run, set once into <see cref="Run.DeadlineAt"/> when it
    /// starts, past which an in-progress Run is presumed stuck even if it is still heartbeating. Four hours
    /// is a chosen default, not a specified requirement -- a round number comfortably above what a single
    /// Run is expected to take today.</summary>
    public static readonly TimeSpan MaxRunDuration = TimeSpan.FromHours(4);

    /// <summary>How many groups the sweeper spreads in-flight Runs across so it can look for stale ones
    /// without every Run competing for the same one. Kept here, next to the timing constants that govern
    /// the same sweep, rather than as a magic number wherever a Run's group is derived -- this number
    /// must never change without reassigning every in-flight Run's group.</summary>
    public const int InFlightShardCount = 10;
}
