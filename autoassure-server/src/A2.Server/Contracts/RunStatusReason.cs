namespace A2.Server.Contracts;

/// <summary>Why a Run reached Abandoned, as returned to the client. Only set when Status is Abandoned --
/// a normal finish (Completed) and a user's cancel (Cancelled) are both self-explanatory.</summary>
public enum RunStatusReason
{
    HeartbeatLost,
    DeadlineExceeded,
    WorkerCrashed,
}
