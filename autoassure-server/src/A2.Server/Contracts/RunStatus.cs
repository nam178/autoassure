namespace A2.Server.Contracts;

/// <summary>A Try/Run's lifecycle state only -- carries no pass/fail judgment; see the activity counts for that.</summary>
public enum RunStatus
{
    Pending,
    Running,
    Completed,
}
