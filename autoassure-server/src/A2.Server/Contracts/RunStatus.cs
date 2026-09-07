namespace A2.Server.Contracts;

/// <summary>A Run's execution state, as returned to the client. Carries no pass/fail judgment: a Run
/// whose every Activity failed is still Completed, and the activity counts say how it went.
///
/// Only Completed, Cancelled and Abandoned are legal terminal values to send on End Run -- Pending and
/// Running name states the server itself moves a Run through and are rejected there.</summary>
public enum RunStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Abandoned,
}
