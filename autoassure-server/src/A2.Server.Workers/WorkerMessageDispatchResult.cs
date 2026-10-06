namespace A2.Server.Workers;

public enum WorkerMessageDispatchResult
{
    Handled,
    UnknownKind,
    TimeLimitExceeded,
}
