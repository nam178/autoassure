namespace A2.Server.Contracts;

/// <summary>Where a Run came from, as returned to the client. Affects retention and whether the Run
/// shows up in the Application's Runs panel -- nothing about how it executes.</summary>
public enum RunTrigger
{
    Manual,
    Scheduled,
    Authoring,
}
