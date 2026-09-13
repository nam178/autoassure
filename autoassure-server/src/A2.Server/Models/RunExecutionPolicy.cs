namespace A2.Server.Models;

/// <summary>Timing constants that govern a Run while it is Running, kept in one place next to
/// <see cref="RunRetentionPolicy"/> so every part of the system that reasons about a Run's liveness derives
/// its numbers from the same source instead of each inventing its own.</summary>
public static class RunExecutionPolicy
{
    /// <summary>The outer time limit on a single Run, measured from <see cref="Run.StartedAt"/>, past which
    /// an in-progress Run is presumed stuck even if it is still heartbeating. Four hours is a chosen
    /// default, not a specified requirement -- a round number comfortably above what a single Run is
    /// expected to take today.</summary>
    public static readonly TimeSpan MaxRunDuration = TimeSpan.FromHours(4);
}
