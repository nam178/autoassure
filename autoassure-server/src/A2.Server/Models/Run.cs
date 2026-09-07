namespace A2.Server.Models;

/// <summary>The header of a Run: its identity, its execution state, and the Environment it ran against.
///
/// A Run's header is deliberately not the whole Run. What it ran -- one snapshot per Scenario -- lives
/// on its own rows (<see cref="RunScenarioSnapshot"/>), and what happened during execution lives on an
/// append-only log of <see cref="RunStatusUpdate"/> rows. This split exists so the header stays small: it
/// is what every state-machine write (Start Run, Update Run Heart Beat, ...) touches, and a small row
/// keeps those writes cheap. A repository's Get Run assembles the header together with the Scenario
/// snapshots read from their own rows into the full picture of "what this Run is" -- it never assembles
/// the status update log, which only the client folds.</summary>
public record Run
{
    public required Guid Id { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid ApplicationId { get; init; }
    public required RunTrigger Trigger { get; init; }
    public required RunStatus Status { get; init; }

    /// <summary>Only set when Status is Abandoned. A normal finish (Completed) and a user's cancel
    /// (Cancelled) are both self-explanatory and carry no reason.</summary>
    public RunStatusReason? StatusReason { get; init; }

    public int TotalActivityCount { get; init; }
    public int PassedActivityCount { get; init; }
    public int FailedActivityCount { get; init; }
    public int SkippedActivityCount { get; init; }

    public required RunEnvironmentSnapshot Environment { get; init; }

    /// <summary>The highest sequence number appended to this Run's status update log so far. Lets a
    /// reader tell whether it is caught up with the log without querying it.</summary>
    public long LastSeq { get; init; }

    /// <summary>Who triggered this Run. A person triggers a Manual or Authoring run. Null means a system
    /// timer triggered it -- a Scheduled run has no user to record. Do not make this required again.
    /// Ordering of Runs relies on <see cref="Id"/> being a time-sortable UUIDv7, not on this field or on
    /// CreatedAt; do not "fix" ordering later by adding a separate sequence.</summary>
    public Guid? TriggeredByUserId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public DateTimeOffset? LastHeartbeatAt { get; init; }
    public DateTimeOffset? DeadlineAt { get; init; }

    /// <summary>The point in time after which this Run stops being retained and can no longer be found.
    /// Null means never expire. Every part of this Run -- the header, every Scenario snapshot and every
    /// status update -- carries the same value, written once at create and never changed.</summary>
    public long? ExpiresAt { get; init; }
}

/// <summary>Where a Run came from. Affects retention and whether the Application's Runs panel lists it --
/// nothing about how the Run executes or is stored.</summary>
public enum RunTrigger
{
    Manual,
    Scheduled,
    Authoring,
}

/// <summary>A Run's execution state. Carries no pass/fail judgment in either direction: a Run whose every
/// Activity failed is still Completed, and the activity counts say how it went.</summary>
public enum RunStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Abandoned,
}

/// <summary>Why a Run reached Abandoned. Not a value for every failure mode a worker could ever hit --
/// see fix_run_design.md decision on StatusReason for why the set stays this small.</summary>
public enum RunStatusReason
{
    HeartbeatLost,
    DeadlineExceeded,
    WorkerCrashed,
}
