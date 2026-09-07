namespace A2.Server.Models;

/// <summary>A Run's header together with everything it ran -- one snapshot per Scenario. This is what a
/// Run *is*: its identity, its execution state, and a copy of what it tested, assembled from the header
/// row and the Scenario snapshot rows. It never carries anything from the status update log -- what
/// happened during execution is a separate, append-only stream that only the client folds (see
/// fix_run_design.md section 7).</summary>
public record RunDetail
{
    public required Run Header { get; init; }

    /// <summary>One snapshot per Scenario the Run ran, in no particular order.</summary>
    public required IReadOnlyList<RunScenarioSnapshot> Scenarios { get; init; }
}
