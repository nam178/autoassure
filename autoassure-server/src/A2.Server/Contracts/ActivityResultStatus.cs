namespace A2.Server.Contracts;

/// <summary>What became of an Activity by the time its result was appended to a Run's status update log,
/// as returned to the client. Only Passed, Failed or Skipped are legal on an appended result -- Pending
/// and Running name the states before an Activity has concluded.</summary>
public enum ActivityResultStatus
{
    Pending,
    Running,
    Passed,
    Failed,
    Skipped,
}
