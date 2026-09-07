namespace A2.Server.Models;

/// <summary>One entry in a Run's append-only status update log: something that happened during
/// execution, in the order it happened. The server stores and returns these rows in sequence order and
/// never interprets them -- only the client folds the log to derive a Run's current state. Rows live
/// three years, so every <see cref="Kind"/> must still replay correctly that far out; see
/// <see cref="RunStatusUpdateKind"/> for what earns a new kind.</summary>
public record RunStatusUpdate
{
    public required long Seq { get; init; }
    public required RunStatusUpdateKind Kind { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Set when Kind is AppendActivityResult; null otherwise. AppendActivityResult is the only
    /// kind today, so this is always set in practice.</summary>
    public ActivityResult? ActivityResult { get; init; }
}

/// <summary>What a RunStatusUpdate row records. Kept few and dumb on purpose: a kind earns its place only
/// by being permanent (worth reading a year later) and order-dependent (its position between the update
/// before and after it matters) -- see fix_run_design.md section 7. AppendActivityResult is the only kind
/// built so far; AppendAgentLog is designed but deliberately not added yet (see decision 13), since its
/// only writer -- the execution agent -- does not exist.</summary>
public enum RunStatusUpdateKind
{
    AppendActivityResult,
}
